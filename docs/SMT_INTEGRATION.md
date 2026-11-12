# SMT 接入与 OCR 0.9.0

2026-11-11：SMT 已接入正式 OCR 引擎。此前的实验、准确度比较和照片鲁棒性文档保留为研究记录。本说明的包体积、准确度及兼容性实测针对当时的 0.9.0 包。

2026-11-12：源码中的 OCR 项目与 manifest 版本已同步为 `1.0.0`。应用发布 BAT 不再构建 OCR；单独运行本地 `build-ocr-addon.bat` 打包插件。下面的 0.9.0 数据保留为历史实测，不代表已生成 1.0.0 发布包。

## 主程序与识别流程

- OCR 对话框选择“五线谱”后，由现有文件 IPC 调用 `GPianoOcrEngine.exe`，再启动本机 CrispEmbed CPU 子进程；简谱继续使用原有 .NET 流程。
- 检测钢琴上下谱表、分行、去浅色水印、统一谱线间距；对有异常的谱行尝试最多四组输入，按无需参考答案的结构指标选择结果，并保护初次识别正常的小节。
- C# 解析模型输出，重建双谱表、和弦、声部、附点、连音和拍号变化，直接生成原有 `ScoreDocument`。分数中的 PPQ 为 480，内部使用精确分数计算节奏。
- 对话框展示识别警告，未识别速度时提示初始速度为 120 BPM。结果的 0.8 置信度是启发值，不代表实测准确率。
- 仅面向清晰、平直的钢琴扫描谱或电子谱截图。不做照片矫正。不能完整定位谱表，或重试后仍有小节、节奏等结构异常时，返回失败并提示换图，避免导入缺行或不完整结果。
- 每行子进程超时 60 秒；取消会终止工作进程及其子进程。默认最多使用四个 CPU 线程。

## 更新兼容性

组件版本升至 0.9.0；manifest schema、文件 IPC 协议仍为 1，启动模式仍为 `file`。ZIP 根目录仍是 `addons/ocr`，文件名规则、SHA-256、RSA 签名格式和独立组件版本发现逻辑不变。无需修改 GitHub/GitCode 更新源。

新 manifest 声明 `staffBackend=smt` 和 16 个必需文件。新版主程序在暂存阶段、安装替换后均校验协议、签名元数据中的版本和资源完整性，失败恢复原目录；仍兼容没有 `requiredFiles` 的旧 manifest。旧主程序可以忽略新增字段并调用新包，已经通过真实旧版宿主代码验证。

应用更新仍保留整个 `addons` 目录；OCR 更新整目录替换组件，因此升级后旧 Python 文件不会残留。包仍需连同 `.sha256`、`.sig` 上传到 Release 后，线上更新源才会发现；本次仅生成本地资产。

## 构建产物

独立打包入口（本地脚本，已加入 `.gitignore`）：

```cmd
build-ocr-addon.bat
build-ocr-addon.bat "D:\ocr-assets" "D:\vc-runtime-x64"
```

需要 PowerShell 7、.NET 10 SDK 和 `GENSHINPIANO_UPDATE_SIGNING_KEY`。参数是 SMT 资源目录和 VC 运行库目录，不是版本号。默认输出到 `publish/addons/ocr`，并在 `publish` 下生成当前插件版本的 ZIP、校验和及签名。也可直接使用已跟踪的 PowerShell 脚本：

```powershell
.\tools\Publish-OcrAddon.ps1 `
    -AddonRootDirectory D:\java\GenshinPiano\publish\smt-release\addons `
    -ArtifactsDirectory D:\java\GenshinPiano\publish `
    -PrivateKeyPath <仓库外的私钥路径>
```

资源默认从 `publish/smt-experiment` 读取，可用 `-SmtTrialDirectory` 覆盖。CPU 归档与模型固定 SHA-256；模型可在资源目录根部或 `models` 子目录。`Publish-OcrSmtRuntime.ps1` 只提取 CPU 推理必需 EXE/DLL、许可证和说明，并加入量化模型、本地 x64 VC 运行库与来源记录。VC 文件默认读取系统目录，也可用 `-MsvcRuntimeDirectory` 指定。

新包不包含 Python/Oemer，但保留简谱的 RapidOCR、OrpheusNet 模型及所需 .NET/ONNX 运行库。旧 Python 打包工具仅保留供研究使用。

实际 ZIP 为 **106,903,860 字节（101.95 MiB）**；本地旧 0.8.0 ZIP 为 **290,770,564 字节（277.30 MiB）**，减少 **63.23%**。体积针对这两份实际归档，未混用此前仅目录裁剪的测算。

## 验证结果

- Release 主程序编译成功，零警告、零错误；144 项自动测试全部通过，覆盖分数时间轴、SMT 解析、谱表分行、候选保护及安装失败回滚。
- 通过真实附加包宿主及文件 IPC 识别清晰《稻香》扫描谱：243 个音符，完整音符（谱表、音高、起点、时值）匹配 241/243，F1 **99.18%**，约 29 秒。这是当前单张人工标注样本的结果，不代表所有谱图。
- 原实验的九张候选输入与 C# 生成图逐像素相同；旧版宿主调用新包与新版宿主输出的音高、起点、时值完全相同。
- 简谱回归样本生成 16 个音符，新旧包的音高、起点、时值完全相同。
- 模糊扫描样本第三行仍有结构异常，实际包返回 `staff_scan_needs_review`；照片样本返回 `staff_scan_layout_unsupported`。这是正式导入的质量门槛，不能把实验中较高的整图评分理解成该样本已可自动导入。
- 2 秒取消测试在约 2.06 秒结束，没有残留引擎/SMT 子进程；仅保留 Windows System32 的 PATH 时，包仍能独立识别清晰样本（243 音符，约 28.6 秒），无需系统 Python。该测试不等同于独立干净虚拟机验证。
- 最终 ZIP 使用程序真实公钥验签通过；通过真实安装器从旧 0.8.0 目录升级到 0.9.0，旧文件清除，必需文件完整。Release 主程序输出目录也已安装这份新包。

`tools/OcrAddonSmoke.csproj` 提供 `recognize`、`verify`、`install` 三种可重复运行的本地验证命令。运行时可设置 `GENSHINPIANO_SMT_DIAGNOSTICS` 为输出目录，以保留候选图、模型输出和选择记录；日常使用默认不保存这些诊断数据。
