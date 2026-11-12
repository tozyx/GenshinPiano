# 五线谱识别后端调研

调研日期：2026-11-11。目标：Windows x64、离线 CPU、钢琴双谱表、保留音高与节奏、减小 OCR 附加包，并尽量避免发布 Python 环境。

后续范围决定：拍照谱试验后，按用户要求放弃 SMT 的照片适配与自动矫正，实施目标仅为清晰、平直扫描谱和电子谱截图。下文照片候选和照片评测建议保留为初始调研资料，不列入当前实施范围。

初始调研核对官方仓库、作者模型卡及原生移植实现。随后已进行 SMT 原生推理与两张扫描谱实测，见 [SMT 试验记录](SMT_EXPERIMENT.md)；其余候选尚未运行。下文的优先级是本项目的工程判断；作者的识别指标不能视为本项目的测试结果。MB 为来源使用的十进制文件大小，MiB 为本项目测量使用的二进制单位；模型文件大小不等于完整附加包 ZIP 大小。

## 结论与选择顺序

1. **先验证 SMT/SMT++ 的原生部署**：同时试整页模型和钢琴谱行模型。公开权重较小，已有第三方 C++ 推理源码，能够先验证去掉 Python 的收益。
2. **再验证 Copista-2m / Copisteria**：约 400 万参数，直接输出 MusicXML，是值得增加的小模型候选；当前参考实现仍依赖 PyTorch，不能直接认定安装包更小。
3. **用 homr 和 Audiveris 做质量对照**：两者能输出 MusicXML，但部署依赖、发布条款和模型来源需要分别核对。
4. **Sheet-Music-Recognition 做轻量探索**：分割模型很小，但作者明确标记实验性，不能以分割模型大小代替完整识别能力。
5. **Transcoda 暂列研究候选**：原生推理已有实现，但模型来源与许可说明存在冲突。

现阶段建议保留 Oemer 作为对照，暂不投入完整算法移植。优先做可以被上述候选复用的输入、结果转换与评测工作。

## SMT 系列：首选验证对象

官方 Camera-GrandStaff 模型是 21.4M 参数，模型文件约 85.5 MB，面向钢琴双谱表的单个谱行系统。这里的谱行系统指同一行的上下谱表，而非整张图片。官方模型卡标注 MIT。它需要整页谱图的系统切分，不能把完整多行谱页直接当成单系统输入。[模型说明](https://huggingface.co/antoniorv6/smt-camera-grandstaff)、[文件列表](https://huggingface.co/antoniorv6/smt-camera-grandstaff/tree/main)

另有 PRAIG 的整页 `smt-fp-grandstaff`，10.9M 参数，仓库文件合计约 43.7 MB，标注 MIT。整页路线能减少自行切分谱行的工作，但必须单独验证密集页面、缩放、序列截断以及拍照输入；不能把单系统 Camera 版本的指标套到整页版本上。[整页权重](https://huggingface.co/PRAIG/smt-fp-grandstaff)、[文件列表](https://huggingface.co/PRAIG/smt-fp-grandstaff/tree/main)

第三方 CrispEmbed 已有 C++/ggml 的 SMT 推理源码，覆盖单系统及上述整页架构，输出 beKern 风格符号序列。其自身代码为 MIT，提供 Windows 构建方式，发布推理时不要求 Python；权重转换阶段仍可使用 Python。原生实现的存在降低了自行导出 ONNX 和重写解码器的必要性，但并不代表已经通过本项目验收。[C++ 源码](https://raw.githubusercontent.com/CrispStrobe/CrispEmbed/main/src/smt_ocr.cpp)、[项目与构建说明](https://github.com/CrispStrobe/CrispEmbed)、[代码许可](https://raw.githubusercontent.com/CrispStrobe/CrispEmbed/main/LICENSE)

**接入工作**：将原生 CLI 作为 OCR 工作进程的子进程调用；实现 beKern 到内部曲谱的转换，处理声部、和弦、休止、附点、连音和谱行拼接。必须按实际模型输出方言实现解析，不能假设通用 kern 解析器完全兼容。后续再决定是否通过 C ABI 调用，或改成 C# ONNX 推理。

**判断**：最值得先做部署验证；如果质量达到现有基线，有机会同时减少 Python 依赖与模型体积。不能仅凭权重大小给出完整包体积或 CPU 耗时承诺。

## Copista-2m / Copisteria：小模型候选

Copisteria 是完整识别流水线，Copista 是其符号检测器。小版包含 1.94M 参数检测器与 2.03M 参数证据模型，合计约 4M；大版合计约 30.7M。流水线输出 MusicXML。代码与小版模型卡均标注 Apache-2.0。[代码及架构](https://github.com/kobimusic/copisteria)、[小版模型卡](https://huggingface.co/kobimusic/copista-2m)

参考实现目前依赖 Python 3.12、PyTorch、torchvision、SciPy、OpenCV 等，并未在所查文档中提供现成的 .NET 部署方案。小模型不等于小运行时；需要验证模型导出及乐谱重建代码的迁移成本。[依赖清单](https://raw.githubusercontent.com/kobimusic/copisteria/main/pyproject.toml)

作者公布了多类扫描谱测试，但这些是作者运行的结果，本次不据此做跨后端准确率排名。模型卡承认小检测器对细小记号更容易漏检，谱表定位仍有规则处理，未写出的三连音标记也可能导致节奏错误。[局限说明](https://huggingface.co/kobimusic/copista-2m#limitations)

**接入工作**：先用原始流水线测钢琴双谱表；质量合格后再验证两个模型的 ONNX 导出和后处理迁移。MusicXML 可以接入现有导入器，但输出的具体元素仍需兼容性测试。

**判断**：包体积潜力突出，优先级仅次于已有原生实现的 SMT。属于较新项目，需要验证稳定性与可重复性。

## homr / Andromr：质量对照与部署参考

homr 面向拍照或 PDF 乐谱，包含分割、谱表结构处理、Transformer 符号识别和 MusicXML 输出，提供 ONNX 检查点。[官方仓库](https://github.com/liebharc/homr)、[ONNX 发布](https://github.com/liebharc/homr/releases/tag/onnx_checkpoints)

Andromr 基于 homr，已将模型推理部分放到 Java：分割和编码器使用 LiteRT，解码器使用 ONNX Runtime。但 Android 应用及部分流程仍使用 Python，所以它是模型推理迁移的参考，不是可直接复用的无 Python Windows 引擎。[Andromr 技术说明](https://github.com/aicelen/Andromr#technical-details)

homr 的代码和权重标注 AGPL-3.0。作者更新的 FAQ 还列出训练数据条款和不确定项，包含 Camera-PrIMuS 来源的非商业标注，不能只检查仓库根目录许可证。[作者许可 FAQ](https://github.com/liebharc/homr/blob/main/LICENSE_FAQ.md)

**判断**：适合作为拍照识别的实测对照。直接替换 Oemer 不意味着减小包体；作为默认发布后端前需要核对具体模型版本与分发方案。

## Audiveris：成熟的无 Python 外部后端

Java 实现，可导出 MusicXML 4.0 的子集，Windows 安装包带 JRE，代码为 AGPL-3.0。[官方项目](https://github.com/Audiveris/audiveris)

**接入工作**：通过批处理接口或用户安装的外部程序交换 MusicXML，验证无人值守处理、失败诊断及输出兼容性。

**判断**：适合清晰印刷谱和人工校正工作流对照，避免 Python 但会引入 Java 部署。没有实测完整安装体积，不能认定其比当前包小；不建议为压缩体积而立即更换为 Audiveris。

## Sheet-Music-Recognition（原 Score-OMR）：极轻量的实验路线

项目提供 Python/Java 接口，输出 MusicXML、MIDI 和 JSON。独立 v4 的 TFLite 分割模型为 1,946,503 参数、3,907,460 字节，代码和权重标注 Apache-2.0。模型输出六类像素分割图，音高和时序由 Java 解码器结合原图恢复。[项目](https://github.com/Luckolite/Sheet-Music-Recognition)、[模型卡](https://github.com/Luckolite/Sheet-Music-Recognition/blob/main/models/MODEL_CARD.md)

已有桌面 ONNX 分割适配文档，但针对旧版 v3；不能把它视为当前 v4 的现成 ONNX 部署。模型卡明确承认复杂复调、升降记号、时值等仍存在错误，没有独立整页识别质量保证。[桌面适配说明](https://github.com/Luckolite/Sheet-Music-Recognition/blob/main/docs/portable-segmentation.md)

**判断**：适合验证“较小模型是否足以满足本项目”，暂不适合作为默认后端。约 3.9 MB 仅是分割模型大小，不含 Java、OCR、解码器及其他可选模型。

## Transcoda：原生实现已有，但许可信息待澄清

59M 参数的整页模型，输出 Humdrum kern。上游参考代码是 AGPL-3.0，模型卡标注 CC BY 4.0，固定输入为 1485×1050；异常长宽比需要调用方处理，卡片也承认真实扫描谱的域差异。[上游代码](https://github.com/btrkeks/transcoda)、[模型卡](https://huggingface.co/btrkeks/transcoda-59M-zeroshot-v1)

CrispEmbed 有第三方原生实现，量化文件卡片称 Q8 约 65 MB；其精度对齐说明仅覆盖所述验证页，不能推导出整个测试集无退化。更重要的是，该量化包页头及新增说明为 CC BY-NC 4.0，正文旧段落仍称 CC BY 4.0，与上游模型卡也不一致。新增说明对编码器预训练权重的来源提出了疑问；本次未独立证实该疑问，因此记录为冲突而非确定的法律结论。[量化包及冲突说明](https://huggingface.co/cstr/transcoda-omr-GGUF)

**判断**：保留研究位置，暂不按宽松许可默认包候选处理。先澄清具体权重的来源、条款和版本，再决定是否评测或分发。

## 其他候选

- **Polyphonic-TrOMR**：复调符号识别，官方提供推理与数据说明，代码标注 Apache-2.0；CrispEmbed 也有第三方原生支持。更像谱行识别组件，整页分割、输出语义和 MusicXML 拼接仍要补齐，排在 SMT 之后。[官方项目](https://github.com/NetEase/Polyphonic-TrOMR)、[原生实现项目](https://github.com/CrispStrobe/CrispEmbed)
- **page-omr**：184M 参数，整页输出 kern 或 ABC，代码标注 MIT。值得作整页质量研究，但体积与自回归推理成本需要另测；按 184M×4 字节估算，仅 FP32 参数就约 736 MB，这不是实际发布包大小。[官方项目](https://github.com/tobiashornbogen/page-omr)
- **LEGATO / LEGATO 2**：面向整页识别。第一代基于 Llama 3.2 11B Vision，还需要接受其基础模型条款；第二代已有公开模型卡但权重访问需要登录并接受条件。更适合作为研究或服务端对照，不符合当前优先缩小离线 CPU 包的目标。[第一代模型卡](https://huggingface.co/guangyangmusic/legato)、[第二代模型卡](https://huggingface.co/guangyangmusic/legato-2)
- **Clarity-OMR**：YOLO 谱表检测加符号识别，直接输出 MusicXML；当前官方部署是 Python/PyTorch，代码 GPL-3.0，推荐 GPU，也支持 CPU。作为对照可以，尚无证据表明直接替换能缩小附加包。[官方项目](https://github.com/clquwu/Clarity-OMR)

## 许可判断的修正

早期选型文档把 AGPL/GPL 后端一概排除在 MIT 附加包之外，表述过于绝对。不能把它们等同于“禁止商业使用”，也不能因独立进程调用就直接认定没有义务。候选需要分别核对组合方式、相应源码提供、各组件与模型权重条款。homr 作者 FAQ 明确说明 AGPL 可在遵守条款的前提下商业使用，同时保留对调用方应用关系的判断。[作者 FAQ](https://github.com/liebharc/homr/blob/main/LICENSE_FAQ.md)

本次优先考虑宽松许可，是为了减少发布与维护工作，不把许可证名称单独作为识别能力的淘汰条件。

## 建议的最小验证

1. 固定约 30 张允许测试的图片：清晰钢琴双谱表、密集复调、手机倾斜/阴影照片各约 10 张。保留原始音高，不以折叠后的 21 键演奏结果评估识别。
2. 第一轮比较 Oemer、SMT 单系统/整页、Copista-2m；可运行且条款明确后，加入 homr 与 Audiveris。不要同时改模型、量化和结果转换，先保留可解释的基线。
3. 人工核对音符增漏、音高、起点、时值、声部、小节对齐；单独记录失败、截断和空结果，不能只统计成功页面。
4. 同机同图记录冷启动、整页耗时和完整 ZIP/解压体积。分开报告扫描件与拍照谱；验证不安装 Python、Java、GPU 工具链的干净机器能否运行。
5. 只有候选质量达到可接受水平，才做正式适配、量化及发布。SMT 的原生运行和 beKern 转换是第一阶段关键；若都不合格，再考虑继续移植 Oemer。

后续 SMT 试验已完成 CPU 原生部署、扫描谱切行和预处理对照，并与当前附加包 Oemer 进行了同图实测，按逐图转录的参考评测了全部 17 小节、243 个发声音符；尚未切换现有识别后端。具体版本及复跑方法见 [SMT 试验记录](SMT_EXPERIMENT.md)，准确度、评分口径与范围见 [扫描谱对照](OCR_SMT_OEMER_COMPARISON.md)。
