# BetterBTD.GameDriver

`BetterBTD.GameDriver` 是独立于 BetterBTD 进程和程序集的 Windows x64 Python CLI。它负责查找或启动真实 BTD6、恢复并激活游戏窗口、保存客户区截图证据，以及用自己的视觉基准离线识别页面和可见元素。

它不引用 `BetterBTD`、`Fischless.GameCapture`、BetterBTD OCR 模板或运行时状态。截图来自桌面合成后的真实可见像素，可作为黑盒测试的原始证据。

## 环境

- Windows 10/11 x64
- Python 3.11 或更高版本
- BTD6 使用窗口化或无边框窗口模式

首次使用时在仓库根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.GameDriver\setup.ps1
```

脚本只在 `tools/BetterBTD.GameDriver/.venv/` 创建虚拟环境，并按 `requirements.txt` 安装锁定版本。不会向系统 Python 安装包。

## 使用

列出默认 BTD6 窗口：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.GameDriver\game-driver.ps1 windows
```

截图并自动生成同名 JSON 与完成标记：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.GameDriver\game-driver.ps1 capture
```

指定输出或在未找到窗口时启动游戏：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.GameDriver\game-driver.ps1 capture `
  --output artifacts\game-driver\manual\frame.png `
  --launch D:\steam\steamapps\common\BloonsTD6\BloonsTD6.exe
```

默认输出位于已被 Git 忽略的 `artifacts/game-driver/<UTC date>/`。可用 `capture --help` 查看窗口句柄、PID、等待时间、禁止激活和覆盖选项。命令成功时向 stdout 输出 JSON；失败时向 stderr 输出稳定的 `error.code` 和消息，并返回非零退出码。

按稳定元素 ID 点击，并要求点击后的独立识别结果为地图选择页：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.GameDriver\game-driver.ps1 click `
  --element mainMenu.start `
  --phase arrange `
  --expect-page mapSelect `
  --expect-view-state mapSelect.beginner01 `
  --output-dir artifacts\game-driver\manual\main-to-map-select
```

`click` 只接受目录内具备动作点和独立可见性检测器的 `button`。输入前必须唯一识别当前页且证据为 Oracle 可用；输入后会等待画面相对操作前明显变化并连续稳定，再捕获最终证据和识别目标页及视口。`--phase` 必须显式为 `arrange` 或 `recover`，CLI 不接受 `act` 或 `assert`。Recover 阶段仍须由测试编排层先通过 BetterBTD Test API 确认脚本已经停止。

早期探索尚未入库的页面时，可按 `1920 x 1080` 参考客户区坐标点击：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.GameDriver\game-driver.ps1 click-point `
  --x 960 `
  --y 540 `
  --phase arrange `
  --output-dir artifacts\game-driver\manual\reference-point
```

`click-point` 使用与 `click` 相同的输入所有权、窗口一致性、前后证据和变化/稳定等待协议，但不要求操作前页面已被目录识别，因此不能把坐标点击本身当作元素级 Oracle。若指定 `--expect-page` 或 `--expect-view-state`，操作后仍必须独立识别到相应页面和视口。轨迹同时记录参考坐标、换算后的客户区物理坐标和屏幕物理坐标。`--expect-view-state` 现在由 `click`、`click-point`、`scroll-point` 和 `drag-point` 共同支持；它引用的视口必须存在且属于同时声明的目标页。

最终 ID 相符本身仍不足以通过断言：目标页证据和目标视口必须继续为 Oracle eligible。捕获警告或其他证据资格问题会分别返回 `expectedPageNotOracleEligible` 或 `expectedViewStateNotOracleEligible`，并保留失败轨迹。

在参考客户区坐标发送垂直滚轮输入，并要求操作后识别到指定视口状态：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.GameDriver\game-driver.ps1 scroll-point `
  --x 1500 `
  --y 850 `
  --direction down `
  --notches 3 `
  --phase arrange `
  --expect-page extras `
  --expect-view-state extras.bottom `
  --output-dir artifacts\game-driver\manual\extras-scroll-bottom
```

`scroll-point` 强制要求 `--phase arrange` 或 `--phase recover`，支持 `up`/`down` 和 1 至 20 个滚轮档位。它把光标放到换算后的客户区点再发送滚轮输入，并保存参考、客户区和屏幕物理坐标以及 Win32 wheel delta。默认要求画面发生变化后连续稳定；在列表已经到顶或到底等合法边界场景，可显式指定 `--allow-no-change`，此时轨迹将结果区分为 `changedStable`、`unchangedStable` 或 `timeout`。`--expect-view-state` 是操作后独立视口状态断言，可与 `--expect-page` 同时使用；仅有滚轮调用成功或像素变化仍不构成 Oracle。

在两个参考客户区坐标之间执行左键拖动：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.GameDriver\game-driver.ps1 drag-point `
  --start-x 250 `
  --start-y 850 `
  --end-x 250 `
  --end-y 250 `
  --duration-ms 500 `
  --steps 10 `
  --phase arrange `
  --allow-no-change `
  --expect-page heroSelect `
  --output-dir artifacts\game-driver\manual\hero-drag-probe
```

`drag-point` 分别换算并记录起点和终点，按 `--duration-ms`（50 至 5000，默认 500）和 `--steps`（1 至 100，默认 10）线性移动。底层在按下左键后用 `finally` 保证发送 mouse-up；即使中途移动或等待抛出异常，也不会有意把左键保持在按下状态。它具有与滚动相同的阶段限制、`--allow-no-change`、`--expect-page`、`--expect-view-state`、窗口一致性和证据协议。上例是允许无变化结果的探索性拖动探针；当前真实英雄网格不接受拖动滚屏，英雄视口切换必须使用 `scroll-point`。mouse-up 保证只描述 Game Driver 的输入清理语义，不代表游戏一定接受了拖动或到达目标状态。

`--change-threshold` 默认 `0.05`，适合页面转换。只改变局部状态的操作必须使用真实轨迹校准更低阈值；例如英雄 `choose -> selected` 在当前 `1920 x 1080` 中文环境使用 `--change-threshold 0.004`，最终仍须由独立状态模板和 `--expect-page` 确认，不能只凭像素变化判定成功。

`scroll-point` 和 `drag-point` 针对局部视口变化默认使用较低的 `--change-threshold 0.005`；不同分辨率、动画和页面仍需要真实轨迹校准。

真实冷启动可能依次出现 `welcome`、加载画面和 `modifiedClientWarning`，也可能直接进入 `mainMenu`。启动页可点击 `welcome.start`；修改客户端警告只开放 `modifiedClientWarning.continue`，会改变账号状态的 `unregister` 和关闭进程的 `closeGame` 没有动作点。加载画面可能先于目标页稳定，因此冷启动编排目前需要在操作后重复 `capture`/`recognize`，直到独立识别到下一个可处理页。

校验随工具提交的独立视觉目录、模板哈希和来源证据链：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.GameDriver\game-driver.ps1 catalog
```

识别一组已经完成提交的截图证据。入口是捕获元数据而不是任意 PNG，命令会先校验相邻 PNG 和 `.complete.json`：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.GameDriver\game-driver.ps1 recognize `
  --evidence artifacts\game-driver\manual\frame.json `
  --annotated-output artifacts\game-driver\manual\frame.annotated.png
```

识别结果包含 `matched`、`unknown` 或 `ambiguous` 状态、页面与锚点分数、稳定元素 ID、基准/客户区边界、动作点和 `oracleEligible`。标注图只用于早期人工复核，不会写回或替代原始截图证据。

从目录声明的来源证据确定性重建模板，并验证生成哈希：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.GameDriver\game-driver.ps1 baseline build --overwrite
```

## 证据协议

每次截图包含：

- 原始 PNG 和文件 SHA-256。
- 宽高前缀加全量可见 RGB 像素的 SHA-256 画面指纹。
- UTC 时间、捕获耗时、窗口句柄、PID、标题、DPI 和前后台状态。
- 窗口矩形、客户区屏幕矩形和 `clientPhysicalPixels` 坐标系。
- `1920 x 1080` 基准坐标到实际客户区坐标的比例、公式和宽高比检查。
- 黑帧/纯色帧诊断和其他警告。
- 最后原子写入的 `.complete.json` 完成标记，其中包含 PNG 与元数据哈希。读取方只有在标记存在且哈希一致时才能接受该组证据。

每次点击、滚动或拖动都在独立输出目录保存 `before`、`after` 两组三件套和原子写入的 `operation.json`。操作轨迹记录输入所有权阶段、参考/客户区/屏幕物理坐标、滚轮方向与档位或拖动时长与步数、每个轮询帧的全像素指纹、相对操作前与上一帧的归一化差异、连续稳定帧数、前后独立识别摘要，以及页面和视口状态期望。单个 `before`/`after` 捕获仍是单帧，所以其 `capture.stabilityCheckPerformed=false`；稳定性结论属于同时保存的操作级轨迹，不能通过改写单帧元数据伪造。

Game Driver 在当前线程启用 Per-Monitor V2 坐标语义，以读取真实物理像素。这不会修改 Windows 的分辨率、缩放或其他系统显示设置。基准坐标仍按 BetterBTD 现有算法在代码中换算：

```text
clientX = clamp(round(referenceX / 1920 * clientWidth), 0, clientWidth - 1)
clientY = clamp(round(referenceY / 1080 * clientHeight), 0, clientHeight - 1)
screenX = clientOriginOnScreenX + clientX
screenY = clientOriginOnScreenY + clientY
```

点坐标和矩形统一采用左闭右开的客户区边界。

## 视觉目录协议

版本化目录位于 `visual-baselines/`。当前 catalog v15 使用 schema v2，覆盖 23 个中文页面、325 个独立模板、297 个目录元素、21 个视口状态和 251 个元素 placement。页面包括 `welcome`、`modifiedClientWarning`、`mainMenu`、`mapSelect`、`difficultySelect`、三种难度的模式选择、`heroSelect`、`inLevel`、`overwriteSaveConfirmation`、`chimpsModeInfo`、`defeatSummary`、`restartGameConfirmation`、`postGameMapReview`、`victoryPlayerStats`、`victorySummary`、`freeplayPrompt`、`stageSettings`、`settings`、`hotkeys`、`accessibility` 和 `extras`。`mapSelect` 的 17 个视口覆盖初级 5 页、中级 5 页、高级 4 页和专家 3 页，共 87 张可见地图；官方英文名为 `Ascent` 的“攀升”同时建模为解锁时可操作的 `mapSelect.ascent` 和锁定时不可操作的 `mapSelect.ascentLocked`，账号状态不参与高级第一页的视口判定。四个分类按钮可区分 `selected`/`unselected`，`doubleCash` 与 `autoStart` 可区分 `enabled`/`disabled`。`heroSelect.top`/`heroSelect.bottom` 合并覆盖 18 个稳定 `HeroType`，`extras.top`/`extras.bottom` 覆盖全部 7 个开关及其状态。玩家名、用户 ID、版本号、货币、活动入口、本地化标签、奖励数值、地图勋章、星光和动态徽章不会成为页面 ID 或页面识别锚点；带本地化文字的按钮模板只用于元素可见性检测。

每个模板记录来源 `evidenceId`、来源图片 SHA-256、裁剪矩形和模板 SHA-256。每页还必须绑定一组 Oracle 可用的正向留出证据，且其图片哈希不能与任何制模源图相同。`catalog` 和 `baseline build` 会重新校验完整证据链。识别代码只依赖 Game Driver 自己的目录和 Pillow，不加载 BetterBTD 截图、OCR 模板、OpenCvSharp 资源或运行时状态。

当前页面识别要求 `16:9` 画面，并把画面规范化到 `1920 x 1080` 后进行固定区域多锚点比较。锚点的 `pageAnchor` 缺省为 `true`；`false` 只用于元素可见性，不影响页面分数和最少锚点数。至少命中页面声明数量的页面锚点并超过总分阈值才返回 `matched`；同类候选分差小于 `0.02` 时返回 `ambiguous`，其余情况返回 `unknown`。`kind=modal` 表示画面顶层可见且阻止底层交互的模态页；当模态页与底层普通页同时命中时，模态页优先，模态页之间仍按相同歧义规则处理。只有完整证据链无警告且识别状态为 `matched` 时，识别结果才标记为 Oracle 可用。

schema v2 在页面身份之下增加 `viewStates`，并允许元素按视口声明 `placements`；每个 placement 自带当前视口中的 `bounds`、`actionPoint`、检测锚点和可选 `states`。锚点的 `sourceBounds` 指定从来源证据裁剪模板的位置，`bounds` 仍是运行时画面中的匹配位置，因此同一来源图可以为另一个视口提供模板而不混淆坐标。识别结果分别报告页面、视口状态、当前 placement 和元素状态。页面 `score` 始终只由 `pageAnchor=true` 的锚点计算；`rankingScore` 可合入视口置信度，只用于跨页面候选竞争。加载器继续接受 schema v1：缺省为无视口状态、无 placement，并令 `sourceBounds == bounds`，已有 v1 目录语义不变。

## 当前限制

- `desktop-gdi-bitblt` 捕获的是用户真实可见桌面，因此要求窗口未被遮挡、未最小化、完整位于虚拟桌面内且会话未锁屏。
- 通知、顶置窗口和覆盖层会进入截图。这是可见证据的一部分，但测试编排需要据此判定现场是否有效。
- 连续帧等待用于 `click`、`click-point`、`scroll-point` 和 `drag-point`，采用规范化全画面差异阈值；不同分辨率、动画强度和显示布局仍需要真实校准。滚动和拖动只在显式 `--allow-no-change` 时接受连续稳定的无变化画面。
- 控制命令在第一个明显变化且连续稳定的画面停止；若操作经过可稳定停留的加载页，它不会自动跨越中间态等待更后的目标页。
- 现有真实游戏验证覆盖中文冷启动、修改客户端警告、主菜单、设置/热键/辅助功能往返、地图选择全部 17 个轮播视口、地图分类切换、地图卡进入/返回、三种难度模式页、简单/困难标准关卡、暂停继续和暂停返回主页。简单标准关卡已从第 38 回合真实推进到胜利，依次采集并识别 `victoryPlayerStats`（holdout 分数 `0.998563`）、`victorySummary`（`0.999793`）和 `freeplayPrompt`（`1.0`）；自由游戏与 `OK` 转换已真实执行，随后经暂停主页、加载中间态恢复 `mainMenu`（分数 `0.999576`）。通关总结的主页和浏览地图按钮具备独立可见性检测与动作点，但仍未分别实点验证。困难 CHIMPS 失败链已真实覆盖存档覆盖确认的取消/确认、CHIMPS 说明确认、第 6 回合一滴血失败、失败总结的主页/重新开始/浏览地图三个分支、重开确认的取消/确认，以及赛后地图检视继续返回失败总结。失败总结的“浏览地图”进入独立 `postGameMapReview`，不是普通选图页 `mapSelect`。五个新增页面的 holdout 分数分别为 `1.0`、`1.0`、`0.9990694`、`0.99916` 和 `0.999403`；最终恢复 `mainMenu` 的独立识别分数为 `0.9997265`。34 张地图 source/holdout 证据均独立识别为正确视口，最低 holdout 视口分数为 `0.996790`，没有跨视口误判；真实回归验证了专家到高级、高级到初级、初级翻页、`TreeStump` 进入难度选择、锁定 `Ascent` 拒绝输入，以及解锁后按 `mapSelect.ascent` 进入难度选择（操作前视口分数 `1.0`、操作后页面分数 `0.99915975`）。高级分类 selected/unselected 检测使用无“新”徽章的独立 source/holdout 校准，检测区域只保留纯图标像素，排除徽章和本地化标签。`doubleCash` 已恢复为 enabled，`autoStart` 已恢复为 disabled。`extras` 已真实验证上下端点滚动及边界 `unchangedStable`。英雄页已真实识别底部视口（页面分数 `0.991168`、视口分数 `0.999160`），按元素 ID 点击 Corvus、Silas 均返回 `changedStable`，在左侧滚动指示区向上 20 档后独立识别 `heroSelect.top`（视口分数 `0.999807`），随后恢复选中 Quincy 并返回 `mainMenu`。英雄网格的真实拖动探针没有产生滚动，因此不把拖动声明为英雄页成功路径。
- `inLevel.healthIcon` 使用 detector-only 锚点，不参与页面身份，使普通模式与 CHIMPS HUD 可共用 `inLevel`；真实 CHIMPS 活动画面识别分数为 `0.996553`。`inLevel.health` 在当前 CHIMPS 证据中报告 `notVisible`，生命、金币和回合数值仍未解析；跨地图、其他受限模式和已开局动态画面仍需扩大真实样本。
- `overwriteSaveConfirmation` 当前只对困难模式页上的 CHIMPS 存档覆盖确认具备 Oracle 资格，`restartGameConfirmation` 当前只对失败总结上的重开确认具备 Oracle 资格；两页有意使用底层上下文锚点，使来自其他难度或暂停设置的同类确认框安全返回 `unknown`。这些稳定 ID 不表示其他入口已经覆盖。
- 297 个目录元素中有 237 个具备独立可见性探测器，201 个按钮声明动作点，其中 178 个同时具备探测器。只有当前页面和视口均独立识别、对应 placement 可见且动作点有效的按钮才能由 `click` 控制；其余动作点仍只表示已声明几何，不满足元素级点击前置条件。`mapSelect.ascentLocked` 和 `extras` 开关只有独立状态检测，不提供动作点。
- 当前实现支持元素/坐标点击、坐标滚轮和坐标拖动；键盘、文本输入以及元素出现/消失等通用等待尚未实现。

## 测试

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.GameDriver\test.ps1
```
