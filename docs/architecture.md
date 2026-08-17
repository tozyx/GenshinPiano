# v3 架构

## 设计目标

GenshinPiano v3 采用 Windows 专用的 C#/.NET 10 LTS 与 WPF 技术栈。曲谱数据与界面、文件格式和按键输出解耦，避免旧版本中窗口、解析和播放逻辑相互依赖的问题。

## 依赖方向

```text
GenshinPiano.App
    ├── GenshinPiano.Application
    └── GenshinPiano.Infrastructure
             ├── GenshinPiano.Application
             └── GenshinPiano.Core

GenshinPiano.Application ──> GenshinPiano.Core
```

`Core` 不依赖 WPF、文件系统、MIDI 库或 Win32。`Application` 只定义用例和抽象端口。`Infrastructure` 实现 JSON、MIDI、旧格式和 Windows 输入适配器。`App` 负责视图、对话框和依赖组装。

## 计划中的适配器

- `JsonScoreDocumentSerializer`：`.gpiano` 权威工程格式
- `MidiScoreImporter` / `MidiScoreExporter`：MIDI 交换格式
- `LegacyGenshinPianoImporter`：旧版只读兼容
- `WindowsInputPlayer`：基于 Win32 `SendInput` 的游戏播放
- `RawInputRecorder`：全局键盘录入
- `MusicXmlImporter`：五线谱 OMR 结果导入

## UI 结构

WPF 界面采用 MVVM。钢琴卷帘和 21 键预览将作为独立自定义控件实现，不让领域模型依赖任何 WPF 类型。
