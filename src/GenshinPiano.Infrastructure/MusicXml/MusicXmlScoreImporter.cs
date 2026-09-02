using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using GenshinPiano.Core.Scores;

namespace GenshinPiano.Infrastructure.MusicXml;

public sealed record MusicXmlImportReport(int NoteCount, int ChromaticNoteCount, int GraceNoteCount, IReadOnlyList<string> Warnings);
public sealed record MusicXmlImportResult(ScoreDocument Score, MusicXmlImportReport Report);

public sealed class MusicXmlScoreImporter
{
    private const int Ppq = 480;

    public Task<MusicXmlImportResult> ImportAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Task.Run(() => Import(path, cancellationToken), cancellationToken);
    }

    private static MusicXmlImportResult Import(string path, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(path);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            // MusicXML exporters commonly emit the official Recordare DOCTYPE.
            // Ignore the declaration while keeping XmlResolver disabled so no
            // external DTD or entity can be fetched or expanded.
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            IgnoreComments = true,
        });
        var root = XDocument.Load(reader).Root ?? throw new InvalidDataException("The MusicXML document is empty.");
        if (root.Name.LocalName != "score-partwise")
            throw new NotSupportedException("Only MusicXML score-partwise documents are supported.");

        var names = Desc(root, "score-part").Where(x => Attr(x, "id") is not null)
            .GroupBy(x => Attr(x, "id")!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => El(g.Last(), "part-name")?.Value.Trim() ?? g.Key, StringComparer.Ordinal);
        var rawTracks = new Dictionary<TrackKey, List<RawNote>>();
        var tempos = new List<TempoChange>();
        var signatures = new List<TimeSignatureChange>();
        var openTies = new Dictionary<TieKey, RawNote>();
        var graceCount = 0;

        foreach (var part in Children(root, "part"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var partId = Attr(part, "id") ?? $"part-{rawTracks.Count + 1}";
            foreach (var sound in Children(part, "sound"))
            {
                if (ParseTempo(Attr(sound, "tempo")) is { } bpm and > 0)
                    tempos.Add(new() { Tick = 0, Bpm = bpm });
            }
            var divisions = 1;
            long measureStart = 0;
            foreach (var measure in Children(part, "measure"))
            {
                long cursor = 0, extent = 0;
                var previousStarts = new Dictionary<string, long>();
                foreach (var item in measure.Elements())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (item.Name.LocalName == "attributes")
                    {
                        if (Int(El(item, "divisions")) is { } d and > 0) divisions = d;
                        if (El(item, "time") is { } time &&
                            Int(El(time, "beats")) is { } beats and > 0 &&
                            Int(El(time, "beat-type")) is { } beatType and > 0)
                            signatures.Add(new() { Tick = measureStart, Numerator = beats, Denominator = beatType });
                        continue;
                    }
                    if (item.Name.LocalName == "direction")
                    {
                        if (ReadTempo(item) is { } bpm and > 0)
                            tempos.Add(new() { Tick = Math.Max(0, measureStart + cursor + Scale(Int(El(item, "offset")) ?? 0, divisions)), Bpm = bpm });
                        continue;
                    }
                    if (item.Name.LocalName is "backup" or "forward")
                    {
                        var movement = Scale(Duration(item), divisions);
                        cursor = item.Name.LocalName == "backup" ? Math.Max(0, cursor - movement) : cursor + movement;
                        extent = Math.Max(extent, cursor);
                        continue;
                    }
                    if (item.Name.LocalName != "note") continue;

                    var chord = El(item, "chord") is not null;
                    var grace = El(item, "grace") is not null;
                    var duration = grace ? 0 : Scale(Duration(item), divisions);
                    var voice = El(item, "voice")?.Value.Trim() ?? "1";
                    var staff = Math.Max(1, Int(El(item, "staff")) ?? 1);
                    var start = chord ? previousStarts.GetValueOrDefault(voice, cursor) : cursor;
                    if (!chord) previousStarts[voice] = start;
                    if (grace) graceCount++;
                    else if (El(item, "rest") is null && TryPitch(item, out var pitch, out var chromatic))
                    {
                        var trackKey = new TrackKey(partId, staff);
                        if (!rawTracks.TryGetValue(trackKey, out var notes)) rawTracks[trackKey] = notes = [];
                        var tieKey = new TieKey(partId, staff, voice, pitch);
                        var tieStart = HasTie(item, "start");
                        var tieStop = HasTie(item, "stop");
                        if (tieStop && openTies.TryGetValue(tieKey, out var open))
                        {
                            open.Duration = Math.Max(open.Duration, measureStart + start + duration - open.Start);
                            if (!tieStart) openTies.Remove(tieKey);
                        }
                        else
                        {
                            var note = new RawNote(pitch, measureStart + start, Math.Max(1, duration), chromatic);
                            notes.Add(note);
                            if (tieStart) openTies[tieKey] = note;
                        }
                    }
                    if (!chord && !grace) cursor += duration;
                    extent = Math.Max(extent, Math.Max(cursor, start + duration));
                }
                // Observed extent preserves pickup and incomplete final measures.
                measureStart = checked(measureStart + extent);
            }
        }
        if (rawTracks.Count == 0) throw new InvalidDataException("The MusicXML document contains no pitched notes.");

        var tracks = rawTracks.OrderBy(x => x.Key.PartId, StringComparer.Ordinal).ThenBy(x => x.Key.Staff)
            .Select(x => new ScoreTrack
            {
                Id = $"musicxml-{x.Key.PartId}-staff-{x.Key.Staff}",
                Name = rawTracks.Keys.Count(k => k.PartId == x.Key.PartId) > 1
                    ? $"{names.GetValueOrDefault(x.Key.PartId, x.Key.PartId)} - Staff {x.Key.Staff}"
                    : names.GetValueOrDefault(x.Key.PartId, x.Key.PartId),
                Instrument = "musicxml",
                Notes = x.Value.OrderBy(n => n.Start).ThenBy(n => n.Pitch).Select(n => new NoteEvent
                {
                    Pitch = n.Pitch,
                    StartTick = n.Start,
                    DurationTick = n.Duration,
                    RhythmTick = n.Duration,
                    DurationMode = DurationMode.Explicit,
                }).ToList(),
            }).ToList();
        var allNotes = rawTracks.Values.SelectMany(x => x).ToArray();
        var chromaticCount = allNotes.Count(x => x.Chromatic);
        var warnings = new List<string>();
        if (chromaticCount > 0) warnings.Add($"{chromaticCount} chromatic note(s) require 21-key range review.");
        if (graceCount > 0) warnings.Add($"{graceCount} grace note(s) were skipped.");
        if (openTies.Count > 0) warnings.Add($"{openTies.Count} tie(s) had no matching stop.");
        var title = El(El(root, "work"), "work-title")?.Value.Trim();
        if (string.IsNullOrWhiteSpace(title)) title = El(root, "movement-title")?.Value.Trim();

        return new(new ScoreDocument
        {
            Metadata = new() { Title = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(path) : title },
            Timing = new() { Ppq = Ppq, TempoMap = NormalizeTempos(tempos), TimeSignatures = NormalizeSignatures(signatures) },
            Tracks = tracks,
            Playback = new() { Mapping = "genshin-21-key", OutOfRangePolicy = OutOfRangePolicy.Reject },
        }, new(allNotes.Length, chromaticCount, graceCount, warnings));
    }

    private static double? ReadTempo(XElement direction)
    {
        var value = Desc(direction, "sound").Select(x => Attr(x, "tempo")).FirstOrDefault(x => x is not null)
                    ?? Desc(direction, "per-minute").Select(x => x.Value).FirstOrDefault();
        return ParseTempo(value);
    }

    private static double? ParseTempo(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var tempo) ? tempo : null;

    private static bool TryPitch(XElement note, out int pitch, out bool chromatic)
    {
        pitch = 0;
        chromatic = false;
        var p = El(note, "pitch");
        var step = El(p, "step")?.Value.Trim();
        var octave = Int(El(p, "octave"));
        if (step is not { Length: 1 } || octave is null) return false;
        var natural = char.ToUpperInvariant(step[0]) switch
        {
            'C' => 0, 'D' => 2, 'E' => 4, 'F' => 5, 'G' => 7, 'A' => 9, 'B' => 11, _ => -1,
        };
        if (natural < 0) return false;
        var alter = Int(El(p, "alter")) ?? 0;
        pitch = (octave.Value + 1) * 12 + natural + alter;
        chromatic = alter != 0;
        return pitch is >= 0 and <= 127;
    }

    private static bool HasTie(XElement note, string type) =>
        Children(note, "tie").Concat(Desc(El(note, "notations"), "tied"))
            .Any(x => string.Equals(Attr(x, "type"), type, StringComparison.OrdinalIgnoreCase));

    private static int Duration(XElement item) =>
        Int(El(item, "duration")) is { } value and >= 0
            ? value
            : throw new InvalidDataException($"MusicXML {item.Name.LocalName} is missing a valid duration.");

    private static long Scale(int value, int divisions) =>
        checked((long)Math.Round(value * (double)Ppq / divisions, MidpointRounding.AwayFromZero));

    private static List<TempoChange> NormalizeTempos(IEnumerable<TempoChange> source)
    {
        var result = source.Where(x => double.IsFinite(x.Bpm) && x.Bpm > 0).GroupBy(x => x.Tick)
            .Select(x => x.Last()).OrderBy(x => x.Tick).ToList();
        if (result.Count == 0 || result[0].Tick != 0) result.Insert(0, new());
        return result;
    }

    private static List<TimeSignatureChange> NormalizeSignatures(IEnumerable<TimeSignatureChange> source)
    {
        var result = source.GroupBy(x => x.Tick).Select(x => x.Last()).OrderBy(x => x.Tick).ToList();
        if (result.Count == 0 || result[0].Tick != 0) result.Insert(0, new());
        return result;
    }

    private static int? Int(XElement? x) =>
        x is not null && int.TryParse(x.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    private static string? Attr(XElement x, string name) =>
        x.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value;
    private static XElement? El(XElement? x, string name) =>
        x?.Elements().FirstOrDefault(e => e.Name.LocalName == name);
    private static IEnumerable<XElement> Children(XElement? x, string name) =>
        x?.Elements().Where(e => e.Name.LocalName == name) ?? [];
    private static IEnumerable<XElement> Desc(XElement? x, string name) =>
        x?.Descendants().Where(e => e.Name.LocalName == name) ?? [];

    private sealed record TrackKey(string PartId, int Staff);
    private sealed record TieKey(string PartId, int Staff, string Voice, int Pitch);
    private sealed class RawNote(int pitch, long start, long duration, bool chromatic)
    {
        public int Pitch { get; } = pitch;
        public long Start { get; } = start;
        public long Duration { get; set; } = duration;
        public bool Chromatic { get; } = chromatic;
    }
}
