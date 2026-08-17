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

### 主题

主题由 `GenshinPiano.App/Resources/Themes` 下的资源字典组成：

- `Theme.Base.xaml` 定义控件的共享尺寸和语义样式。
- `Theme.Dark.xaml` 与 `Theme.Light.xaml` 分别定义 Fluent 主题和语义颜色。
- 界面通过 `DynamicResource` 引用颜色，`ThemeService` 在运行时替换颜色字典。

新增主题时应沿用现有语义画刷键，避免在页面中直接写颜色值。

### 本地化

界面文案位于 `GenshinPiano.App/Resources/Localization`。XAML 使用 `DynamicResource`，ViewModel 和文件对话框通过 `ILocalizationService` 获取文案。切换语言时替换字符串字典并刷新动态状态文本。

曲谱标题、作者、轨道名称等属于用户数据，不随界面语言切换而改写。新增语言文件时必须与 `Strings.zh-CN.xaml` 保持相同的资源键集合。
