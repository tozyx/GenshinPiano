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

## 适配器

- `JsonScoreDocumentSerializer`：`.gpiano` 权威工程格式
- `LegacyGenshinPianoImporter`：旧版 `.GenshinPiano` 只读兼容
- `WindowsKeyboardInput`：基于 Win32 `SendInput` 扫描码的游戏播放

以下适配器仍在计划中：

- `MidiScoreImporter` / `MidiScoreExporter`：MIDI 交换格式
- `RawInputRecorder`：全局键盘录入
- `MusicXmlScoreImporter`：将可替换五线谱 OMR 后端输出的 MusicXML 转为内部曲谱，保留弱起、双谱表、独立声部游标、和弦和跨小节连音；模型选择与阶段计划见 [五线谱 OCR 技术选型](STAFF_OCR.md)

## 播放管线

`ScorePlaybackPlanner` 将未静音音轨的音符展开为独立的按下和抬起事件，结合 tempo map 计算绝对时间，再按照 `PlaybackSettings` 进行移调和 21 键音域处理。`ScorePlaybackService` 使用单调时钟执行完整按键时间轴，因此实际保持时间来自音符的 `durationTick`。取消、手动暂停、失焦或异常时都会释放仍处于按下状态的按键。

`WindowsForegroundProcessGuard` 通过前台窗口所属进程控制播放许可，默认白名单为国服 `YuanShen.exe` 和国际服 `GenshinImpact.exe`。失焦期间的时长会加入暂停累计值，因此重新聚焦后从原时间位置继续，不会补发暂停期间的音符。

首次播放会先等待白名单窗口获得焦点，再要求窗口连续保持前台 3 秒后启动；倒计时期间失焦会重置倒计时。`WindowsGlobalEscapeListener` 使用不拦截输入的低级键盘钩子监听 Esc，仅当播放进行中且白名单窗口位于前台时请求暂停，因此游戏仍能正常处理该 Esc 按键。

WPF 层只负责倒计时、状态展示和取消操作；具体 Win32 调用被隔离在 Infrastructure，因此核心时间与映射逻辑可使用假键盘进行自动化测试。

## 旧谱转换

`LegacyGenshinPianoImporter` 读取旧版按键和时值代码，转换为标准 MIDI 音高与 tick。旧版时值保存为 `rhythmTick`，音符采用 `auto` 与 `natural`，实际保持时间按节奏跨度的 80% 生成。`LegacyBatchConversionService` 递归处理目录，保留相对路径，通过统一 JSON 序列化器写出 `.gpiano`。旧格式不含 BPM，当前默认采用 120 BPM 与 480 PPQ，且不覆盖既有输出文件。

## UI 结构

WPF 界面采用 MVVM。`PianoRollEditor` 由固定 21 键键位栏和可滚动的 `PianoRollSurface` 组成。表面通过 `OnRender` 直接绘制网格、音符和自动时长轮廓，不为每个音符创建 WPF 子控件。

卷帘支持双击空白处创建、Ctrl 点选、空白拖动框选、Ctrl 框选追加、吸附成组移动、Ctrl+拖动成组复制、Delete 批量删除和 Ctrl+Z/Y 撤销重做。创建音符时向左吸附到当前网格单元起点，拖动时则吸附到最近的网格线。复制过程中原音符保持显示，目标位置使用带 `+` 标记的半透明预览。右键任一选中音符会对整组选区打开持续时间浮层，可通过紧凑节拍按钮并在 10%–95% 内即时调整发声比例。`[`/`]` 在选中音符时批量缩短/延长节拍，未选中时调整吸附网格并同步工具栏选项。Shift+滚轮横移，Ctrl+滚轮缩放时间轴，Ctrl+Shift+滚轮缩放键位高度。左侧键位栏提供“字母 + 按键、数字 + 按键、字母、数字”四种模式；数字简谱以 `1-`、`1`、`1+` 表示低、中、高三个八度，避免依赖字体的组合上下点。

新建音符采用当前吸附网格作为 `rhythmTick`，默认为 `auto + natural + 80%`。持续时间浮层会保存为自动时长；30%、50%、80%、95% 映射到四种预设，其余数值保存为 `custom + gateRatio`。

所有增删改通过 Core 中的 `ScoreEditor` 生成新的不可变 `ScoreDocument`，再由双向绑定写回 `ScoreWorkspace`。因此保存和播放始终使用同一份最新曲谱，领域模型不依赖 WPF 类型。

### 用户配置

`UserSettingsService` 负责加载、校验和原子写入用户配置。配置文件位于可执行文件目录下的 `config/settings.json`，路径以 `AppContext.BaseDirectory` 为基准，不受启动命令当前目录影响。配置采用带版本号的 UTF-8 JSON；字段缺失、取值非法或文件损坏时使用安全默认值，写入失败不会中断编辑和播放。当前保存卷帘吸附网格、默认演奏法和音名显示模式，后续设置可继续加入对应分组。发布包应保证整个程序目录可写，适合 ZIP 解压和便携运行；若未来安装到 `Program Files`，需改用用户可写安装目录或恢复用户数据目录方案。

### 主题

主题由 `GenshinPiano.App/Resources/Themes` 下的资源字典组成：

- `Theme.Base.xaml` 定义控件的共享尺寸和语义样式。
- `Theme.Dark.xaml` 与 `Theme.Light.xaml` 分别定义 Fluent 主题和语义颜色。
- 界面通过 `DynamicResource` 引用颜色，`ThemeService` 在运行时替换颜色字典。

新增主题时应沿用现有语义画刷键，避免在页面中直接写颜色值。

### 本地化

界面文案位于 `GenshinPiano.App/Resources/Localization`。XAML 使用 `DynamicResource`，ViewModel 和文件对话框通过 `ILocalizationService` 获取文案。切换语言时替换字符串字典并刷新动态状态文本。

曲谱标题、作者、轨道名称等属于用户数据，不随界面语言切换而改写。新增语言文件时必须与 `Strings.zh-CN.xaml` 保持相同的资源键集合。

### 窗口与未保存检测

主窗口使用 WPF `WindowChrome` 和软件内标题栏，保留系统缩放边框，同时自行提供拖动、双击最大化、最小化、还原和关闭操作。`ScoreWorkspace.IsDirty` 是未保存状态的唯一来源：新建或打开后为干净状态，曲谱文档发生替换时标脏，成功保存后恢复干净。关闭按钮、文件菜单退出和 Alt+F4 均进入统一的“保存 / 不保存 / 取消”流程；新文件选择保存时先请求目标路径。
