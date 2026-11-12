# 构建与维护工具

本目录提供应用构建、OCR 资源部署、签名和离线维护工具。以下命令从仓库根目录运行。项目原理与软件使用见 [项目文档](../docs/README.md)。

## 环境与常规构建

需要 Windows 10/11 x64、.NET 10 SDK 和 Git。编辑器可使用支持 WPF 的 Visual Studio 或 VS Code。自包含应用的更新器采用 Native AOT，该发布方式还需要 C++ 桌面构建工具。

~~~powershell
dotnet restore GenshinPiano.sln
dotnet build GenshinPiano.sln -c Debug
dotnet run --project src/GenshinPiano.App/GenshinPiano.App.csproj
~~~

运行已有自动化测试：

~~~powershell
dotnet test tests/GenshinPiano.Core.Tests/GenshinPiano.Core.Tests.csproj
~~~

普通构建不需要 OCR 模型，也不自动下载模型。`bin`、`obj`、`artifacts` 和发布产物不纳入 Git。

## 发布应用

三个 BAT 支持双击运行，会自动定位仓库根目录。第一个参数覆盖发布版本，省略时使用 BAT 内的默认值；应用默认为 `3.1.0`，OCR 默认为 `1.0.0`。

应用发布需要 `publish/songs` 中至少有一个曲谱。配置现有签名私钥后，选择发布类型：

~~~powershell
$env:GENSHINPIANO_UPDATE_SIGNING_KEY = 'D:\secure\GenshinPiano-update-signing-private.xml'
tools\build-release.bat 3.1.0
tools\build-release-framework.bat 3.1.0
~~~

路径仅为示例，实际私钥应保存在仓库外。自包含版本无需目标电脑预装 .NET；framework 版本需要 .NET 10 Desktop Runtime x64。两个脚本只构建应用、更新器和曲谱，不打包 OCR。

应用输出位于 `publish/GenshinPiano-win-x64` 或 `publish/GenshinPiano-win-x64-framework`，同时生成对应 ZIP、`.sha256` 和 `.sig`。将每个 ZIP 与其两个配套文件一并上传到 Release。

无需私钥的本地/CI 包验证入口：

~~~powershell
pwsh -File tools/Build-UnsignedAppPackage.ps1
~~~

输出位于 `artifacts/package-check`。该无签名包不能用于应用内更新。

## OCR 资源与发布

OCR 发布需要 PowerShell 7、.NET 10 SDK 和同一签名密钥环境变量。手动下载以下固定资源，放到 `publish/smt-experiment`；模型也可以放在该目录的 `models` 子目录：

- [CrispEmbed v0.17.18 CPU Windows x64](https://github.com/CrispStrobe/CrispEmbed/releases/tag/v0.17.18)：`crispembed-windows-x86_64.zip`。
- [GrandStaff Q8 模型](https://huggingface.co/cstr/smt-grandstaff-GGUF/tree/8e1595591659aa03cf1e71aab5aa03e8f3fccd7e)：`smt-grandstaff-q8_0.gguf`。

发布脚本校验固定 SHA-256，提取必要运行文件、许可证和模型，并加入 x64 VC 运行库。VC DLL 默认从 Windows `System32` 读取。可覆盖资源和 VC 运行库目录：

~~~powershell
tools\build-ocr-addon.bat 1.0.0
tools\build-ocr-addon.bat 1.0.0 "D:\ocr-assets" "D:\vc-runtime-x64"
tools\build-ocr-addon.bat --help
~~~

版本参数同时应用到引擎构建、产物 manifest 和 ZIP 名称，不修改源码版本。输出为 `publish/addons/ocr` 和 `publish/ocr-addons-<version>-win-x64.zip` 及其校验和、签名。

也可直接调用 `Publish-OcrAddon.ps1`，使用 `-Version` 指定版本；省略时读取项目版本并校验源码 manifest 是否一致。`Publish-OcrSmtRuntime.ps1` 供发布及调试部署共用，支持 `-SmtTrialDirectory` 和 `-MsvcRuntimeDirectory`。

调试部署入口：

~~~powershell
pwsh -File tools/Setup-OcrDevelopment.ps1
~~~

该命令构建 Debug 引擎并将完整插件部署到主程序 Debug 输出的 `addons/ocr`，使用同一套手动准备的资源，无需 Python。

## 签名工具

`Sign-UpdatePackage.ps1` 使用现有私钥生成签名。仓库只保存用于验签的公钥，私钥不写入脚本或发布 ZIP。签名格式和安装行为见 [更新机制](../docs/UPDATES.md)。

`New-UpdateSigningKey.ps1` 用于首次建立密钥或经过规划的密钥轮换，不用于每次发布。直接换公钥会改变旧客户端的信任关系。

~~~powershell
pwsh -File tools/New-UpdateSigningKey.ps1 -PrivateKeyPath "D:\secure\GenshinPiano-update-signing-private.xml"
~~~

## 模型、资源与离线维护

### 图标

`generate_windows_icons.py` 从主图生成 Windows 图标和应用资源，需要 Pillow。素材说明见 [图标资源](../src/GenshinPiano.App/Assets/README.md)。

### OCR 宿主验证

`ocr/smoke/OcrAddonSmoke.csproj` 提供 `recognize`、`verify`、`install` 命令，需要 .NET 10 SDK 和实际插件包。`install` 会替换指定目录的插件，应使用专用测试目录。

~~~powershell
dotnet run --project tools/ocr/smoke/OcrAddonSmoke.csproj -- recognize publish/addons/ocr "D:\scores\scan.png" "D:\results\recognized.json" Staff
dotnet run --project tools/ocr/smoke/OcrAddonSmoke.csproj -- verify publish/ocr-addons-1.0.0-win-x64.zip src/GenshinPiano.App/Assets/Security/UpdateSigningPublicKey.xml
~~~

### 简谱模型导出

`ocr/models/export_orpheusnet.py` 和 `export_orpheusnet_srcnn.py` 从本地 OrpheusNet 源码/权重重新生成并验证 ONNX 模型，需要独立的 PyTorch、ONNX、NumPy 开发环境。

~~~powershell
python tools/ocr/models/export_orpheusnet.py "D:\research\OrpheusNet" "D:\models"
python tools/ocr/models/export_orpheusnet_srcnn.py "D:\research\srcnn.pth" "D:\models\orpheusnet-srcnn-x3.onnx"
~~~

### 离线评分与回归

`ocr/research` 保留扫描谱预处理、SMT 输出解析、离线评分、参考标注和回归工具。它们不进入应用/OCR 包，正式识别使用 C# 引擎。

- `Test-Smt.ps1`：单次原生推理，支持 System/Page 模型，记录输出、耗时并处理超时。
- `prepare_smt_scan.py`、`optimize_smt_scan.py`：扫描谱布局与候选处理，需要 NumPy、Pillow。
- `compare_staff_ocr.py`、`evaluate_smt_scan_optimization.py`：结合 `reference` 中的人工标注进行离线评分。
- `evaluate_ocr_score.py`：对照人工 `.gpiano` 文件评估简谱结果。
- `test_smt_scan_pipeline.py`：无需模型的布局与候选保护回归。

Page 仅用于离线研究，需要另行准备 [整页 GrandStaff Q8 模型](https://huggingface.co/cstr/smt-fp-grandstaff-GGUF/tree/522e448e70c5c4b5ab4ceb793610b0aad9d27c53) `smt-fp-grandstaff-q8_0.gguf`；正式插件使用 System 模型。固定哈希由相应脚本校验。

~~~powershell
python -B -m unittest discover -s tools/ocr/research -p test_smt_scan_pipeline.py -v
python tools/ocr/research/optimize_smt_scan.py "D:\scores\scan.png" "D:\results\new-run"
~~~

## English

Build requirements: Windows x64 and .NET 10 SDK. The self-contained updater also needs the Desktop C++ build tools. Run commands above from the repository root.

The tracked BAT files publish the self-contained application, framework-dependent application, and OCR add-on separately. Their first argument is the release version; OCR also accepts resource and VC runtime directories. OCR packaging needs PowerShell 7 and manually downloaded assets.

Set `GENSHINPIANO_UPDATE_SIGNING_KEY` to an existing private key outside the repository. Publish each ZIP with its matching `.sha256` and `.sig` files. Build and test commands, unsigned packaging, Debug OCR setup, and maintenance tools are listed above.

For current product behavior and architecture, see [project documentation](../docs/README.md).
