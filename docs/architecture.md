# 系统架构

[简体中文](#简体中文) | [English](#english)

## 简体中文

GenshinPiano 使用 C#/.NET 10 与 WPF，面向 Windows x64。项目以统一曲谱模型连接编辑、导入、试听、练习和游戏按键输出，各种输入格式和系统能力通过适配器接入。

### 模块职责与依赖

~~~text
GenshinPiano.App ──────────> Application ──> Core
        └────────────────> Infrastructure ──> Application / Core
GenshinPiano.Ocr.Engine ──> Application / Core
GenshinPiano.Updater ─────> 独立更新进程
~~~

- `Core`：曲谱模型、校验、音高映射、编辑规则和播放事件规划，不依赖 WPF 或 Win32。
- `Application`：工作区、导入转换、试听时间轴、练习会话、OCR 和更新用例，以及外部能力的接口。
- `Infrastructure`：JSON、MIDI、MusicXML、旧曲谱格式、Windows 输入、音频输出、OCR 进程和更新源的实现。
- `App`：窗口、视图模型、绘制控件、配置服务和依赖组装。
- `Ocr.Engine`：可选识别引擎，在独立进程中处理图片并返回曲谱。
- `Updater`：等待主程序退出后替换程序文件，处理备份、失败恢复和重启。

### 曲谱与工作区

`ScoreDocument` 是各功能共享的数据模型，使用 MIDI 音高和整数 tick 表达音乐。游戏中的字母按键仅在输出映射阶段生成，文件不依赖某个游戏键位布局。

`ScoreEditor` 根据编辑操作生成新的曲谱文档，`ScoreWorkspace` 管理当前文档、文件路径和未保存状态。编辑器、试听与练习从同一工作区读取内容。保存时捕获文档快照，先写入临时文件再替换目标文件；若保存期间继续编辑，新改动仍保持未保存状态。

导入流程将外部数据转换为同一模型：

~~~text
.gpiano ──> 版本迁移与校验 ──┐
MIDI ─────> 导入转换 ────────┤
旧版曲谱 ─> 格式转换 ────────┼──> ScoreDocument ──> 编辑 / 试听 / 练习 / 演奏
图片 ─────> OCR 插件 ────────┘
~~~

MIDI 导入由 `DryWetMidiScoreImporter` 处理；旧版 `.GenshinPiano` 转换保留相对路径并默认跳过已有输出。`MusicXmlScoreImporter` 提供 MusicXML 的时序、声部、和弦与连音转换。当前五线谱 OCR 的 SMT 输出直接转换为曲谱，不经过 MusicXML。

文件字段和兼容规则见 [曲谱格式](gpiano-format.md)。

### 编辑器与界面

界面采用 MVVM。`PianoRollEditor` 负责编辑交互，`PianoRollSurface` 使用 `OnRender` 绘制网格、音符和选区，避免为每个音符创建独立控件。21 键、88 键和曲谱音域视图共用底层曲谱。

编辑器的音符操作、试听和浮层按职责拆分；`PracticePage` 的音频和工具栏也分别组织。领域编辑、练习判定与试听时间计算位于 Core/Application，WPF 层负责输入、绘制和页面状态。

主题使用资源字典与语义画刷，界面通过 `DynamicResource` 引用颜色。中英文文案位于本地化资源字典；用户的曲谱标题、作者和音轨名不随界面语言切换。

### 播放、试听与练习

游戏演奏由 `ScorePlaybackPlanner` 结合速度图、移调和音域策略生成按下/抬起时间轴，再由播放服务使用单调时钟调度。`WindowsKeyboardInput` 通过 `SendInput` 输出按键，并跟踪需要释放的键。

前台进程检查限制按键发送的目标。失焦、取消或异常会释放按键；恢复焦点后按暂停后的时间位置继续，避免补发失焦期间的音符。进入游戏演奏前提供前台确认与倒计时。

本地试听使用独立音频输出，不向游戏发送按键。采样音色使用预解码 PCM 和单输出流多声部混音；重复同音可以保留前一次延音，空闲后释放音频设备。

`PracticeSession` 管理按键组合与练习进度。跟弹等待完成当前组合，节奏练习按时间推进；音游模式自动播放曲谱，玩家输入用于时间判定与反馈。`PracticeSurface` 绘制按键预览和垂直卷帘，判定不依赖逐音符的 WPF 控件。

操作细节见 [练习与音游](PRACTICE.md)。

### OCR 与更新边界

OCR 模型和原生运行时位于 `addons/ocr`，不嵌入主程序 ZIP。主程序通过版本化 manifest 发现插件，通过文件 IPC 调用引擎，并验证返回曲谱。识别任务和插件替换互斥，避免运行时被正在执行的任务占用。详见 [OCR 插件](OCR.md)。

AI 辅助识谱通过 `IAiScoreImporter` 将外部模型返回的小节/声部 JSON 转换为同一曲谱模型。文本与文件共用 `AiScoreJsonImporter`，窗口只处理输入、校验展示和导入确认，后续 API 请求可复用该转换层。

更新流程分别管理主程序与 OCR 的版本和安装过程，共用下载校验能力。主程序通过独立更新器替换程序；OCR 在组件目录中暂存、校验和替换。详见 [更新机制](UPDATES.md)。

### 配置与持久化

程序以 `AppContext.BaseDirectory` 为便携目录基准：

- `config/settings.json`：主题、语言、编辑器、练习、音量和更新偏好。
- `data/recovery`：未保存曲谱的恢复数据。
- `logs`：运行日志。
- `addons/ocr`：可选 OCR 引擎与模型。
- `update-cache`：更新下载、暂存目录和回滚备份。

配置写入采用临时文件替换；缺失或损坏的配置按安全默认值加载。单实例协调使用互斥锁和命名管道，避免多个实例同时写入同一份便携数据，并将后续启动时打开的曲谱转交已有窗口。

## English

GenshinPiano uses C#/.NET 10 and WPF on Windows x64. A shared score model connects editing, importing, local audition, practice, and in-game keyboard playback. Adapters provide access to file formats and operating-system services.

### Module responsibilities and dependencies

~~~text
GenshinPiano.App ──────────> Application ──> Core
        └────────────────> Infrastructure ──> Application / Core
GenshinPiano.Ocr.Engine ──> Application / Core
GenshinPiano.Updater ─────> Separate update process
~~~

- `Core` contains score models, validation, pitch mapping, editing rules, and playback-event planning. It has no WPF or Win32 dependency.
- `Application` contains the workspace, import/conversion use cases, audition timeline, practice sessions, OCR and update use cases, and interfaces to external capabilities.
- `Infrastructure` implements JSON, MIDI, MusicXML, legacy score formats, Windows input, audio output, OCR processes, and update sources.
- `App` provides windows, view models, drawing controls, configuration services, and dependency composition.
- `Ocr.Engine` is an optional recognition engine that processes images in a separate process and returns a score.
- `Updater` waits for the main application to exit, replaces application files, and handles backups, recovery, and restart.

### Scores and the workspace

`ScoreDocument` is the shared data model. It represents music using MIDI pitches and integer ticks. Game key letters are generated only during output mapping, so the file is independent of a particular game-key layout.

`ScoreEditor` produces a new score document for each edit. `ScoreWorkspace` manages the current document, file path, and unsaved state. The editor, audition, and practice views read the same workspace. Saving captures a document snapshot, writes a temporary file, and replaces the destination. Edits made during a save remain marked as unsaved.

Importers normalize external data into that model:

~~~text
.gpiano ─────> Schema migration and validation ──┐
MIDI ────────> Import conversion ────────────────┤
Legacy score > Format conversion ───────────────┼─> ScoreDocument
Image ───────> OCR add-on ──────────────────────┘
                                                    |
                                         Edit / Audition / Practice / Play
~~~

`DryWetMidiScoreImporter` handles MIDI import. Legacy `.GenshinPiano` conversion preserves relative paths and skips existing output by default. `MusicXmlScoreImporter` converts MusicXML timing, voices, chords, and ties. The current SMT staff-recognition pipeline converts its output directly into the score model without going through MusicXML.

See [Score format](gpiano-format.md#english) for fields and compatibility rules.

### Editor and user interface

The interface uses MVVM. `PianoRollEditor` handles editing interactions, while `PianoRollSurface` draws grids, notes, and selections through `OnRender` instead of creating a separate control for each note. The 21-key, 88-key, and score-range views share the underlying score.

Note operations, audition, and popups are organized by responsibility. `PracticePage` also separates audio and toolbar behavior. Domain editing, practice judgments, and audition timing live in Core/Application; WPF handles input, rendering, and page state.

Themes use resource dictionaries and semantic brushes referenced through `DynamicResource`. Localized text lives in language resource dictionaries. Changing the UI language does not change user-authored score titles, authors, or track names.

### Playback, audition, and practice

`ScorePlaybackPlanner` combines the tempo map, transposition, and range policy into a key-down/key-up timeline. The playback service schedules it with a monotonic clock. `WindowsKeyboardInput` sends keys through `SendInput` and tracks keys that must be released.

Foreground-process checks restrict the destination of simulated input. Focus loss, cancellation, and errors release held keys. Playback resumes from the paused position after focus returns rather than sending missed notes in a burst. Foreground confirmation and a countdown precede in-game playback.

Local audition uses separate audio output and does not send game keys. Sampled instruments use pre-decoded PCM and polyphonic mixing through one output stream. Repeated attacks can retain the previous note's tail, and the audio device is released when idle.

`PracticeSession` manages key combinations and practice progress. Follow mode waits for the current combination; timed practice advances with the score clock. Rhythm-game mode plays the score automatically and uses player input for timing judgments and feedback. `PracticeSurface` draws key previews and the vertical roll without a WPF control per note.

See [Practice and rhythm-game guide](PRACTICE.md#english) for controls.

### OCR and update boundaries

OCR models and native runtimes reside in `addons/ocr` and are excluded from the application ZIP. The host discovers the add-on through a versioned manifest, invokes it through file IPC, and validates its returned score. Recognition and add-on replacement are mutually exclusive so that an active task's runtime cannot be replaced. See [OCR component](OCR.md#english).

AI-assisted import uses `IAiScoreImporter` to convert external model output, expressed as measures/voices, into the same score model. Text and files share `AiScoreJsonImporter`; the dialog handles input, review, and confirmation. Future API requests can reuse this conversion layer.

Application and OCR updates manage versions and installation separately while sharing download verification. A separate updater replaces application files; OCR is staged, validated, and replaced within the component directory. See [Updates and component management](UPDATES.md#english).

### Configuration and persistence

Portable paths are based on `AppContext.BaseDirectory`:

- `config/settings.json` stores theme, language, editor, practice, volume, and update preferences.
- `data/recovery` stores recovery data for unsaved scores.
- `logs` contains runtime logs.
- `addons/ocr` contains the optional OCR engine and models.
- `update-cache` contains downloads, staging directories, and rollback backups.

Configuration writes use temporary-file replacement. Missing or damaged configuration falls back to safe defaults. Single-instance coordination uses a mutex and named pipe to prevent concurrent writers to the same portable data and forwards score-opening requests from later launches to the existing window.
