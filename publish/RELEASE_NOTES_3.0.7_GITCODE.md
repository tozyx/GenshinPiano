# GenshinPiano v3.0.7

GenshinPiano v3.0.7 重点改进本地试听与练习播放的稳定性。密集音符、首次发声和蓝牙耳机切换后的首音表现更加可靠，卷帘滚动也更加平滑。

## 本次更新

- 将游戏乐器采样改为预解码 PCM 与单输出流多声部混音，改善密集音符下的漏音、静音和资源占用；重复按下同一音符不会截断前一次延音。
- 启动后在后台缓存常用乐器音色；音频设备切换或窗口重新激活时短暂预热输出，并在发声前检查输出流状态，减少首音延迟与按下音缺失。
- 音频空闲后仍会释放输出设备，避免软件待机时持续占用蓝牙耳机，影响手机与电脑之间的音频切换。
- 调整游戏乐器采样与内置 MIDI 合成音的相对响度，使切换音色时的试听音量更接近。
- 修复曲谱试听末尾音符偶尔跳过的问题。
- 优化卷帘绘制与播放进度更新，减少长时间播放、密集谱面和不同帧率设置下的偶发跳帧。
- 练习页提前预热音色；音游模式在音频就绪后再启动卷帘时钟，改善开头的卡顿与音画不同步。
- README 增加功能建议入口和第三方项目致谢。

## 下载

推荐普通用户使用自包含版本，无需预先安装 .NET：

[GenshinPiano-3.0.7-win-x64.zip](https://gitcode.com/tozyx/GenshinPiano/releases/download/v3.0.7/GenshinPiano-3.0.7-win-x64.zip)

轻量版本，需要安装 `.NET 10 Desktop Runtime x64`：

[GenshinPiano-3.0.7-win-x64-framework.zip](https://gitcode.com/tozyx/GenshinPiano/releases/download/v3.0.7/GenshinPiano-3.0.7-win-x64-framework.zip)

可选 OCR 附加包，用于简谱和五线谱识别：

[ocr-addons-0.8.0-win-x64.zip](https://gitcode.com/tozyx/GenshinPiano/releases/download/v3.0.7/ocr-addons-0.8.0-win-x64.zip)

OCR 附加包也可以在软件内自动下载和更新，无需手动解压。建议将主程序 ZIP 完整解压到具有写入权限的目录后运行。
