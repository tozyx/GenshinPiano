# `.gpiano` 格式 v1

[简体中文](#简体中文) | [English](#english)

## 简体中文

`.gpiano` 是 UTF-8、无 BOM 的 JSON 文件。它是 GenshinPiano 的权威可编辑工程格式，而 MIDI 是导入导出的交换格式。

### 时间单位

所有事件使用整数 tick。`timing.ppq` 表示每个四分音符包含多少 tick。真实播放时间由当前 tick 位置对应的 `tempoMap` 计算，不能直接把 tick 当作毫秒。

### 音高与和弦

`pitch` 使用 MIDI 音高编号 0–127。多个音符拥有相同的 `startTick` 时自然形成和弦。Q、W、E 等游戏按键不是曲谱数据，只在播放映射阶段产生。

### 音符时长

每个音符包含以下时长字段：

- `startTick`：按下位置。
- `durationTick`：最终实际保持的 tick 数，文件中始终保存一个大于零的具体值。
- `rhythmTick`：可选的记谱节奏跨度。休止符存在时，它可以与下一音起点间隔不同。
- `durationMode`：`explicit` 表示完全采用 `durationTick`；`auto` 表示根据节奏和演奏法重新计算。
- `articulation`：自动时长的演奏法，可为 `legato`、`natural`、`detached`、`staccato` 或 `custom`。
- `gateRatio`：可选的实际发声比例，范围为 0.10–0.95。填写后优先于 `articulation`，例如 `0.67` 表示保持节奏跨度的 67%。

四种快捷规则分别使用节奏跨度的 95%、80%、50% 和 30%，其他比例使用 `custom`。编辑器把比例限制在 10%–95%，避免零时长、负时长和音符严重重叠。存在 `rhythmTick` 时优先使用它；否则使用同音轨下一组音符的起点间隔；末尾音符默认使用一拍。序列化器会把自动计算结果写回 `durationTick`，使文件在不支持自动规则的软件中仍能按最终时长播放。

旧文件缺少 `durationMode` 时默认解析为 `explicit`，因此原有 `durationTick` 不会被自动覆盖。已有自动时长文件缺少 `gateRatio` 时仍采用 `articulation` 对应的四种比例。旧版 `.GenshinPiano` 转换结果显式写入 `gateRatio: 0.8`，与原先的 `natural` 规则等价。

### 顶层字段

- `schemaVersion`：格式版本，当前为 `1`
- `metadata`：标题、作者、编曲者和说明
- `timing`：PPQ、速度图和拍号图
- `tracks`：音轨及音符事件
- `playback`：移调、键位方案和超音域策略

### 版本与兼容

当前格式版本为 `1`。读取流程先解析 JSON 并检查 `schemaVersion`，再按顺序迁移、反序列化、校验曲谱，最后计算自动音符时长。缺少版本号的早期文件按版本 `0` 处理，迁移器补齐基础对象及数组结构后进入版本 `1`。

高于当前支持版本的文件会被拒绝，避免旧程序写回时丢弃新字段。保存时始终写入当前 schema 版本；格式版本与软件发布版本独立。

具有安全默认值的可选字段通常可保持当前版本；改变字段含义、删除或重命名字段、引入必填值或改变结构时，需要明确的逐版本迁移。迁移仅依赖文件内容，不依赖界面语言、联网或机器路径，并保留中间步骤以支持跨多个版本升级。

## English

A `.gpiano` file is UTF-8 JSON without a BOM. It is GenshinPiano's authoritative editable project format; MIDI is an interchange format.

### Time units

All events use integer ticks. `timing.ppq` defines the number of ticks per quarter note. Real playback time is calculated from the `tempoMap` at each tick position; ticks are not milliseconds.

### Pitches and chords

`pitch` is a MIDI note number from 0 to 127. Notes sharing a `startTick` naturally form a chord. Game keys such as Q, W, and E are not stored as score data; they are produced during playback mapping.

### Note durations

Each note has these timing fields:

- `startTick`: note-on position.
- `durationTick`: the final key-hold duration in ticks, always stored as a concrete positive value.
- `rhythmTick`: an optional notated rhythmic span. With rests, it can differ from the gap to the next note.
- `durationMode`: `explicit` uses `durationTick` directly; `auto` recalculates it from rhythm and articulation.
- `articulation`: the automatic-duration rule, one of `legato`, `natural`, `detached`, `staccato`, or `custom`.
- `gateRatio`: an optional sounding proportion from 0.10 to 0.95. It overrides `articulation`; for example, `0.67` holds a note for 67% of its rhythmic span.

The four presets use 95%, 80%, 50%, and 30% of the rhythmic span. Other proportions use `custom`. The editor limits ratios to 10%–95% to avoid zero/negative durations and excessive note overlap. Duration calculation prefers `rhythmTick` when present; otherwise it uses the gap to the next group of notes in the same track. The final note defaults to one beat. The serializer materializes the result into `durationTick` so software without automatic-duration rules can still play the final duration.

Older files without `durationMode` default to `explicit`, preserving their existing `durationTick` values. Automatic-duration files without `gateRatio` use the four `articulation` presets. Converted legacy `.GenshinPiano` scores explicitly store `gateRatio: 0.8`, equivalent to the `natural` rule.

### Top-level fields

- `schemaVersion`: file-format version, currently `1`.
- `metadata`: title, author, arranger, and description.
- `timing`: PPQ, tempo map, and time signatures.
- `tracks`: tracks and note events.
- `playback`: transposition, key layout, and out-of-range policy.

### Versioning and compatibility

The current format version is `1`. Loading parses JSON and checks `schemaVersion`, then applies migrations in order, deserializes, validates the score, and materializes automatic durations. Early files without a version are treated as version `0`; migration supplies the basic object/array structures before advancing to version `1`.

Files newer than the supported schema are rejected to prevent an older application from discarding new fields when saving. Saved files always use the current schema version. File-format versions are independent of application releases.

Optional fields with safe defaults generally do not require a new version. Changes to field meanings, renamed/removed fields, new required values, or structural changes need explicit sequential migrations. A migration depends only on file content, not UI language, network access, or machine paths. Intermediate steps are retained so users can upgrade across multiple versions.
