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

### 6. 准备正式发布

两个发布脚本都要求以下目录存在并至少包含一个曲谱文件：

```text
publish\songs
```

该目录是本地发布素材，不应提交到 Git。发布脚本会把它复制到应用 ZIP 根目录下的 `songs`。

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
build-release.bat 3.0.1-preview.1
```

如果不传版本参数，脚本使用自身定义的默认版本。建议正式发布时始终明确传入版本。

此脚本还会以 Native AOT 构建独立更新器，因此需要 C++ 桌面构建工具。

### 8. 生成框架依赖 ZIP

框架依赖版本更小，但用户电脑必须安装对应的 .NET 10 Desktop Runtime：

```cmd
build-release-framework.bat 3.0.1-preview.1
```

### 9. 发布输出

脚本会在 `publish` 下生成：

```text
GenshinPiano-win-x64\
GenshinPiano-win-x64-framework\
GenshinPiano-<version>-win-x64.zip
GenshinPiano-<version>-win-x64.zip.sha256
GenshinPiano-<version>-win-x64.zip.sig
GenshinPiano-<version>-win-x64-framework.zip
GenshinPiano-<version>-win-x64-framework.zip.sha256
GenshinPiano-<version>-win-x64-framework.zip.sig
addons\ocr\
ocr-addons-<ocr-version>-win-x64.zip
ocr-addons-<ocr-version>-win-x64.zip.sha256
ocr-addons-<ocr-version>-win-x64.zip.sig
```

应用 ZIP 与 OCR ZIP 是彼此独立的下载项。将每个 ZIP 连同对应的 `.sha256` 和 `.sig` 一起上传到 GitHub/GitCode Release。不要把 `publish\addons` 手动塞回主程序 ZIP。

### 10. 版本来源

- 主程序开发版本：`src/GenshinPiano.App/GenshinPiano.App.csproj`
- OCR 引擎版本：`src/GenshinPiano.Ocr.Engine/GenshinPiano.Ocr.Engine.csproj`
- 发布 ZIP 版本：传给 BAT 的第一个参数

发布时应确保主程序项目版本、BAT 参数和 Release 标签符合预期。OCR 版本独立管理，远程下载器从 `ocr-addons-<version>-win-x64.zip` 文件名读取组件版本。

### 11. 发布前检查清单

- Debug 和 Release 均能编译
- 自动化测试全部通过
- 主程序能新建、打开、保存和试听曲谱
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

### Publish packages

Self-contained package (includes .NET; Native AOT C++ prerequisites required for the updater):

```cmd
build-release.bat 3.0.1-preview.1
```

Framework-dependent package (requires .NET 10 Desktop Runtime on the target machine):

```cmd
build-release-framework.bat 3.0.1-preview.1
```

Both scripts create application ZIP/checksum/signature artifacts and an independently versioned OCR
add-on ZIP/checksum/signature set under `publish`. Upload every ZIP together with its matching
`.sha256` and `.sig` files. The OCR add-on is not part of the application ZIP.

See [OCR add-on distribution](ocr-addon-distribution.md) and
[OCR protocol](ocr-addon-protocol.md) for component-specific details.

Before publishing, test a clean extracted copy, application update and rollback, OCR component
download, score loading/saving, and local playback.
