# 项目文档

[简体中文](#简体中文) | [English](#english)

## 简体中文

GenshinPiano 是面向 Windows 的曲谱编辑、游戏演奏与练习工具。曲谱以 `.gpiano` 保存，OCR 作为可选插件独立安装。

### 使用软件

- [用户指南](USER_GUIDE.md)：文件管理、卷帘编辑、导入、试听、游戏演奏和常见设置。
- [练习与音游](PRACTICE.md)：练习视图、跟弹、节奏判定、延迟校准和标记续练。

### 理解项目

- [系统架构](architecture.md)：各模块的职责、数据流和进程边界。
- [曲谱格式](gpiano-format.md)：音高、时间、音符时长及文件兼容规则。
- [OCR 插件](OCR.md)：简谱与五线谱识别流程、支持范围和插件接口。
- [更新机制](UPDATES.md)：版本检查、独立插件更新、签名验证与回滚。

构建、发布和维护工具的操作入口见 [tools](../tools/README.md)。

## English

GenshinPiano is a Windows tool for score editing, in-game playback, and practice. Scores are stored as `.gpiano` files, and OCR is installed as an optional component.

### Using the application

- [User guide](USER_GUIDE.md#english): file management, piano-roll editing, import, local audition, in-game playback, and common settings.
- [Practice and rhythm-game guide](PRACTICE.md#english): practice views, follow mode, timing judgments, latency calibration, and marked-note resume.

### Understanding the project

- [System architecture](architecture.md#english): module responsibilities, data flow, and process boundaries.
- [Score format](gpiano-format.md#english): pitches, timing, note durations, and file compatibility.
- [OCR component](OCR.md#english): numbered/staff recognition, supported input, and the add-on interface.
- [Updates and component management](UPDATES.md#english): version checks, independent add-on updates, signature verification, and rollback.

For build, release, and maintenance commands, see [Build and maintenance tools](../tools/README.md#english).
