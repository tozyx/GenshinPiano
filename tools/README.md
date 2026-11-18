# 构建与维护工具

[简体中文](#简体中文) | [English](#english)

## 简体中文

本目录提供应用构建、OCR 资源部署、签名和离线维护工具。以下命令从仓库根目录运行。项目原理与软件使用见 [项目文档](../docs/README.md)。

### 先选择需要的操作

- **从源代码构建**：把源码编译为程序，检查能否编译，不自动打开软件，也不生成发布 ZIP。
- **从源代码运行**：在本机编译并启动软件，适合体验、调试或修改后验证。
- **自行打包**：生成可解压运行的 Release ZIP，可选择自带 .NET 运行时，或使用目标电脑已安装的运行时。普通用户无需私钥。
- **签名发布**：维护者生成应用内更新认可的签名包，需要对应私钥。

以下版本号 `3.1.1` 仅为示例。打包参数只设置包版本，**不会切换源代码版本**；请先检出希望构建的标签或提交。

### 准备源码与环境

本地构建需要 Windows 10/11 x64、Git 和 **.NET 10 SDK**（只安装 Runtime 不够）。SDK 版本遵循根目录的 `global.json`。命令行构建无需安装 IDE；可选用支持 WPF 的 Visual Studio。生成自带运行时的包时，Native AOT 更新器还需要 Visual Studio / Build Tools 的 **“使用 C++ 的桌面开发”**工作负载及 Windows SDK。

~~~powershell
git clone --branch v3.0 https://github.com/tozyx/GenshinPiano.git
cd GenshinPiano
# 如需固定版本：git checkout <你已核对的标签或完整提交号>
git rev-parse HEAD
dotnet --version
~~~

下文使用 Windows 自带的 PowerShell 5.1，也可改用 PowerShell 7。首次构建需要联网还原 NuGet 依赖。模型与 CrispEmbed 原始 ZIP 已随源码纳入 Git，无需 Git LFS；普通构建不会把这些可选资源自动部署到运行目录。`bin`、`obj`、`artifacts` 和生成的发布包不纳入 Git。

### 从源代码构建（无需私钥）

~~~powershell
dotnet restore GenshinPiano.sln
dotnet build GenshinPiano.sln -c Debug --no-restore
~~~

主程序生成在 `src/GenshinPiano.App/bin/Debug/net10.0-windows/GenshinPiano.exe`。这一步只编译，不启动程序。运行测试可执行：

~~~powershell
dotnet test tests/GenshinPiano.Core.Tests/GenshinPiano.Core.Tests.csproj -c Debug --no-build
~~~

### 从源代码运行（无需私钥）

直接编译并启动：

~~~powershell
dotnet run --project src/GenshinPiano.App/GenshinPiano.App.csproj -c Debug
~~~

如果已完成上面的 Debug 构建，可跳过编译：

~~~powershell
dotnet run --project src/GenshinPiano.App/GenshinPiano.App.csproj -c Debug --no-build
~~~

需要使用 OCR 时，先关闭正在运行的软件，部署可选资源，再重新运行：

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Setup-OcrDevelopment.ps1
dotnet run --project src/GenshinPiano.App/GenshinPiano.App.csproj -c Debug --no-build
~~~

该脚本部署到默认 Debug 输出的 `addons/ocr`，无需私钥、Python 或额外 .NET 运行时。自定义输出路径时，改用下面的资源发布脚本指定 `-AddonRootDirectory`。

### 自行生成可运行的 ZIP（无需私钥）

不希望直接使用 Release 成品时，可在已核对的源码上运行以下命令。两个版本都包含主程序、更新器、音色和曲谱，解压整个 ZIP 后运行 `GenshinPiano.exe`。

**自带运行时（SelfContained）**：目标电脑不需要安装 .NET；构建电脑需要上述 C++ 工具链。

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-UnsignedAppPackage.ps1 -Version 3.1.1 -PackageType SelfContained
~~~

**使用已安装运行时（Framework）**：构建电脑无需 C++ 工具链；目标电脑需要 [.NET 10 Desktop Runtime x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)，不是仅用于控制台或 ASP.NET 的运行时。

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-UnsignedAppPackage.ps1 -Version 3.1.1 -PackageType Framework
~~~

默认输出到 `artifacts/package-check`：

- `GenshinPiano-3.1.1-win-x64-unsigned.zip`：自带运行时。
- `GenshinPiano-3.1.1-win-x64-framework-unsigned.zip`：依赖已安装运行时。
- 每个 ZIP 附带 `.sha256` 和 `.build-info.json`，记录校验值、源码提交、工作树是否有改动、SDK、包类型和 OCR 资源版本。

可增加 `-IncludeOcrResources`，将 OCR 模型及原生依赖部署到包内的 `addons/ocr`，解压后即可使用识谱；省略时仅内置识别代码。OCR 资源本身不包含 .NET。用 `-OutputDirectory` 更换输出目录；已有同名产物时脚本会停止，避免覆盖。

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-UnsignedAppPackage.ps1 -Version 3.1.1 -PackageType SelfContained -IncludeOcrResources -OutputDirectory artifacts/my-build
Get-FileHash artifacts/my-build/GenshinPiano-3.1.1-win-x64-unsigned.zip -Algorithm SHA256
~~~

这些命令不会读取签名私钥，也不需要设置 `GENSHINPIANO_UPDATE_SIGNING_KEY`。无签名包可解压独立运行，但不能作为签名更新包导入应用内更新器。自构建程序仍保留源码中配置的官方更新信任关系；希望持续使用自己构建的版本时，请更新源码后重新构建。

### 在 GitHub Actions 中自行构建（无需私钥）

仓库提供 [Build from source 工作流](../.github/workflows/build-from-source.yml)，适合不想在本机安装构建工具的用户：

1. Fork 仓库，在自己的仓库启用 Actions，并核对待构建的源码。手动工作流文件必须存在于该仓库的默认分支，才能显示运行入口。
2. 打开 **Actions → Build from source → Run workflow**，选择包含此工作流的分支，填写包版本，按需勾选包含 OCR 资源。
3. 工作流测试后分别构建 Framework 和 SelfContained 两个版本，无需上传私钥或配置 Secrets，也不会发布 Release。
4. 完成后在该次运行的 **Artifacts** 下载对应版本。解开 GitHub 的外层压缩包，再解开其中的应用 ZIP，运行主程序。产物保留 14 天，构建信息记录实际源码提交号。

手动运行规则见 [GitHub 官方说明](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow)。Actions 运行在 GitHub 托管环境；需要完全在自己的电脑构建时，请使用上面的本地命令。

自行构建可明确应用源码与产物的对应关系，但仍使用 NuGet 依赖、Microsoft 运行时，以及仓库中预编译的 CrispEmbed 与模型权重，并不等于从源码重新编译了所有第三方依赖，也不保证与官方包逐字节相同。资源来源和固定校验值见 [OCR 资源说明](../src/GenshinPiano.Ocr.Engine/models/README.md)。

### 维护者签名发布（需要私钥）

普通用户不需要作者私钥。仅当维护对应的更新信任链时，使用以下 BAT 生成签名发布包；这里的 `.sig` 是应用更新验签文件，不是 Windows Authenticode 签名。

~~~powershell
$env:GENSHINPIANO_UPDATE_SIGNING_KEY = 'D:\secure\GenshinPiano-update-signing-private.xml'
.\tools\build-release.bat 3.1.1
.\tools\build-release-framework.bat 3.1.1
.\tools\build-ocr-addon.bat 1.0.0
~~~

私钥路径仅为示例，实际密钥应保存在仓库外。三个 BAT 支持双击运行并自动定位仓库；第一个参数覆盖各自默认版本。应用发布需要 `publish/songs` 中至少一个曲谱。两个应用 BAT 均不包含可选 OCR 资源，OCR BAT 单独发布资源包。

应用输出到 `publish/GenshinPiano-win-x64` 或 `publish/GenshinPiano-win-x64-framework`，同时生成对应 ZIP、`.sha256` 和 `.sig`。发布更新时需上传 ZIP 与两个配套文件。自签密钥不会自动获得官方客户端信任，公钥变更与兼容规则见 [更新机制](../docs/UPDATES.md)。

仓库保留三个 BAT 的初始版本，便于其他电脑克隆后使用。`.gitignore` 不会隐藏已跟踪脚本的本地设置改动；需要保留本地版本号等设置时，可在每个克隆中执行一次：

~~~powershell
git update-index --skip-worktree tools/build-release.bat tools/build-release-framework.bat tools/build-ocr-addon.bat
~~~

恢复跟踪时将 `--skip-worktree` 换为 `--no-skip-worktree`。该标记仅用于本地设置管理，不能代替将私钥保存在仓库外。

### OCR 资源与发布

OCR 资源发布需要 .NET 10 SDK 和同一签名密钥环境变量。BAT 优先使用可用的 PowerShell 7，找不到时自动使用 Windows 自带的 PowerShell 5.1，无需另行安装 PowerShell 7。仓库在 `src/GenshinPiano.Ocr.Engine/models/smt-resource` 中提供以下固定资源，克隆后无需另行下载；文件缺失时可从以下上游来源恢复。模型也可以放在该目录的 `models` 子目录：

- [CrispEmbed v0.17.18 CPU Windows x64](https://github.com/CrispStrobe/CrispEmbed/releases/tag/v0.17.18)：`crispembed-windows-x86_64.zip`。
- [GrandStaff Q8 模型](https://huggingface.co/cstr/smt-grandstaff-GGUF/tree/8e1595591659aa03cf1e71aab5aa03e8f3fccd7e)：`smt-grandstaff-q8_0.gguf`。

保留 CrispEmbed 原始 ZIP，无需手动解压。它提供原生推理程序，不是 GGUF 模型；发布脚本校验固定 SHA-256，仅提取必要 EXE/DLL 和声明，另复制 GGUF 模型及 x64 VC 运行库，不会把 ZIP、服务器、量化工具和开发头文件塞进资源包。VC DLL 默认从 Windows `System32` 读取。输入检查在替换旧部署目录前完成。可覆盖资源和 VC 运行库目录：

~~~powershell
tools\build-ocr-addon.bat 1.0.0
tools\build-ocr-addon.bat 1.0.0 "D:\ocr-assets" "D:\vc-runtime-x64"
tools\build-ocr-addon.bat --help
~~~

版本参数应用到资源 manifest 的 `resourceVersion` 和 ZIP 名称，不修改源码版本或主程序版本。输出为 `publish/addons/ocr` 和 `publish/ocr-resources-<version>-win-x64.zip` 及其校验和、签名。资源包只有模型、原生运行库与声明，不包含独立托管引擎或 .NET。

也可直接调用 `Publish-OcrResources.ps1`，使用 `-Version` 指定资源版本；省略时读取源码 manifest 的 `resourceVersion`。脚本从 NuGet 缓存提取 ONNX/Skia 原生库与 RapidOCR 模型，复制仓库的 OrpheusNet 模型，并部署校验过的 SMT 资源。`Publish-OcrAddon.ps1` 仅作为旧命令的转发入口。发布及调试部署统一支持 `-SmtResourceDirectory` 和 `-MsvcRuntimeDirectory`，旧参数名 `-SmtTrialDirectory` 仍为兼容别名。资源来源、目录分工和固定校验值见 [OCR 资源说明](../src/GenshinPiano.Ocr.Engine/models/README.md)。

普通用户也可单独构建 OCR 资源包，无需私钥：

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Publish-OcrResources.ps1 -Version 1.0.0 -AddonRootDirectory artifacts/manual-ocr/addons -ArtifactsDirectory artifacts/manual-ocr/packages -SkipSigning
~~~

结果位于 `artifacts/manual-ocr/packages`；本机使用时，也可直接把部署好的 `artifacts/manual-ocr/addons/ocr` 复制到关闭后的软件目录下的 `addons/ocr`。无签名资源 ZIP 不能通过要求验签的安装/更新入口导入。仅部署目录、不生成 ZIP 时可再加 `-SkipArchive`。新资源包使用独立文件名前缀与组件标识，旧客户端需先升级主程序，见 [更新兼容规则](../docs/UPDATES.md)。

OCR 资源默认从源码模型目录读取；旧版初始 BAT 若仍传入 `publish/smt-resource`，该目录不存在时会转用源码模型目录。其他自定义目录仍严格按传入值检查。SMT 模型与 CrispEmbed 原始 ZIP 直接纳入 Git，不使用 Git LFS；模型目录内只忽略解压和测试产物。

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

- `Test-Smt.ps1`：需要 PowerShell 7；单次原生推理，支持 System/Page 模型，记录输出、耗时并处理超时。默认从源码模型目录读取输入，解压文件和结果写入 `artifacts/smt-research`。
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

### Choose an operation

- **Build from source**: compile the code without launching the application or creating a release ZIP.
- **Run from source**: compile and start the application locally to try it, debug it, or check changes.
- **Create your own package**: produce a Release ZIP that can be extracted and run, either with .NET included or using an installed runtime. No private key is needed.
- **Publish signed updates**: maintainers create packages accepted by the in-app updater using the corresponding private key.

Version `3.1.1` below is an example. Package parameters set the output version; **they do not select a source revision**. Check out the tag or commit you intend to build first.

### Get the source and prerequisites

Local builds require Windows 10/11 x64, Git, and the **.NET 10 SDK** (the runtime alone is insufficient). SDK selection follows `global.json` at the repository root. No IDE is required for command-line builds; Visual Studio with WPF support is optional. Self-contained packaging also requires Visual Studio / Build Tools with **Desktop development with C++** and a Windows SDK for the Native AOT updater.

~~~powershell
git clone --branch v3.0 https://github.com/tozyx/GenshinPiano.git
cd GenshinPiano
# To pin a revision: git checkout <reviewed-tag-or-full-commit-id>
git rev-parse HEAD
dotnet --version
~~~

Commands below use built-in Windows PowerShell 5.1; PowerShell 7 also works. The first build needs network access to restore NuGet dependencies. Models and the original CrispEmbed ZIP are committed directly to Git, with no Git LFS requirement. Ordinary builds do not deploy those optional resources into the runtime directory. Build directories (`bin`, `obj`, `artifacts`) and generated release packages are excluded from Git.

### Build from source (no private key)

~~~powershell
dotnet restore GenshinPiano.sln
dotnet build GenshinPiano.sln -c Debug --no-restore
~~~

The main executable is `src/GenshinPiano.App/bin/Debug/net10.0-windows/GenshinPiano.exe`. This compiles the application without starting it. To run the tests:

~~~powershell
dotnet test tests/GenshinPiano.Core.Tests/GenshinPiano.Core.Tests.csproj -c Debug --no-build
~~~

### Run from source (no private key)

Compile and start the application:

~~~powershell
dotnet run --project src/GenshinPiano.App/GenshinPiano.App.csproj -c Debug
~~~

After completing the Debug build above, skip compilation with:

~~~powershell
dotnet run --project src/GenshinPiano.App/GenshinPiano.App.csproj -c Debug --no-build
~~~

For OCR, close the running application, deploy its optional resources, and start it again:

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Setup-OcrDevelopment.ps1
dotnet run --project src/GenshinPiano.App/GenshinPiano.App.csproj -c Debug --no-build
~~~

The setup script deploys to `addons/ocr` under the default Debug output. No private key, Python, or additional .NET runtime is needed. For custom output directories, use the resource publisher below with `-AddonRootDirectory`.

### Create a runnable ZIP (no private key)

To build your own copy instead of using a Release download, run these commands against the source you reviewed. Both variants include the application, updater, instrument sounds, and bundled scores. Extract the entire ZIP and launch `GenshinPiano.exe`.

**SelfContained** includes .NET, so the destination computer does not need a .NET installation. The build computer needs the C++ toolchain described above.

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-UnsignedAppPackage.ps1 -Version 3.1.1 -PackageType SelfContained
~~~

**Framework** uses an installed runtime. Its build does not need the C++ toolchain; the destination computer needs [.NET 10 Desktop Runtime x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), rather than just the console or ASP.NET runtime.

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-UnsignedAppPackage.ps1 -Version 3.1.1 -PackageType Framework
~~~

Default output is `artifacts/package-check`:

- `GenshinPiano-3.1.1-win-x64-unsigned.zip`: runtime included.
- `GenshinPiano-3.1.1-win-x64-framework-unsigned.zip`: requires an installed runtime.
- Each ZIP has `.sha256` and `.build-info.json` sidecars recording the checksum, source commit, working-tree status, SDK, package type, and OCR resource version.

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-UnsignedAppPackage.ps1 -Version 3.1.1 -PackageType SelfContained -IncludeOcrResources -OutputDirectory artifacts/my-build
Get-FileHash artifacts/my-build/GenshinPiano-3.1.1-win-x64-unsigned.zip -Algorithm SHA256
~~~

Add `-IncludeOcrResources` to either variant to deploy OCR models and native dependencies into the ZIP. These commands never read a signing private key or require `GENSHINPIANO_UPDATE_SIGNING_KEY`. Unsigned packages run independently after extraction but cannot be imported as signed update packages. Self-built applications retain the official update trust configuration in the source; update the source and rebuild if you want to keep using your own builds.

### Build with GitHub Actions (no private key)

The [Build from source workflow](../.github/workflows/build-from-source.yml) provides builds without installing a local toolchain:

1. Fork the repository, enable Actions in your fork, and review the source to build. The manual workflow must exist on the fork's default branch for the run button to appear.
2. Open **Actions → Build from source → Run workflow**, select a branch containing this workflow, enter a package version, and optionally include OCR resources.
3. The workflow tests and builds Framework and SelfContained packages separately. It needs no private keys or Secrets and does not publish a Release.
4. Download the desired **Artifacts** from the completed run. Extract GitHub's outer archive, then the application ZIP inside it, and launch the application. Artifacts are retained for 14 days; build information records the actual source commit.

See [GitHub's manual workflow instructions](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow). Actions uses GitHub-hosted infrastructure; use the local commands above if you want to build entirely on your own computer.

Building from source makes the application source revision explicit, but still uses NuGet packages, Microsoft runtimes, precompiled CrispEmbed, and model weights. It does not rebuild every third-party dependency from source or promise byte-identical output to official packages. See [OCR source assets](../src/GenshinPiano.Ocr.Engine/models/README.md#english) for upstream sources and pinned checksums.

### Publish signed updates (private key required)

Ordinary users do not need the author's private key. These BAT files are for maintainers of the corresponding update trust chain. Their `.sig` files are application update signatures, not Windows Authenticode signatures.

~~~powershell
$env:GENSHINPIANO_UPDATE_SIGNING_KEY = 'D:\secure\GenshinPiano-update-signing-private.xml'
.\tools\build-release.bat 3.1.1
.\tools\build-release-framework.bat 3.1.1
.\tools\build-ocr-addon.bat 1.0.0
~~~

The key path is an example; keep the actual key outside the repository. The three BAT files support double-clicking and locate the repository automatically. The first argument overrides their default versions. Application packaging requires at least one score under `publish/songs`. Both application BAT files exclude optional OCR resources; the OCR BAT publishes them separately.

Application output goes to `publish/GenshinPiano-win-x64` or `publish/GenshinPiano-win-x64-framework`, with ZIP, `.sha256`, and `.sig` files. Upload the ZIP and both sidecars together for updates. A self-generated key does not automatically gain official client trust; see [Update mechanisms](../docs/UPDATES.md#english) for public-key changes and compatibility.

The repository retains starter BAT files for other computers. `.gitignore` does not hide edits to tracked scripts. To retain local version/settings changes, run once per clone:

~~~powershell
git update-index --skip-worktree tools/build-release.bat tools/build-release-framework.bat tools/build-ocr-addon.bat
~~~

Use `--no-skip-worktree` to track changes again. This is local settings management and does not replace keeping private keys outside the repository.

### OCR resources and publishing

OCR publishing requires the .NET 10 SDK and the same signing-key environment variable. The BAT prefers available PowerShell 7 and otherwise uses built-in Windows PowerShell 5.1, so installing PowerShell 7 is optional for release packaging.

The repository includes these pinned assets in `src/GenshinPiano.Ocr.Engine/models/smt-resource`, so no separate download is needed after cloning. The upstream links can be used to recover missing files. The model can alternatively be placed in that directory's `models` subfolder:

- [CrispEmbed v0.17.18 CPU Windows x64](https://github.com/CrispStrobe/CrispEmbed/releases/tag/v0.17.18): `crispembed-windows-x86_64.zip`.
- [GrandStaff Q8 model](https://huggingface.co/cstr/smt-grandstaff-GGUF/tree/8e1595591659aa03cf1e71aab5aa03e8f3fccd7e): `smt-grandstaff-q8_0.gguf`.

The publisher checks pinned SHA-256 hashes, extracts the required runtime files, licenses, and model, and includes x64 VC runtime DLLs. VC DLLs default to Windows `System32`. The second and third BAT arguments override the resource and VC runtime directories:

~~~powershell
tools\build-ocr-addon.bat 1.0.0
tools\build-ocr-addon.bat 1.0.0 "D:\ocr-assets" "D:\vc-runtime-x64"
tools\build-ocr-addon.bat --help
~~~

The version is applied to the resource manifest's `resourceVersion` and ZIP filename without changing source or application versions. Output is `publish/addons/ocr` plus `publish/ocr-resources-<version>-win-x64.zip` and its checksum/signature sidecars. Resources contain models, native libraries, and notices, with no separate managed engine or .NET runtime.

Keep the original CrispEmbed ZIP without manually extracting it. It supplies the native inference executable and libraries, while the GGUF file supplies the model weights. The publisher verifies pinned hashes and extracts only the necessary EXE/DLL files and notices; it excludes the original ZIP, server, quantization tool, headers, and import libraries. It validates inputs before replacing an existing deployment.

Invoke `Publish-OcrResources.ps1` directly with `-Version`, or omit it to use the source manifest's `resourceVersion`. It extracts ONNX/Skia native libraries and RapidOCR models from the NuGet cache, copies the repository's OrpheusNet models, and deploys verified SMT assets. `Publish-OcrAddon.ps1` is a compatibility forwarding entry point. Release and Debug deployment accept `-SmtResourceDirectory` and `-MsvcRuntimeDirectory`; the previous `-SmtTrialDirectory` name remains an alias. See [OCR source assets](../src/GenshinPiano.Ocr.Engine/models/README.md#english) for the inventory, directory roles, and pinned checksums.

Users can also build OCR resources separately without a private key:

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Publish-OcrResources.ps1 -Version 1.0.0 -AddonRootDirectory artifacts/manual-ocr/addons -ArtifactsDirectory artifacts/manual-ocr/packages -SkipSigning
~~~

Packages are written to `artifacts/manual-ocr/packages`. For local use, close the application and copy the deployed `artifacts/manual-ocr/addons/ocr` into its `addons/ocr` directory. The unsigned resource ZIP cannot be imported through installers/updaters that require signature verification. Add `-SkipArchive` to deploy only the directory. The new prefix/component ID requires old clients to update the application first; see [Update compatibility](../docs/UPDATES.md#english).

The OCR publisher defaults to the source model directory. Older starter BATs that still pass `publish/smt-resource` are supported: if that former default does not exist, the publisher uses the source model directory. Other explicit overrides remain strict. The SMT model and original CrispEmbed ZIP are stored directly in Git without Git LFS; only extracted files and test outputs are ignored in the model directory.

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

- `Test-Smt.ps1` runs native inference with System/Page models, records output and elapsed time, and handles timeouts. Inputs default to the source model directory; extracted tools and results default to `artifacts/smt-research`. This research script requires PowerShell 7.
- `prepare_smt_scan.py` and `optimize_smt_scan.py` handle scan layout and candidates; they require NumPy and Pillow.
- `compare_staff_ocr.py` and `evaluate_smt_scan_optimization.py` score results against manual annotations in `reference`.
- `evaluate_ocr_score.py` compares numbered-notation results with a manually authored `.gpiano` file.
- `test_smt_scan_pipeline.py` checks layout and candidate protection without a model.

Page is for offline research only and needs the separate [full-page GrandStaff Q8 model](https://huggingface.co/cstr/smt-fp-grandstaff-GGUF/tree/522e448e70c5c4b5ab4ceb793610b0aad9d27c53), `smt-fp-grandstaff-q8_0.gguf`. The released add-on uses the System model. The corresponding scripts verify pinned hashes.

~~~powershell
python -B -m unittest discover -s tools/ocr/research -p test_smt_scan_pipeline.py -v
python tools/ocr/research/optimize_smt_scan.py "D:\scores\scan.png" "D:\results\new-run"
~~~
