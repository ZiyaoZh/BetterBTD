# 独立 BTD6 Game Driver

`tools/BetterBTD.GameDriver` 是 Agent 黑盒测试架构中的独立游戏侧驱动。它直接观察真实 BTD6 窗口，不经过 BetterBTD 的捕获、OCR、模板、状态服务或脚本返回值。

## Oracle 边界

对于黑盒可见行为，Game Driver 保存的真实客户区截图是原始证据，独立识别层对这些截图的解释才可以成为测试 Oracle。BetterBTD 自身提供的截图、OCR、状态判断、日志和脚本结果只能作为被测行为或 `nonOracleDiagnostics`，不能单独构成通过条件。

```text
独立 Game Driver ──真实窗口截图/输入──> BTD6
        │
        ▼
测试编排层 ──控制 API──> BetterBTD ──脚本输入──> BTD6
```

Game Driver 不导入 BetterBTD Python 模块，也不引用 BetterBTD 或 Fischless 程序集。它可以复用公开的 Win32 原理和稳定坐标协议，但不能读取 BetterBTD 运行时对象、模板资源或识别结果。

## 当前实现

当前实现已经提供：

- 按默认 BTD6 标题与进程名、显式 HWND、PID 或进程名发现窗口。
- 未发现窗口时从显式可执行文件启动并等待主窗口。
- 恢复窗口、请求激活并验证前台所有权。
- 读取物理像素客户区、窗口矩形、DPI 和虚拟桌面边界。
- 从桌面合成画面捕获客户区，保存 PNG、JSON、规范化 RGB 全像素指纹和文件哈希。
- 最后原子写入完成标记；标记缺失或其中任一哈希不一致时，该组证据无效。
- 输出 `1920 x 1080` 基准空间与实际客户区之间的独立换算参数。
- 在读取阶段校验截图、元数据和完成标记三件套，忽略元数据中不可迁移的绝对图片路径。
- 版本化独立视觉目录、稳定页面/元素 ID、模板来源证据和全链路 SHA-256 校验。
- 从已校验来源证据确定性重建模板，不读取 BetterBTD OCR 或模板资源。
- 对 `16:9` 真实截图执行离线多锚点识别，输出 `matched`、`unknown` 或 `ambiguous`、锚点分数、元素边界和动作点。
- 第一组中文主菜单基准：4 个稳定图标锚点、19 个目录元素、一份制模证据、一份不同动画帧的真实正向留出证据和一份真实启动画面负向证据。只有绑定锚点的元素报告独立可见性，其余元素返回 `notEvaluated`。
- 中文地图选择基准：返回、搜索、左右翻页和猴子草甸 5 个锚点、13 个目录元素，以及不同动画时刻的制模/留出证据。
- `click` 控制命令：只点击当前已识别页面中具备独立可见性检测的目录按钮，保存操作前后证据和独立轨迹。
- 操作后按连续帧差异等待画面发生变化并稳定，可用 `--expect-page` 要求最终独立识别到指定页面。

Python 依赖全部锁定在工具自己的虚拟环境。实现和使用说明见 [Game Driver README](../../tools/BetterBTD.GameDriver/README.md)。

## 坐标协议

截图像素使用 `clientPhysicalPixels`：原点为客户区左上角，X 向右，Y 向下，边界左闭右开。Game Driver 线程使用 Per-Monitor V2 取得物理坐标；这只影响当前进程解释 Win32 坐标，不修改系统显示配置。

稳定元素规则使用 `btd6Reference1920x1080`。从基准点映射到截图点时，分别按宽和高缩放、四舍六入五成双，并钳制到客户区范围。矩形先缩放四条边，再计算实际宽高，以免累积舍入误差。该算法与 BetterBTD 现有坐标和识图缩放约定一致，但实现完全独立。

非 `16:9` 客户区仍会保存原始截图并标记 `hasReferenceAspectRatio=false`。识别与操作层不得忽略该字段后盲目使用基准规则。

## 视觉基准与 Oracle 资格

视觉目录位于 `tools/BetterBTD.GameDriver/visual-baselines/`。模板只能从目录内保留的 Game Driver 原始证据裁剪，并记录来源 `evidenceId`、来源图片哈希、裁剪矩形与模板哈希。标注文件和人工复核叠加图属于独立解释层，不能修改或取代原始截图。

`recognize` 必须以捕获元数据 JSON 为入口，并根据相邻文件名找到 PNG 与完成标记。只有三件套哈希一致、来源为 `BetterBTD.GameDriver`、宽高比适用、无捕获警告且页面唯一匹配时，`recognition.oracleEligible` 才为 `true`。BetterBTD 内部诊断不能写入此字段。

首版比较器将截图规范化到基准尺寸，在固定稳定区域比较多个独立模板。页面必须同时满足最少锚点数和总分阈值；未达到阈值返回 `unknown`，多个页面分数过近返回 `ambiguous`，两者都不能作为通过条件。

## 输入所有权

| 阶段 | 游戏输入方 | Game Driver 行为 |
| --- | --- | --- |
| Arrange | Game Driver | 建立地图、难度、模式、英雄等前置状态 |
| Act | BetterBTD | 只截图观察，不点击或发送按键 |
| Assert | 无或 BetterBTD | 独立判断可见结果 |
| Recover | Game Driver | API 确认脚本停止后才恢复现场 |

`capture`、`recognize` 和 `catalog` 是观察命令；`click` 是控制命令。`click` 强制要求 `--phase arrange` 或 `--phase recover`，不接受 Act/Assert，但 CLI 无法独立证明 BetterBTD 脚本是否正在执行。测试编排层必须保证 Arrange 尚未启动脚本，Recover 则已通过 API 停止脚本并确认停止后再调用 Game Driver。

点击前必须同时满足：当前原始证据完整且无警告、页面唯一匹配、目标元素属于当前页、角色为按钮、动作点有效、绑定锚点全部可见。点击后 Win32 输入调用成功不构成通过条件；只有独立截图轨迹观察到明显变化、连续稳定，并在指定时匹配 `--expect-page`，命令才成功。

## 后续迭代

1. 用已具备独立可见性检测的猴子草甸入口扩展难度和模式页面。
2. 增加按键、文本输入、滚动和元素出现/消失等待，并继续保存操作前后证据。
3. 扩展英雄、关卡内、暂停、胜负和弹窗的真实视觉基准。
4. 增加每个元素的独立文本、数值和选中状态识别。
5. 与 BetterBTD Test API 组合，但保持截图与识别 Oracle 独立。

桌面 GDI 首版后端要求游戏可见且无遮挡。锁屏、断开的 RDP、独占全屏和部分 DirectFlip 路径可能无法提供有效画面；单帧证据会明确标记 `occlusionSensitive=true` 和 `stabilityCheckPerformed=false`。`click` 的稳定性记录在同目录 `operation.json` 中，测试编排不得把两者混为同一字段。
