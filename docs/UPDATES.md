# 更新与组件管理

[简体中文](#简体中文) | [English](#english)

## 简体中文

主程序包含 OCR 识别代码，OCR 模型与原生依赖作为独立版本的可选资源包下载和安装。GitHub、GitCode 提供同名发布资产，软件选择可用更新源；联网、自动更新和预览版由用户设置控制。

### 手动检查与静默更新

主界面手动“检查更新”会并行读取主程序和 OCR 的版本信息，完成后显示已安装版本、最新版本及各自的检查结果。用户可以更新程序、安装或更新 OCR，或者取消；单纯检查版本不会启动新的包下载。一个组件检查失败不阻止另一个组件显示结果。

自动更新延续主程序的后台下载和退出/重启安装流程。用户在后台下载时手动检查、关闭版本窗口或选择 OCR，不会取消已有主程序下载；选择同一程序更新时会等待并复用已验证的下载结果。较旧选择不会覆盖已经准备好的较新更新。

OCR 插件由用户主动下载安装，不随主程序自动更新静默下载。OCR 窗口显示当前、可更新或未知状态，并与主界面共享检查结果；打开窗口时可复用最近五分钟内的检查信息。

关闭自动更新会取消自动操作。关闭联网还会取消手动网络操作；已安装插件的本地识别不需要联网。

### 版本与发布资产

主程序版本来自应用发布包，OCR 资源版本来自包文件名和 manifest 的 `resourceVersion`。资源包可以附在主程序 Release 中分发，版本不必与 Release 标签相同；识别代码的修复随主程序更新。

主程序有自包含和 framework 两种 Windows x64 包。每个 ZIP 均配套校验和与签名：

~~~text
GenshinPiano-<version>-win-x64.zip
GenshinPiano-<version>-win-x64.zip.sha256
GenshinPiano-<version>-win-x64.zip.sig

GenshinPiano-<version>-win-x64-framework.zip
GenshinPiano-<version>-win-x64-framework.zip.sha256
GenshinPiano-<version>-win-x64-framework.zip.sig

ocr-resources-<resource-version>-win-x64.zip
ocr-resources-<resource-version>-win-x64.zip.sha256
ocr-resources-<resource-version>-win-x64.zip.sig
~~~

主程序 ZIP 包含应用、托管 OCR 代码、独立更新器和示例曲谱。资源 ZIP 根目录包含 `addons/ocr`，其中只有模型、原生依赖、manifest 与许可声明，不携带第二份 .NET 运行时或托管引擎。

新资源包使用组件标识 `addon.ocr.resources.win-x64`，旧完整插件使用 `addon.ocr.win-x64` / `ocr-addons-*`。新客户端仅从在线发布中选择资源包；旧客户端会忽略新文件名前缀，需要先升级主程序。兼容的旧完整插件仍可使用，新客户端允许同版本迁移为资源包，并在成功后移除冗余引擎和 .NET 文件。同类资源不会自动降级。回滚到不支持资源包的旧主程序后，需重新安装与其兼容的旧完整插件。

更新源支持断点下载。GitHub/GitCode 返回不同版本时，在首个有效响应后的短暂等待窗口内比较结果，优先选择已返回的较新兼容版本；另一来源失败或超时则使用可用结果。

### 下载校验与信任

下载器首先校验 ZIP 的 SHA-256，再使用程序内置公钥验证 RSA-3072/SHA-256 签名。签名为 PKCS#1 v1.5 格式，`.sig` 保存 Base64 文本。

签名内容是以下 UTF-8 文本，行尾为 LF，哈希使用大写十六进制：

~~~text
GenshinPiano.Update.v1
<ZIP 文件名>
<SHA-256>
~~~

最后一行也包含换行符。签名绑定协议标识、文件名和内容哈希，避免已签名文件被改名冒充其他版本。校验文件或签名缺失、下载损坏、签名无效时，不会进入可安装状态。

仓库和发布包仅包含公钥。私钥保存在维护者的仓库外位置，发布时由环境变量提供。私钥不进入源码、应用包或 OCR 包；更换公钥需要处理已发布客户端的信任衔接。

### 主程序安装与回滚

下载验证完成后，主程序将包安全解压到更新暂存目录，创建安装计划，并从缓存中启动独立更新器。更新器等待主程序退出后替换应用文件，保留备份，并按安装计划重启程序。

更新和回滚保留 `config`、`songs`、`logs`、`update-cache`、`addons` 和 `data`。安装失败时尝试恢复旧程序；“手动回滚”使用最近的可用备份。主程序回滚不会同时降级 OCR 插件。

### OCR 安装与替换

OCR 下载使用独立缓存。安装器解压到暂存目录，检查 ZIP 路径、manifest 协议、引擎兼容代次、组件类型、签名元数据中的版本和最低必需资源，随后替换整个 `addons/ocr`。替换失败时恢复旧组件目录，成功替换会清除旧版本遗留的运行文件。手动安装后再次读取本地版本，即使沿用最近的远程检查结果，也会重新判断更新或迁移状态。

识别任务与插件替换互斥，正在识别时不替换引擎。安装成功后刷新主界面和 OCR 窗口中的版本状态。插件使用范围与接口见 [OCR 插件](OCR.md)。

构建与发布命令见 [工具说明](../tools/README.md)。

## English

The application contains OCR recognition code. Models and native dependencies are an optional resource package with its own version, download, and installation flow. GitHub and GitCode serve matching asset names. Network access, automatic updates, and preview releases are controlled by user settings.

### Manual checks and background updates

Manual **Check for updates** retrieves application and OCR metadata in parallel, then displays installed/latest versions and each check's result. Users can update the application, install/update OCR, or cancel. Checking metadata alone starts no new package download. A failed check for one component does not block the other component's result.

Automatic updates retain background application downloads and installation on exit/restart. A manual check, closing the overview, or choosing OCR does not cancel an active background application download. Selecting the same application update waits for and reuses the verified result. An older selection cannot replace a newer prepared update.

OCR installation and downloads are user initiated, not silently bundled with automatic application updates. The OCR dialog displays current, update-available, or unknown status and shares check results with the main window. Opening the dialog can reuse metadata checked within the last five minutes.

Disabling automatic updates cancels automatic operations. Disabling network access also cancels manual network operations. Local recognition with an installed add-on requires no network access.

### Versions and release assets

Application versions come from application packages. OCR resource versions come from the package filename and manifest's `resourceVersion`. Resource packages can be attached to an application Release without matching its tag. Recognition-code fixes ship through application updates.

The application has self-contained and framework-dependent Windows x64 builds. Each ZIP has a checksum and signature sidecar:

~~~text
GenshinPiano-<version>-win-x64.zip
GenshinPiano-<version>-win-x64.zip.sha256
GenshinPiano-<version>-win-x64.zip.sig

GenshinPiano-<version>-win-x64-framework.zip
GenshinPiano-<version>-win-x64-framework.zip.sha256
GenshinPiano-<version>-win-x64-framework.zip.sig

ocr-resources-<resource-version>-win-x64.zip
ocr-resources-<resource-version>-win-x64.zip.sha256
ocr-resources-<resource-version>-win-x64.zip.sig
~~~

Application ZIPs contain the application, managed OCR code, separate updater, and sample scores. Resource ZIPs contain `addons/ocr` with models, native dependencies, a manifest, and license notices. They contain no second .NET runtime or managed engine.

Resources use component ID `addon.ocr.resources.win-x64`; legacy full add-ons use `addon.ocr.win-x64` and `ocr-addons-*` filenames. New clients select only resource archives from online releases. Old clients ignore the new filename prefix and must update the application first. Compatible legacy full packages still work. New clients allow migration to resources at the same version, removing redundant engine/.NET files after a successful replacement. Resources are not automatically downgraded within the same package type. Rolling back to an application that predates resource support requires reinstalling its compatible legacy full add-on.

Downloads are resumable. When GitHub and GitCode return different versions, the source-selection logic compares responses received within a short grace window after the first valid response and prefers the newer compatible version. If another source fails or times out, it uses the available result.

### Verification and trust

The downloader first checks the ZIP's SHA-256, then verifies an RSA-3072/SHA-256 signature with the embedded public key. Signatures use PKCS#1 v1.5, and `.sig` stores Base64 text.

The signed payload is the following UTF-8 text, with LF line endings and an uppercase hexadecimal hash:

~~~text
GenshinPiano.Update.v1
<ZIP filename>
<SHA-256>
~~~

The final line also ends in LF. The signature binds the protocol identifier, filename, and content hash, preventing a signed file from being renamed to impersonate a different version. Missing sidecars, damaged downloads, or invalid signatures prevent a package from becoming ready to install.

Only the public key is included in the repository and release packages. The maintainer keeps the private key outside the repository and supplies its path through an environment variable when publishing. It is excluded from source, application packages, and OCR packages. Public-key replacement requires a transition for already released clients.

### Application installation and rollback

After download verification, the application safely extracts the package into staging, creates an installation plan, and starts a separate updater from the cache. The updater waits for the application to exit, replaces application files, keeps a backup, and restarts the application according to the plan.

Updates and rollbacks preserve `config`, `songs`, `logs`, `update-cache`, `addons`, and `data`. A failed installation attempts to restore the previous application. Manual rollback uses the latest available backup. Application rollback does not also downgrade OCR.

### OCR installation and replacement

OCR downloads use a separate cache. The installer validates ZIP paths, manifest protocol, engine compatibility generation, component type, version consistency with signed metadata, and minimum required resources before replacing the complete `addons/ocr` directory. A failed replacement restores the previous component. Successful replacement removes obsolete runtime files. Local versions are reread after manual installation, so update/migration status is recalculated even when recent remote metadata is reused.

Recognition and runtime replacement are mutually exclusive; the engine is not replaced during recognition. Successful installation refreshes version status in the main window and OCR dialog. See [OCR component](OCR.md#english) for recognition scope and interfaces.

See [Build and maintenance tools](../tools/README.md#english) for build and release commands.
