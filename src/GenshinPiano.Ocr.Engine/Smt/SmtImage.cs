using System.Runtime.InteropServices;
using SkiaSharp;

namespace GenshinPiano.Ocr.Engine.Smt;

internal sealed record GrayImage(int Width, int Height, byte[] Pixels)
{
    public static GrayImage Load(string path)
    {
        using var bitmap = SKBitmap.Decode(path) ?? throw new InvalidDataException("Cannot decode the score image.");
        if (bitmap.Width < 40 || bitmap.Height < 40 || (long)bitmap.Width*bitmap.Height > 40_000_000)
            throw new InvalidDataException("Score image dimensions are unsupported (40 million pixels maximum).");
        var pixels = bitmap.Pixels;
        var gray = new byte[pixels.Length];
        for (var i=0;i<pixels.Length;i++)
        {
            var c=pixels[i];
            var r=(c.Red*c.Alpha+255*(255-c.Alpha)+127)/255;
            var g=(c.Green*c.Alpha+255*(255-c.Alpha)+127)/255;
            var b=(c.Blue*c.Alpha+255*(255-c.Alpha)+127)/255;
            gray[i]=(byte)((r*19595+g*38470+b*7471+32768)>>16);
        }
        return new(bitmap.Width,bitmap.Height,gray);
    }

    public int[] RowInk(int threshold,int left=0,int? width=null)
    {
        var counts=new int[Height];
        var end=left+(width ?? Width);
        for (var y=0;y<Height;y++)
            for (var x=left;x<end;x++)
                if (Pixels[y*Width+x]<threshold) counts[y]++;
        return counts;
    }

    public int[] ColumnInk(int threshold,int top,int bottom,int left=0,int? right=null)
    {
        var counts=new int[Width];
        for (var y=top;y<bottom;y++)
            for (var x=left;x<(right ?? Width);x++)
                if (Pixels[y*Width+x]<threshold) counts[x]++;
        return counts;
    }

    public void Render(SmtSystem system,string path,int spacing,int? whiteAbove)
    {
        var width=system.Right-system.Left;
        var height=system.Bottom-system.Top;
        var data=new byte[width*height];
        for (var y=0;y<height;y++)
            for (var x=0;x<width;x++)
            {
                var value=Pixels[(y+system.Top)*Width+x+system.Left];
                data[y*width+x]=whiteAbove is { } threshold && value>threshold ? (byte)255 : value;
            }
        var factor=spacing/system.Spacing;
        var targetWidth=Math.Max(1,(int)Math.Round(width*factor));
        var targetHeight=Math.Max(1,(int)Math.Round(height*factor));
        var resized=ResizeBilinear(data,width,height,targetWidth,targetHeight);
        using var output=new SKBitmap(new SKImageInfo(targetWidth,Math.Max(256,targetHeight),SKColorType.Gray8,SKAlphaType.Opaque));
        output.Erase(SKColors.White);
        for (var y=0;y<targetHeight;y++)
            Marshal.Copy(resized,y*targetWidth,IntPtr.Add(output.GetPixels(),y*output.RowBytes),targetWidth);
        using var encoded=output.Encode(SKEncodedImageFormat.Png,100);
        using var file=File.Create(path);
        encoded.SaveTo(file);
    }

    // Separable area-aware bilinear filtering keeps deployment inputs aligned
    // with the development Pillow preprocessing, including 22-bit rounding.
    internal static byte[] ResizeBilinear(byte[] input,int width,int height,int targetWidth,int targetHeight)
    {
        static (int Start,int[] Weights)[] Coefficients(int source,int target)
        {
            var scale=(double)source/target;
            var support=Math.Max(1,scale);
            return Enumerable.Range(0,target).Select(i=>
            {
                var center=(i+0.5)*scale;
                var first=Math.Max(0,(int)(center-support+0.5));
                var end=Math.Min(source,(int)(center+support+0.5));
                var weights=Enumerable.Range(first,end-first).Select(x=>Math.Max(0,1-Math.Abs((x+0.5-center)/support))).ToArray();
                var sum=weights.Sum();
                return (first,weights.Select(w=>(int)(w/sum*(1<<22)+0.5)).ToArray());
            }).ToArray();
        }
        var horizontal=Coefficients(width,targetWidth);
        var vertical=Coefficients(height,targetHeight);
        var temp=new byte[targetWidth*height];
        for (var y=0;y<height;y++)
            for (var x=0;x<targetWidth;x++)
            {
                var (first,weights)=horizontal[x];
                var total=1<<21;
                for (var k=0;k<weights.Length;k++) total+=input[y*width+first+k]*weights[k];
                temp[y*targetWidth+x]=(byte)Math.Clamp(total>>22,0,255);
            }
        var output=new byte[targetWidth*targetHeight];
        for (var y=0;y<targetHeight;y++)
            for (var x=0;x<targetWidth;x++)
            {
                var (first,weights)=vertical[y];
                var total=1<<21;
                for (var k=0;k<weights.Length;k++) total+=temp[(first+k)*targetWidth+x]*weights[k];
                output[y*targetWidth+x]=(byte)Math.Clamp(total>>22,0,255);
            }
        return output;
    }
}
