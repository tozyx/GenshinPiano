# GenshinPiano 开发与构建指南

[简体中文](#简体中文) | [English](#english)

## 简体中文

### 1. 环境要求

- Windows 10/11 x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git
- Visual Studio Code + C# Dev Kit，或安装了“.NET 桌面开发”工作负载的 Visual Studio

项目仅面向 Windows，主界面使用 WPF。VS Code 足以完成日常编辑、编译、测试和调试；完整 Visual Studio 不是运行项目的必要条件。

自包含发布脚本会将更新器编译为 Native AOT，因此还需要 Visual Studio Build Tools 中的“使用 C++ 的桌面开发”工作负载。若尚未安装该工作负载，可先使用框架依赖版脚本。

检查 SDK：

```powershell
dotnet --info
```

### 2. 获取代码和还原依赖

在仓库根目录运行：

```powershell
dotnet restore GenshinPiano.sln
```

公司网络或离线环境中，如果依赖已经存在于本机 NuGet 缓存，但漏洞数据源暂时无法访问，可以使用：

```powershell
dotnet restore GenshinPiano.sln --ignore-failed-sources -p:NuGetAudit=false
```

不要将 `bin`、`obj` 或本机 NuGet 缓存提交到仓库。

### 3. 编译和运行 Debug 版本

编译整个解决方案：

```powershell
dotnet build GenshinPiano.sln -c Debug
```

运行 WPF 主程序：

```powershell
dotnet run --project .\src\GenshinPiano.App\GenshinPiano.App.csproj
```

如果刚清理过仓库，WPF 生成文件缺失或 `obj` 中间状态异常，可以执行：

```powershell
dotnet clean GenshinPiano.sln
dotnet restore GenshinPiano.sln
dotnet build GenshinPiano.sln -c Debug
```

直接启动 Debug 输出：

```text
src\GenshinPiano.App\bin\Debug\net10.0-windows\GenshinPiano.exe
```

程序在需要向以管理员权限运行的游戏发送按键时，也必须由用户以管理员身份启动。开发和普通本地试听不需要管理员权限。

### 4. 运行测试

```powershell
dotnet test .\tests\GenshinPiano.Core.Tests\GenshinPiano.Core.Tests.csproj -c Debug
```

提交代码前建议同时检查 Release 构建：

```powershell
dotnet build GenshinPiano.sln -c Release
```

### 5. OCR 附加包调试

OCR 引擎项目位于：

```text
src\GenshinPiano.Ocr.Engine
```

主程序从可执行文件同目录的以下位置发现 OCR 引擎：

```text
addons\ocr\manifest.json
```

发布脚本会生成完整 OCR 运行目录和独立下载包。协议和远程分发说明见：

- [OCR 附加包协议](ocr-addon-protocol.md)
- [OCR 附加包发布与更新流程](ocr-addon-distribution.md)

普通主程序开发不需要 OCR 模型。0.9.0 起，五线谱使用本机 SMT CPU 后端和量化 GGUF 模型，简谱仍使用现有 .NET 识别流程；发布包不再包含 Python/Oemer。

将手动下载的 `crispembed-windows-x86_64.zip` 和 `smt-grandstaff-q8_0.gguf` 放在 `publish\smt-experiment`（模型也可放在其 `models` 子目录），然后运行：

```powershell
.\tools\Setup-OcrDevelopment.ps1
```

脚本校验固定 SHA-256，构建 Debug OCR 引擎，部署到主程序 Debug 输出的 `addons\ocr`，并附带本地 x64 VC 运行库。可使用 `-SmtTrialDirectory` 和 `-MsvcRuntimeDirectory` 指定资源位置。不需要 Python，也不自动下载模型。旧 Oemer/Python 环境部署入口已移除。

GitHub CI 不执行模型部署脚本，只验证普通 Release 构建和测试。完整接入、兼容性和实测结果见 [SMT 接入说明](SMT_INTEGRATION.md)。

完整工具目录与用途见 [工具说明](../tools/README.md)。相关脚本：

- `tools/Setup-OcrDevelopment.ps1`：部署 Debug 附加包
- `tools/Publish-OcrSmtRuntime.ps1`：校验并部署原生 CPU Runtime 和模型
- `tools/Publish-OcrAddon.ps1`：构建独立 OCR ZIP、校验和及签名

### 6. 从干净克隆验证打包

无需签名私钥或 OCR 运行环境即可生成应用测试包，默认输出到
`artifacts/package-check/GenshinPiano-0.0.0-test-win-x64-framework-unsigned.zip`：

```powershell
.\tools\Build-UnsignedAppPackage.ps1
```

测试包包含主程序、独立更新器和示例曲谱，需要 .NET 10 Desktop Runtime。
CI 会执行该脚本。无签名测试包不能用于应用内更新。

正式发布使用 `tools` 目录中随 Git 跟踪的三个 BAT 脚本。从仓库根目录运行以下命令，或直接双击脚本；脚本会自动定位仓库根目录：

- `tools\build-release.bat`：仅构建自包含主程序、更新器及应用签名包。
- `tools\build-release-framework.bat`：仅构建框架依赖主程序、更新器及应用签名包。
- `tools\build-ocr-addon.bat`：独立构建 OCR 插件及其签名包。

两个应用发布脚本都要求以下目录存在并至少包含一个曲谱文件；OCR 独立打包不需要曲谱库：

```text
publish\songs
```

该目录中的示例曲谱可以随仓库维护。发布脚本会把它复制到应用 ZIP 根目录下的 `songs`。

正式发布还需要 RSA 私钥为应用包和 OCR 附加包签名。私钥不得提交到仓库。仅在当前 PowerShell 会话中配置：

```powershell
$env:GENSHINPIANO_UPDATE_SIGNING_KEY = "D:\secure\GenshinPiano.Update.PrivateKey.xml"
```

也可以设置为当前用户的持久环境变量：

```powershell
[Environment]::SetEnvironmentVariable(
    "GENSHINPIANO_UPDATE_SIGNING_KEY",
    "D:\secure\GenshinPiano.Update.PrivateKey.xml",
    "User")
```

重新打开终端后生效。仓库中只包含用于验证发布包的公钥；公钥不会影响其他开发者正常构建和调试。

### 7. 生成自包含 ZIP

自包含版本包含 .NET 运行时，体积更大，但目标电脑无需预装 .NET Desktop Runtime：

```cmd
tools\build-release.bat 3.1.0
```

如果不传版本参数，脚本使用自身定义的默认版本。建议正式发布时始终明确传入版本。

此脚本还会以 Native AOT 构建独立更新器，因此需要 C++ 桌面构建工具。

### 8. 生成框架依赖 ZIP

框架依赖版本更小，但用户电脑必须安装对应的 .NET 10 Desktop Runtime：

```cmd
tools\build-release-framework.bat 3.1.0
```

### 9. 发布输出

两个应用发布脚本不再调用 OCR 打包，也不依赖 OCR 模型或原生资源。它们分别在 `publish` 下生成：

```text
GenshinPiano-win-x64\
GenshinPiano-win-x64-framework\
GenshinPiano-<version>-win-x64.zip
GenshinPiano-<version>-win-x64.zip.sha256
GenshinPiano-<version>-win-x64.zip.sig
GenshinPiano-<version>-win-x64-framework.zip
GenshinPiano-<version>-win-x64-framework.zip.sha256
GenshinPiano-<version>-win-x64-framework.zip.sig
```

OCR 插件单独打包（需要 PowerShell 7、.NET 10 SDK 和同一签名私钥环境变量）：

```cmd
tools\build-ocr-addon.bat
tools\build-ocr-addon.bat 1.0.1
```

与应用 BAT 一样，第一个参数为发布版本号；省略时使用 BAT 中的默认值 `1.0.0`，双击发布前也可直接修改 `if not defined VERSION set "VERSION=1.0.0"`。版本号会应用到引擎构建、打包后的 `manifest.json` 和 ZIP 文件名，源码项目与 manifest 保持原值。支持 `1.0.1-preview.1` 这样的预发布版本。

默认从 `publish\smt-experiment` 读取 `crispembed-windows-x86_64.zip` 和 `smt-grandstaff-q8_0.gguf`（模型也可放在其 `models` 子目录）。默认从 Windows `System32` 读取三个 x64 VC 运行库 DLL。第二、第三个参数可依次指定资源目录、VC 运行库目录：

```cmd
tools\build-ocr-addon.bat 1.0.1 "D:\ocr-assets" "D:\vc-runtime-x64"
tools\build-ocr-addon.bat --help
```

独立 OCR 打包会在 `publish` 下生成：

```text
addons\ocr\
ocr-addons-<ocr-version>-win-x64.zip
ocr-addons-<ocr-version>-win-x64.zip.sha256
ocr-addons-<ocr-version>-win-x64.zip.sig
```

应用 ZIP 与 OCR ZIP 是彼此独立的下载项，可分别发布。将每个 ZIP 连同对应的 `.sha256` 和 `.sig` 一起上传到 GitHub/GitCode Release。不要把 `publish\addons` 手动塞回主程序 ZIP。

也可直接调用 PowerShell 脚本发布 OCR：

```powershell
pwsh -NoProfile -File .\tools\Publish-OcrAddon.ps1 `
    -Version 1.0.1 `
    -AddonRootDirectory .\publish\addons `
    -ArtifactsDirectory .\publish `
    -PrivateKeyPath $env:GENSHINPIANO_UPDATE_SIGNING_KEY `
    -SmtTrialDirectory "D:\ocr-assets"
```

### 10. 版本来源

- 主程序开发版本：`src/GenshinPiano.App/GenshinPiano.App.csproj`
- OCR 开发版本：`src/GenshinPiano.Ocr.Engine/GenshinPiano.Ocr.Engine.csproj`，与其 `manifest.json` 保持一致
- 应用与 OCR 发布版本：对应 BAT 的第一个参数，省略时使用 BAT 内的默认版本；直接调用 `Publish-OcrAddon.ps1` 时用 `-Version` 覆盖，省略则读取项目版本并校验源码 manifest

发布时应确保主程序项目版本、BAT 参数和 Release 标签符合预期。OCR 版本独立管理，远程下载器从 `ocr-addons-<version>-win-x64.zip` 文件名读取组件版本。

### 11. 发布前检查清单

- Debug 和 Release 均能编译
- 自动化测试全部通过
- 主程序能新建、打开、保存和试听曲谱
- 练习页的跟弹、节奏和音游开关均可开始、暂停、停止并从标记音符定位
- 音游校准与反馈音量可以保存，七种采样和内置 MIDI 在相同音量下无明显响度跳变或削波
- `publish\songs` 已准备且不包含不应分发的文件
- ZIP 根目录包含 `GenshinPiano.exe`、`GenshinPiano.Updater.exe` 和 `songs`
- 应用包和 OCR 包均生成 `.sha256` 与 `.sig`
- 私钥未进入 Git 工作树、日志或发布 ZIP
- 在一份干净解压的目录中测试启动、更新、回滚及 OCR 下载

---

## English

### Requirements

- Windows 10/11 x64
- .NET 10 SDK
- Git
- Visual Studio Code with C# Dev Kit, or Visual Studio with the **.NET desktop development** workload

The application targets Windows and uses WPF. VS Code is sufficient for normal editing, building,
testing, and debugging. The self-contained release script publishes the updater with Native AOT, so
the **Desktop development with C++** build tools are additionally required for that release variant.

### Restore, build, run, and test

Run these commands from the repository root:

```powershell
dotnet restore GenshinPiano.sln
dotnet build GenshinPiano.sln -c Debug
dotnet run --project .\src\GenshinPiano.App\GenshinPiano.App.csproj
dotnet test .\tests\GenshinPiano.Core.Tests\GenshinPiano.Core.Tests.csproj -c Debug
dotnet build GenshinPiano.sln -c Release
```

For an offline or restricted network with all packages already cached:

```powershell
dotnet restore GenshinPiano.sln --ignore-failed-sources -p:NuGetAudit=false
```

### Release signing

Prepare `publish\songs`, then point the environment variable to the private RSA signing key. Never
commit the private key:

```powershell
$env:GENSHINPIANO_UPDATE_SIGNING_KEY = "D:\secure\GenshinPiano.Update.PrivateKey.xml"
```

The public verification key committed to the repository does not prevent contributors from building
or debugging the application.

### OCR development environment

Normal application development does not require OCR model files. Since add-on 0.9.0, staff recognition uses the native SMT CPU runtime and a quantized GGUF model; numbered notation retains the existing .NET pipeline. Python/Oemer is no longer shipped.

Place the downloaded `crispembed-windows-x86_64.zip` and `smt-grandstaff-q8_0.gguf` in `publish\smt-experiment` (the model may also be in its `models` subfolder), then run:

```powershell
.\tools\Setup-OcrDevelopment.ps1
```

The script verifies pinned hashes and deploys the Debug engine, model, and app-local x64 VC runtime to the application's Debug `addons\ocr` directory. Override resource paths with `-SmtTrialDirectory` and `-MsvcRuntimeDirectory`. No Python installation or model download is required. The legacy Oemer/Python setup path has been removed.

See [SMT integration](SMT_INTEGRATION.md) for scope, compatibility, and validation. CI only builds and tests the regular solution without downloading OCR models.

### Publish packages

A clean clone can build an unsigned, framework-dependent test ZIP with
`pwsh -File tools/Build-UnsignedAppPackage.ps1`. The CI job runs the same script.
The ZIP includes the application, standalone updater, and sample scores. It is
not accepted by the in-app updater. The three signing launchers below are tracked under `tools`.
Run them from the repository root or double-click them; they resolve the repository root automatically.

Self-contained package (includes .NET; Native AOT C++ prerequisites required for the updater):

```cmd
tools\build-release.bat 3.1.0
```

Framework-dependent package (requires .NET 10 Desktop Runtime on the target machine):

```cmd
tools\build-release-framework.bat 3.1.0
```

These two scripts build only the application, updater, and application ZIP/checksum/signature set.
They do not package OCR or require OCR assets. Package the OCR add-on independently with PowerShell 7:

```cmd
tools\build-ocr-addon.bat
tools\build-ocr-addon.bat 1.0.1
tools\build-ocr-addon.bat 1.0.1 "D:\ocr-assets" "D:\vc-runtime-x64"
tools\build-ocr-addon.bat --help
```

The first argument is the OCR release version, defaulting to `1.0.0` in the BAT; edit that default for
double-click publishing. It sets the build version, packaged manifest version, and ZIP name without
modifying source versions. The second and third arguments override the SMT resource directory and
x64 VC runtime directory, defaulting to `publish\smt-experiment` and Windows `System32`. The launcher
uses `GENSHINPIANO_UPDATE_SIGNING_KEY` and creates `publish\addons\ocr` plus the OCR ZIP, checksum,
and signature. You can also run `tools/Publish-OcrAddon.ps1` directly with `-Version` as shown above. Without `-Version`,
the PowerShell script reads the project version and checks the source manifest matches it.

Upload each application or OCR ZIP together with its matching `.sha256` and `.sig` files. The OCR
add-on is not part of the application ZIP, and the packages can be released separately.

See [OCR add-on distribution](ocr-addon-distribution.md) and
[OCR protocol](ocr-addon-protocol.md) for component-specific details.

Before publishing, test a clean extracted copy, application update and rollback, OCR component
download, score loading/saving, local playback, all practice modes, rhythm-game calibration, and
sampled/MIDI loudness at the same volume setting.
