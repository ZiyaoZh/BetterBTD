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
- 页面锚点与 detector-only 元素模板分离；`pageAnchor=false` 的动态或可滚动元素不参与页面分数和最少页面锚点数。
- catalog schema v2 在稳定页面之下识别独立 `viewState`，并按视口解析元素 placement、可见性、动作点与元素状态；schema v3 增加来源可审计的独立数值模型，加载器保持对 schema v1/v2 的兼容。
- 第一组中文主菜单基准：4 个稳定图标锚点、19 个目录元素、一份制模证据、一份不同动画帧的真实正向留出证据和一份真实启动画面负向证据。只有绑定锚点的元素报告独立可见性，其余元素返回 `notEvaluated`。
- 中文地图选择基准：4 个固定页面锚点和 17 个独立轮播视口，覆盖初级 5 页、中级 5 页、高级 4 页、专家 3 页及共 87 张可见地图；每个视口均绑定独立 source/holdout 证据、页指示锚点、地图卡检测与 placement。四个分类按钮和两个底部选项具有独立状态检测；官方 `Ascent` 分为解锁按钮和锁定状态两个互斥元素，账号状态不参与视口身份。
- 中文冷启动基准：`welcome` 和阻断式 `modifiedClientWarning`；后者只允许安全的继续动作，不为注销账号或关闭游戏提供动作点。
- 中文关卡入口基准：`difficultySelect` 的 `Easy/Medium/Hard`，以及 `easyModeSelect` 的 `Standard/PrimaryOnly/Deflation`；这些 ID 与脚本持久化枚举名一致，不使用本地化显示文本。
- 中文中级/困难模式基准：覆盖 `MilitaryOnly`、`Apopalypse`、`Reverse`、`MagicOnly`、`DoubleHpMoabs`、`HalfCash`、`AlternateBloonsRounds`、`Impoppable` 和 `CHIMPS`，并保留可见的 `Sandbox`。
- 中文英雄选择基准：页面 Oracle 使用固定控件，`heroSelect.top`/`heroSelect.bottom` 合并覆盖 18 个 `HeroType` 及 30 个视口 placement，并独立区分 `choose` 与 `selected` 状态；真实验证包括底部视口识别、Corvus/Silas 元素点击、滚轮返回顶部、Quincy 选择和恢复主菜单。英雄网格不接受拖动滚屏。
- 中文关卡内与暂停基准：`inLevel` 只使用跨难度公共 HUD，生命图标和动态开始按钮框为 detector-only，稳定右侧栏参与页面身份；`inLevel.roundReady`/`inLevel.roundActive` 区分待开回合与活动回合，并为开始/快进按钮解析对应 placement。生命、金币和当前回合由独立数值模型解析。`stageSettings` 使用底部命令图标。真实验证简单/困难标准、CHIMPS 待开及活动画面、暂停继续和暂停返回主页。
- 中文失败基准：`overwriteSaveConfirmation`、`chimpsModeInfo`、`defeatSummary`、`retryLastRoundConfirmation`、`restartGameConfirmation` 和 `postGameMapReview` 覆盖存档覆盖确认、模式说明、首回合与后续回合失败、重新开始、重试上一回合和赛后地图检视。失败总结的三按钮/四按钮布局由两个视口状态约束；主页、重新开始、浏览地图、重试上一回合及两个确认框的全部安全分支均已真实执行。失败页“浏览地图”进入的是独立 `postGameMapReview`，不是 `mapSelect`。重开和重试确认使用各自的中文标题锚点区分相同外壳；暂停设置中的重开确认仍未覆盖。
- 通用存档覆盖确认基准：`overwriteSaveConfirmation` 使用中文标题锚点，不依赖底层困难模式背景。真实验证已覆盖困难 CHIMPS 与简单 Deflation 的取消/确认；Deflation 确认后经过加载直接进入 `inLevel`，没有额外模式说明页。
- 中文设置基准：`settings`、`hotkeys` 和 `accessibility` 完整声明当前可见控件，配置、账号和外部链接保持不可操作；真实验证从主菜单进入、子页往返和恢复主菜单。
- 中文额外设置基准：`extras.top`/`extras.bottom` 覆盖全部 7 个开关、14 个视口 placement 和 28 个 `enabled`/`disabled` 状态声明；开关不提供动作点，真实验证包括上下端点滚动和边界 `unchangedStable`。
- 中文 Sandbox 基准：`sandboxIntro`、气球控制视图 `sandbox`、塔控制视图 `sandboxTower`、`sandboxHealthEditor` 和 `sandboxCashEditor` 覆盖说明弹窗、生命/金币编辑器、回合参数、三种气球属性、17 种气球、清除、力量、当前可见的 11 个标准塔、气球/塔模式入口及开始/快进。三个关卡视图的生命、金币、回合共享独立白色 HUD 数值模型。
- `click` 控制命令：点击当前已识别页面中具备独立可见性检测的目录按钮，或已独立匹配且 Oracle 可用的数值 value，保存操作前后证据和独立轨迹，并可独立断言最终页面与视口。
- `click-point` 控制命令：按 `1920 x 1080` 参考客户区坐标执行引导采集，并保留相同的输入所有权、窗口校验、前后证据和独立轨迹；它不要求操作前页面已识别，因此不能充当元素 Oracle，但仍可独立断言最终页面与视口。
- `scroll-point` 控制命令：在参考客户区点发送指定方向和档位的垂直滚轮输入；默认要求画面变化后稳定，只有显式 `--allow-no-change` 才接受滚动边界的 `unchangedStable`，并可用 `--expect-view-state` 独立确认最终视口。
- `drag-point` 控制命令：将两个参考点分别换算到客户区，按指定 `duration-ms` 和 `steps` 执行线性左键拖动；底层用 `finally` 保证 mouse-up 清理，也支持变化/稳定、`--allow-no-change` 和视口期望。
- `press-key` 控制命令：按规范键名发送一个受限键盘组合，固定修饰键顺序并在异常路径逆序释放已按下按键；BetterBTD 全局脚本启停键 `F10` 和系统级危险组合会在输入前拒绝，也支持变化/稳定和页面/视口期望。
- 控制操作保存前后证据和 `operation.json`，按连续帧差异等待画面稳定，并可用 `--expect-page` 和 `--expect-view-state` 声明独立最终状态断言。存在期望时，稳定但不匹配的加载页、未知页或其他页面只作为非 Oracle 候选记录，等待会在同一绝对超时预算内继续；最终 `after` 三件套仍是断言和 Oracle 资格的唯一依据。`press-key` 在首个 key-down 前先写入待处理轨迹；底层输入或释放失败时更新稳定错误信息，此时可能没有 `after`，但输入尝试仍可审计。

Python 依赖全部锁定在工具自己的虚拟环境。实现和使用说明见 [Game Driver README](../../tools/BetterBTD.GameDriver/README.md)。

## 坐标协议

截图像素使用 `clientPhysicalPixels`：原点为客户区左上角，X 向右，Y 向下，边界左闭右开。Game Driver 线程使用 Per-Monitor V2 取得物理坐标；这只影响当前进程解释 Win32 坐标，不修改系统显示配置。

稳定元素规则使用 `btd6Reference1920x1080`。从基准点映射到截图点时，分别按宽和高缩放、四舍六入五成双，并钳制到客户区范围。矩形先缩放四条边，再计算实际宽高，以免累积舍入误差。该算法与 BetterBTD 现有坐标和识图缩放约定一致，但实现完全独立。

非 `16:9` 客户区仍会保存原始截图并标记 `hasReferenceAspectRatio=false`。识别与操作层不得忽略该字段后盲目使用基准规则。

## 视觉基准与 Oracle 资格

视觉目录位于 `tools/BetterBTD.GameDriver/visual-baselines/`。模板只能从目录内保留的 Game Driver 原始证据裁剪，并记录来源 `evidenceId`、来源图片哈希、裁剪矩形与模板哈希。每页的正向留出证据必须是另一组完整 Oracle 证据，图片哈希不得与任一制模源图相同。标注文件和人工复核叠加图属于独立解释层，不能修改或取代原始截图。

`recognize` 必须以捕获元数据 JSON 为入口，并根据相邻文件名找到 PNG 与完成标记。只有三件套哈希一致、来源为 `BetterBTD.GameDriver`、宽高比适用、无捕获警告且页面唯一匹配时，`recognition.oracleEligible` 才为 `true`。BetterBTD 内部诊断不能写入此字段。

首版比较器将截图规范化到基准尺寸，在固定稳定区域比较多个独立模板。`pageAnchor` 缺省为 `true`；设为 `false` 的模板仍可判断元素可见性，但不能抬高或拉低页面分数，也不能满足最少页面锚点数。页面必须同时满足最少锚点数和总分阈值；未达到阈值返回 `unknown`，同类页面分数过近返回 `ambiguous`，两者都不能作为通过条件。遮挡并阻止底层交互的顶层画面声明为 `kind=modal`；命中的 modal 优先于普通 page，modal 候选之间仍按相同分差规则拒绝歧义结果。

当前 catalog v18 使用 schema v3，共 29 个页面、411 个锚点模板、10 个数值字形模板、372 个元素、25 个视口状态和 260 个元素 placement。`page` 表示稳定逻辑页面，`viewState` 表示同页内可独立识别的轮播、滚动或控制布局状态。元素可按视口声明 `placements`，每项包含该视口中的边界、动作点、检测锚点和可选 `states`；识别结果仅采用当前已识别视口对应的 placement。页面 `score` 只计算页面锚点，`rankingScore` 可合入视口分数以参与跨页面候选竞争。元素状态使用独立锚点返回 `matched`、`ambiguous` 或 `unknown`。307 个元素具有独立可见性或数值探测器，260 个元素声明动作点，其中 237 个同时可独立观察并具有动作点。

schema v2 锚点的 `sourceBounds` 表示从来源证据确定性裁剪模板的位置，运行时仍在 `bounds` 指定的位置比较。schema v1 目录继续受支持：页面没有 `viewStates`，元素沿用顶层几何与锚点，且未声明 `sourceBounds` 时令其等于 `bounds`。目录 schema 升级不改变截图证据或识别输出各自已有的 schema 版本。

schema v3 的 `numberModels` 对 `0..9` 每个字形记录来源证据、裁剪边界和模板哈希，并固定前景分割、归一化、最低匹配分数和最低候选分差。value 元素通过 `number` 声明模型、精确边界、格式和数字位数范围。数值区域直接在客户区原生像素中裁剪，使用 8 邻接连通域、单像素形状容差和封闭区域拓扑匹配，避免先将整帧缩放到参考空间后再次缩放字形。全部字形匹配后才返回整数 `value`；低置信度返回 `unknown`，候选分离不足返回 `ambiguous`，两者都 fail closed 且不影响页面识别。`progressCurrent` 还必须独立验证 `/` 及右侧目标数字结构。即使字形匹配，来源证据不具备 Oracle 资格时数值也只能作为诊断。当前支持 `inLevel`、`sandbox` 与 `sandboxTower` 各自的 `health`、`cash`、`round`。

页面锚点可用 `matchGroup` 声明同一稳定语义的互斥视觉候选，组内任一锚点命中即按一个页面锚点计数和计分。当前 `inLevel.powersAvailable` 与 `inLevel.powersDisabled` 构成 `inLevel.powersMode`，使力量按钮可用和禁用状态都能维持同一页面身份；组名必须位于页面 ID 命名空间内，detector-only 锚点不能加入组。

## 输入所有权

| 阶段 | 游戏输入方 | Game Driver 行为 |
| --- | --- | --- |
| Arrange | Game Driver | 建立地图、难度、模式、英雄等前置状态 |
| Act | BetterBTD | 只截图观察，不点击或发送按键 |
| Assert | 无或 BetterBTD | 独立判断可见结果 |
| Recover | Game Driver | API 确认脚本停止后才恢复现场 |

`capture`、`recognize` 和 `catalog` 是观察命令；`click`、`click-point`、`scroll-point`、`drag-point` 与 `press-key` 是控制命令。五种控制命令都支持 `--expect-page` 和 `--expect-view-state`，并要求视口存在；若同时声明目标页，视口还必须归属该页。控制命令强制要求 `--phase arrange` 或 `--phase recover`，不接受 Act/Assert，但 CLI 无法独立证明 BetterBTD 脚本是否正在执行。测试编排层必须保证 Arrange 尚未启动脚本，Recover 则已通过 API 停止脚本并确认停止后再调用 Game Driver。

元素点击前必须同时满足：当前原始证据完整且无警告、页面唯一匹配、目标元素属于当前页、需要视口 placement 时视口也唯一匹配、当前动作点有效；按钮还要求绑定锚点全部可见，数值 value 则要求 `number.status=matched` 且 `number.oracleEligible=true`。坐标点击、滚动、拖动和按键不具备这些元素前置条件，只适用于显式探索与引导采集。鼠标或键盘 Win32 调用成功不构成通过条件；只有独立截图轨迹达到所要求的变化/稳定状态，最终证据和视口仍为 Oracle eligible，并在指定时匹配 `--expect-page` 和 `--expect-view-state`，命令才成功。ID 匹配但最终证据失去 Oracle 资格时分别返回 `expectedPageNotOracleEligible` 或 `expectedViewStateNotOracleEligible`。拖动输入无论正常完成还是中途异常都会在清理路径发送 mouse-up；按键输入会按 `ctrl`、`alt`、`shift` 的固定顺序按下，并在主键之后逆序释放已成功按下的修饰键。两种清理语义都不证明游戏接受了输入。

## 后续迭代

1. 扩展完整热键表及更多滚动中间视口，并为适合拖动的页面补充真实手势验证；`extras` 与 `heroSelect` 上下视口已完成真实滚轮、状态观察和现场恢复验证。
2. 增加文本输入和元素出现/消失等待，并继续保存操作轨迹；受限按键和控制命令按最终页面/视口期望跨加载中间态的等待已完成。
3. 扩展 Sandbox 当前不可滚动到的隐藏塔，以及奖励、结算和编辑框等其他数值/文本/选中状态；生命、金币和回合的首批独立数值识别已完成。
4. 扩展英文界面、更多账号状态、其他地图/难度/模式确认、奖励和通用弹窗的真实视觉基准；[测试场景协议](script-test-scenario.md) 已建立，后续封装编排 Skill，同时保持截图与识别 Oracle 独立。中文简单标准胜利/自由游戏、CHIMPS 首回合与后续回合失败、简单 Deflation 存档覆盖分支已完成纵切片。

桌面 GDI 首版后端要求游戏可见且无遮挡。锁屏、断开的 RDP、独占全屏和部分 DirectFlip 路径可能无法提供有效画面；单帧证据会明确标记 `occlusionSensitive=true` 和 `stabilityCheckPerformed=false`。控制命令的稳定性记录在同目录 `operation.json` 中，测试编排不得把两者混为同一字段。指定最终页面或视口期望后，操作会跨过稳定但不匹配的中间态，并在轮询记录中保存非 Oracle 的候选识别摘要；没有期望时仍停在第一个满足条件的稳定画面。冷启动存在多个合法目标分支时仍需编排层重复观察。真实胜利恢复曾先停在加载图标，现可在目标唯一时直接等待 `mainMenu`，但该能力尚未在真实 BTD6 胜利链重新验证。地图选择的 34 张独立 source/holdout 截图全部匹配正确视口，最低 holdout 视口分数 `0.996790`；既有地图、Extras 与英雄真实输入验证范围保持不变。真实简单标准胜利链的三个页面 holdout 分数为 `0.998563`、`0.999793`、`1.0`，通关总结的主页和浏览地图两个动作仍未分别实点。CHIMPS 三按钮/四按钮失败 holdout 页面分数为 `0.9976735`/`0.9889`，视口分数均为 `1.0`；后续失败页四个分支与重开/重试确认框的安全分支已真实执行，重试确认 holdout 分数为 `0.99938875`。简单 Deflation 存档覆盖确认 holdout 分数为 `0.999958`，确认后无额外说明页。待开/活动回合 `inLevel` 留出证据分别精确读取 `200 / $1700 / 1` 和 `1 / $1516 / 13`。Sandbox 气球视图 source/holdout 在同一地图、中文、`1920x1080`、DPI 192 会话中分别读取 `908172 / $3456789 / 84` 和 `345689 / $9081726 / 57`；塔视图 source/holdout 均读取 `345689 / $9081726 / 57`，且不会误匹配普通 `inLevel`。自动化在 `960x540` 至 `3840x2160` 的八种 16:9 尺寸精确通过。真实数值元素点击、生命编辑器识别和取消恢复均通过；气球/塔模式往返也已真实执行，局部侧栏转换需使用 `--change-threshold 0.02`。塔列表滚轮和拖动探索返回 `unchangedStable`，隐藏塔尚未建模。其他地图、英文界面和其他显示器布局仍未验证。`press-key` 的事件顺序、清理、轨迹和最终视觉断言已由自动化测试覆盖，但尚未在真实 BTD6 中发送按键验证。
