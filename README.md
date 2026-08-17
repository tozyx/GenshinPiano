# GenshinPiano v3

GenshinPiano v3 是面向 Windows 的原神乐器编曲与演奏工具。目前正在使用 C#、.NET 10 LTS 和 WPF 完全重构。

## 目标

- 使用 UTF-8 JSON `.gpiano` 作为可编辑工程格式
- 导入和导出标准 MIDI 文件
- 兼容导入旧版 `.GenshinPiano` 曲谱
- 提供钢琴卷帘、21 键预览、移调和音域映射
- 提供可靠的 Windows 按键播放与录入能力
- 后续接入键盘谱 OCR、印刷五线谱 OMR 和简谱 OMR

## 解决方案结构

- `src/GenshinPiano.Core`：曲谱领域模型与纯业务规则
- `src/GenshinPiano.Application`：用例、端口和应用服务
- `src/GenshinPiano.Infrastructure`：JSON、MIDI、旧格式及 Windows 系统适配器
- `src/GenshinPiano.App`：WPF 桌面界面
- `tests/GenshinPiano.Core.Tests`：核心模型测试
- `docs`：格式与架构文档

## 开发环境

- Windows 10/11 x64
- .NET 10 SDK
- Visual Studio 2026，安装“.NET 桌面开发”工作负载；或使用支持 WPF 的其他 IDE

## 构建

```powershell
dotnet restore
dotnet build GenshinPiano.sln
dotnet test GenshinPiano.sln
```

本项目使用 [GNU GPL v3](LICENSE) 许可证。
