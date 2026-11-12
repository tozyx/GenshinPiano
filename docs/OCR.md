# OCR 插件

OCR 是可选的曲谱识别组件，支持简谱和五线谱两条处理路线。用户在导入窗口明确选择谱面类型；主程序负责展示进度、预览和导入结果，引擎负责将图片转换为统一曲谱。

## 组成与识别流程

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

## 支持范围与限制

五线谱目前面向清晰、平直的钢琴扫描谱和电子谱截图，不提供拍照谱的透视矫正或曲面展平。多乐器排版、模糊谱线、遮挡和复杂水印可能导致识别失败。

布局不能完整覆盖谱表，或重试后仍有明显小节、节奏异常时，引擎返回失败并提示更换图片。结构检查只能发现部分错误，成功识别后仍需试听、检查音高与时值。返回的置信度不是经过校准的准确率。

引擎不依赖系统 Python。简谱依赖插件内的 .NET/ONNX 模型，五线谱依赖插件内的原生运行时与量化模型。

## 插件发现与兼容

主程序读取 `manifest.json`。当前 manifest 使用 schema 1、IPC 协议 1，包含：

- `engineVersion`：插件的独立版本号。
- `executable`：相对插件目录的引擎路径。
- `launchMode`：当前为 `file`。
- `staffBackend`：当前为 `smt`。
- `requiredFiles`：运行所需资源的相对路径列表。

主程序校验协议、路径范围及声明资源的存在性和非空状态。旧 manifest 可省略新增字段；缺少 `launchMode` 的旧引擎通过标准输入输出模式调用。

插件版本、协议版本与主程序版本各自独立。升级主程序保留已安装插件，插件可以单独更新；安装与签名验证见 [更新机制](UPDATES.md)。

## 进程与数据接口

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
