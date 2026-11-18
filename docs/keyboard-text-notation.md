# 键盘文本谱与数字按键谱 / Keyboard text notation

[简体中文](#简体中文) | [English](#english)

## 简体中文

### 这是什么格式

B 站原神演奏作者发布的“键盘谱”“手机谱”“数字谱”，常用纯文本记录按键顺序与组合。它们属于作者约定的按键记谱方式，没有统一文件规范；不能仅凭“数字谱”三个字就按普通数字简谱解析。

以下说明针对原神三排、每排七键的谱例，不涵盖其他游戏的键位编号。

### 来源与写法差异

- [是星璃啦：《Lemon》原神琴谱](https://www.bilibili.com/opus/859289537085440004)：正文同时提供字母和数字文本。圆括号表示同时弹，方括号表示从左到右快速弹；空格用于提示停顿，斜杠用于节拍分组。作者没有将这些空格定义为固定拍数，也没有将每个斜杠都定义为小节线。
- [与君歌一曲い：《夏天的风》原神琴谱](https://www.bilibili.com/opus/728595907205922856)：正文同时提供两种文本。圆括号表示同时按，花括号表示错开起音的琶音；竖线是小节线，空格提示间隔，无空格时连续按。
- [旋律透明：《JOJO 黄金之风处刑曲》](https://www.bilibili.com/video/BV15Y411M7YC/)：视频简介提供字母谱、手机谱和符号解释。圆括号表示同时点击，英文方括号表示快速连点，中文括号 `〖〗` 表示琶音，斜杠用于分小节。
- [指尖灬旋律丿：《蒙德城繁忙的午后》](https://www.bilibili.com/video/BV1ck4y1H7eY/)：简介提供 TXT 网盘链接，并说明数字前缀表示键位所在的排，圆括号表示同时按。该链接所指向的文件未下载核验；下面单独说明用户提供的两份 TXT，不能据此认定它们属于这个视频的曲谱。

上述规则来自公开正文、简介及搜索索引，不代表观看视频逐音校验的结果。部分视频直链抓取受限；旋律透明的规则来自该视频的搜索索引，指尖灬旋律丿的简介通过 [B 站列表页](https://www.bilibili.com/list/76052941?bvid=BV1ck4y1H7eY&oid=742291501)读取。作者可能调整说明，实际导入时应以对应谱子的说明为准。

### 常见音高与组合规则

在上面的三排七键约定中，数字从左到右编号；`+`、`-` 是**紧随其后的一个数字**的排前缀，不是升降号，也不持续作用于后续数字。

```text
上排：Q W E R T Y U  <=>  +1 +2 +3 +4 +5 +6 +7
中排：A S D F G H J  <=>   1  2  3  4  5  6  7
下排：Z X C V B N M  <=>  -1 -2 -3 -4 -5 -6 -7
```

以下均为自行编写的语法示例，不摘自歌曲：

```text
QAW       <=> +11+2       顺序按 Q、A、W
(ZAE)     <=> (-11+3)     三个键同时按
[QEW]     <=> [+1+3+2]    按方括号约定快速依次按
{ZDG}     <=> {-135}      按花括号约定错开起音
〖ZDG〗   <=> 〖-135〗    采用另一作者的琶音符号
```

`+11` 应拆成 `+1`、`1`；`-135` 应拆成 `-1`、`3`、`5`。括号内多个数字不能合并成一个多位键号。`[]`、`{}`、`〖〗` 的解释需要匹配作者说明，不能无条件互换。

### 节奏信息的限制

这些文本往往能确定“按什么”和“大致先后”，却不能唯一确定 BPM、拍号、每个音的时值和松键时间。空格可能是间隔提示，也可能混入排版空白；换行往往只用于分段。不能把一个空格自动解释为一拍，或把 `/` 一律解释为一小节。

数字前的 `-` 是排前缀，与普通简谱中的延长线不是同一种符号。这里也没有足够依据为所有作者统一规定 `0`、附点、连音线、升降号或反复记号的含义。

### 指尖 TXT 的结构与转换

仓库中的 [指尖-键盘谱.txt](../samples/keyboard-text/指尖-键盘谱.txt) 与 [指尖-数字谱.txt](../samples/keyboard-text/指尖-数字谱.txt) 是用户从网盘复制的原文。两份文件均为 UTF-8、无 BOM，没有曲名、BPM、拍号或符号说明。它们不包含反斜杠 `\`，所以不能沿用其他粘贴谱例中“反斜杠分小节”的推测。

按前三排键位映射，并保留圆括号同时按、斜杠分组和空分组，两份文件的全部 78 行谱文逐项一致：共有 360 个起音事件、485 个音符。这里一个和弦算一个起音事件，和弦内每个按键各算一个音符；没有发现未知按键或未配对括号。

前三段分别有 8、8、48 行，每行按 `/` 拆分后恰好四组，包括空组。第四段有 14 行：11 行有六个分隔符并以 `/` 结尾，2 行有三个分隔符并以 `/` 结尾，最后一行只有 `Q`（数字版为 `+1`）。第四段的行尾 `/` 若视为关闭最后一组，便是十一行六组、两行三组及一个结束音；这是一种结构候选，尚不是已确认的节奏。

这意味着转换必须区分三件事：

1. **分组与小节不是同一概念。** 前三段适合先预览为四组一行；第四段可能涉及拍号变化，也可能只是改为一行排多个小节。六组可能对应一个六拍小节，也可能是两个三拍小节。还必须确认一组对应四分音符还是八分音符，不能自动定成 `6/4` 或 `6/8`。
2. **空组必须保留，行尾分隔符需要按段落解释。** 第 23 行 `(XS) /(BG)(QT)/     /` 拆分为四组，后两组为空。第 101 行 `(ZA) /     /B  /W  /     /Z   /` 的中间空组也必须保留，而最后一个分隔符可能只是关闭第六组。无条件删除所有末尾空组，或无条件增加一个空组，都会改变时间线。
3. **组内均分是默认规则，不是原文件明确给出的时值。** 第 15 行第一组 `(CD)G` 有两次起音；若一组为一拍，可先各分半拍。第 97、99 行出现四次起音的组，均分时各占四分之一组。空格不能按字符个数折算成拍数；它们仍应保留在原文预览中，供人工判断是否暗示不等分节奏。

建议转换流程为：

```text
原文与行号
  → 字母／数字词法解析
  → 段落、分组、顺序起音、同时按组合、空组
  → 用户确认每组长度、段落拍号、行尾分隔符规则、BPM
  → 起音位置与记谱跨度
  → ScoreDocument
  → 编辑器校对并保存为 .gpiano
```

在 PPQ 为 480 时，若确认一组为四分音符，则一组长 480 tick，两个均分起音各占 240 tick，四个各占 120 tick；若一组是八分音符，这些值相应减半。组长为 `L`、组内有 `N` 次起音时，第 `i` 次起音的边界取 `round(i × L / N)`，再用相邻边界之差计算跨度，避免逐项取整造成累积偏移。圆括号内音符共用起点；空组仅推进时间，不生成音符。

建议将组内分配的跨度写入 `RhythmTick`，实际发声采用现有自动时长规则，例如 `DurationMode.Auto`、`GateRatio = 0.8`。这样不会仅因后续空组而自动延长前一个音。结束音的时值缺失，应显示默认值并允许用户修改。

“导入 → 键盘／数字文本谱”支持粘贴和 TXT 文件，展示原文及段落设置。选择“指尖灬旋律丿”规则，确认 BPM、每组长度及各段拍号；第四段的行宽变化和短行会在预览中提示。导入器直接生成 `ScoreDocument`，无需 OCR、大模型或新增运行时，也不必先转换成 AI JSON。导入后进入 21 键卷帘，保存为新的 `.gpiano`，不会覆盖原始 TXT。

### 与本项目的关系

本项目的 `GenshinKeyMap` 已使用同一套三排七键映射，因此基本按键、数字排前缀和同时按组合可以直接对应到音高。当前旧版 `.GenshinPiano` 导入器要求“和弦按键 + 时值代码”，例如 `ADG 4`；这里介绍的 B 站文本谱不能直接当作该格式导入。

导入时的兼容规则：

1. 默认自动识别，也可手动选择 UP 主规则。检测会校验整份谱文；唯一语法匹配才自动继续，多种匹配则显示候选，必须手动选择。指尖、是星璃啦将 `/` 视为节奏分组；与君歌一曲将 `|` 视为小节线；旋律透明将 `/` 视为小节线。快速连弹、琶音括号也按所选规则限制，不能混用。
2. 字母与带排前缀的数字按键可以自动区分；混合两种表示、普通数字简谱和旧版时值代码不兼容。支持全角括号、分隔符、正负号、数字及小写键名，不支持嵌套括号。网页复制的 `&#x20;`、`&#32;`、`&nbsp;` 等空格实体按空白处理，空组仍保留；独立反斜杠和行尾反斜杠可作为排版换行。
3. “第一段：”“前奏：”“主歌：（DAOKO）”“A段：”“C段：（米津玄师、DAOKO）”和 `Section 1:`、`Verse 1:` 等标题分出可独立设置的段落，名字中的字母、数字不会被当作音符。连续标题合并，例如“主歌”后紧接“A段”。速度、拍号和组长仍由用户确认；没有精确时值时均分估算。修改输入或设置后必须重新校验，并确认提示后才能导入。
4. 默认把空格作为排版；“空白块作休止”选项可将组内一段空白分配为一个休止槽。空分组始终推进时间，行尾分隔符可按段落显式覆盖。短行不自动补齐，未知符号报告原文行列，不静默忽略。
5. `[]` 快速连弹在自身槽长内均分；`{}` / `〖〗` 琶音在槽长前 20% 内依次起音，共用节奏结束位置。这些是可编辑的估算，不能视为原作者精确时值。

独立说明行可以留在原文中：支持“说明：”“备注：”“曲名：”、`Note:`、`Title:` 和 `#` 等明确前缀，以及以中文开头且不含谱面分隔符的独立文字。预览会列出被跳过的行号及内容。说明不会生成休止或改变节奏，也不会自动将文字中的 BPM、拍号应用到设置。谱行内部的说明、未知字符仍报错；未标记的英文说明建议加 `Note:` 前缀。请检查预览中的跳过提示，避免遗漏本来需要演奏的内容。

自动识别匹配的是已支持的记谱规则，不是作者身份。多数行分为四组且行尾常不带斜杠时，会建议指尖规则；排版只是辅助依据，不会触发自动选择。只有按键、圆括号、斜杠的谱文，以及 `[]` 同时适用于多个规则的谱文，仍需人工确认。BPM、拍号、组长和演奏时值不会由格式检测自动推断。

样例与原文见 [样例说明](../samples/keyboard-text/README.md)，操作步骤见 [用户指南](USER_GUIDE.md)。不明来源或符号约定不同的谱文需要先核对说明，不能仅按作者名字保证兼容。

## English

### Format and sources

Genshin keyboard sheets shared on Bilibili often encode key sequences and simultaneous key presses as plain text. Their numeric versions commonly describe positions on three rows of seven keys. They are author-defined conventions rather than a standard file format or conventional numbered musical notation.

The public sources above illustrate several variants:

- **是星璃啦, Lemon:** parentheses mean simultaneous presses, square brackets mean a rapid left-to-right sequence, spaces suggest pauses, and slashes separate rhythmic groups.
- **与君歌一曲い, 夏天的风:** parentheses mean simultaneous presses, braces mean staggered arpeggios, and vertical bars mark measures.
- **旋律透明, Golden Wind:** parentheses mean simultaneous presses, square brackets mean rapid sequences, `〖〗` marks arpeggios, and slashes separate measures.
- **指尖灬旋律丿, Bustling Afternoon of Mondstadt:** the description explains row prefixes and simultaneous presses and links to external TXT files. That download was not inspected; the user-supplied TXT files described below cannot be attributed to this particular video without additional evidence.

These observations come from public articles, descriptions, and indexed text, without note-by-note video verification. Direct access to some video pages was restricted; the Golden Wind description was available through its search index, and the Mondstadt description through the linked Bilibili list page. Follow the individual sheet's legend when interpreting symbols.

### Keys and timing

The mapping is `QWERTYU` → `+1 … +7`, `ASDFGHJ` → `1 … 7`, and `ZXCVBNM` → `-1 … -7`. Each prefix applies only to the following digit: `+11` means `Q`, then `A`, not a multi-digit key number. A minus prefix selects the lower row; it is not a duration extension or a flat.

Parenthesized keys sound together. Other brackets, spaces, line breaks, and separators require the author's convention. Spaces do not establish exact beat lengths, and a slash does not universally mean a measure. BPM, meter, durations, and release times may be absent.

### User-supplied 指尖 TXT files

The linked key and numeric files contain identical musical tokens across all 78 notation rows after applying the row mapping: 360 attack events and 485 individual notes. Both files are UTF-8 without a BOM, contain no backslashes, and provide no title, tempo, meter, or notation legend. All keys and parentheses parsed successfully.

The first three sections contain 8, 8, and 48 rows, each split into four slash-separated groups including empty groups. The fourth section has eleven rows with six slashes, two with three slashes, and a final single note. All slash-containing rows in that section end in a slash. Treating that last slash as a closing delimiter yields six- and three-group rows, but this interpretation is provisional. Six groups could describe one measure or multiple measures, and each group could represent a quarter or eighth note. Neither meter nor tempo can be established from the layout alone.

A parser must preserve empty groups and source locations. It must not universally remove or add an empty group after a trailing slash: section two's last row contains meaningful empty positions under the four-group interpretation. Spaces must remain visible for review rather than being counted as fixed-duration units.

A practical import flow is text → tokens → sections and groups → user-confirmed timing → `ScoreDocument`. Equally dividing each group's duration among its attack events is a useful editable default, not a verified transcription of the original rhythm. At PPQ 480, a quarter-note group spans 480 ticks; two attacks receive 240 ticks each, or four receive 120 ticks each. Round cumulative boundaries rather than individual durations to avoid timing drift. Chord members share their start tick, and empty groups advance the timeline without adding notes.

Store rhythmic spans in `RhythmTick` and use the existing automatic-duration calculation for sounding lengths. Ask users to confirm tempo, group length, meter by section, trailing-delimiter interpretation, and the final note's duration. A dedicated text importer can create the existing domain model directly without OCR, AI, additional runtimes, or intermediate AI JSON. Reuse workspace import and unsaved-change handling, and save the result as `.gpiano` rather than overwriting the input TXT.

### Application compatibility

The existing `GenshinKeyMap` matches these keys, but the legacy `.GenshinPiano` importer expects chord-and-duration records such as `ADG 4`. Use the dedicated **Import → Keyboard / numeric text score** dialog for these sheets, not the legacy importer.

Automatic detection validates the complete source against all supported grammars. A unique match resolves automatically; multiple matches require explicit selection. Layout can suggest Zhijian for predominantly four-group rows without trailing slashes, but never resolves shared timing semantics by itself. Detection matches conventions, not creator identities, and does not establish BPM, meter, group length, or performance durations.

Zhijian/Xingli use slash-separated rhythm groups, Yujun uses vertical-bar measures, and Touming uses slash-separated measures. Bracket meanings are restricted to that convention. Letter/numeric representation is detected; mixed versions and legacy duration codes are rejected. Full-width punctuation/numbers and lowercase keys are normalized; nested brackets remain unsupported. Copied HTML space entities such as `&#x20;`, `&#32;`, and `&nbsp;` are treated as whitespace without losing empty groups. Standalone and trailing backslashes are accepted as formatting line breaks.

Analyze sections, confirm BPM, meters, group lengths, expected row widths, and trailing separators, then validate and acknowledge the preview. Chinese numbered headings, common song labels such as 前奏/主歌/副歌/间奏/结尾, lettered sections such as A段, and English labels such as `Intro:`, `Verse 1:`, and `Section 1:` create independently configurable sections. Labels may include singer credits; consecutive labels are combined. Letters and numbers in labels are excluded from notation detection. Input/settings changes invalidate earlier previews. Empty groups advance time without adding notes; short rows continue without padding. Optional internal whitespace runs become single rest slots. Error locations identify the original line and column.

Standalone explanatory text can remain in the source. Explicit prefixes such as 说明：, 备注：, 曲名：, `Note:`, `Title:`, or `#` are recognized, as is Chinese prose that starts with a Chinese character and contains no score separators. The preview lists skipped text with its original line number. Such text adds no duration and does not automatically apply tempo or meter settings. Inline prose and invalid musical tokens still produce errors. Prefix other English explanations with `Note:` and review all skipped-line warnings before importing.

Rapid bracket sequences evenly subdivide their own slot; arpeggios stagger over its first 20% and share its rhythmic end. All such timing is an editable estimate. Imported results open in the 21-key roll and are saved as new `.gpiano` projects without overwriting the TXT.

See the [sample notes](../samples/keyboard-text/README.md) for a short authentic excerpt in both notations. The syntax examples in the Chinese section are newly written illustrations, not excerpts from songs.
