# B 站键盘文本谱样例 / Bilibili keyboard text sample

本目录保存键盘文本谱的格式参考。以下两个 `lemon-excerpt` 文件是是星璃啦发布的 [《Lemon》原神琴谱](https://www.bilibili.com/opus/859289537085440004) 开头的极短摘录，仅展示记谱格式，不包含完整曲谱或歌词。

- [lemon-excerpt-keys.txt](lemon-excerpt-keys.txt)：字母按键版。
- [lemon-excerpt-numbers.txt](lemon-excerpt-numbers.txt)：来源正文中的对应数字版。

两份文件对应同一段，不能当作前后两段拼接。`QW` 对应 `+1+2`，`(NE)` 对应 `(-6+3)`，`QH` 对应 `+16`。圆括号表示同时弹；空格和 `/` 保留来源的分组方式，不提供精确时值。文件使用普通 ASCII 空格表示网页中的空白。

另有用户从网盘复制的 [指尖-键盘谱.txt](指尖-键盘谱.txt) 和 [指尖-数字谱.txt](指尖-数字谱.txt)，保留原文不作修改。两者也是同一份谱的两种表示，共 78 行谱文、360 个起音事件、485 个音符；文件未注明曲名、BPM 或拍号，不能据此认定来自上面链接的歌曲。前三段每行四组，第四段行宽变化，转换时需要确认分组、拍号和节奏。详见下方格式说明。

这些文件不是 `.gpiano`，也不是旧版 `.GenshinPiano` 格式。通过“导入 → 键盘／数字文本谱”打开；`lemon-excerpt` 选择“是星璃啦”，`指尖` 文件选择“指尖灬旋律丿”，确认节奏后导入。完整规则、其他作者的写法差异及来源见 [键盘文本谱说明](../../docs/keyboard-text-notation.md)。原曲和原作者曲谱的权利归相应权利人所有，本仓库许可证不授予这些作品的额外使用权。

## English

The two `lemon-excerpt` files contain a very short opening excerpt from **是星璃啦's Lemon Genshin sheet**, linked above. They illustrate notation only and do not include the full arrangement or lyrics.

The key and numeric files represent the same passage. Parentheses denote simultaneous presses; spaces and slashes retain the source's grouping without specifying exact durations. Web whitespace is represented with ordinary ASCII spaces.

The separate `指尖-键盘谱.txt` and `指尖-数字谱.txt` files were copied from a cloud-drive source by the user and remain unchanged. They encode the same 78 notation rows, 360 attack events, and 485 notes. Their title, tempo, and meter are unspecified; they are not identified as the song linked above. The layout changes in section four, so group boundaries and timing require confirmation before conversion. See the format guide for details.

Open these through **Import → Keyboard / numeric text score**: select Xingli for `lemon-excerpt` and Zhijian for the `指尖` files, then confirm timing. They are not `.gpiano` or legacy `.GenshinPiano` files. See the linked format guide for conventions and compatibility. Rights to the original music and arrangement remain with their respective holders; the repository license does not grant additional rights to those works.
