# 系统架构

GenshinPiano 使用 C#/.NET 10 与 WPF，面向 Windows x64。项目以统一曲谱模型连接编辑、导入、试听、练习和游戏按键输出，各种输入格式和系统能力通过适配器接入。

## 模块职责与依赖

~~~text
GenshinPiano.App ──────────> Application ──> Core
        └────────────────> Infrastructure ──> Application / Core
GenshinPiano.Ocr.Engine ──> Application / Infrastructure / Core
GenshinPiano.Updater ─────> 独立更新进程
~~~

- `Core`：曲谱模型、校验、音高映射、编辑规则和播放事件规划，不依赖 WPF 或 Win32。
- `Application`：工作区、导入转换、试听时间轴、练习会话、OCR 和更新用例，以及外部能力的接口。
- `Infrastructure`：JSON、MIDI、MusicXML、旧曲谱格式、Windows 输入、音频输出、OCR 进程和更新源的实现。
- `App`：窗口、视图模型、绘制控件、配置服务和依赖组装。
- `Ocr.Engine`：可选识别引擎，在独立进程中处理图片并返回曲谱。
- `Updater`：等待主程序退出后替换程序文件，处理备份、失败恢复和重启。

## 曲谱与工作区

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

## 编辑器与界面

界面采用 MVVM。`PianoRollEditor` 负责编辑交互，`PianoRollSurface` 使用 `OnRender` 绘制网格、音符和选区，避免为每个音符创建独立控件。21 键、88 键和曲谱音域视图共用底层曲谱。

编辑器的音符操作、试听和浮层按职责拆分；`PracticePage` 的音频和工具栏也分别组织。领域编辑、练习判定与试听时间计算位于 Core/Application，WPF 层负责输入、绘制和页面状态。

主题使用资源字典与语义画刷，界面通过 `DynamicResource` 引用颜色。中英文文案位于本地化资源字典；用户的曲谱标题、作者和音轨名不随界面语言切换。

## 播放、试听与练习

游戏演奏由 `ScorePlaybackPlanner` 结合速度图、移调和音域策略生成按下/抬起时间轴，再由播放服务使用单调时钟调度。`WindowsKeyboardInput` 通过 `SendInput` 输出按键，并跟踪需要释放的键。

前台进程检查限制按键发送的目标。失焦、取消或异常会释放按键；恢复焦点后按暂停后的时间位置继续，避免补发失焦期间的音符。进入游戏演奏前提供前台确认与倒计时。

本地试听使用独立音频输出，不向游戏发送按键。采样音色使用预解码 PCM 和单输出流多声部混音；重复同音可以保留前一次延音，空闲后释放音频设备。

`PracticeSession` 管理按键组合与练习进度。跟弹等待完成当前组合，节奏练习按时间推进；音游模式自动播放曲谱，玩家输入用于时间判定与反馈。`PracticeSurface` 绘制按键预览和垂直卷帘，判定不依赖逐音符的 WPF 控件。

操作细节见 [练习与音游](PRACTICE.md)。

## OCR 与更新边界

OCR 模型和原生运行时位于 `addons/ocr`，不嵌入主程序 ZIP。主程序通过版本化 manifest 发现插件，通过文件 IPC 调用引擎，并验证返回曲谱。识别任务和插件替换互斥，避免运行时被正在执行的任务占用。详见 [OCR 插件](OCR.md)。

更新流程分别管理主程序与 OCR 的版本和安装过程，共用下载校验能力。主程序通过独立更新器替换程序；OCR 在组件目录中暂存、校验和替换。详见 [更新机制](UPDATES.md)。

## 配置与持久化

程序以 `AppContext.BaseDirectory` 为便携目录基准：

- `config/settings.json`：主题、语言、编辑器、练习、音量和更新偏好。
- `data/recovery`：未保存曲谱的恢复数据。
- `logs`：运行日志。
- `addons/ocr`：可选 OCR 引擎与模型。
- `update-cache`：更新下载、暂存目录和回滚备份。

配置写入采用临时文件替换；缺失或损坏的配置按安全默认值加载。单实例协调使用互斥锁和命名管道，避免多个实例同时写入同一份便携数据，并将后续启动时打开的曲谱转交已有窗口。
