# GenshinPiano 用户指南

[简体中文](#简体中文) | [English](#english)

## 简体中文

### 开始使用

1. 将发布 ZIP 完整解压到具有写入权限的文件夹，不要直接在压缩包内运行。
2. 启动 `GenshinPiano.exe`。首次启动会根据 Windows 设置选择语言和主题。
3. 点击左侧“当前曲谱”旁的 `+` 新建曲谱，或从“文件 → 打开”载入 `.gpiano` / `.mid`。
4. 也可以把 `.gpiano` 或 `.mid` 文件直接拖到主窗口。

软件采用便携模式，设置、日志、更新缓存和附加组件均保存在程序目录中。不建议放入 `C:\Program Files`。

### 文件与曲谱库

- `Ctrl + N`：新建曲谱。
- `Ctrl + O`：打开曲谱或 MIDI。
- `Ctrl + Shift + S`：另存为 `.gpiano`。
- 点击“曲谱文件夹”右侧的文件夹图标选择一个目录；列表只显示该目录当前层的 `.gpiano` 和 `.mid` 文件。
- 点击当前曲谱名会平滑滚动并定位到列表中的同名文件。
- 在曲目列表中右键选择“重命名”，或选中文件后按 `F2`；文件名和曲谱内部标题会同步更新。
- 存在未保存修改时，关闭、打开其他文件或新建曲谱都会先提示保存。
- “设置 → 文件关联”可注册或移除 `.gpiano` 的 Windows 文件关联。

### 卷帘编辑

- 单击空白网格：按“新建长度”创建音符并试听对应音高。
- 单击音符：选择并拖动；`Ctrl + 单击`追加或取消选择。
- 从空白处拖动：框选；`Ctrl + 框选`追加选择。
- `Ctrl + A`：全选音符。
- `Ctrl + Shift + ← / →`：选择播放游标之前 / 游标及之后的音符。
- `← / →`：按吸附网格移动选中音符；`Shift + ← / →`：移动一拍。
- `Ctrl + 拖动音符`：复制选中音符。
- `Delete / Backspace`：删除；`Ctrl + Z / Ctrl + Y`：撤销 / 重做。
- 右键空白处：取消全部选择。

“吸附网格”决定创建和移动的位置步进；“新建长度”决定新音符的节拍长度。新建长度会继承最近创建、调整或选中的音符长度。

### 节拍与按键持续时间

- 拖动音符右边缘：按吸附网格调整音符节拍长度。
- 右键音符：打开时长面板，可选择节奏值并拖动实心区域右边缘设置按键持续比例。
- `[` / `]`：缩短 / 延长选中音符；没有选择音符时用于调整吸附网格。中文输入法下的 `【` / `】` 同样支持。
- “编辑 → 智能优化弹奏时长”适合本地试听；“生成短按时长”更接近外部键盘乐器的短促触发效果。

### 时间轴、缩放与本地试听

- 单击或拖动顶部时间尺设置播放游标，显示时间会按 BPM 自动计算。
- `Space`：播放 / 暂停本地试听。
- 选中音符后启用循环按钮，可循环试听选区；取消选择后循环自动关闭。
- `Ctrl + 滚轮`：水平缩放。
- `Shift + 滚轮`：横向滚动。
- `Ctrl + Shift + 滚轮`：垂直缩放。
- 底部可调整 BPM、自然延音、音量和本地试听音色。
- “设置 → 卷帘帧率”可选择 30 FPS、60 FPS 或垂直同步。

### 游戏内演奏

1. 点击左下角播放按钮或按 `F5`。
2. 切换到国服 `YuanShen.exe` 或国际服 `GenshinImpact.exe`。
3. 软件检测到白名单窗口后开始 3 秒倒计时，随后发送按键。
4. 游戏失去焦点时播放会暂停，重新聚焦后继续。
5. 播放期间在游戏窗口按 `Esc` 可暂停并释放当前按键。
6. `Shift + F5` 停止播放；结束、停止或异常退出时软件会尝试释放全部模拟按键。

如果游戏以管理员权限运行，Windows 不允许普通权限进程向其注入按键。此时需要由用户右键以管理员身份启动 GenshinPiano。软件不会自动申请管理员权限。

### MIDI 与旧版曲谱

- 直接打开 `.mid`：进入 MIDI 导入流程，可检查并转换为当前卷帘曲谱。
- “导入 → 批量转换 MIDI”：处理所选目录当前层的 MIDI，自动合并音轨并输出 `.gpiano`；子目录会被忽略。
- “导入 → 批量转换旧版曲谱”：转换旧 `.GenshinPiano` 文件。
- MIDI 导入后可根据用途保留原始短音，或使用编辑菜单中的按下时长工具重新生成。

### OCR 简谱识别（测试功能）

1. 打开“导入 → OCR 曲谱识别”。
2. 首次使用时点击“下载附加包”；软件会从 GitCode/GitHub 下载、校验签名并安装 OCR 引擎。
3. 选择图片、谱面类型、水印抑制强度及是否识别伴奏。
4. 点击“开始识别”，完成后检查音符数量和置信度，再导入卷帘。

OCR 目前主要面向数字简谱，支持上下音高点、节拍线、水印抑制、谱行/声部分析和伴奏识别，但复杂排版、低分辨率、水印或连音仍可能产生错误。导入后请人工试听和校对。

“设置 → 通知 → OCR 完成时通知”可以控制软件未聚焦时的 Windows 通知。

### 更新、回滚与联网

- “设置 → 更新”可控制联网、自动更新和是否接收预览版。
- 更新包支持断点下载，并使用 SHA-256 与 RSA 签名验证。
- 下载完成后可选择重启更新；更新失败会尽量恢复原版本。
- “手动回滚”可恢复最近一次备份。
- OCR 是独立附加组件，更新或回滚主程序不会删除已安装的 `addons`。

### 日志与反馈

运行日志位于程序目录的 `logs`。出现崩溃、更新失败或 OCR 异常时，请保留相应日志，并在以下地址提交 Issue：

[https://github.com/tozyx/GenshinPiano/issues](https://github.com/tozyx/GenshinPiano/issues)

---

## English

### Getting started

Extract the complete release ZIP to a writable directory and run `GenshinPiano.exe`. Do not run it
inside the ZIP or install the portable build under `C:\Program Files`. Create a score with the `+`
button, use **File → Open**, or drag a `.gpiano` / `.mid` file onto the main window.

### Files and score folder

- `Ctrl + N`: new score; `Ctrl + O`: open; `Ctrl + Shift + S`: save as.
- Choose a score folder from the folder icon in the sidebar. Click the current title to locate it in
  the list.
- Rename a listed score from its context menu or with `F2`; the file name and internal title are
  updated together.
- Unsaved changes are checked before closing, creating, or opening another score.
- Windows `.gpiano` association is available under **Settings → File association**.

### Piano-roll editing

- Click empty grid space to create and audition a note; click and drag a note to select and move it.
- `Ctrl + click` and `Ctrl + marquee` add to the selection; `Ctrl + A` selects all notes.
- `Ctrl + Shift + Left/Right` selects notes before or at/after the playback cursor.
- `Left/Right` nudges selected notes by the snap grid; add `Shift` to move one beat.
- `Ctrl + drag` copies notes. `Delete` / `Backspace` removes them.
- `Ctrl + Z` / `Ctrl + Y` undo and redo. Right-click empty space to clear selection.
- Drag a note's right edge to change its rhythmic length. Right-click a note for rhythmic value and
  key-hold ratio.
- `[` / `]` changes selected-note length, or changes the snap grid when no note is selected.

### Navigation and local audition

- Click or drag the top ruler to position the playback cursor.
- `Space` toggles local audition. A selected range can be looped.
- `Ctrl + wheel`: horizontal zoom; `Shift + wheel`: horizontal scroll;
  `Ctrl + Shift + wheel`: vertical zoom.
- BPM, sustain, volume, instrument, and render frame rate can be adjusted in the editor/settings.

### In-game playback

Press the sidebar play button or `F5`, then focus `YuanShen.exe` or `GenshinImpact.exe`. Playback
starts after a three-second countdown, pauses when the game loses focus, and resumes when it returns.
Press `Esc` in the game to pause and release held keys, or `Shift + F5` to stop.

If the game runs elevated, start GenshinPiano manually as administrator as well. The application does
not request elevation automatically.

### MIDI, legacy files, and OCR

- Open a MIDI file for interactive import, or use **Import → Batch Convert MIDI** for automatic
  current-folder conversion.
- Legacy `.GenshinPiano` files can be batch-converted from the Import menu.
- Experimental numbered-notation OCR is available from **Import → OCR score recognition**. The
  signed OCR add-on can be downloaded in the dialog. Review and audition OCR results after import.

### Updates and support

Network access, automatic updates, and preview releases are controlled under **Settings → Updates**.
Packages use resumable downloads plus SHA-256/RSA verification. The optional OCR component is kept
when the main application is updated or rolled back.

Logs are stored under `logs`. Report problems with relevant logs at:

[https://github.com/tozyx/GenshinPiano/issues](https://github.com/tozyx/GenshinPiano/issues)
