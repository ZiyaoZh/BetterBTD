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

第一迭代已经提供：

- 按默认 BTD6 标题与进程名、显式 HWND、PID 或进程名发现窗口。
- 未发现窗口时从显式可执行文件启动并等待主窗口。
- 恢复窗口、请求激活并验证前台所有权。
- 读取物理像素客户区、窗口矩形、DPI 和虚拟桌面边界。
- 从桌面合成画面捕获客户区，保存 PNG、JSON、规范化 RGB 全像素指纹和文件哈希。
- 最后原子写入完成标记；标记缺失或其中任一哈希不一致时，该组证据无效。
- 输出 `1920 x 1080` 基准空间与实际客户区之间的独立换算参数。

Python 依赖全部锁定在工具自己的虚拟环境。实现和使用说明见 [Game Driver README](../../tools/BetterBTD.GameDriver/README.md)。

## 坐标协议

截图像素使用 `clientPhysicalPixels`：原点为客户区左上角，X 向右，Y 向下，边界左闭右开。Game Driver 线程使用 Per-Monitor V2 取得物理坐标；这只影响当前进程解释 Win32 坐标，不修改系统显示配置。

稳定元素规则使用 `btd6Reference1920x1080`。从基准点映射到截图点时，分别按宽和高缩放、四舍六入五成双，并钳制到客户区范围。矩形先缩放四条边，再计算实际宽高，以免累积舍入误差。该算法与 BetterBTD 现有坐标和识图缩放约定一致，但实现完全独立。

非 `16:9` 客户区仍会保存原始截图并标记 `hasReferenceAspectRatio=false`。识别与操作层不得忽略该字段后盲目使用基准规则。

## 输入所有权

| 阶段 | 游戏输入方 | Game Driver 行为 |
| --- | --- | --- |
| Arrange | Game Driver | 建立地图、难度、模式、英雄等前置状态 |
| Act | BetterBTD | 只截图观察，不点击或发送按键 |
| Assert | 无或 BetterBTD | 独立判断可见结果 |
| Recover | Game Driver | API 确认脚本停止后才恢复现场 |

当前版本尚未提供输入命令，因此不会与 BetterBTD 争用输入设备。后续增加输入时必须在 CLI 层显式区分观察与控制命令，并由测试编排层维护上述阶段约束。

## 后续迭代

1. 增加连续帧指纹、画面稳定和变化等待。
2. 增加客户区坐标点击、按键、输入和滚动，并保存操作前后证据。
3. 从真实截图建立独立页面目录、元素 ID、标注和模板。
4. 增加页面、元素、文本、数值和选中状态识别。
5. 与 BetterBTD Test API 组合，但保持截图与识别 Oracle 独立。

桌面 GDI 首版后端要求游戏可见且无遮挡。锁屏、断开的 RDP、独占全屏和部分 DirectFlip 路径可能无法提供有效画面；命令输出会明确标记 `occlusionSensitive=true` 和 `stabilityCheckPerformed=false`，测试编排不得忽略这些条件。
