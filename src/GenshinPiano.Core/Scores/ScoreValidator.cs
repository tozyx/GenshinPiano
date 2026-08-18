namespace GenshinPiano.Core.Scores;

public static class ScoreValidator
{
    public static IReadOnlyList<string> Validate(ScoreDocument score)
    {
        ArgumentNullException.ThrowIfNull(score);

        var errors = new List<string>();

        if (score.SchemaVersion != ScoreDocument.CurrentSchemaVersion)
        {
            errors.Add($"不支持曲谱版本 {score.SchemaVersion}，当前版本为 {ScoreDocument.CurrentSchemaVersion}。");
        }

        if (string.IsNullOrWhiteSpace(score.Metadata.Title))
        {
            errors.Add("曲谱标题不能为空。");
        }

        if (score.Timing.Ppq is < 24 or > 9600)
        {
            errors.Add("PPQ 必须在 24 到 9600 之间。");
        }

        foreach (var tempo in score.Timing.TempoMap)
        {
            if (tempo.Tick < 0 || tempo.Bpm is < 1 or > 1000)
            {
                errors.Add("速度事件包含无效的 tick 或 BPM。");
            }
        }

        foreach (var signature in score.Timing.TimeSignatures)
        {
            if (signature.Tick < 0 || signature.Numerator <= 0 || !IsPowerOfTwo(signature.Denominator))
            {
                errors.Add("拍号事件包含无效的 tick、分子或分母。");
            }
        }

        var duplicateTrackIds = score.Tracks
            .GroupBy(track => track.Id, StringComparer.Ordinal)
            .Where(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicateTrackIds.Length > 0)
        {
            errors.Add("音轨 ID 不能为空或重复。");
        }

        foreach (var note in score.Tracks.SelectMany(track => track.Notes))
        {
            if (note.Pitch is < 0 or > 127)
            {
                errors.Add($"音符 {note.Id} 的 MIDI 音高超出 0 到 127。" );
            }

            if (note.StartTick < 0 || note.DurationTick <= 0)
            {
                errors.Add($"音符 {note.Id} 的起始位置或时长无效。" );
            }

            if (note.RhythmTick is <= 0)
            {
                errors.Add($"音符 {note.Id} 的节奏时值必须大于零。" );
            }

            if (note.GateRatio is < NoteDurationCalculator.MinimumGateRatio or
                > NoteDurationCalculator.MaximumGateRatio)
            {
                errors.Add($"音符 {note.Id} 的持续比例必须在 10% 到 95% 之间。" );
            }

            if (note.DurationMode == DurationMode.Auto &&
                note.Articulation == NoteArticulation.Custom &&
                note.GateRatio is null)
            {
                errors.Add($"音符 {note.Id} 使用自定义演奏法时必须指定持续比例。" );
            }

            if (note.Velocity is < 1 or > 127)
            {
                errors.Add($"音符 {note.Id} 的力度超出 1 到 127。" );
            }
        }

        return errors;
    }

    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;
}
