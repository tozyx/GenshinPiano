# OCR 资源 / OCR source assets

## 简体中文

这里存放引擎的模型源文件，不是可直接安装的 OCR 插件目录。普通构建不会把这些文件复制进主程序；发布脚本负责部署。

- `orpheusnet/`：仓库已有的三个简谱 ONNX 模型，分别用于识别、元数据判断和小图增强。导出工具位于 `tools/ocr/models`，来源见根目录 `THIRD-PARTY-NOTICES.md`。
- `smt-resource/`：随仓库保存的五线谱模型与 CrispEmbed 原始 ZIP，保持下面的文件名，不需要手动解压。克隆仓库即可取得这两个文件，无需额外下载或 Git LFS。
- RapidOCR 的四个 `v5` 模型/字典，以及 ONNX Runtime、SkiaSharp 的 Windows x64 原生库：由发布脚本从项目锁定版本对应的 NuGet 缓存提取，无需再在这里保存副本。
- VC x64 运行库：默认从 Windows `System32` 复制；也可通过 `-MsvcRuntimeDirectory` 指定来源。资源包包含这些 DLL，不附带另一套 .NET；运行时由主程序提供。

纳入仓库的固定资源及其上游来源（文件缺失时可重新下载）：

- [CrispEmbed v0.17.18 CPU Windows x64](https://github.com/CrispStrobe/CrispEmbed/releases/tag/v0.17.18)：`smt-resource/crispembed-windows-x86_64.zip`。
- [固定版本的 GrandStaff Q8 模型](https://huggingface.co/cstr/smt-grandstaff-GGUF/tree/8e1595591659aa03cf1e71aab5aa03e8f3fccd7e)：`smt-resource/smt-grandstaff-q8_0.gguf`。

固定 SHA-256：

```text
crispembed-windows-x86_64.zip
e82337bcdefb1d54b0d97c26fc7e64ff35847d1db285e5062d6674df91ce6017
smt-grandstaff-q8_0.gguf
f6612885a8d7015bef5ab7f045a37a0f667575f4b65b324a353cf348d6edc752
```

CrispEmbed ZIP 是程序分发包，不是模型压缩包。发布时仅提取 `crispembed.exe`、`crispembed.dll`、`ggml.dll`、`ggml-base.dll`、`ggml-cpu.dll` 及 README/许可证/声明，和 GGUF 模型一起放入 `addons/ocr/staff-omr/smt`。服务器、量化工具、开发头文件、`.lib` 以及原始 ZIP 不进入发布包。识别时运行解压后的 EXE。

生成的插件部署与 ZIP 放在 `publish`；开发、离线推理的临时产物放在 `artifacts`，不要写回模型源目录。调试部署到主程序 Debug 输出的 `addons/ocr`，使用 `tools/Setup-OcrDevelopment.ps1`。详细命令见 [构建工具说明](../../../tools/README.md)。

## English

This directory holds model source assets, not an installable OCR add-on. Ordinary builds do not copy these files into the main application; the resource publisher deploys them.

- `orpheusnet/` contains the three tracked ONNX assets for numbered-notation recognition, metadata inference, and small-crop enhancement. Export tools are in `tools/ocr/models`; provenance is described in the root `THIRD-PARTY-NOTICES.md`.
- `smt-resource/` contains the pinned GGUF model and original CrispEmbed ZIP in Git. Cloning the repository includes both files without a separate download or Git LFS. Keep their filenames and do not extract the ZIP manually. The links above identify their upstream sources and can be used to recover missing files.
- RapidOCR v5 models/dictionary and Windows x64 ONNX Runtime/SkiaSharp libraries are extracted from the NuGet packages at the versions declared by the project. Do not duplicate them here.
- App-local x64 VC runtime DLLs come from Windows `System32`, or a directory supplied through `-MsvcRuntimeDirectory`. The resource package does not contain a .NET runtime.

The pinned SHA-256 values above are verified during packaging. The CrispEmbed archive supplies the executable and libraries, while the GGUF file supplies the weights. Only `crispembed.exe`, `crispembed.dll`, the three GGML DLLs, README, licenses, and notices are extracted into `addons/ocr/staff-omr/smt`, alongside the GGUF model. Server/quantization tools, headers, import libraries, and the original ZIP are excluded. Recognition launches the extracted EXE.

Generated deployments and release archives belong under `publish`; development/inference outputs belong under `artifacts`, separate from source assets. `tools/Setup-OcrDevelopment.ps1` deploys to the main application's Debug `addons/ocr` directory. See [Build tools](../../../tools/README.md#english) for commands.
