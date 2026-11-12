using System.Globalization;
using System.Text.RegularExpressions;

namespace GenshinPiano.Ocr.Engine.Smt;

internal sealed record SmtEvent(int Measure, int Staff, int Voice, Beat Start, Beat Duration, int? Pitch, bool TieStart, bool TieStop);
internal sealed record SmtMeasure(int Number, Beat Lower, Beat Upper)
{
    public Beat Extent => Lower > Upper ? Lower : Upper;
}
internal sealed record SmtMeter(int Measure, int Numerator, int Denominator)
{
    public Beat Length => new(Numerator * 4, Denominator);
}
internal sealed record SmtDocument(List<SmtEvent> Events, List<SmtMeasure> Measures, List<SmtMeter> Meters, int NullRows, double? Tempo);

internal static partial class SmtNotation
{
    private sealed record Spine(int Staff, int Voice, Beat End);
    [GeneratedRegex(@"^(\d+)(\.*)([A-Ga-g]+|r)([#n-]*)")]
    private static partial Regex AtomRegex();

    public static List<string[]> Normalize(string text)
    {
        text = text.Trim().Replace("\\t", "\t").Replace("\\n", "\n").Replace("·", "");
        var serialized = text.Contains("<b>", StringComparison.Ordinal) || text.Contains("<t>", StringComparison.Ordinal);
        var records = serialized ? text.Split("<b>") : text.Replace("\r", "").Split('\n');
        return records.Where(r => !string.IsNullOrWhiteSpace(r)).Select(record =>
            (serialized ? record.Split("<t>") : record.Split('\t')).Select(field => serialized
                ? string.Join(' ', field.Split("<s>").Select(atom => string.Concat(atom.Where(c => !char.IsWhiteSpace(c))))).Trim()
                : field.Trim()).ToArray()).ToList();
    }

    public static SmtDocument Parse(string text)
    {
        var rows = Normalize(text);
        if (rows.Count == 0 || rows[0].Length != 2 || rows[0].Any(f => !f.StartsWith("**", StringComparison.Ordinal)))
            throw new InvalidDataException("SMT did not return a two-staff notation header.");
        var spines = new List<Spine> { new(2, 1, Beat.Zero), new(1, 2, Beat.Zero) };
        var events = new List<SmtEvent>();
        var measures = new List<SmtMeasure>();
        var meters = new List<SmtMeter>();
        var clock = Beat.Zero;
        var number = 1;
        var nextVoice = 3;
        var hasData = false;
        var terminated = false;
        var nullRows = 0;
        double? tempo = null;
        foreach (var row in rows)
        {
            if (row[0].StartsWith("!!", StringComparison.Ordinal)) continue;
            if (terminated) throw new InvalidDataException("SMT returned events after spine termination.");
            if (row.Length != spines.Count || row.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException("SMT returned an inconsistent number of voices.");
            if (row[0].StartsWith('*'))
            {
                if (row.Any(f => !f.StartsWith('*'))) throw new InvalidDataException("Mixed SMT interpretation/data record.");
                if (row.Any(f => f.StartsWith("*M", StringComparison.Ordinal) && !f.StartsWith("*MM", StringComparison.Ordinal)))
                {
                    var values = row.Where(f => f.StartsWith("*M", StringComparison.Ordinal) && !f.StartsWith("*MM", StringComparison.Ordinal))
                        .Select(f => f[2..]).Distinct().ToArray();
                    if (values.Length != 1) throw new InvalidDataException("SMT staves disagree on the time signature.");
                    var pair = values[0].Split('/');
                    if (pair.Length != 2 || !int.TryParse(pair[0], out var numerator) || !int.TryParse(pair[1], out var denominator)
                        || numerator is < 1 or > 64 || denominator is < 1 or > 128 || (denominator & (denominator-1)) != 0)
                        throw new InvalidDataException("Invalid SMT time signature.");
                    meters.Add(new(number, numerator, denominator));
                }
                foreach (var field in row.Where(f => f.StartsWith("*MM", StringComparison.Ordinal)))
                    if (double.TryParse(field[3..], NumberStyles.Float, CultureInfo.InvariantCulture, out var bpm) && bpm is >= 1 and <= 1000)
                        tempo = bpm;
                if (row.All(f => f == "*-")) { terminated = true; continue; }
                if (row.Any(f => f is "*-" or "*x" or "*+"))
                    throw new InvalidDataException("Unsupported partial termination or spine exchange/addition.");
                var changed = new List<Spine>();
                for (var i = 0; i < row.Length; i++)
                {
                    var spine = spines[i];
                    if (row[i] == "*^")
                    {
                        changed.Add(spine);
                        changed.Add(spine with { Voice = nextVoice++ });
                    }
                    else if (row[i] == "*v")
                    {
                        var end = i + 1;
                        while (end < row.Length && row[end] == "*v") end++;
                        var group = spines.GetRange(i, end-i);
                        if (group.Count < 2 || group.Any(s => s.Staff != spine.Staff))
                            throw new InvalidDataException("Unpaired or cross-staff SMT voice merge.");
                        changed.Add(spine with { End = group.Max(s => s.End) });
                        i = end-1;
                    }
                    else changed.Add(spine);
                }
                spines = changed;
                continue;
            }
            if (row[0].StartsWith('!')) continue;
            if (row[0].StartsWith('='))
            {
                if (row.Any(f => !f.StartsWith('='))) throw new InvalidDataException("SMT staves disagree on a barline.");
                if (hasData)
                {
                    measures.Add(new(number++, spines.Where(s => s.Staff == 2).Max(s => s.End), spines.Where(s => s.Staff == 1).Max(s => s.End)));
                    clock = Beat.Zero;
                    spines = spines.Select(s => s with { End = Beat.Zero }).ToList();
                    hasData = false;
                }
                continue;
            }
            if (row.All(f => f == ".")) { nullRows++; continue; }
            for (var i = 0; i < row.Length; i++)
            {
                if (row[i] == ".") continue;
                var atoms = row[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var duration = Beat.Zero;
                foreach (var atom in atoms)
                {
                    var match = AtomRegex().Match(atom);
                    if (!match.Success || !int.TryParse(match.Groups[1].Value, out var reciprocal) || reciprocal is < 1 or > 4096 || atom.Contains('q'))
                        throw new InvalidDataException($"Unsupported SMT symbol: {atom}");
                    var length = new Beat(4, reciprocal);
                    var addition = length;
                    if (match.Groups[2].Length > 4) throw new InvalidDataException("Too many SMT augmentation dots.");
                    for (var dot = 0; dot < match.Groups[2].Length; dot++) { addition /= 2; length += addition; }
                    var letters = match.Groups[3].Value;
                    int? pitch = null;
                    if (letters != "r")
                    {
                        if (letters.Distinct().Count() != 1) throw new InvalidDataException("Invalid SMT pitch spelling.");
                        var octave = char.IsLower(letters[0]) ? letters.Length+3 : 4-letters.Length;
                        var step = char.ToUpperInvariant(letters[0]) switch { 'C'=>0, 'D'=>2, 'E'=>4, 'F'=>5, 'G'=>7, 'A'=>9, 'B'=>11, _=>throw new InvalidDataException() };
                        pitch = 12*(octave+1)+step+match.Groups[4].Value.Count(c => c == '#')-match.Groups[4].Value.Count(c => c == '-');
                        if (pitch is < 0 or > 127) throw new InvalidDataException("SMT pitch is outside MIDI range.");
                    }
                    events.Add(new(number, spines[i].Staff, spines[i].Voice, clock, length, pitch,
                        atom.Contains('[') || atom.Contains('_'), atom.Contains(']') || atom.Contains('_')));
                    if (length > duration) duration = length;
                }
                if (duration <= Beat.Zero) throw new InvalidDataException("Empty SMT event.");
                spines[i] = spines[i] with { End = clock+duration };
                hasData = true;
            }
            var future = spines.Where(s => s.End > clock).Select(s => s.End).ToArray();
            if (future.Length > 0) clock = future.Min();
        }
        if (!terminated || hasData || measures.Count == 0 || events.All(e => e.Pitch is null))
            throw new InvalidDataException("SMT notation is empty, truncated or missing its last barline.");
        return new(events, measures, meters, nullRows, tempo);
    }
}
