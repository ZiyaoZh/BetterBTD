# BTD6 独立视觉基准

本目录只包含从 BetterBTD 之外采集和解释的真实 BTD6 画面。`catalog.json` 使用稳定 ASCII 页面/元素 ID、`1920 x 1080` 基准坐标和左闭右开矩形，不使用本地化显示文本作为 ID。

`samples/` 中每个样本保留 Game Driver 生成的 PNG、元数据和完成标记三件套。读取方必须重新校验完成标记中的图片与元数据 SHA-256；不能信任元数据中记录的原始绝对路径。`*.annotations.json` 是对原始画面的独立解释，不会修改原始证据。

`templates/` 只能由 `baseline build` 根据目录声明的来源证据和裁剪矩形确定性生成。每个模板在目录中记录来源 `evidenceId`、来源图片哈希和模板哈希，禁止从 BetterBTD、OpenCvSharp 资源或 BetterBTD OCR 模板复制。

新增页面时至少需要：

1. 导入一组通过完整性检查、无捕获警告的真实证据。
2. 用 image-view 或其他独立人工复核方式标注页面和稳定元素。
3. 选择避开动画、账号数值、活动徽章和本地化文本的多个锚点。
4. 保留一张未参与模板生成且图片哈希不同的真实截图，作为页面 `positiveHoldout` 验证正向识别；目录加载会拒绝与制模源图相同的留出证据。另用不同页面验证 `unknown`。
5. 重新生成模板、校验目录，并记录真实游戏版本、语言和未覆盖条件。

当前 catalog v19 使用 schema v4，覆盖 29 个中文页面、411 个锚点模板、10 个数值字形模板、372 个元素、25 个视口状态和 260 个元素 placement。其中 307 个元素具有独立可见性或数值探测器，260 个元素声明动作点，237 个元素同时可独立观察并具有动作点：

- `welcome` 与 `modifiedClientWarning`：用于真实冷启动；玩家名、用户 ID、版本号和本地化正文不参与识别。修改客户端警告仅允许继续，注销和关闭游戏保持不可操作。
- `mainMenu` 与 `mapSelect`：地图页固定控件使用 4 个页面锚点；17 个独立视口覆盖初级 5 页、中级 5 页、高级 4 页和专家 3 页，共 87 张可见地图。每个视口拥有独立 source/holdout 证据、轮播页检测和地图卡 placement；地图名、勋章、星光和活动状态不参与页面识别。四个分类按钮独立区分 `selected`/`unselected`，`doubleCash` 与 `autoStart` 独立区分 `enabled`/`disabled`。官方英文名 `Ascent` 使用 `mapSelect.ascent` 表示解锁按钮、`mapSelect.ascentLocked` 表示无动作点的锁定状态；两者都不参与高级第一页的视口身份。
- `difficultySelect` 与 `easyModeSelect`：难度肖像和简单模式图标分别绑定 `Easy/Medium/Hard` 与 `Standard/PrimaryOnly/Deflation`；奖励、勋章和本地化说明不参与识别。
- `mediumModeSelect` 与 `hardModeSelect`：覆盖主项目稳定枚举名对应的所有模式路径，并额外保留 `Sandbox`；真实可逆开关状态作为独立留出证据，开关区域不参与锚点。
- `heroSelect`：固定页面控件与滚动视口分离计分；`heroSelect.top` 和 `heroSelect.bottom` 合并覆盖 18 个英雄，英雄按钮使用主项目 `HeroType` 的稳定英文枚举名，并独立区分 `choose` 与 `selected` 状态。
- `inLevel` 与 `stageSettings`：公共 HUD 用于跨简单/困难标准及 CHIMPS 关卡识别；生命图标和动态开始按钮框是 detector-only 元素，稳定右侧栏参与页面识别。`inLevel.roundReady`/`inLevel.roundActive` 独立区分待开回合和活动回合，并为同一个开始/快进动作提供 placement。暂停页只开放继续和 Recover 所需的主页动作，商店与重新开始保持不可操作。
- `overwriteSaveConfirmation` 与 `chimpsModeInfo`：存档覆盖确认通过中文标题锚点独立于底层难度页面，已覆盖困难 CHIMPS 和简单 Deflation 的取消/确认；模式说明开放 `OK`。Deflation 确认覆盖存档后经过加载直接进入关卡，没有额外说明页。
- `defeatSummary`、`retryLastRoundConfirmation`、`restartGameConfirmation` 与 `postGameMapReview`：失败总结用两个视口状态分别覆盖首回合三按钮布局和后续回合四按钮布局；后者增加“重试上一回合”。重试与重开确认框分别使用中文语义标题锚点区分相同外壳，并都开放取消/确认。地图检视开放继续，且是独立页面，不是普通选图页 `mapSelect`。暂停设置中的重开确认尚未覆盖。
- `victoryPlayerStats`、`victorySummary` 与 `freeplayPrompt`：覆盖简单标准关卡通关后的两页胜利流程及自由游戏提示；玩家名、最高塔、统计数值、奖励数值和动画星光不参与锚点。通关统计开放下一页，通关总结开放主页、浏览地图和自由游戏，自由游戏提示开放 `OK`。
- `settings`：完整声明屏幕尺寸、点唱机、音量、启用状态和所有底部入口；账号、注销、语言、配置修改和外部链接没有动作点。
- `hotkeys`：独立识别返回、恢复默认、三组键位区域和当前普通光标选中状态；键位、恢复默认和光标尺寸不开放动作。
- `accessibility`：完整声明效果比例、两个开关、四种范围圈模式、返回和 `OK`；只开放两个退出动作。
- `extras`：`extras.top` 和 `extras.bottom` 都独立覆盖 `doubleCash`、`fastTrack`、`bigBloons`、`smallBloons`、`bigMonkeyTowers`、`smallMonkeyTowers`、`smallBosses` 七个开关，并分别检测 `enabled`/`disabled`；这些配置开关没有动作点。
- `sandboxIntro`、`sandbox`、`sandboxTower`、`sandboxHealthEditor` 与 `sandboxCashEditor`：覆盖 Sandbox 说明、气球/塔控制视图、生命/金币编辑器，以及回合输入、间隔、数量、三种属性、17 种气球、清除、力量、当前可见的 11 个标准塔、模式切换和开始/快进等可见控件。生命和金币 value 只有在数值 Oracle 匹配时才可点击打开编辑器；输入框不会自动聚焦，实际编辑流程必须先点击输入框再发送 `Ctrl+A` 和数字。

`pageAnchor` 缺省为 `true`；设为 `false` 的模板只检测元素可见性，不计入页面分数或最少页面锚点数。该规则用于可滚动英雄头像和动态选择状态，目录校验会拒绝用 detector-only 模板凑足 `minimumMatchedAnchors`。同一 `matchGroup` 中的页面锚点表示互斥视觉候选，组内任一锚点命中即按一个页面锚点计数和计分；当前 `inLevel.powersAvailable` 与 `inLevel.powersDisabled` 组成 `inLevel.powersMode`。普通页面使用 `kind=page`；遮挡并阻止底层交互的顶层页面使用 `kind=modal`。命中的 modal 优先于底层 page，modal 之间仍执行分差歧义检查，不能借页面类型绕过唯一性要求。

schema v2 保留稳定 `page` 身份，并用 `viewStates` 描述同一页面的不同滚动视口。可滚动元素通过 `placements` 为每个可见视口声明独立 `bounds`、`actionPoint`、`anchorIds` 和可选 `states`；识别器只输出已识别视口中的 placement。锚点 `sourceBounds` 是制模来源截图中的裁剪区域，`bounds` 是运行时匹配区域，二者可不同。元素 `states` 使用独立锚点输出 `matched`、`ambiguous` 或 `unknown`，当前用于 `extras` 七个开关的 `enabled`/`disabled`。schema v1 仍可加载：它没有 `viewStates`/`placements`，且未声明 `sourceBounds` 时按 `bounds` 处理。

schema v3 的根级 `numberModels` 为每个 `0..9` 字形记录来源证据、裁剪范围和模板哈希，并声明前景分割、归一化、最低匹配分数和最低候选分差。schema v4 在目录加载时强制校验数字模板的 RGBA 二值 alpha mask、非空单连通域和最小尺寸。制模时只保留亮且严格 `R=G=B` 的源像素，以二值 alpha mask 表示字形，透明像素不参与签名。value 元素的 `number` 绑定模型、精确数值区域、`integer`/`currency`/`progressCurrent` 格式和允许位数。识别器在客户区原生像素中优先按严格中性灰提取 8 邻接连通域，严格结果不能匹配时才按声明的通道差容差回退；两种分割给出不同数值时 fail closed。单像素形状容差和封闭区域拓扑用于字形匹配；`progressCurrent` 还会独立验证 `/` 及其右侧目标数字结构。任一字形低于匹配阈值时数值为 `unknown`，候选分差不足时为 `ambiguous`；两者都不返回 `value`，也不影响页面本身的匹配结果。当前 `inLevel`、`sandbox` 和 `sandboxTower` 各自的 `health`、`cash`、`round` 具备该能力。schema v1/v2/v3 仍可加载，旧 schema v3 RGB 字形按原有分割规则识别和重建；未声明 `number` 的 value 不能作为 `ElementNumber` Oracle。

跨地图数值 holdout 包含 `Monkey Meadow`、`Frozen Over`、`Midnight Mansion` 和 `Spice Islands` 的真实简单标准待开局画面，分别覆盖常规绿色、近白雪地、暗色和高饱和水面背景。测试同时要求页面唯一匹配 `inLevel`、生命/金币/回合为 `200 / 1700 / 1`，且三个值均通过 `strictNeutral` 分割。`Frozen Over` 还固定回归两个地图相关页面锚点的校准区间。

`heroSelect` 的 `1920 x 1080` 参考动作点如下。上下视口各可见 15 个英雄，重叠 12 个，合集为 18 个；`Silas` 在上下视口都可见，只有 `Psi`、`Geraldo`、`Corvus` 仅位于底部视口，因此不再使用“Silas/Corvus 均未覆盖”的旧语义。

| 英雄 ID | `heroSelect.top` | `heroSelect.bottom` |
| --- | --- | --- |
| `Quincy` | `(100, 220)` | - |
| `Gwendolin` | `(255, 220)` | - |
| `StrikerJones` | `(405, 220)` | - |
| `ObynGreenfoot` | `(100, 415)` | `(100, 160)` |
| `DanDeMonk` | `(255, 415)` | `(255, 160)` |
| `Benjamin` | `(405, 415)` | `(405, 160)` |
| `PatFusty` | `(100, 605)` | `(100, 330)` |
| `CaptainChurchill` | `(255, 605)` | `(255, 330)` |
| `Ezili` | `(405, 605)` | `(405, 330)` |
| `Silas` | `(100, 800)` | `(100, 520)` |
| `Etienne` | `(255, 800)` | `(255, 520)` |
| `Sauda` | `(405, 800)` | `(405, 520)` |
| `Rosalia` | `(100, 990)` | `(100, 710)` |
| `Adora` | `(255, 990)` | `(255, 710)` |
| `AdmiralBrickell` | `(405, 990)` | `(405, 710)` |
| `Psi` | - | `(100, 900)` |
| `Geraldo` | - | `(255, 900)` |
| `Corvus` | - | `(405, 900)` |

地图选择的 17 组 source/holdout（34 张真实截图）均独立匹配正确视口，最低 holdout 视口分数为 `0.996790`，没有跨视口误判。另有独立 source/holdout 覆盖解锁 `Ascent` 和不带未读徽章的高级分类状态；真实输入验证范围保持不变。简单标准胜利链的三个页面 holdout 分数分别为 `0.998563`、`0.999793` 和 `1.0`，通关总结的主页与浏览地图动作尚未分别实点。困难 CHIMPS 已增加后续回合失败样本：旧三按钮和新四按钮 holdout 页面分数分别为 `0.9976735` 和 `0.9889`，对应视口分数均为 `1.0`；主页、重开、浏览地图和重试上一回合四个分支，以及重开/重试确认框的取消/确认均已实点。`retryLastRoundConfirmation` holdout 分数为 `0.99938875`，确认后恢复 `inLevel`。简单 Deflation 的存档覆盖确认也已真实执行取消/确认，holdout 分数为 `0.999958`，加载后直接进入 `inLevel`。待开/活动回合的 `inLevel` 留出证据分别精确读取 `200 / $1700 / 1` 和 `1 / $1516 / 13`；真实 `Monkey Meadow`、`Frozen Over`、`Midnight Mansion` 与 `Spice Islands` 简单标准待开局画面也均读取 `200 / $1700 / 1`。雪地样本由严格中性灰分割排除了生命 ROI 中的近白背景，并用于校准两个地图相关的 `inLevel` 页面锚点。Sandbox 气球视图 source/holdout 在同一地图、中文、`1920x1080`、DPI 192 会话中分别精确读取 `908172 / $3456789 / 84` 和 `345689 / $9081726 / 57`；塔视图 source/holdout 均读取 `345689 / $9081726 / 57`，且不会误匹配普通 `inLevel`。八种 `960x540` 至 `3840x2160` 自动化缩放样本均精确通过；生命元素点击、编辑器识别和取消恢复已真实执行。气球/塔模式往返也已真实执行，局部侧栏转换需使用 `--change-threshold 0.02`。塔列表滚轮和拖动探索返回 `unchangedStable`，隐藏塔尚未建模；其他关卡地图、Sandbox 的其他地图、英文界面、其他显示器布局、地图活动入口、其他难度/模式的存档确认和暂停设置重开确认尚未验证；完整热键长列表仍需补充视口覆盖。
