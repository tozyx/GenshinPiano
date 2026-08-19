# GenshinPiano v3

[English](README.en.md) | 简体中文

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

## 便携配置

程序以便携模式运行，用户设置保存在可执行文件同目录下的 `config/settings.json`。发布 ZIP 包时应整体解压到用户具有写权限的目录，不建议直接放入 `Program Files`。

## 当前可用功能

- 打开、校验和保存 UTF-8 JSON `.gpiano` 曲谱
- 按照速度变化和音符时间轴，将曲谱映射为原神 21 键并通过 Windows `SendInput` 演奏
- 点击播放后先等待原神窗口获得焦点，再执行完整的 3 秒安全倒计时；倒计时中失焦会重新等待并重置倒计时
- 左侧栏播放按钮支持播放、手动暂停和继续，播放时停止按钮以展开动画出现
- 仅在国服 `YuanShen.exe` 或国际服 `GenshinImpact.exe` 位于前台时发送按键；失去焦点时冻结时间轴，重新聚焦后继续
- 播放及倒计时期间全局监听 Esc；只要原神处于前台，按下 Esc 就会释放按键并暂停播放，同时 Esc 仍正常传递给游戏
- 从“导入 → 批量转换旧版曲谱”递归转换 `.GenshinPiano` 文件，并保留原目录结构
- 内置原神 21 键钢琴卷帘，支持 Ctrl 点选、空白框选、Ctrl 框选追加及成组移动、复制和删除；复制时保留原音符并显示半透明目标预览。右键音符可即时调整时长，`[`/`]` 可批量修改选中音符或当前吸附网格

旧版格式没有保存 BPM。批量转换目前按 120 BPM、480 PPQ 导入，旧版时值保存为节奏跨度，并以“自然”规则生成 80% 的实际按键保持时间；输出目录中已有的同名 `.gpiano` 默认跳过。

本项目使用 [GNU GPL v3](LICENSE) 许可证。
