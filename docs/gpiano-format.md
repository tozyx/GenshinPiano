# `.gpiano` 格式 v1

`.gpiano` 是 UTF-8、无 BOM 的 JSON 文件。它是 GenshinPiano 的权威可编辑工程格式，而 MIDI 是导入导出的交换格式。

## 时间单位

所有事件使用整数 tick。`timing.ppq` 表示每个四分音符包含多少 tick。真实播放时间由当前 tick 位置对应的 `tempoMap` 计算，不能直接把 tick 当作毫秒。

## 音高与和弦

`pitch` 使用 MIDI 音高编号 0–127。多个音符拥有相同的 `startTick` 时自然形成和弦。Q、W、E 等游戏按键不是曲谱数据，只在播放映射阶段产生。

## 音符时长

每个音符包含以下时长字段：

- `startTick`：按下位置。
- `durationTick`：最终实际保持的 tick 数，文件中始终保存一个大于零的具体值。
- `rhythmTick`：可选的记谱节奏跨度。休止符存在时，它可以与下一音起点间隔不同。
- `durationMode`：`explicit` 表示完全采用 `durationTick`；`auto` 表示根据节奏和演奏法重新计算。
- `articulation`：自动时长的演奏法，可为 `legato`、`natural`、`detached`、`staccato` 或 `custom`。
- `gateRatio`：可选的实际发声比例，范围为 0.10–0.95。填写后优先于 `articulation`，例如 `0.67` 表示保持节奏跨度的 67%。

四种快捷规则分别使用节奏跨度的 95%、80%、50% 和 30%，其他比例使用 `custom`。编辑器把比例限制在 10%–95%，避免零时长、负时长和音符严重重叠。存在 `rhythmTick` 时优先使用它；否则使用同音轨下一组音符的起点间隔；末尾音符默认使用一拍。序列化器会把自动计算结果写回 `durationTick`，使文件在不支持自动规则的软件中仍能按最终时长播放。

旧文件缺少 `durationMode` 时默认解析为 `explicit`，因此原有 `durationTick` 不会被自动覆盖。已有自动时长文件缺少 `gateRatio` 时仍采用 `articulation` 对应的四种比例。旧版 `.GenshinPiano` 转换结果显式写入 `gateRatio: 0.8`，与原先的 `natural` 规则等价。

## 顶层字段

- `schemaVersion`：格式版本，当前为 `1`
- `metadata`：标题、作者、编曲者和说明
- `timing`：PPQ、速度图和拍号图
- `tracks`：音轨及音符事件
- `playback`：移调、键位方案和超音域策略

## 兼容策略

读取器必须检查 `schemaVersion`。未来版本通过显式迁移器升级旧文件，不能在读取过程中静默丢弃未知数据。
