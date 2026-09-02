# 五线谱 OCR 技术选型

## 目标与约束

五线谱 OCR（OMR）继续作为 `addons/ocr` 中的可选能力发布，不增加主程序和 framework 发布包的体积。首个版本面向印刷体钢琴谱和清晰拍照图片，输出至少包含音高、起始时间、时值、声部、小节、拍号和速度；力度、指法、歌词、连奏线等信息可以在后续版本补充。

GenshinPiano 只有 21 个自然音按键。OMR 仍应保留源谱的 MIDI 音高，导入预览再明确提示无法直接演奏的半音和超出音域的音符，不能在识别阶段静默改变原谱。

## 候选方案

### SMT Camera-GrandStaff（首选长期方案）

- 许可证：MIT。
- 模型：约 2140 万参数，ConvNeXt 图像编码器与自回归 Transformer 解码器。
- 优点：专门面向钢琴大谱表和相机图像，模型与代码许可适合随 MIT 项目的 OCR 附加包发布；规模比完整通用 OMR 工具链更容易控制。
- 限制：输出为 beKern 风格序列，不直接输出 MusicXML；需要自行完成谱表裁切、序列解析、左右手/声部对齐和 MusicXML 或内部曲谱转换。自回归解码器转换为 ONNX 也比普通分类模型复杂。
- 定位：完成基准测试后作为默认随包模型。

### oemer（首个可运行基线）

- 许可证：MIT（发布前仍需逐项确认模型权重和训练数据的再分发条款）。
- 输出：MusicXML。
- 优点：默认使用 ONNX Runtime，已有倾斜和手机照片处理，接入路径最短；MusicXML 转换层也可以被其他后端复用。
- 限制：模型较旧，项目说明中的 GPU 推理时间仍为分钟级，并且项目自身推荐更强的 homr；不宜在没有实测前确定为最终后端。
- 定位：用于尽快打通“图像 → OMR → MusicXML → `ScoreDocument`”完整链路和建立基准结果。

### homr（质量参考后端）

- 许可证：AGPL-3.0。
- 输出：MusicXML。
- 优点：包含符号分割、谱表重建与去畸变、Transformer 乐谱识别，针对拍照图片和复调谱面更完整。
- 限制：不直接并入或随 MIT 发布包分发。可作为开发期对照基线，或者由用户另行安装后通过文件接口调用；具体分发方式仍需单独审核许可证义务。
- 定位：质量上限与回归测试参考，不作为默认附加包。

### Audiveris（传统工程参考）

- 许可证：AGPL-3.0。
- 输出：MusicXML 4.0 子集。
- 优点：成熟、可人工校正、Windows 安装包自带 JRE，适合复杂印刷谱的离线对照。
- 限制：Java 工具链和 AGPL 不适合直接嵌入当前 MIT 附加包；自动化调用和包体也不如专用模型轻量。
- 定位：人工校正和复杂谱面对照工具。

### 暂不采用

- Polyphonic-TrOMR：Apache-2.0 友好，但公开实现偏研究性质，预训练与复现链不如上述方案完整。
- Clarity-OMR：GPL-3.0 且项目仍较新，不适合作为当前发布基线。
- 通用文字 OCR：能辅助识别标题、速度和文本，不能可靠恢复谱表结构、复调、连音和时值，不应承担五线谱主体识别。

## 接入架构

```text
OcrAnalysisRequest
        |
        v
NotationRouter -- Numbered --> existing JianpuRecognizer
        |
        +-------- Staff -----> StaffBackend process
                                  |
                       MusicXML or beKern
                                  |
                       canonical OMR document
                                  |
                       MusicXmlScoreImporter
                                  |
                           ScoreDocument
```

后端继续由独立后台进程执行，并沿用现有文件 IPC、进度通知、取消和附加包下载机制。主程序协议只接收规范化结果，不依赖 Python、PyTorch、Java 或某个模型的私有类型。

MusicXML 是第一阶段的交换格式。SMT 的 beKern 结果先转换为同一规范化中间层，避免分别维护两套节拍、声部和小节对齐逻辑。

## 实施阶段

1. **转换基座**：实现 MusicXML `score-partwise` 导入，覆盖 divisions、拍号、tempo、measure、voice、chord、backup/forward、tie 和休止符；建立最小单谱表、钢琴大谱表、复调和弱起小节测试。
2. **基线后端**：已通过附加包内的精简便携 Python 3.11 CPU Runtime 接入 oemer，不把 Python 或模型放入主程序 ZIP；继续记录处理时间、峰值内存、音高/时值/小节准确率和失败原因。
3. **模型评测**：用相同测试集比较 oemer、SMT Camera-GrandStaff、homr 和 Audiveris。开发集与验收集分离，避免只针对少量样图调参。
4. **默认模型**：若 SMT 达到基线质量，完成 beKern 解析与模型打包；否则保留 oemer 为临时默认，并继续训练或微调 MIT 模型。
5. **导入校正**：在写入当前曲谱前展示小节/声部、低置信度符号、半音和越界音符，让用户选择保留、折叠或忽略。

## 首轮验收指标

- 清晰扫描件和普通手机照片分别统计，不混用一个平均值掩盖退化。
- 音高准确率、音符起点准确率、时值准确率和小节对齐率分别达到可用阈值。
- 双谱表不得因下方声部晚入而整体左移；`backup`、弱起和多声部必须保持各自时间游标。
- 失败时返回可诊断错误，不生成看似成功但节拍结构损坏的曲谱。
- OCR 附加包使用 CPU 版 ONNX Runtime，控制发布体积并避免要求用户安装 CUDA/cuDNN 环境。

## 许可证与发布

每个模型包必须单独记录代码许可证、权重许可证、训练数据来源、版本、下载地址和 SHA-256。签名沿用 OCR 附加包现有发布流程。AGPL/GPL 后端不得混入默认 MIT 附加包；如提供外部适配器，需在发布前再次审查实际组合和分发方式。

## oemer 开发环境部署

将 Python 环境安装到 OCR 附加包的 `staff-omr` 目录。支持 `oemer.exe`、`.venv/Scripts/oemer.exe`、`python/Scripts/oemer.exe`，也支持 `python/python.exe` 或 `.venv/Scripts/python.exe`。Python 后端通过随 OCR 引擎发布的 `staff-omr/oemer_bridge.py` 调用，以跳过非必要的分析图生成，并兼容上游对空白谱线分区处理不完整的问题。开发构建还会自动查找工作区同级的 `_research/oemer/.venv/Scripts/python.exe`。

开发时也可通过 `GENSHINPIANO_OEMER_EXECUTABLE` 指向 `oemer.exe` 或 `python.exe`。当前界面不做谱面类型自动检测：用户选择“五线谱”后，引擎才会在临时目录运行 Oemer 后端、导入其 MusicXML，并在结束或取消后清理临时文件；选择“简谱”时不会启动 Python。

推荐从仓库根目录运行 `tools/Setup-OcrDevelopment.ps1`。脚本会准备固定的上游版本、依赖、模型及补丁，再把完整附加包部署到主程序 Debug 输出。模型和便携 Runtime 体积较大，因此不提交到 Git；正式发布时由 `tools/Publish-OcrAddon.ps1` 生成独立 OCR ZIP。

已验证的 Windows CPU 开发环境为 Python 3.11、NumPy 1.26.4、SciPy 1.11.4、ONNX Runtime 1.17.3、OpenCV 4.10.0 和 scikit-learn 1.2.0。scikit-learn 应保持 1.2.0，与随 oemer 分发的 SVC 模型保存版本一致。正式附加包使用 CPU 版 `onnxruntime`，不包含 CUDA/cuDNN。`types-Pillow` 和 `types-tensorflow` 仅是上游开发类型依赖，运行时不需要。

官方 ONNX 权重放置及校验值：

- `checkpoints/unet_big/model.onnx`：70,767,752 字节，SHA-256 `37512E858731096439746F60B377C049F07055B4A23EC6EB9A178CE92CFBA174`
- `checkpoints/seg_net/model.onnx`：38,448,467 字节，SHA-256 `ED2E1A86EA75712EE6CDC740E96F7A36753543CF9BB980227C071C9256D9D82E`

2026-09-02 在本地 CPU 环境使用 oemer 文档中的 Wind2 单侧校正谱面完成基线验证：约 7 分 44 秒生成 112,356 字节 MusicXML；主程序安全忽略标准 MusicXML DOCTYPE 后成功导入 2 个谱表轨道、352 个有音高音符（其中 44 个升降音）和 90 BPM。文档中的 `*_deskew.jpg` 是左右对比拼接图，测试时必须先裁出单侧，不能直接作为 OMR 输入。
