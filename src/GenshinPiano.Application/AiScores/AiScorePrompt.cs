namespace GenshinPiano.Application.AiScores;

public static class AiScorePrompt
{
    public const string ExampleJson = """
        {
          "format": "genshinpiano-score",
          "version": 1,
          "title": "Example",
          "author": "",
          "bpm": 120,
          "timeSignature": [4, 4],
          "complete": true,
          "warnings": [],
          "measures": [
            {
              "number": 1,
              "notes": [
                {"voice": "right", "pitch": "C4", "start": "0", "duration": "1"},
                {"voice": "left", "pitch": "C3", "start": "0", "duration": "4"},
                {"voice": "right", "pitch": "E4", "start": "1", "duration": "1"},
                {"voice": "right", "pitch": null, "start": "2", "duration": "2"}
              ]
            }
          ]
        }
        """;

    public static string Create(bool english) => (english ? """
        Transcribe the attached score image(s) into GenshinPiano's AI score JSON format. Read the actual image, not a memorized version of the song. Return one complete JSON object, without explanations or code fences. The example below illustrates the format; do not copy its notes unless they occur in the image.

        Rules:
        - Use exactly format="genshinpiano-score", version=1. Include title, bpm, timeSignature, complete, warnings, and measures. author is optional.
        - bpm is a number from 1 to 1000, or null when unknown (the application then uses 120 and shows a warning). timeSignature is [numerator, denominator].
        - All start, duration, and length values use QUARTER NOTES, independent of the time signature: in 6/8 a full measure has length 3. Use integer/decimal numbers or exact fraction strings such as "1/3"; prefer fractions for tuplets.
        - Number measures consecutively from 1 in playback order. Expand repeats only when their order is clear; otherwise set complete=false and explain in warnings. Do not silently omit pages, systems, accompaniment, or measures.
        - Each measure contains number and notes. Optional timeSignature/bpm changes apply at its start. Optional length specifies an actual shorter measure (pickup/final partial bar); omit it for a full bar. Never invent a length to conceal missing music.
        - Each note/rest contains voice, pitch, start, duration. start is relative to that measure, begins at 0, and duration is positive; events must remain within the measure. Order events chronologically.
        - Use stable voice names such as right, left, right-2. Cover each voice's entire measure using explicit rests, including full-bar rests in inactive measures. Chord notes share start and voice; do not add their simultaneous durations together.
        - pitch is an absolute MIDI integer 0..127 or a scientific pitch such as C4, F#4, Bb3 (C4 = MIDI 60). Apply clefs, key signatures, accidentals, and octave signs. Never map pitches to game keys or remove chromatic notes.
        - pitch=null means a genuine written rest, NOT an unreadable note. If a pitch or section cannot be determined, set complete=false and explain; never silently skip it or replace it with a rest.
        - Optional tie is "start", "continue", or "stop" for an actual same-pitch tie, keeping the same voice and contiguous timing. Do not use ties for slurs. Omit tie on other notes.
        - Optional uncertain=true flags a tentative reading for review. Put uncertainties and inferred information in warnings (array of strings). complete=true means every requested image/section has been transcribed; it does not certify accuracy.
        - Do not output IDs, ticks, velocity, lyrics, extra fields, or comments. Preserve the source's pitches, rhythms, and voices. If the image cannot be transcribed reliably, return complete=false rather than fabricate a finished score.

        Format example:
        """ : """
        请把随附曲谱图片逐项转写为 GenshinPiano 的 AI 识谱 JSON。根据实际图片读取，不要凭歌曲记忆补写。只返回一个完整 JSON 对象，不附解释或代码围栏。下面的例子仅说明格式，不能把示例音符当作识别结果。

        规则：
        - format 固定为 "genshinpiano-score"，version 固定为 1。包含 title、bpm、timeSignature、complete、warnings、measures；author 可选。
        - bpm 为 1–1000 的数字；无法确定时填 null（软件会使用 120 并提示）。timeSignature 是 [分子, 分母]。
        - start、duration、length 全部以“四分音符”为单位，不随拍号改变：6/8 一小节长度是 3。可用整数、小数或 "1/3" 等精确分数字符串；连音组优先使用分数。
        - 按实际播放顺序从 1 连续编号小节。反复顺序明确时展开；不明确时 complete=false，并在 warnings 解释。不得默默遗漏页面、谱行、伴奏或小节。
        - 每小节包含 number、notes。可选 timeSignature/bpm 在该小节开始时生效。弱起或结尾不完整小节可用 length 指明实际较短长度；完整小节不填。不要通过缩短小节掩盖漏识别。
        - 每个音符/休止包含 voice、pitch、start、duration。start 是小节内从 0 开始的位置，duration 大于 0，事件不得越过小节。事件按开始位置排序。
        - voice 使用 right、left、right-2 等稳定名称。各声部应以明确休止覆盖完整小节，尚未进入的声部写全小节休止。和弦音共用 start、voice，同时发生的时值不能相加。
        - pitch 是绝对 MIDI 整数 0–127，或 C4、F#4、Bb3 等科学音高（C4=MIDI 60）。应用谱号、调号、临时升降号、八度记号。不要映射成游戏按键或删除半音。
        - pitch=null 只表示真正的休止，不表示看不清的音。无法确定音高或段落时 complete=false 并解释，不得跳过或改成休止。
        - 真正同音高延音线可使用 tie="start"/"continue"/"stop"，声部相同且时间连续；连奏弧线不能当延音线。其他音符省略 tie。
        - 可用 uncertain=true 标记需要核对的暂定读数；warnings 是不确定或推定信息的字符串数组。complete=true 表示已转写所有要求的图片/段落，不代表准确率保证。
        - 不输出 ID、tick、力度、歌词、额外字段或注释。保留原始音高、节奏与声部。图片无法可靠识读时返回 complete=false，不要编造完整结果。

        格式示例：
        """) + Environment.NewLine + ExampleJson;
}
