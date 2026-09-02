# GenshinPiano v3

[English](README.en.md) | 简体中文

GenshinPiano v3 是面向 Windows 的原神乐器编曲与演奏工具。目前正在使用 C#、.NET 10 LTS 和 WPF 完全重构。

## 目标

- 使用 UTF-8 JSON `.gpiano` 作为可编辑工程格式
- 导入和导出标准 MIDI 文件
- 兼容导入旧版 `.GenshinPiano` 曲谱
- 提供钢琴卷帘、21 键预览、移调和音域映射
- 提供可靠的 Windows 按键播放与录入能力
- 通过独立附加包提供简谱 OCR 与印刷五线谱 OMR

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

## 如何构建

开发环境配置、调试、测试、Release 发布、更新签名及 OCR 附加包构建请参阅：

[开发与构建指南](docs/BUILDING.md)

## 使用帮助

软件操作、卷帘编辑快捷键、MIDI/OCR 导入、试听和游戏演奏说明请参阅：

[GenshinPiano 用户指南](docs/USER_GUIDE.md)

## 便携配置

程序以便携模式运行，用户设置保存在可执行文件同目录下的 `config/settings.json`。发布 ZIP 包时应整体解压到用户具有写权限的目录，不建议直接放入 `Program Files`。

## 当前可用功能

- 编辑、校验和保存 UTF-8 JSON `.gpiano` 曲谱，支持文件夹曲谱库、拖放打开、重命名与恢复未保存内容
- 21 键、完整 88 键和曲谱音域三种卷帘视图，支持创建、试听、框选、多选、复制、批量移动、节拍长度及按键持续时间编辑；显示模式与缩放比例会被记忆
- 本地多音色试听、BPM 调整、自然延音、播放游标、选区循环和高刷新率平滑滚动
- 安全的游戏内按键演奏：目标窗口检测、3 秒倒计时、失焦暂停、全局 Esc 暂停及结束时强制释放按键
- 直接打开或批量转换 MIDI，兼容导入旧版 `.GenshinPiano` 曲谱
- 曲谱分析、21 键音域调整、智能按下时长优化及短按时长生成
- 实验性曲谱 OCR：可手动选择简谱或五线谱；简谱支持水印抑制、谱行/声部分析、节拍与连音重建，五线谱通过 Oemer/MusicXML 保留复调、升降音和时值；结果可保留在 88 键卷帘或自动映射到 21 键
- 深浅主题、中英文界面、便携配置、`.gpiano` 文件关联和单实例运行
- GitHub/GitCode 双更新源、断点下载、RSA 签名验证、无感更新、更新日志与手动回滚

旧版格式没有保存 BPM。批量转换目前按 120 BPM、480 PPQ 导入，旧版时值保存为节奏跨度，并以“自然”规则生成 80% 的实际按键保持时间；输出目录中已有的同名 `.gpiano` 默认跳过。

## 曲谱资源说明
本目录中的曲谱文件用于展示和测试 GenshinPiano 的曲谱编辑、文件读取、本地试听、格式转换及游戏内演奏功能。

除非曲谱文件或其附带信息另有明确说明：

- 曲谱仅供个人学习、软件测试和非商业交流使用。
- 不得将曲谱用于出售、付费分发、商业演出或其他商业用途。
- 曲谱可能是对现有音乐作品进行的简化、转录或重新编排。
- 原音乐作品的著作权及其他权利归相应的作曲者、作者、发行方或其他权利人所有。
- 提供曲谱文件不代表 GenshinPiano 项目取得了原作品的版权或完整授权。
- 提供曲谱文件也不代表项目向用户授予原作品的复制、改编、传播、公开表演或商业使用权。
- 用户应自行确认其下载、使用、修改和分享曲谱的行为是否符合所在地法律法规及相关平台规则。

GenshinPiano 软件源代码使用 MIT License 发布。MIT License 仅适用于项目有权许可的软件代码及相关原创内容，不自动适用于本目录中的第三方音乐作品、曲谱、名称或其他材料。

如果你是相关作品的著作权人或授权代表，并认为本目录中的内容侵犯了你的合法权益，请通过 GitHub Issue 联系：

[https://github.com/tozyx/GenshinPiano/issues](https://github.com/tozyx/GenshinPiano/issues)


本项目使用 [MIT](LICENSE) 许可证。
