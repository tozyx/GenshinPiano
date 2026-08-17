# `.gpiano` 格式 v1

`.gpiano` 是 UTF-8、无 BOM 的 JSON 文件。它是 GenshinPiano 的权威可编辑工程格式，而 MIDI 是导入导出的交换格式。

## 时间单位

所有事件使用整数 tick。`timing.ppq` 表示每个四分音符包含多少 tick。真实播放时间由当前 tick 位置对应的 `tempoMap` 计算，不能直接把 tick 当作毫秒。

## 音高与和弦

`pitch` 使用 MIDI 音高编号 0–127。多个音符拥有相同的 `startTick` 时自然形成和弦。Q、W、E 等游戏按键不是曲谱数据，只在播放映射阶段产生。

## 顶层字段

- `schemaVersion`：格式版本，当前为 `1`
- `metadata`：标题、作者、编曲者和说明
- `timing`：PPQ、速度图和拍号图
- `tracks`：音轨及音符事件
- `playback`：移调、键位方案和超音域策略

## 兼容策略

读取器必须检查 `schemaVersion`。未来版本通过显式迁移器升级旧文件，不能在读取过程中静默丢弃未知数据。
