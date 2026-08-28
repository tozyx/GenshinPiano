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

        if (score.Metadata is null)
        {
            errors.Add("曲谱 metadata 不能为空。");
        }
        else if (string.IsNullOrWhiteSpace(score.Metadata.Title))
        {
            errors.Add("曲谱标题不能为空。");
        }

        if (score.Timing is null)
        {
            errors.Add("曲谱 timing 不能为空。");
        }
        else
        {
            if (score.Timing.Ppq is < 24 or > 9600)
            {
                errors.Add("PPQ 必须在 24 到 9600 之间。");
            }

            if (score.Timing.TempoMap is null)
            {
                errors.Add("曲谱 tempoMap 不能为空。");
            }
            else
            {
                foreach (var tempo in score.Timing.TempoMap)
                {
                    if (tempo is null || tempo.Tick < 0 || tempo.Bpm is < 1 or > 1000)
                    {
                        errors.Add("速度事件包含无效的 tick 或 BPM。");
                    }
                }
            }

            if (score.Timing.TimeSignatures is null)
            {
                errors.Add("曲谱 timeSignatures 不能为空。");
            }
            else
            {
                foreach (var signature in score.Timing.TimeSignatures)
                {
                    if (signature is null || signature.Tick < 0 || signature.Numerator <= 0 ||
                        !IsPowerOfTwo(signature.Denominator))
                    {
                        errors.Add("拍号事件包含无效的 tick、分子或分母。");
                    }
                }
            }
        }

        if (score.Playback is null)
        {
            errors.Add("曲谱 playback 不能为空。");
        }
        else if (score.Playback.Transpose is < -127 or > 127)
        {
            errors.Add("播放移调必须在 -127 到 127 个半音之间。");
        }

        if (score.Tracks is null)
        {
            errors.Add("曲谱 tracks 不能为空。");
            return errors;
        }

        var validTracks = score.Tracks.Where(track => track is not null).ToArray();
        if (validTracks.Length != score.Tracks.Count)
        {
            errors.Add("曲谱包含空音轨。");
        }

        var duplicateTrackIds = validTracks
            .GroupBy(track => track!.Id, StringComparer.Ordinal)
            .Where(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicateTrackIds.Length > 0)
        {
            errors.Add("音轨 ID 不能为空或重复。");
        }

        foreach (var track in validTracks)
        {
            if (track!.Notes is null)
            {
                errors.Add($"音轨 {track.Id} 的 notes 不能为空。");
                continue;
            }

            foreach (var note in track.Notes)
            {
                if (note is null)
                {
                    errors.Add($"音轨 {track.Id} 包含空音符。");
                    continue;
                }

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
        }

        return errors;
    }

    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;
}
