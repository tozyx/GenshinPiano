# 构建与维护工具

[简体中文](#简体中文) | [English](#english)

## 简体中文

本目录提供应用构建、OCR 资源部署、签名和离线维护工具。以下命令从仓库根目录运行。项目原理与软件使用见 [项目文档](../docs/README.md)。

### 环境与常规构建

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

### 发布应用

三个 BAT 支持双击运行，会自动定位仓库根目录。第一个参数覆盖发布版本，省略时使用 BAT 内的默认值；应用默认为 `3.1.0`，OCR 默认为 `1.0.0`。

应用发布需要 `publish/songs` 中至少有一个曲谱。配置现有签名私钥后，选择发布类型：

~~~powershell
$env:GENSHINPIANO_UPDATE_SIGNING_KEY = 'D:\secure\GenshinPiano-update-signing-private.xml'
tools\build-release.bat 3.1.0
tools\build-release-framework.bat 3.1.0
~~~

路径仅为示例，实际私钥应保存在仓库外。自包含版本无需目标电脑预装 .NET；framework 版本需要 .NET 10 Desktop Runtime x64。两个脚本构建应用（含托管 OCR 代码）、更新器和曲谱，不打包可选 OCR 模型与原生依赖。

应用输出位于 `publish/GenshinPiano-win-x64` 或 `publish/GenshinPiano-win-x64-framework`，同时生成对应 ZIP、`.sha256` 和 `.sig`。将每个 ZIP 与其两个配套文件一并上传到 Release。

无需私钥的本地/CI 包验证入口：

~~~powershell
pwsh -File tools/Build-UnsignedAppPackage.ps1
~~~

输出位于 `artifacts/package-check`。该无签名包不能用于应用内更新。

### OCR 资源与发布

OCR 资源发布需要 .NET 10 SDK 和同一签名密钥环境变量。BAT 优先使用可用的 PowerShell 7，找不到时自动使用 Windows 自带的 PowerShell 5.1，无需另行安装 PowerShell 7。手动下载以下固定资源，放到 `publish/smt-resource`；模型也可以放在该目录的 `models` 子目录：

- [CrispEmbed v0.17.18 CPU Windows x64](https://github.com/CrispStrobe/CrispEmbed/releases/tag/v0.17.18)：`crispembed-windows-x86_64.zip`。
- [GrandStaff Q8 模型](https://huggingface.co/cstr/smt-grandstaff-GGUF/tree/8e1595591659aa03cf1e71aab5aa03e8f3fccd7e)：`smt-grandstaff-q8_0.gguf`。

发布脚本校验固定 SHA-256，提取必要运行文件、许可证和模型，并加入 x64 VC 运行库。VC DLL 默认从 Windows `System32` 读取。可覆盖资源和 VC 运行库目录：

~~~powershell
tools\build-ocr-addon.bat 1.0.0
tools\build-ocr-addon.bat 1.0.0 "D:\ocr-assets" "D:\vc-runtime-x64"
tools\build-ocr-addon.bat --help
~~~

版本参数应用到资源 manifest 的 `resourceVersion` 和 ZIP 名称，不修改源码版本或主程序版本。输出为 `publish/addons/ocr` 和 `publish/ocr-resources-<version>-win-x64.zip` 及其校验和、签名。资源包只有模型、原生运行库与声明，不包含独立托管引擎或 .NET。

也可直接调用 `Publish-OcrResources.ps1`，使用 `-Version` 指定资源版本；省略时读取源码 manifest 的 `resourceVersion`。脚本从 NuGet 缓存提取 ONNX/Skia 原生库与 RapidOCR 模型，复制仓库的 OrpheusNet 模型，并部署校验过的 SMT 资源。`Publish-OcrAddon.ps1` 仅作为旧命令的转发入口。`Publish-OcrSmtRuntime.ps1` 供发布及调试部署共用，支持 `-SmtTrialDirectory` 和 `-MsvcRuntimeDirectory`。

无签名本地验证可使用 `-SkipSigning`；仅部署目录可使用 `-SkipArchive`。无签名包不能用于正式更新。新资源包使用独立文件名前缀与组件标识，旧客户端需先升级主程序，见 [更新兼容规则](../docs/UPDATES.md)。

调试部署入口：

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Setup-OcrDevelopment.ps1
~~~

调试部署脚本支持 Windows PowerShell 5.1，将同一套资源部署到主程序默认 Debug 输出的 `addons/ocr`，不生成独立引擎。先执行常规 Debug 构建再启动主程序即可使用；自定义构建输出应通过资源发布脚本的 `-AddonRootDirectory` 指向对应的 `addons` 目录。无需 Python。

### 签名工具

`Sign-UpdatePackage.ps1` 使用现有私钥生成签名。仓库只保存用于验签的公钥，私钥不写入脚本或发布 ZIP。签名格式和安装行为见 [更新机制](../docs/UPDATES.md)。

`New-UpdateSigningKey.ps1` 用于首次建立密钥或经过规划的密钥轮换，不用于每次发布。直接换公钥会改变旧客户端的信任关系。

~~~powershell
pwsh -File tools/New-UpdateSigningKey.ps1 -PrivateKeyPath "D:\secure\GenshinPiano-update-signing-private.xml"
~~~

### 模型、资源与离线维护

#### 图标

`generate_windows_icons.py` 从主图生成 Windows 图标和应用资源，需要 Pillow。素材说明见 [图标资源](../src/GenshinPiano.App/Assets/README.md)。

#### OCR 宿主验证

`ocr/smoke/OcrAddonSmoke.csproj` 提供 `recognize`、`verify`、`install` 命令，需要 .NET 10 SDK 和实际插件包。`install` 会替换指定目录的插件，应使用专用测试目录。

~~~powershell
dotnet run --project tools/ocr/smoke/OcrAddonSmoke.csproj -- recognize publish/addons/ocr "D:\scores\scan.png" "D:\results\recognized.json" Staff 0 "D:\app\GenshinPiano.exe"
dotnet run --project tools/ocr/smoke/OcrAddonSmoke.csproj -- verify publish/ocr-resources-1.0.0-win-x64.zip src/GenshinPiano.App/Assets/Security/UpdateSigningPublicKey.xml
~~~

`recognize` 最后两个参数是取消等待毫秒数（0 为不自动取消）与宿主 EXE；资源包需要宿主路径。`install <zip> <public-key> <install-root> <host-executable>` 可验证完整安装流程。

#### 简谱模型导出

`ocr/models/export_orpheusnet.py` 和 `export_orpheusnet_srcnn.py` 从本地 OrpheusNet 源码/权重重新生成并验证 ONNX 模型，需要独立的 PyTorch、ONNX、NumPy 开发环境。

~~~powershell
python tools/ocr/models/export_orpheusnet.py "D:\research\OrpheusNet" "D:\models"
python tools/ocr/models/export_orpheusnet_srcnn.py "D:\research\srcnn.pth" "D:\models\orpheusnet-srcnn-x3.onnx"
~~~

#### 离线评分与回归

`ocr/research` 保留扫描谱预处理、SMT 输出解析、离线评分、参考标注和回归工具。它们不进入应用/OCR 包，正式识别使用 C# 引擎。

- `Test-Smt.ps1`：需要 PowerShell 7；单次原生推理，支持 System/Page 模型，记录输出、耗时并处理超时。
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

This directory contains application build tools, OCR resource deployment, signing, and offline maintenance utilities. Run the commands below from the repository root. See [Project documentation](../docs/README.md#english) for product behavior and architecture.

### Environment and regular builds

Use Windows 10/11 x64, the .NET 10 SDK, and Git. Visual Studio with WPF support or VS Code can be used for editing. The self-contained application uses a Native AOT updater, which also requires the Desktop C++ build tools.

~~~powershell
dotnet restore GenshinPiano.sln
dotnet build GenshinPiano.sln -c Debug
dotnet run --project src/GenshinPiano.App/GenshinPiano.App.csproj
~~~

Run the existing automated tests:

~~~powershell
dotnet test tests/GenshinPiano.Core.Tests/GenshinPiano.Core.Tests.csproj
~~~

Ordinary builds do not require or download OCR models. Build directories (`bin`, `obj`, `artifacts`) and generated release packages are excluded from Git.

### Publishing the application

The three tracked BAT files can be double-clicked and locate the repository root automatically. Their first argument overrides the release version. With no argument they use the defaults defined in the BAT: `3.1.0` for the application and `1.0.0` for OCR.

Application packaging requires at least one score under `publish/songs`. Configure the existing signing key, then choose the package type:

~~~powershell
$env:GENSHINPIANO_UPDATE_SIGNING_KEY = 'D:\secure\GenshinPiano-update-signing-private.xml'
tools\build-release.bat 3.1.0
tools\build-release-framework.bat 3.1.0
~~~

The key path is only an example; keep the actual private key outside the repository. The self-contained build needs no .NET installation on the target computer. The framework-dependent build requires .NET 10 Desktop Runtime x64. Both scripts package the application (including managed OCR code), updater, and scores; optional OCR models/native dependencies are published separately.

Application output is written to `publish/GenshinPiano-win-x64` or `publish/GenshinPiano-win-x64-framework`, with a corresponding ZIP, `.sha256`, and `.sig`. Upload each ZIP and both sidecars together to the Release.

The local/CI packaging check does not need a signing key:

~~~powershell
pwsh -File tools/Build-UnsignedAppPackage.ps1
~~~

Output is placed under `artifacts/package-check`. This unsigned package is not accepted by the in-app updater.

### OCR resources and publishing

OCR publishing requires the .NET 10 SDK and the same signing-key environment variable. The BAT prefers available PowerShell 7 and otherwise uses built-in Windows PowerShell 5.1, so installing PowerShell 7 is optional for release packaging.

Download the pinned assets manually into `publish/smt-resource`. The model can alternatively be placed in that directory's `models` subfolder:

- [CrispEmbed v0.17.18 CPU Windows x64](https://github.com/CrispStrobe/CrispEmbed/releases/tag/v0.17.18): `crispembed-windows-x86_64.zip`.
- [GrandStaff Q8 model](https://huggingface.co/cstr/smt-grandstaff-GGUF/tree/8e1595591659aa03cf1e71aab5aa03e8f3fccd7e): `smt-grandstaff-q8_0.gguf`.

The publisher checks pinned SHA-256 hashes, extracts the required runtime files, licenses, and model, and includes x64 VC runtime DLLs. VC DLLs default to Windows `System32`. The second and third BAT arguments override the resource and VC runtime directories:

~~~powershell
tools\build-ocr-addon.bat 1.0.0
tools\build-ocr-addon.bat 1.0.0 "D:\ocr-assets" "D:\vc-runtime-x64"
tools\build-ocr-addon.bat --help
~~~

The version is applied to the resource manifest's `resourceVersion` and ZIP filename without changing source or application versions. Output is `publish/addons/ocr` plus `publish/ocr-resources-<version>-win-x64.zip` and its checksum/signature sidecars. Resources contain models, native libraries, and notices, with no separate managed engine or .NET runtime.

Invoke `Publish-OcrResources.ps1` directly with `-Version`, or omit it to use the source manifest's `resourceVersion`. It extracts ONNX/Skia native libraries and RapidOCR models from the NuGet cache, copies the repository's OrpheusNet models, and deploys verified SMT assets. `Publish-OcrAddon.ps1` is a compatibility forwarding entry point. `Publish-OcrSmtRuntime.ps1` is shared by release packaging and Debug deployment; paths can be overridden with `-SmtTrialDirectory` and `-MsvcRuntimeDirectory`.

Use `-SkipSigning` for unsigned local checks, or `-SkipArchive` for directory deployment only. Unsigned packages are not accepted for release updates. The new prefix/component ID requires old clients to update the application first; see [Update compatibility](../docs/UPDATES.md#english).

Debug deployment uses:

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Setup-OcrDevelopment.ps1
~~~

The development script supports Windows PowerShell 5.1 and deploys resources into `addons/ocr` in the application's default Debug output. It does not build a separate engine. Build Debug normally before launching the application. For custom build output, pass its `addons` path as `-AddonRootDirectory` to the resource publisher. Python is not required.

### Signing tools

`Sign-UpdatePackage.ps1` signs packages with the existing private key. Only the verification public key is stored in the repository. Private keys must not be written into scripts or release ZIPs. See [Updates and component management](../docs/UPDATES.md#english) for the signature format and installation behavior.

`New-UpdateSigningKey.ps1` is for initial setup or a planned key rotation, not for each release. Replacing the public key changes the trust relationship with existing clients.

~~~powershell
pwsh -File tools/New-UpdateSigningKey.ps1 -PrivateKeyPath "D:\secure\GenshinPiano-update-signing-private.xml"
~~~

### Models, assets, and offline maintenance

#### Icons

`generate_windows_icons.py` generates Windows icons and application assets from the master image and requires Pillow. See [Icon assets](../src/GenshinPiano.App/Assets/README.md).

#### OCR host verification

`ocr/smoke/OcrAddonSmoke.csproj` provides `recognize`, `verify`, and `install` commands. It requires the .NET 10 SDK and an actual add-on/package. The `install` command replaces the add-on in the selected directory, so use a dedicated test installation.

~~~powershell
dotnet run --project tools/ocr/smoke/OcrAddonSmoke.csproj -- recognize publish/addons/ocr "D:\scores\scan.png" "D:\results\recognized.json" Staff 0 "D:\app\GenshinPiano.exe"
dotnet run --project tools/ocr/smoke/OcrAddonSmoke.csproj -- verify publish/ocr-resources-1.0.0-win-x64.zip src/GenshinPiano.App/Assets/Security/UpdateSigningPublicKey.xml
~~~

The final `recognize` arguments are cancellation delay in milliseconds (0 disables automatic cancellation) and the host EXE. Resources require a host path. Use `install <zip> <public-key> <install-root> <host-executable>` for complete installation verification.

#### Numbered-notation model export

`ocr/models/export_orpheusnet.py` and `export_orpheusnet_srcnn.py` regenerate and verify ONNX models from local OrpheusNet source/checkpoints. They require a separate Python development environment with PyTorch, ONNX, and NumPy.

~~~powershell
python tools/ocr/models/export_orpheusnet.py "D:\research\OrpheusNet" "D:\models"
python tools/ocr/models/export_orpheusnet_srcnn.py "D:\research\srcnn.pth" "D:\models\orpheusnet-srcnn-x3.onnx"
~~~

#### Offline scoring and regression checks

`ocr/research` contains scan preprocessing, SMT output parsing, offline scoring, reference annotations, and regression utilities. They are excluded from application/OCR packages; production recognition uses the C# engine.

- `Test-Smt.ps1` runs native inference with System/Page models, records output and elapsed time, and handles timeouts. This research script requires PowerShell 7.
- `prepare_smt_scan.py` and `optimize_smt_scan.py` handle scan layout and candidates; they require NumPy and Pillow.
- `compare_staff_ocr.py` and `evaluate_smt_scan_optimization.py` score results against manual annotations in `reference`.
- `evaluate_ocr_score.py` compares numbered-notation results with a manually authored `.gpiano` file.
- `test_smt_scan_pipeline.py` checks layout and candidate protection without a model.

Page is for offline research only and needs the separate [full-page GrandStaff Q8 model](https://huggingface.co/cstr/smt-fp-grandstaff-GGUF/tree/522e448e70c5c4b5ab4ceb793610b0aad9d27c53), `smt-fp-grandstaff-q8_0.gguf`. The released add-on uses the System model. The corresponding scripts verify pinned hashes.

~~~powershell
python -B -m unittest discover -s tools/ocr/research -p test_smt_scan_pipeline.py -v
python tools/ocr/research/optimize_smt_scan.py "D:\scores\scan.png" "D:\results\new-run"
~~~
