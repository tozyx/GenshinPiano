# 曲谱识别与 AI 导入

[简体中文](#简体中文) | [English](#english)

## 简体中文

OCR 是可选的曲谱识别组件，支持简谱和五线谱两条处理路线。用户在导入窗口明确选择谱面类型；主程序负责展示进度、预览和导入结果，引擎负责将图片转换为统一曲谱。

### 组成与识别流程

插件安装在程序目录的 `addons/ocr`：

~~~text
addons/ocr/
  manifest.json
  GPianoOcrEngine.exe
  models/                 简谱 OCR 与 OrpheusNet 模型
  staff-omr/smt/          原生 SMT CPU 运行时、GGUF 模型及依赖
~~~

简谱使用 RapidOCR、OrpheusNet 与几何后处理，分析数字、音高点、节奏线、谱行、声部和伴奏，重建音符及时间关系。

五线谱使用原生 CPU SMT 后端：检测钢琴上下谱表并配对，按谱行裁剪、统一谱线间距和处理水印，再解析模型输出中的音高、时值、和弦、声部及连音，生成 `ScoreDocument`。对出现结构异常的谱行进行有限重试，候选选择不使用人工参考答案，并保护首次识别正常的小节。

主程序按同一套曲谱模型接收两条路线的输出。默认保留完整 MIDI 音高并在 88 键卷帘中打开；用户选择自动映射时，才执行一次可撤销的 21 键映射。

### 支持范围与限制

五线谱目前面向清晰、平直的钢琴扫描谱和电子谱截图，不提供拍照谱的透视矫正或曲面展平。多乐器排版、模糊谱线、遮挡和复杂水印可能导致识别失败。

布局不能完整覆盖谱表，或重试后仍有明显小节、节奏异常时，引擎返回失败并提示更换图片。结构检查只能发现部分错误，成功识别后仍需试听、检查音高与时值。返回的置信度不是经过校准的准确率。

引擎不依赖系统 Python。简谱依赖插件内的 .NET/ONNX 模型，五线谱依赖插件内的原生运行时与量化模型。

### 插件发现与兼容

主程序读取 `manifest.json`。当前 manifest 使用 schema 1、IPC 协议 1，包含：

- `engineVersion`：插件的独立版本号。
- `executable`：相对插件目录的引擎路径。
- `launchMode`：当前为 `file`。
- `staffBackend`：当前为 `smt`。
- `requiredFiles`：运行所需资源的相对路径列表。

主程序校验协议、路径范围及声明资源的存在性和非空状态。旧 manifest 可省略新增字段；缺少 `launchMode` 的旧引擎通过标准输入输出模式调用。

插件版本、协议版本与主程序版本各自独立。升级主程序保留已安装插件，插件可以单独更新；安装与签名验证见 [更新机制](UPDATES.md)。

### 进程与数据接口

当前文件模式使用 UTF-8 JSON 请求、响应和进度文件。引擎启动器分离实际工作进程，主程序跟踪工作进程和任务生命周期。旧标准流模式将单个 JSON 请求写入 stdin，响应从 stdout 读取，诊断信息写入 stderr。

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

OCR is an optional score-recognition component with separate numbered-notation and staff-notation pipelines. Users explicitly choose the notation type in the import dialog. The host displays progress and handles preview/import; the engine converts images into the shared score model.

### Components and recognition flow

The add-on is installed under `addons/ocr` beside the application:

~~~text
addons/ocr/
  manifest.json
  GPianoOcrEngine.exe
  models/                 Numbered-notation OCR and OrpheusNet models
  staff-omr/smt/          Native SMT CPU runtime, GGUF model, and dependencies
~~~

Numbered notation uses RapidOCR, OrpheusNet, and geometric post-processing to analyze digits, octave dots, rhythm lines, notation rows, voices, and accompaniment, then reconstruct notes and timing.

Staff recognition uses a native CPU SMT backend. It detects and pairs the upper/lower piano staves, crops systems, normalizes staff spacing, handles watermarks, and parses pitches, durations, chords, voices, and ties into `ScoreDocument`. Systems with structural anomalies receive a bounded number of retries. Candidate selection uses no annotated reference answers and protects measures that were already structurally valid.

Both pipelines return the same score model. By default, the host preserves full MIDI pitches and opens results in the 88-key roll. A single undoable 21-key mapping operation is applied only when the user selects automatic mapping.

### Supported input and limitations

Staff recognition targets clear, flat piano scans and rendered sheet images. It does not provide photograph perspective correction or curved-page flattening. Multiple-instrument layouts, blurred staves, occlusion, and complex watermarks can cause failure.

The engine rejects incomplete staff coverage and obvious measure/rhythm anomalies that remain after retries, asking the user to try another image. Structural checks detect only some errors. Successful results still need audition and pitch/duration review. Reported confidence is not a calibrated accuracy score.

The engine does not require system Python. Numbered notation uses the add-on's .NET/ONNX models; staff recognition uses its bundled native runtime and quantized model.

### Discovery and compatibility

The host reads `manifest.json`. The current manifest uses schema 1 and IPC protocol 1, with these fields:

- `engineVersion`: independently managed add-on version.
- `executable`: engine path relative to the add-on directory.
- `launchMode`: currently `file`.
- `staffBackend`: currently `smt`.
- `requiredFiles`: relative paths of required runtime resources.

The host validates the protocol, path boundaries, and the existence/nonzero size of declared resources. Legacy manifests can omit newer fields. Older engines without `launchMode` use standard-input/output mode.

Add-on, protocol, and application versions are independent. Application updates preserve the installed add-on, which can be updated separately. See [Updates and component management](UPDATES.md#english) for installation and signature verification.

### Processes and data interface

Current file mode uses UTF-8 JSON request/response files and a progress file. The engine launcher detaches the worker, and the host tracks its process and task lifecycle. Legacy stream mode receives one JSON request through stdin, returns a response through stdout, and writes diagnostics to stderr.

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
