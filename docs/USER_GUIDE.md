# GenshinPiano 用户指南

[简体中文](#简体中文) | [English](#english)

## 简体中文

### 开始使用

1. 将发布 ZIP 完整解压到具有写入权限的文件夹，不要直接在压缩包内运行。
2. 启动 `GenshinPiano.exe`。首次启动会根据 Windows 设置选择语言和主题。
3. 点击左侧“当前曲谱”旁的 `+` 新建曲谱，或从“文件 → 打开”载入 `.gpiano` / `.mid`。
4. 也可以把 `.gpiano`、`.json`、`.mid` 或 `.midi` 文件拖到主窗口的编辑区、练习区或侧栏；MIDI 会先显示导入设置，替换当前曲谱前会检查未保存修改。
5. 将 PNG、JPG/JPEG、BMP、TIF/TIFF 或 WebP 图片拖到主窗口，会打开 OCR 并选中图片；也可直接拖到已打开的 OCR 窗口。选择谱面类型后点击“开始识别”，确认结果后才导入。识别期间不能切换图片。

一次拖入多个文件时，仅处理其中第一个支持的文件；OCR 窗口只接收图片。播放或本地试听期间，主窗口暂停接收文件拖放。

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

- 工具栏可切换“21 键”“88 键”和“曲谱音域”视图；曲谱音域只显示当前曲谱实际使用的音高范围。视图与缩放比例会自动保存。
- 单击或拖动顶部时间尺设置播放游标，显示时间会按 BPM 自动计算。
- `Space`：播放 / 暂停本地试听。
- 选中音符后启用循环按钮，可循环试听选区；取消选择后循环自动关闭。
- `Ctrl + 滚轮`：水平缩放。
- `Shift + 滚轮`：横向滚动。
- `Ctrl + Shift + 滚轮`：垂直缩放。
- 底部可调整 BPM、自然延音、音量和本地试听音色。
- 游戏内采样音色在列表中始终显示；它们适用于“21 键”视图，在“88 键”和“曲谱音域”视图中会显示为不可选，悬停可查看说明。切回“21 键”即可使用。
- “设置 → 卷帘帧率”可选择 30 FPS、60 FPS 或垂直同步。

### 练习与音游

- 顶部“练习 / 曲谱编辑”标签可进入独立练习页；练习曲目直接复用左侧曲谱列表及 `.gpiano` / MIDI 打开流程。
- “游戏按键”显示 21 个圆形游戏乐器按键及当前组合提示；“垂直卷帘”用于下落式读谱。
- “跟弹模式”只有正确完成当前组合后才前进；“节奏模式”按曲谱时间推进，并允许在目标时间前后一定范围内命中。两者都不判断松键时机。
- 练习页支持 `Space` 播放或暂停。操作下拉框后焦点会返回练习区；中文输入法开启时仍按物理键位识别。
- 垂直卷帘可设置 25%、50%、100% 或 125% 播放速度，以及 100%–200% 音符间距。这些选项会保存到便携配置。
- 拖动卷帘中的上隐游标可以改变音符开始显现的位置。暂停时可滚轮浏览；再次播放会先回到当前待弹音符。
- 左键单击音符可设置标记，右键取消；“返回标记”只负责定位并保持暂停，不会立即开始播放。
- 节奏或音游练习暂停时，已完成音符会淡化；再次开始且定位完成后，已完成音符不再绘制。跟弹模式维持当前音符贴近底线的浏览方式。
- 垂直卷帘底部的音符图标用于锁定音游功能。开启后完整曲谱自动播放，玩家按键只负责判定，音符统一显示为固定高度，不使用原始时值判断松键。
- 开启音游后会滑出“校准”和“设置”按钮。校准时以四拍敲击声为准，按 `Space` 查看判定线反馈，再上下拖动判定线，使第四拍与音符触线同步。
- 音游设置可将独立反馈音调整为 0–100；未开始播放时也可以按键试听。该音量不影响曲谱伴奏和普通乐器试听。

完整说明见 [练习与音游指南](PRACTICE.md)。

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

### OCR 曲谱识别

1. 打开“导入 → OCR 曲谱识别”。
2. 首次使用时点击“下载附加包”；软件会从 GitCode/GitHub 下载、校验签名并安装 OCR 引擎。
3. 明确选择“简谱”或“五线谱”，再选择图片、水印抑制强度及是否识别伴奏。软件不会自动猜测谱面类型。
4. 点击“开始识别”，完成后检查音符数量和置信度，再导入卷帘。

简谱路线使用 RapidOCR、数字分类与几何后处理，识别数字、上下音高点、升降号、延音线、节拍线、谱行/声部和伴奏。五线谱路线使用插件内的原生 SMT CPU 后端，按钢琴谱行识别并重建音高、时值、声部和小节结构，不需要安装 Python。两条路线由“谱面类型”下拉框选择。

默认关闭“自动映射到原神 21 键”：识别结果会切换到 88 键卷帘，保留升降音及完整音域。启用后，导入结果会切换到 21 键并执行一次可撤销的自动映射；无法合理映射的半音或严重越界音符可能被忽略。谱面类型、水印模式、伴奏和自动映射选项都会保存到便携配置中。

五线谱目前支持清晰、平直的钢琴扫描谱和电子谱截图，暂不支持拍照谱矫正。无法完整定位谱表或仍存在明显结构异常时，会提示更换图片；识别成功后仍需人工试听和校对。支持范围与识别原理见 [OCR 插件](OCR.md)。

“设置 → 通知 → OCR 完成时通知”可以控制软件未聚焦时的 Windows 通知。

### AI 辅助识谱

在“导入 → AI 辅助识谱”中复制提示词，与谱图一起发给大模型。将返回的 JSON 文本粘贴到窗口，或选择 JSON 文件，点击“解析并检查”。核对音符数量和提示后导入，结果保留原音高并进入 88 键卷帘。

此功能无需 OCR 插件。它使用专用识谱 JSON，不直接读取任意聊天内容；格式错误会指出字段位置，检查结果可复制回大模型修正。导入会检查当前未保存修改。格式说明见 [曲谱识别](OCR.md)。

### 更新、回滚与联网

- “设置 → 更新”可控制联网、自动更新和是否接收预览版。
- 主界面“检查更新”会同时显示程序和 OCR 的已安装/最新版本，可分别选择更新程序、安装或更新 OCR，或取消。
- OCR 窗口也显示插件是否为最新版；OCR 下载安装需由用户主动选择。
- 更新包支持断点下载，并使用 SHA-256 与 RSA 签名验证。
- 下载完成后可选择重启更新；更新失败会尽量恢复原版本。
- “手动回滚”可恢复最近一次备份。
- OCR 是独立附加组件，更新或回滚主程序不会删除已安装的 `addons`。

后台已在下载主程序时，手动更新会等待并复用相同版本的下载；关闭版本窗口或选择 OCR 不会中断后台任务。完整行为见 [更新机制](UPDATES.md)。

### 日志与反馈

运行日志位于程序目录的 `logs`。出现崩溃、更新失败或 OCR 异常时，请保留相应日志，并在以下地址提交 Issue：

[https://github.com/tozyx/GenshinPiano/issues](https://github.com/tozyx/GenshinPiano/issues)

---

## English

### Getting started

Extract the complete release ZIP to a writable directory and run `GenshinPiano.exe`. Do not run it
inside the ZIP or install the portable build under `C:\Program Files`. Create a score with the `+`
button, use **File → Open**, or drag a `.gpiano`, `.json`, `.mid`, or `.midi` file onto the
editor, practice area, or sidebar. MIDI opens its import settings, and unsaved changes are
checked before replacing the current score.

Drop a PNG, JPG/JPEG, BMP, TIF/TIFF, or WebP image onto the main window to open OCR with
that image selected, or drop it directly into an existing OCR window. Choose the notation
type and start recognition, then review and confirm import. Images cannot be replaced during
recognition. Each drop uses the first supported file; the OCR window only accepts images.
The main window does not accept file drops during playback or local audition.

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

- Choose the 21-key, full 88-key, or score-range piano-roll view from the toolbar. Score range only
  shows pitches used by the current score. The selected view and zoom levels are persisted.
- Click or drag the top ruler to position the playback cursor.
- `Space` toggles local audition. A selected range can be looped.
- `Ctrl + wheel`: horizontal zoom; `Shift + wheel`: horizontal scroll;
  `Ctrl + Shift + wheel`: vertical zoom.
- BPM, sustain, volume, instrument, and render frame rate can be adjusted in the editor/settings.
- Sampled game instruments always remain in the list. They are selectable in the 21-key view;
  in 88-key and score-range views they are disabled with an explanatory tooltip. Switch back to
  21 keys to use them.

### Practice and rhythm game

- Open the dedicated practice page from the **Practice / Score editor** tabs. It reuses the sidebar score list and the existing `.gpiano` / MIDI loading path.
- **Game keys** presents the 21 circular instrument keys and upcoming combinations. **Vertical roll** provides falling-note reading.
- **Follow mode** advances after the current combination is completed. **Timed mode** follows score time and accepts hits within an early/late timing window. Key release is not judged.
- `Space` starts or pauses practice. Focus returns to the practice surface after using a selector, and physical keys remain usable with a Chinese IME active.
- Vertical roll supports 25%, 50%, 100%, and 125% playback speed plus 100%–200% note spacing. Both settings are persisted.
- Drag the hidden-note cursor to control where notes become visible. While paused, use the wheel to inspect the roll; starting first returns to the current target note.
- Left-click a note to mark it and right-click to clear the marker. Returning to a marker only navigates and remains paused.
- Completed notes are dimmed while timed/rhythm-game practice is paused, then omitted after playback starts and positioning finishes. Follow mode keeps its existing play-line behavior.
- The note icon in the vertical-roll status bar locks the rhythm-game layer. The full score then autoplays while player input is used only for timing judgments; note blocks have a fixed visual height and release timing is ignored.
- Enabling rhythm game reveals calibration and settings buttons. During calibration, use the four audible beats as the reference, press `Space` to inspect line feedback, and drag the line until the falling note meets it on beat four.
- Rhythm hit-sound volume is independently adjustable from 0–100 and can be previewed before starting. It does not change score accompaniment or normal instrument audition volume.

See [Practice and rhythm-game guide](PRACTICE.md) for details.

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
- Experimental score OCR is available from **Import → OCR score recognition**. Explicitly choose
  numbered or staff notation; the application does not auto-detect the notation type. Numbered
  notation uses the OCR/post-processing pipeline, while staff notation uses the bundled native CPU
  SMT backend to recognize piano systems and reconstruct the score. No Python installation is needed.
- With **Auto-map to 21 keys** disabled, results open in the 88-key roll and preserve accidentals and
  the full pitch range. Enabling it switches to 21 keys and applies one undoable mapping operation;
  unmappable accidentals or notes far outside the range may be ignored. OCR choices are persisted.
- Staff recognition supports clear, flat piano scans and rendered sheet images. Photograph
  correction is unsupported; incomplete layouts and unresolved structural anomalies are rejected.
  Always review and audition imported results. The dialog displays add-on version status and offers
  installation/updates. See [OCR component](OCR.md#english) for its scope and interface.

### AI-assisted score import

Choose **Import → AI-assisted score import**, copy the prompt, and send it with score images to your model. Paste the returned JSON or open a JSON file, choose **Parse and check**, and review the note counts and messages before importing. Original pitches open in the 88-key roll.

No OCR add-on is required. This uses a dedicated score JSON format, not arbitrary chat text. Errors identify field locations, and the review report can be copied back to the model for correction. Existing unsaved changes are checked before replacement. See [Score recognition](OCR.md#english) for the format.

### Updates and support

Network access, automatic updates, and preview releases are controlled under **Settings → Updates**.
Packages use resumable downloads plus SHA-256/RSA verification. The optional OCR component is kept
when the main application is updated or rolled back.

Manual **Check for updates** shows installed/latest versions for the application and OCR together,
with separate update/install actions and Cancel. OCR downloads are user initiated. A manual
application update waits for and reuses a background download of the same version; closing the
overview or choosing OCR does not interrupt that background task. See [Update behavior](UPDATES.md#english).

Logs are stored under `logs`. Report problems with relevant logs at:

[https://github.com/tozyx/GenshinPiano/issues](https://github.com/tozyx/GenshinPiano/issues)
