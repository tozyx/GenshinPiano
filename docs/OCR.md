# 曲谱识别与 AI 导入

[简体中文](#简体中文) | [English](#english)

## 简体中文

OCR 支持简谱和五线谱两条处理路线。识别代码随主程序发布，模型与大型原生依赖通过可选资源包安装。用户在导入窗口明确选择谱面类型；主界面展示进度、预览和导入结果，独立工作进程将图片转换为统一曲谱。

### 组成与识别流程

资源包安装在程序目录的 `addons/ocr`：

~~~text
addons/ocr/
  manifest.json
  native/                 ONNX Runtime 与 SkiaSharp 原生库
  models/                 简谱 OCR 与 OrpheusNet 模型
  staff-omr/smt/          原生 SMT CPU 运行时、GGUF 模型及依赖
~~~

简谱使用 RapidOCR、OrpheusNet 与几何后处理，分析数字、音高点、节奏线、谱行、声部和伴奏，重建音符及时间关系。

五线谱使用原生 CPU SMT 后端：检测钢琴上下谱表并配对，按谱行裁剪、统一谱线间距和处理水印，再解析模型输出中的音高、时值、和弦、声部及连音，生成 `ScoreDocument`。对出现结构异常的谱行进行有限重试，候选选择不使用人工参考答案，并保护首次识别正常的小节。

主程序按同一套曲谱模型接收两条路线的输出。默认保留完整 MIDI 音高并在 88 键卷帘中打开；用户选择自动映射时，才执行一次可撤销的 21 键映射。

### 支持范围与限制

五线谱目前面向清晰、平直的钢琴扫描谱和电子谱截图，不提供拍照谱的透视矫正或曲面展平。多乐器排版、模糊谱线、遮挡和复杂水印可能导致识别失败。

布局不能完整覆盖谱表，或重试后仍有明显小节、节奏异常时，引擎返回失败并提示更换图片。结构检查只能发现部分错误，成功识别后仍需试听、检查音高与时值。返回的置信度不是经过校准的准确率。

引擎不依赖系统 Python。简谱使用 ONNX 模型，五线谱使用原生 SMT 运行时与量化模型。资源包不携带 .NET：自包含主程序的工作进程使用随应用打包的运行时，framework 版本使用已安装的 .NET 10 Desktop Runtime x64。

### 插件发现与兼容

主程序读取 `manifest.json`。当前资源 manifest 使用 schema 2、IPC 协议 1，包含：

- `resourceVersion`：资源包的独立版本号。
- `engineCompatibilityVersion`：引擎与资源的兼容代次，当前为 1。
- `smtModelSha256`：SMT 模型的 SHA-256。
- `staffBackend`：当前为 `smt`。
- `requiredFiles`：运行所需资源的相对路径列表。

主程序校验协议、兼容代次、路径范围和资源存在性/非空状态，并要求内置最低资源列表，不能通过删改 `requiredFiles` 绕过检查。schema 2 不指定可执行文件，工作进程路径由主程序确定。

识别代码随主程序更新，资源版本与主程序版本独立。升级主程序保留已安装资源。旧 schema 1 完整插件仍可使用：兼容的 1.0.0 文件模式资源可交给内置工作进程；其他旧插件保留原引擎调用方式。新资源包不会被旧客户端误识别为完整插件。安装与签名验证见 [更新机制](UPDATES.md)。

### 进程与数据接口

主程序通过同目录运行别名启动独立子进程 `GenshinPiano.Ocr.exe --ocr-worker --resources <资源目录>`，在初始化 WPF、音频、更新服务和单实例锁之前进入识别模式。主界面进程仍为 `GenshinPiano.exe`。推理及其原生库均在工作进程内加载，不进入界面进程。文件 IPC 使用 UTF-8 JSON 请求、响应和进度文件，主程序直接跟踪工作进程。旧插件仍支持启动器分离进程及标准流模式；后者通过 stdin 接收请求、stdout 返回响应、stderr 输出诊断。

运行别名在首次识别时生成，NTFS 上优先使用硬链接共享主程序文件；不支持硬链接时回退为本地副本。两个名称位于同一目录，适用于源码构建和单文件发行版。后续启动会校验并刷新过期别名，发布 ZIP 不重复打包一份引擎或 .NET。

OCR 窗口使用标准 WPF 模态对话框，由 WPF 管理主窗口的禁用、恢复和关闭后的激活关系。打开期间可切换到其他软件，但不能编辑主界面；异步识别、进度显示及取消仍正常运行。点击窗口关闭按钮会取消进行中的识别或安装，待清理完成后自动关闭。“取消识别”仅停止任务并保留窗口。主窗口的图片拖放事件先返回，再打开对话框；打开前将主窗口设为当前 UI 线程的活动窗口，使 WPF 记录正确的关闭返回目标。

请求字段：

- `protocolVersion`：IPC 协议版本。
- `imagePath`：输入图片的绝对路径。
- `notationHint`：`numbered` 或 `staff`；旧 `auto` 请求按简谱处理。
- `language`：提示语言。
- `watermarkMode`：`auto`、`strong` 或 `off`，默认 `auto`。
- `includeAccompaniment`：是否返回伴奏，默认 `true`。

响应包含 `protocolVersion`、`success`、`score`、`confidence` 和可选的 `warnings`。失败时提供 `errorCode` 与 `errorMessage`；成功响应必须包含通过校验的曲谱。

进度格式为 `OCR_PROGRESS|StageName|0.0-1.0`。阶段包括 `Preparing`、`WatermarkSuppression`、`TextDetection`、`SuperResolution` 和 `ScoreReconstruction`。文件模式写入进度文件，旧模式写入 stderr。

主程序拒绝不兼容协议、越界可执行路径、非零退出码、超时、损坏的 JSON 或无有效曲谱的成功响应。取消会结束引擎及其推理子进程，任务结束后清理临时通信文件。扫描谱常见失败码为 `staff_scan_layout_unsupported` 和 `staff_scan_needs_review`。

操作入口见 [用户指南](USER_GUIDE.md)，构建与资源准备见 [工具说明](../tools/README.md)。

### AI 辅助识谱导入

“导入 → AI 辅助识谱”可复制识谱提示词。将提示词与谱图发送给支持图片的大模型，再把结果粘贴到窗口，或选择 UTF-8 JSON 文件。两种来源使用同一个解析器，不要求安装 OCR 附加包，也不会由软件自动发送图片。解析后显示小节、声部、音符数量与核对提示；存在提示时需确认后导入。导入以 88 键视图保留原始音高，保存时使用正常的 `.gpiano` 格式。

识谱 JSON 与 `.gpiano` 是不同格式。固定 `format="genshinpiano-score"`、`version=1`；包含 `title`、`timeSignature`、`complete` 和 `measures`，可提供 `author`、`bpm` 和 `warnings`。未知速度可填 `bpm=null`，软件以 120 BPM 试听并提示核对。`complete=false` 的结果会被拒绝导入。

每个小节以 `number` 从 1 连续编号，并包含 `notes`。事件使用稳定的 `voice` 名称、`pitch`、`start`、`duration`；同时开始的音符形成和弦，真正休止用 `pitch=null`。音高可为 MIDI 整数或 `C4`、`F#4` 等绝对音名，`C4` 对应 MIDI 60。不要将无法识读的音符写成休止。

`start`、`duration` 和可选的小节 `length` 统一以四分音符为单位，支持数字及 `"1/3"` 等分数；6/8 一小节为 3 个四分音符。小节可声明拍号、速度变化或弱起长度。同声部、同音高且时间连续的延音用 `tie="start"/"continue"/"stop"` 表示，软件将其合并；连奏弧线不使用此字段。`uncertain=true` 和 `warnings` 用于标记待核对内容。

软件检查字段、版本、事件范围、连续编号和延音关系，并提示声部覆盖缺口或 480 PPQ 下的时间量化。休止保留后续音符的时间位置；末尾纯休止不单独生成音符，软件会提示播放终点的限制。格式通过不代表识谱正确；导入后仍需试听。提示词、文本、文件与后续 API 结果共用这一转换边界。

最小示例：

~~~json
{"format":"genshinpiano-score","version":1,"title":"Example","bpm":120,"timeSignature":[4,4],"complete":true,"warnings":[],"measures":[{"number":1,"notes":[{"voice":"melody","pitch":"C4","start":0,"duration":4}]}]}
~~~

## English

OCR has separate numbered-notation and staff-notation pipelines. Recognition code ships with the application; models and large native dependencies are installed as an optional resource package. Users explicitly choose the notation type. The UI handles progress, preview, and import; a separate worker process converts images into the shared score model.

### Components and recognition flow

The resource package is installed under `addons/ocr` beside the application:

~~~text
addons/ocr/
  manifest.json
  native/                 ONNX Runtime and SkiaSharp native libraries
  models/                 Numbered-notation OCR and OrpheusNet models
  staff-omr/smt/          Native SMT CPU runtime, GGUF model, and dependencies
~~~

Numbered notation uses RapidOCR, OrpheusNet, and geometric post-processing to analyze digits, octave dots, rhythm lines, notation rows, voices, and accompaniment, then reconstruct notes and timing.

Staff recognition uses a native CPU SMT backend. It detects and pairs the upper/lower piano staves, crops systems, normalizes staff spacing, handles watermarks, and parses pitches, durations, chords, voices, and ties into `ScoreDocument`. Systems with structural anomalies receive a bounded number of retries. Candidate selection uses no annotated reference answers and protects measures that were already structurally valid.

Both pipelines return the same score model. By default, the host preserves full MIDI pitches and opens results in the 88-key roll. A single undoable 21-key mapping operation is applied only when the user selects automatic mapping.

### Supported input and limitations

Staff recognition targets clear, flat piano scans and rendered sheet images. It does not provide photograph perspective correction or curved-page flattening. Multiple-instrument layouts, blurred staves, occlusion, and complex watermarks can cause failure.

The engine rejects incomplete staff coverage and obvious measure/rhythm anomalies that remain after retries, asking the user to try another image. Structural checks detect only some errors. Successful results still need audition and pitch/duration review. Reported confidence is not a calibrated accuracy score.

The engine does not require system Python. Numbered notation uses ONNX models; staff recognition uses the native SMT runtime and quantized model. The resource package contains no .NET runtime. The self-contained worker uses the application's bundled runtime; the framework-dependent worker uses the installed .NET 10 Desktop Runtime x64.

### Discovery and compatibility

The host reads `manifest.json`. Resource manifests use schema 2 and IPC protocol 1, with these fields:

- `resourceVersion`: independently managed resource version.
- `engineCompatibilityVersion`: engine/resource compatibility generation, currently 1.
- `smtModelSha256`: SHA-256 of the SMT model.
- `staffBackend`: currently `smt`.
- `requiredFiles`: relative paths of required runtime resources.

The host validates the protocol, compatibility generation, path boundaries, and resource existence/nonzero size. A built-in minimum file list prevents a manifest from omitting essential dependencies. Schema 2 specifies no executable: the application selects its trusted worker path.

Recognition code updates with the application; resource and application versions are independent. Application updates preserve installed resources. Legacy schema 1 full packages remain supported: compatible 1.0.0 file-mode resources can use the bundled worker, while other legacy packages retain their original engine invocation. Old clients do not mistake new resource archives for full add-ons. See [Updates and component management](UPDATES.md#english).

### Processes and data interface

The application starts a separate child process through a same-directory executable alias, `GenshinPiano.Ocr.exe --ocr-worker --resources <directory>`, before initializing WPF, audio, updates, or the single-instance lock. The UI process remains `GenshinPiano.exe`. Inference and native libraries load inside the worker rather than the UI process. File IPC uses UTF-8 JSON requests/responses and a progress file, with the host tracking the worker directly. Legacy add-ons retain detached-launcher and stream-mode support; stream mode uses stdin for requests, stdout for responses, and stderr for diagnostics.

The alias is created on first recognition. NTFS uses a hard link to share the application file when available; other filesystems fall back to a local copy. Both names stay in the same directory, supporting source builds and single-file releases. Later launches validate and refresh stale aliases, without adding another engine or .NET bundle to the release ZIP.

OCR uses a standard WPF modal dialog, with WPF managing the main window's disabled state, restoration, and activation on close. Users can switch to other applications but cannot edit the main window while OCR is open. Asynchronous recognition, progress, and cancellation continue normally. The window's close button cancels any active recognition or installation and closes automatically after cleanup. **Cancel recognition** stops the task while retaining the dialog. The main window's image-drop handler returns before the dialog opens. Before opening, it establishes the main window as the UI thread's active window so WPF records the correct activation return target.

Request fields:

- `protocolVersion`: IPC protocol version.
- `imagePath`: absolute path to the input image.
- `notationHint`: `numbered` or `staff`; legacy `auto` requests follow the numbered-notation route.
- `language`: language for messages.
- `watermarkMode`: `auto`, `strong`, or `off`; default `auto`.
- `includeAccompaniment`: whether to return accompaniment; default `true`.

Responses contain `protocolVersion`, `success`, `score`, `confidence`, and optional `warnings`. Failures provide `errorCode` and `errorMessage`. Successful responses must contain a valid score.

Progress uses `OCR_PROGRESS|StageName|0.0-1.0`. Stages are `Preparing`, `WatermarkSuppression`, `TextDetection`, `SuperResolution`, and `ScoreReconstruction`. File mode appends progress to the progress file; legacy mode writes it to stderr.

The host rejects incompatible protocols, executable paths outside the add-on, nonzero exit codes, timeouts, malformed JSON, and successful responses without a valid score. Cancellation terminates the engine and its inference subprocesses. Temporary communication files are cleaned up after the task. Common scan failures are `staff_scan_layout_unsupported` and `staff_scan_needs_review`.

See the [User guide](USER_GUIDE.md#english) for controls and [Build and maintenance tools](../tools/README.md#english) for resource preparation and packaging.

### AI-assisted score import

Use **Import → AI-assisted score import** to copy the recognition prompt. Send it with score images to a vision-capable model, then paste the response or open a UTF-8 JSON file. Both sources use the same parser. No OCR add-on is required, and the application does not send images automatically. Parsing shows measure, voice, and note counts plus review notes; those notes must be acknowledged before import. The 88-key view preserves original pitches, and normal saving writes a `.gpiano` file.

Recognition JSON is distinct from `.gpiano`. Set `format="genshinpiano-score"` and `version=1`. Include `title`, `timeSignature`, `complete`, and `measures`; `author`, `bpm`, and `warnings` may also be provided. Use `bpm=null` for unknown tempo; audition defaults to 120 BPM with a review note. Results with `complete=false` cannot be imported.

Measures contain `number` (consecutive from 1) and `notes`. Events use stable `voice` names, `pitch`, `start`, and `duration`. Simultaneous notes form chords; genuine rests use `pitch=null`. Pitch can be a MIDI integer or an absolute scientific pitch such as `C4` or `F#4`; `C4` is MIDI 60. Never use a rest to conceal an unreadable note.

`start`, `duration`, and optional measure `length` all use quarter notes, accepting numbers or fraction strings such as `"1/3"`. A 6/8 bar therefore has length 3. Measures can declare meter/tempo changes or a pickup length. Contiguous ties with matching voice and pitch use `tie="start"/"continue"/"stop"` and are merged by the importer; slurs do not use this field. `uncertain=true` and `warnings` identify readings that need review.

The importer checks fields, versions, event bounds, numbering, and tie relationships, and flags voice-coverage gaps or time rounding at 480 PPQ. Rests preserve later note positions. Trailing rests do not create notes, and the importer flags the resulting playback-end limitation. Passing validation does not certify recognition accuracy; audition after import. Prompts, pasted text, files, and future API results share this conversion boundary.

Minimal example:

~~~json
{"format":"genshinpiano-score","version":1,"title":"Example","bpm":120,"timeSignature":[4,4],"complete":true,"warnings":[],"measures":[{"number":1,"notes":[{"voice":"melody","pitch":"C4","start":0,"duration":4}]}]}
~~~
