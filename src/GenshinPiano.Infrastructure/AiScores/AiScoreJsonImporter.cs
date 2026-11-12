using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using GenshinPiano.Application.AiScores;
using GenshinPiano.Core.Scores;

namespace GenshinPiano.Infrastructure.AiScores;

/// <summary>Converts the model-independent, quarter-note-based AI score format into the domain model.</summary>
public sealed partial class AiScoreJsonImporter : IAiScoreImporter
{
    public AiScoreImportResult Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw Error("Empty", "$");
        if (json.Length > AiScoreImportLimits.MaxTextLength) throw Error("Limit", "$");
        json = json.Trim().TrimStart('\uFEFF').Trim();
        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            var newline = json.IndexOf('\n');
            var header = newline < 0 ? "" : json[..newline].Trim();
            if (header is not ("```" or "```json") || !json.EndsWith("```", StringComparison.Ordinal) ||
                json.Length <= newline + 4) throw Error("Json", "$");
            json = json[(newline + 1)..^3].Trim();
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            return Convert(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw Error("Json", exception.Path ?? "$");
        }
    }

    private static AiScoreImportResult Convert(JsonElement root)
    {
        CheckObject(root, "$", "format", "version", "title", "author", "bpm", "timeSignature", "complete", "warnings", "measures");
        if (Text(Required(root, "format", "$"), "$.format", 64) != "genshinpiano-score")
            throw Error("Format", "$.format");
        if (Integer(Required(root, "version", "$"), "$.version") != 1) throw Error("Version", "$.version");
        if (!Boolean(Required(root, "complete", "$"), "$.complete")) throw Error("Incomplete", "$.complete");
        var title = Text(Required(root, "title", "$"), "$.title", 256);
        var author = root.TryGetProperty("author", out var authorValue) ? Text(authorValue, "$.author", 256, allowEmpty: true) : "";
        var warnings = new List<AiScoreImportWarning>();
        var omittedWarnings = 0;
        void Warn(string code, string location, string detail = "")
        {
            if (warnings.Count < 200) warnings.Add(new(code, location, detail));
            else omittedWarnings++;
        }

        var bpm = 120d;
        if (root.TryGetProperty("bpm", out var bpmValue) && bpmValue.ValueKind != JsonValueKind.Null)
            bpm = Tempo(bpmValue, "$.bpm");
        else Warn("TempoDefault", "$.bpm");
        if (root.TryGetProperty("warnings", out var modelWarnings))
        {
            CheckArray(modelWarnings, "$.warnings", 100);
            var index = 0;
            foreach (var item in modelWarnings.EnumerateArray())
                Warn("Model", $"$.warnings[{index++}]", Text(item, "$.warnings", 2000));
        }

        var signature = Signature(Required(root, "timeSignature", "$"), "$.timeSignature");
        var timing = new TimingDefinition
        {
            Ppq = 480,
            TempoMap = [new() { Tick = 0, Bpm = bpm }],
            TimeSignatures = [new() { Tick = 0, Numerator = signature.Numerator, Denominator = signature.Denominator }],
        };
        var measures = Required(root, "measures", "$");
        CheckArray(measures, "$.measures", AiScoreImportLimits.MaxMeasures);
        if (measures.GetArrayLength() == 0) throw Error("Empty", "$.measures");

        var tracks = new Dictionary<string, List<NoteEvent>>(StringComparer.Ordinal);
        var ties = new Dictionary<(string Voice, int Pitch), (int Index, Beat End)>();
        var seen = new HashSet<(string Voice, int? Pitch, Beat Start)>();
        var measureVoices = new List<HashSet<string>>();
        var offset = Beat.Zero;
        var noteCount = 0;
        var rounded = false;
        var measureIndex = 0;
        foreach (var measure in measures.EnumerateArray())
        {
            var path = $"$.measures[{measureIndex}]";
            CheckObject(measure, path, "number", "length", "timeSignature", "bpm", "notes");
            if (Integer(Required(measure, "number", path), path + ".number") != measureIndex + 1)
                throw Error("Numbering", path + ".number");

            if (measure.TryGetProperty("timeSignature", out var signatureValue))
            {
                var next = Signature(signatureValue, path + ".timeSignature");
                if (next != signature)
                {
                    signature = next;
                    var tick = offset.Ticks(out var wasRounded);
                    rounded |= wasRounded;
                    if (tick == 0) timing.TimeSignatures.Clear();
                    timing.TimeSignatures.Add(new() { Tick = tick, Numerator = next.Numerator, Denominator = next.Denominator });
                }
            }
            if (measure.TryGetProperty("bpm", out var measureBpm))
            {
                var next = Tempo(measureBpm, path + ".bpm");
                if (next != bpm)
                {
                    bpm = next;
                    var tick = offset.Ticks(out var wasRounded);
                    rounded |= wasRounded;
                    if (tick == 0) timing.TempoMap.Clear();
                    timing.TempoMap.Add(new() { Tick = tick, Bpm = next });
                }
            }
            var nominalLength = new Beat(signature.Numerator * 4, signature.Denominator);
            var length = measure.TryGetProperty("length", out var lengthValue) ? Beat.Parse(lengthValue, path + ".length") : nominalLength;
            if (length <= Beat.Zero || length > nominalLength) throw Error("Range", path + ".length");
            if (length < nominalLength) Warn("ShortMeasure", path);

            var notes = Required(measure, "notes", path);
            CheckArray(notes, path + ".notes", AiScoreImportLimits.MaxNotes);
            if (notes.GetArrayLength() == 0) throw Error("Empty", path + ".notes");
            var intervals = new Dictionary<string, List<(Beat Start, Beat End)>>(StringComparer.Ordinal);
            var noteIndex = 0;
            foreach (var note in notes.EnumerateArray())
            {
                if (++noteCount > AiScoreImportLimits.MaxNotes) throw Error("Limit", path + ".notes");
                var notePath = $"{path}.notes[{noteIndex++}]";
                CheckObject(note, notePath, "voice", "pitch", "start", "duration", "tie", "uncertain");
                var voice = Text(Required(note, "voice", notePath), notePath + ".voice", 64);
                if (!tracks.TryGetValue(voice, out var track))
                {
                    if (tracks.Count >= AiScoreImportLimits.MaxVoices) throw Error("Limit", notePath + ".voice");
                    tracks[voice] = track = [];
                }
                var pitchValue = Required(note, "pitch", notePath);
                var pitch = pitchValue.ValueKind == JsonValueKind.Null ? (int?)null : Pitch(pitchValue, notePath + ".pitch");
                var start = Beat.Parse(Required(note, "start", notePath), notePath + ".start");
                var duration = Beat.Parse(Required(note, "duration", notePath), notePath + ".duration");
                var end = start + duration;
                if (start < Beat.Zero || duration <= Beat.Zero || end > length) throw Error("Bounds", notePath);
                if (!seen.Add((voice, pitch, offset + start))) throw Error("Duplicate", notePath);
                if (!intervals.TryGetValue(voice, out var spans)) intervals[voice] = spans = [];
                spans.Add((start, end));
                if (note.TryGetProperty("uncertain", out var uncertain) && Boolean(uncertain, notePath + ".uncertain"))
                    Warn("Uncertain", notePath);
                var tie = note.TryGetProperty("tie", out var tieValue) ? Text(tieValue, notePath + ".tie", 16) : "none";
                if (tie is not ("none" or "start" or "continue" or "stop")) throw Error("Tie", notePath + ".tie");
                if (pitch is null)
                {
                    if (tie != "none") throw Error("Tie", notePath + ".tie");
                    continue;
                }
                var absoluteStart = offset + start;
                var absoluteEnd = offset + end;
                var startTick = absoluteStart.Ticks(out var roundedStart);
                var endTick = absoluteEnd.Ticks(out var roundedEnd);
                rounded |= roundedStart || roundedEnd;
                if (endTick <= startTick) throw Error("Precision", notePath + ".duration");
                var key = (voice, pitch.Value);
                if (tie is "continue" or "stop")
                {
                    if (!ties.TryGetValue(key, out var previous) || previous.End != absoluteStart)
                        throw Error("Tie", notePath + ".tie");
                    var original = track[previous.Index];
                    track[previous.Index] = original with
                    {
                        DurationTick = endTick - original.StartTick,
                        RhythmTick = endTick - original.StartTick,
                    };
                    if (tie == "stop") ties.Remove(key);
                    else ties[key] = (previous.Index, absoluteEnd);
                }
                else
                {
                    if (ties.ContainsKey(key)) throw Error("Tie", notePath + ".tie");
                    track.Add(new()
                    {
                        Pitch = pitch.Value,
                        StartTick = startTick,
                        DurationTick = endTick - startTick,
                        RhythmTick = endTick - startTick,
                        DurationMode = DurationMode.Explicit,
                    });
                    if (tie == "start") ties[key] = (track.Count - 1, absoluteEnd);
                }
            }
            foreach (var (voice, spans) in intervals)
            {
                var covered = Beat.Zero;
                var gap = false;
                foreach (var span in spans.OrderBy(span => span.Start))
                {
                    if (span.Start > covered) gap = true;
                    if (span.End > covered) covered = span.End;
                }
                if (gap || covered < length) Warn("VoiceGap", path, voice);
            }
            measureVoices.Add(intervals.Keys.ToHashSet(StringComparer.Ordinal));
            offset += length;
            if (!offset.IsWithinBudget) throw Error("Limit", path + ".length");
            measureIndex++;
        }
        if (ties.Count != 0) throw Error("Tie", "$.measures");
        for (var index = 0; index < measureVoices.Count; index++)
            foreach (var voice in tracks.Keys)
                if (!measureVoices[index].Contains(voice)) Warn("MissingVoice", $"$.measures[{index}]", voice);
        var score = new ScoreDocument
        {
            Metadata = new() { Title = title, Author = author },
            Timing = timing,
            Tracks = tracks.Select(pair => new ScoreTrack
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = pair.Key,
                Notes = pair.Value.OrderBy(note => note.StartTick).ThenBy(note => note.Pitch).ToList(),
            }).ToList(),
        };
        if (score.Tracks.Sum(track => track.Notes.Count) == 0) throw Error("Empty", "$.measures");
        if (ScoreValidator.Validate(score).Count != 0) throw Error("Range", "$");
        var scoreEndTick = offset.Ticks(out var roundedScoreEnd);
        if (scoreEndTick > score.Tracks.SelectMany(track => track.Notes).Max(note => note.StartTick + note.DurationTick))
            Warn("TrailingRest", "$.measures");
        if (rounded || roundedScoreEnd) Warn("Rounded", "$.measures");
        if (omittedWarnings > 0) warnings.Add(new("More", "$", omittedWarnings.ToString(CultureInfo.InvariantCulture)));
        return new(score, measureIndex, warnings);
    }

    private static AiScoreImportException Error(string code, string path) => new(code, path);

    private static void CheckObject(JsonElement value, string path, params string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Error("Type", path);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!seen.Add(property.Name)) throw Error("Duplicate", path + "." + property.Name);
            if (!allowed.Contains(property.Name, StringComparer.Ordinal)) throw Error("UnknownField", path + "." + property.Name);
        }
    }

    private static JsonElement Required(JsonElement value, string name, string path) =>
        value.TryGetProperty(name, out var property) ? property : throw Error("Required", path + "." + name);

    private static void CheckArray(JsonElement value, string path, int maximum)
    {
        if (value.ValueKind != JsonValueKind.Array) throw Error("Type", path);
        if (value.GetArrayLength() > maximum) throw Error("Limit", path);
    }

    private static string Text(JsonElement value, string path, int maximum, bool allowEmpty = false)
    {
        if (value.ValueKind != JsonValueKind.String) throw Error("Type", path);
        var text = value.GetString()!.Trim();
        if ((!allowEmpty && text.Length == 0) || text.Length > maximum) throw Error("Range", path);
        return text;
    }

    private static int Integer(JsonElement value, string path) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : throw Error("Type", path);

    private static bool Boolean(JsonElement value, string path) =>
        value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : throw Error("Type", path);

    private static double Tempo(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var bpm) || !double.IsFinite(bpm) || bpm is < 1 or > 1000)
            throw Error("Range", path);
        return bpm;
    }

    private static (int Numerator, int Denominator) Signature(JsonElement value, string path)
    {
        CheckArray(value, path, 2);
        if (value.GetArrayLength() != 2) throw Error("Type", path);
        var numerator = Integer(value[0], path + "[0]");
        var denominator = Integer(value[1], path + "[1]");
        if (numerator is < 1 or > 64 || denominator is < 1 or > 64 || (denominator & (denominator - 1)) != 0)
            throw Error("Range", path);
        return (numerator, denominator);
    }

    private static int Pitch(JsonElement value, string path)
    {
        if (value.ValueKind == JsonValueKind.Number)
        {
            var midi = Integer(value, path);
            return midi is >= 0 and <= 127 ? midi : throw Error("Pitch", path);
        }
        var match = PitchPattern().Match(Text(value, path, 12));
        if (!match.Success) throw Error("Pitch", path);
        var semitone = char.ToUpperInvariant(match.Groups[1].Value[0]) switch
        {
            'C' => 0, 'D' => 2, 'E' => 4, 'F' => 5, 'G' => 7, 'A' => 9, 'B' => 11, _ => 0,
        };
        var accidental = match.Groups[2].Value;
        semitone += accidental switch { "#" or "♯" => 1, "##" => 2, "b" or "♭" => -1, "bb" => -2, _ => 0 };
        var octave = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        var pitch = (octave + 1) * 12 + semitone;
        return pitch is >= 0 and <= 127 ? pitch : throw Error("Pitch", path);
    }

    [GeneratedRegex(@"\A([A-Ga-g])(##|bb|#|b|♯|♭)?(-?[0-9])\z", RegexOptions.CultureInvariant)]
    private static partial Regex PitchPattern();

    private readonly record struct Beat : IComparable<Beat>
    {
        public static Beat Zero => new(0, 1);
        private BigInteger Numerator { get; }
        private BigInteger Denominator { get; }
        public bool IsWithinBudget => Denominator.GetBitLength() <= 4096;

        public Beat(BigInteger numerator, BigInteger denominator)
        {
            var divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
            Numerator = numerator / divisor;
            Denominator = denominator / divisor;
        }

        public static Beat Parse(JsonElement value, string path)
        {
            if (value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number)) throw Error("Number", path);
            var text = value.ValueKind == JsonValueKind.String ? value.GetString()!.Trim() : value.GetRawText();
            if (text.Length is 0 or > 64) throw Error("Number", path);
            var parts = text.Split('/');
            if (parts.Length == 2)
            {
                if (!BigInteger.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var numerator) ||
                    !BigInteger.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var denominator) ||
                    denominator <= 0) throw Error("Number", path);
                return new(numerator, denominator);
            }
            if (parts.Length != 1 || !decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                throw Error("Number", path);
            var bits = decimal.GetBits(number);
            var unsigned = (BigInteger)(uint)bits[0] | ((BigInteger)(uint)bits[1] << 32) | ((BigInteger)(uint)bits[2] << 64);
            return new((bits[3] & int.MinValue) != 0 ? -unsigned : unsigned, BigInteger.Pow(10, (bits[3] >> 16) & 0xff));
        }

        public long Ticks(out bool rounded)
        {
            var result = BigInteger.DivRem(Numerator * 480, Denominator, out var remainder);
            rounded = remainder != 0;
            if (remainder * 2 >= Denominator) result++;
            return checked((long)result);
        }

        public int CompareTo(Beat other) => (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);
        public static Beat operator +(Beat left, Beat right) => new(left.Numerator * right.Denominator + right.Numerator * left.Denominator, left.Denominator * right.Denominator);
        public static bool operator <(Beat left, Beat right) => left.CompareTo(right) < 0;
        public static bool operator >(Beat left, Beat right) => left.CompareTo(right) > 0;
        public static bool operator <=(Beat left, Beat right) => left.CompareTo(right) <= 0;
        public static bool operator >=(Beat left, Beat right) => left.CompareTo(right) >= 0;
    }
}
