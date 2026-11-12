# 开发与发布工具

所有示例从仓库根目录执行。根目录保留应用/OCR 构建、签名和调试部署入口；OCR 模型维护与研究工具放在 `ocr` 子目录，均不随应用发布。

## 构建、签名与资源

- `Build-UnsignedAppPackage.ps1`：构建供本地和 CI 验证的无签名 framework 应用包，不需要私钥或 OCR 资源。
- `Publish-OcrAddon.ps1`：构建 OCR 独立 ZIP、SHA-256 和签名；支持 `-Version`，详见 [构建说明](../docs/BUILDING.md)。
- `Publish-OcrSmtRuntime.ps1`：校验手动下载的 CPU 归档及量化模型，提取必要运行文件并部署 x64 VC 运行库；由发布与调试部署脚本调用。
- `Setup-OcrDevelopment.ps1`：构建并部署 Debug OCR 附加包，仅使用当前 SMT 后端，无需 Python。
- `Sign-UpdatePackage.ps1`：使用仓库外的现有私钥签名更新包。
- `New-UpdateSigningKey.ps1`：仅用于首次建立签名密钥，日常发布使用现有私钥；用法见 [更新签名](../docs/update-signing.md)。
- `generate_windows_icons.py`：根据主图重新生成 Windows 图标及应用资源，需要 Pillow。

三个构建 BAT 位于本目录并随 Git 跟踪，可双击或从仓库根目录运行。应用默认版本为 `3.1.0`，OCR 默认版本为 `1.0.0`，第一个参数可覆盖版本。签名私钥仅从 `GENSHINPIANO_UPDATE_SIGNING_KEY` 环境变量读取，不应写入脚本或仓库。

~~~cmd
tools\build-release.bat 3.1.0
tools\build-release-framework.bat 3.1.0
tools\build-ocr-addon.bat 1.0.0
~~~

## OCR 维护工具

### `ocr/smoke`：真实宿主链路验证

`OcrAddonSmoke.csproj` 提供 `recognize`、`verify` 和 `install` 命令，验证真实附加包 IPC、校验和/签名以及组件安装流程。需要 .NET 10 SDK 与已构建的附加包，正常调用不需要 Python。

```powershell
dotnet run --project tools/ocr/smoke/OcrAddonSmoke.csproj -- recognize publish/addons/ocr "D:\scores\scan.png" "D:\results\recognized.json" Staff
dotnet run --project tools/ocr/smoke/OcrAddonSmoke.csproj -- verify publish/ocr-addons-1.0.0-win-x64.zip src/GenshinPiano.App/Assets/Security/UpdateSigningPublicKey.xml
```

`install <zip> <public-key> <install-root>` 会替换指定安装目录中的 OCR 插件，应指定专用测试目录。验证构建可通过 `--artifacts-path` 放到仓库 `artifacts` 目录下。

### `ocr/models`：简谱模型导出

保留 `export_orpheusnet.py` 和 `export_orpheusnet_srcnn.py`，用于从本地 OrpheusNet 上游源码/权重重新生成并验证简谱 ONNX 模型。需要独立的 Python 开发环境及 PyTorch、ONNX、NumPy；不属于普通应用构建或 OCR 发布依赖。

```powershell
python tools/ocr/models/export_orpheusnet.py "D:\research\OrpheusNet" "D:\models"
python tools/ocr/models/export_orpheusnet_srcnn.py "D:\research\srcnn.pth" "D:\models\orpheusnet-srcnn-x3.onnx"
```

### `ocr/research`：离线评测与研究回归

保留 SMT 扫描谱实验、结构解析、离线评分与人工参考标注，以便复核历史结论和比较后续模型。正式产品使用 `src/GenshinPiano.Ocr.Engine/Smt` 中的 C# 实现，这些 Python 脚本不进入 OCR 发布包。

- `Test-Smt.ps1`：调用已校验的原生 SMT 后端，记录输出、哈希、耗时与超时信息。
- `prepare_smt_scan.py`、`smt_scan_layout.py`：扫描谱布局检测和图像准备。
- `optimize_smt_scan.py`、`smt_scan_quality.py`：结构检查与有限候选重试。
- `analyze_smt_output.py`、`smt_notation.py`：输出结构分析和共享事件解析。
- `compare_staff_ocr.py`、`evaluate_smt_scan_optimization.py`、`reference/`：五线谱离线评分及参考标注；复核旧 Oemer 结果时读取已有 MusicXML，不再部署 Oemer 环境。
- `evaluate_ocr_score.py`：对照人工 `.gpiano` 曲谱评估简谱结果。
- `test_smt_scan_pipeline.py`：无需模型的布局、结构校验和候选保护回归测试。

图像处理和回归测试需要 NumPy、Pillow；原生推理还需要手动准备 `publish/smt-experiment` 中的 CPU 归档和 GGUF 模型。

```powershell
python -B -m unittest discover -s tools/ocr/research -p test_smt_scan_pipeline.py -v
python tools/ocr/research/optimize_smt_scan.py "D:\scores\scan.png" "D:\results\new-run"
```

历史评测及复跑说明见 [扫描谱优化](../docs/SMT_SCAN_OPTIMIZATION.md) 和 [SMT/Oemer 比较](../docs/OCR_SMT_OEMER_COMPARISON.md)。

## 已移除的旧工具

旧 Oemer/Python 运行时打包器、依赖锁定文件、兼容补丁、运行时验证脚本以及调试部署的 `-LegacyOemer` 分支已删除。当前 OCR 发布与调试部署统一使用原生 SMT；历史实验结果保留在文档中，旧工具可从 Git 历史查阅。
