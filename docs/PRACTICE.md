# GenshinPiano 练习与音游指南

[简体中文](#简体中文) | [English](#english)

## 简体中文

### 页面与模式

练习页直接使用当前打开的 `.gpiano` 或 MIDI 曲目，并提供“游戏按键”和“垂直卷帘”两种视图。跟弹模式等待玩家完成当前按键组合；节奏模式按照曲谱时间前进，在目标时间前后提供容错。两者均只判断按下事件。

垂直卷帘底部的音符按钮用于锁定音游功能。音游不是第三个标签模式：开启和关闭不会改变跟弹/节奏选项。启用后完整曲谱自动播放，玩家按键只负责判定并播放独立反馈音；谱面音符采用固定像素高度，不以原始时值判断按住或松开。

### 播放、暂停与定位

- `Space` 或底部播放按钮：开始/暂停。
- 停止按钮：回到曲谱开头并清除本轮统计。
- 跟弹模式将当前音符保持在演奏线附近。
- 节奏和音游开始前会先把当前待弹音符移动到预备位置，再开始时钟或伴奏。
- 暂停时自动选中当前待弹音符，已完成音符以低透明度显示；重新开始且定位结束后，已完成音符完全不绘制。
- 暂停时可以滚轮查看卷帘。再次开始会先平滑返回当前待弹音符，不会从浏览位置误启动。

### 标记与读谱

- 左键点击卷帘音符设置标记，该音符使用游标强调色。
- 右键任意位置取消标记。
- “返回标记”会停止当前练习、定位到标记音符并保持暂停；需要再次点击播放才继续。
- 上隐游标可以上下拖动。游标上方的音符保持隐藏，经过游标后逐渐显示。
- 播放速度支持 25%、50%、100% 和 125%；音符间距支持 100%、125%、150% 和 200%。设置会自动保存。

### 音游校准与声音

音游启用后，设置和校准按钮会从音符按钮左侧滑出。反馈音量范围为 0–100，与曲谱伴奏和普通乐器试听音量相互独立；尚未开始时也可通过按键试听。

校准窗口循环播放四拍短促声音并绘制一个下落音符。以声音为准，在敲击时按 `Space`，判定线会变色并强调。上下拖动判定线，直到音符在第四拍听到时刚好触线。保存后偏移量仅用于音游模式，不影响跟弹和普通节奏练习。

### 乐器与响度

练习区提供七种 21 键游戏乐器。重复触发同一音高会创建独立声部，不会截断上一声余音。分发的采样已统一峰值余量，采样播放器和内置 MIDI 使用相同的感知音量映射。

## English

### Views and modes

The practice page uses the current `.gpiano` or MIDI score and offers **Game keys** and **Vertical roll** views. Follow mode waits until the current key combination is completed. Timed mode advances on score time and accepts presses inside an early/late window. Neither mode judges key release.

The note button in the vertical-roll status bar locks the rhythm-game layer. Rhythm game is not a third tab mode, so toggling it does not alter the Follow/Timed selection. When enabled, the full score autoplays and player presses are used only for judgments and an independent hit sound. Notes use a fixed pixel height and their source duration does not create hold judgments.

### Playback, pause, and positioning

- `Space` or the status-bar play button starts and pauses practice.
- Stop returns to the beginning and clears run statistics.
- Follow mode keeps the current note near the play line.
- Timed and rhythm-game playback first lift the pending note to its lead-in position, then start the clock or accompaniment.
- Pausing selects the pending note and dims completed notes. After restart positioning finishes, completed notes are omitted entirely.
- The wheel can inspect the roll while paused. Starting always scrolls back to the pending note before playback begins.

### Markers and reading controls

- Left-click a roll note to mark it; the marker uses the cursor accent color.
- Right-click anywhere to clear the marker.
- Return to marker stops the current run, navigates to that note, and remains paused.
- Drag the hidden-note cursor vertically to choose where falling notes begin to appear.
- Playback speed supports 25%, 50%, 100%, and 125%; note spacing supports 100%, 125%, 150%, and 200%. Both are persisted.

### Rhythm calibration and sound

Enabling rhythm game slides settings and calibration controls out from the left of the note button. Hit-sound volume ranges from 0–100 and is independent of score accompaniment and normal instrument audition. It can be previewed before playback starts.

Calibration loops four short audible beats and one falling note. Treat audio as the reference: press `Space` on your strike to emphasize the judgment line, then drag the line until the note touches it on audible beat four. The saved offset applies only to rhythm game.

### Instruments and loudness

Practice includes seven sampled 21-key game instruments. Repeated attacks create independent voices instead of truncating the previous decay. Distributed samples share normalized peak headroom, and sampled playback and built-in MIDI use the same perceptual volume mapping.
