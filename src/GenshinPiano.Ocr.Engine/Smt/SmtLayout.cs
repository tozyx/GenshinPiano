namespace GenshinPiano.Ocr.Engine.Smt;

internal sealed record SmtStaff(int[] Lines,double Spacing,int Inferred,double Strength);
internal sealed record SmtSystem(int[] Upper,int[] Lower,int Left,int Top,int Right,int Bottom,double Spacing,int? ExpectedMeasures);
internal sealed record SmtLayout(List<SmtSystem> Systems,int Threshold,int InferredLines);

internal static class SmtLayoutDetector
{
    private const string Hint="Use a clear, flat scan or directly rendered score image. Photographed pages and automatic rectification are not supported.";
    internal static double Median(IEnumerable<double> source)
    {
        var values=source.Order().ToArray();
        if (values.Length==0) throw new InvalidDataException("No staff spacing was detected.");
        return (values[(values.Length-1)/2]+values[values.Length/2])/2;
    }
    private static List<int> Centers(IEnumerable<int> indices)
    {
        var result=new List<int>();
        var group=new List<int>();
        foreach (var value in indices)
        {
            if (group.Count>0 && value>group[^1]+1) { result.Add((int)Math.Round(group.Average()));group.Clear(); }
            group.Add(value);
        }
        if (group.Count>0) result.Add((int)Math.Round(group.Average()));
        return result;
    }
    private static bool Overlaps(int[] lines,SmtStaff other,double margin) => lines[0]<=other.Lines[^1]+margin && lines[^1]>=other.Lines[0]-margin;

    private static (List<SmtStaff> Staves,int Bands) Find(GrayImage image,int threshold,double coverage,int left=0,int? width=null)
    {
        var projection=image.RowInk(threshold,left,width);
        var centers=Centers(Enumerable.Range(0,image.Height).Where(y=>projection[y]>(width ?? image.Width)*coverage));
        var candidates=new List<SmtStaff>();
        for (var i=0;i+4<centers.Count;i++)
        {
            var lines=centers.GetRange(i,5).ToArray();
            var gaps=Enumerable.Range(0,4).Select(j=>(double)(lines[j+1]-lines[j])).ToArray();
            var spacing=Median(gaps);
            if (spacing<3 || spacing>image.Height/10d || gaps.Any(gap=>Math.Abs(gap-spacing)>Math.Max(1,spacing*0.18))) continue;
            candidates.Add(new(lines,spacing,0,lines.Average(y=>projection[y])));
        }
        var selected=new List<SmtStaff>();
        foreach (var staff in candidates.OrderByDescending(c=>c.Strength))
            if (!selected.Any(other=>Overlaps(staff.Lines,other,other.Spacing/2))) selected.Add(staff);
        if (selected.Count==0) return ([],centers.Count);
        var typical=Median(selected.Select(s=>s.Spacing));
        foreach (var center in centers)
            for (var position=0;position<5;position++)
            {
                var top=center-typical*position;
                if (top<0 || top+typical*4>=image.Height) continue;
                var lines=new int[5];
                var inferred=0;
                for (var offset=0;offset<5;offset++)
                {
                    var expected=top+offset*typical;
                    var nearest=centers.MinBy(y=>Math.Abs(y-expected));
                    if (Math.Abs(nearest-expected)<=Math.Max(1,typical*0.16)) lines[offset]=nearest;
                    else { lines[offset]=(int)Math.Round(expected);inferred++; }
                }
                if (inferred==1 && lines.Distinct().Count()==5 && !selected.Any(s=>Overlaps(lines,s,typical/2)))
                    selected.Add(new(lines,typical,1,0));
            }
        return (selected.OrderBy(s=>s.Lines[0]).ToList(),centers.Count);
    }

    private static bool Connected(GrayImage image,int threshold,SmtStaff upper,SmtStaff lower)
    {
        var top=upper.Lines[^1]+1;
        var bottom=lower.Lines[0];
        if (bottom<=top) return false;
        var columns=image.ColumnInk(threshold,top,bottom);
        return Centers(Enumerable.Range(0,image.Width).Where(x=>columns[x]>(bottom-top)*0.75)).Count>=2;
    }

    private static void Audit(GrayImage image,List<SmtStaff> staves,CancellationToken cancellationToken)
    {
        var spacing=Median(staves.Select(s=>s.Spacing));
        var observations=new List<(double Middle,int Slice)>();
        for (var slice=0;slice<4;slice++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var center=(int)Math.Round(image.Width*(slice+1)*0.2);
            var half=Math.Max(4,(int)Math.Round(image.Width*0.06));
            var left=Math.Max(0,center-half);
            var width=Math.Min(image.Width,center+half)-left;
            var local=new List<SmtStaff>();
            foreach (var threshold in new[]{150,180,200,220})
            {
                var (found,_)=Find(image,threshold,0.55,left,width);
                if (found.Count>local.Count) local=found;
            }
            foreach (var staff in local.Where(s=>s.Inferred==0 && s.Spacing>=0.75*spacing && s.Spacing<=1.3*spacing))
            {
                var middle=staff.Lines.Average();
                if (!staves.Any(other=>Math.Abs(middle-other.Lines.Average())<=Math.Max(2,1.5*spacing))) observations.Add((middle,slice));
            }
        }
        var clusters=new List<List<(double Middle,int Slice)>>();
        foreach (var item in observations.OrderBy(o=>o.Middle))
        {
            var cluster=clusters.FirstOrDefault(group=>Math.Abs(item.Middle-group.Average(x=>x.Middle))<=2.5*spacing);
            if (cluster is null) clusters.Add([item]);else cluster.Add(item);
        }
        if (clusters.Any(c=>c.Select(x=>x.Slice).Distinct().Count()>=2))
            throw new InvalidDataException("Some staff lines are outside the extracted layout. "+Hint);
    }

    public static SmtLayout Detect(GrayImage image,CancellationToken cancellationToken=default)
    {
        var choices=new List<(List<SmtStaff> Staves,int Threshold,double Coverage,int Inferred)>();
        var observed=new Dictionary<int,int>();
        foreach (var threshold in new[]{150,180,200,220})
            foreach (var coverage in new[]{0.5,0.35,0.22})
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (staves,_)=Find(image,threshold,coverage);
                observed[threshold]=Math.Max(observed.GetValueOrDefault(threshold),staves.Count);
                if (staves.Count==0 || staves.Count%2!=0) continue;
                var valid=true;
                for (var i=0;i<staves.Count;i+=2)
                    if (Math.Abs(staves[i].Spacing-staves[i+1].Spacing)>Math.Max(1,staves[i].Spacing*0.2) || !Connected(image,threshold,staves[i],staves[i+1])) { valid=false;break; }
                if (valid) choices.Add((staves,threshold,coverage,staves.Sum(s=>s.Inferred)));
            }
        if (choices.Count==0) throw new InvalidDataException("Cannot reliably pair the piano staves. "+Hint);
        var best=choices.OrderByDescending(c=>c.Staves.Count).ThenBy(c=>c.Inferred).ThenByDescending(c=>c.Coverage).ThenBy(c=>c.Threshold).First();
        if (observed[best.Threshold]>best.Staves.Count) throw new InvalidDataException("Unpaired staff lines were detected. "+Hint);
        Audit(image,best.Staves,cancellationToken);
        var systems=new List<SmtSystem>();
        for (var i=0;i<best.Staves.Count;i+=2)
        {
            var upper=best.Staves[i].Lines;
            var lower=best.Staves[i+1].Lines;
            var spacing=Median(Enumerable.Range(0,4).Select(j=>(double)(upper[j+1]-upper[j])).Concat(Enumerable.Range(0,4).Select(j=>(double)(lower[j+1]-lower[j]))));
            var rowInk=new int[image.Width];
            foreach (var y in upper.Concat(lower))
                for (var x=0;x<image.Width;x++) if (image.Pixels[y*image.Width+x]<best.Threshold) rowInk[x]++;
            var active=Enumerable.Range(0,image.Width).Where(x=>rowInk[x]>=5).ToArray();
            if (active.Length==0) throw new InvalidDataException("Cannot locate the horizontal staff extent.");
            var left=Math.Max(0,(int)(active[0]-3*spacing));
            var right=Math.Min(image.Width,(int)(active[^1]+spacing+1));
            var top=Math.Max(0,(int)(upper[0]-3*spacing));
            var bottom=Math.Min(image.Height,(int)(lower[^1]+3*spacing+1));
            if (i>0) top=Math.Max(top,(best.Staves[i-1].Lines[^1]+upper[0])/2);
            if (i+2<best.Staves.Count) bottom=Math.Min(bottom,(lower[^1]+best.Staves[i+2].Lines[0])/2);
            var columns=image.ColumnInk(best.Threshold,upper[0],lower[^1]+1,left,right);
            var bars=Centers(Enumerable.Range(left,right-left).Where(x=>columns[x]>(lower[^1]+1-upper[0])*0.7));
            var merged=new List<int>();
            foreach (var x in bars)
                if (merged.Count>0 && x-merged[^1]<spacing*0.8) merged[^1]=x;else merged.Add(x);
            systems.Add(new(upper,lower,left,top,right,bottom,spacing,merged.Count>=2?merged.Count-1:null));
        }
        return new(systems,best.Threshold,best.Inferred);
    }
}
