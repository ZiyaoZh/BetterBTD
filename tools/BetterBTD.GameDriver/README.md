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
  --output-dir artifacts\game-driver\manual\main-to-map-select
```

`click` 只接受目录内具备动作点和独立可见性检测器的 `button`。输入前必须唯一识别当前页且证据为 Oracle 可用；输入后会等待画面相对操作前明显变化并连续稳定，再捕获最终证据和识别目标页。`--phase` 必须显式为 `arrange` 或 `recover`，CLI 不接受 `act` 或 `assert`。Recover 阶段仍须由测试编排层先通过 BetterBTD Test API 确认脚本已经停止。

`--change-threshold` 默认 `0.05`，适合页面转换。只改变局部状态的操作必须使用真实轨迹校准更低阈值；例如英雄 `choose -> selected` 在当前 `1920 x 1080` 中文环境使用 `--change-threshold 0.004`，最终仍须由独立状态模板和 `--expect-page` 确认，不能只凭像素变化判定成功。

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

每次元素点击在独立输出目录保存 `before`、`after` 两组三件套和原子写入的 `operation.json`。操作轨迹记录客户区/屏幕物理坐标、点击阶段、每个轮询帧的全像素指纹、相对操作前与上一帧的归一化差异、连续稳定帧数和前后独立识别摘要。单个 `before`/`after` 捕获仍是单帧，所以其 `capture.stabilityCheckPerformed=false`；稳定性结论属于同时保存的操作级轨迹，不能通过改写单帧元数据伪造。

Game Driver 在当前线程启用 Per-Monitor V2 坐标语义，以读取真实物理像素。这不会修改 Windows 的分辨率、缩放或其他系统显示设置。基准坐标仍按 BetterBTD 现有算法在代码中换算：

```text
clientX = clamp(round(referenceX / 1920 * clientWidth), 0, clientWidth - 1)
clientY = clamp(round(referenceY / 1080 * clientHeight), 0, clientHeight - 1)
screenX = clientOriginOnScreenX + clientX
screenY = clientOriginOnScreenY + clientY
```

点坐标和矩形统一采用左闭右开的客户区边界。

## 视觉目录协议

版本化目录位于 `visual-baselines/`。当前覆盖 11 个中文页面：`welcome`、`modifiedClientWarning`、`mainMenu`、`mapSelect`、`difficultySelect`、三种难度的模式选择、`heroSelect`、`inLevel` 和 `stageSettings`，共 72 个独立模板和 123 个目录元素。主流程已能独立选择 `MonkeyMeadow` 的简单、中级和困难模式入口；英雄页可检测首屏 15 个 `HeroType`、`choose`/`selected` 状态和返回动作。真实验证已进入简单/困难标准关卡、通过暂停菜单继续或返回主页，并完成 Quincy/Gwendolin 的选择、恢复与英雄页往返。玩家名、用户 ID、版本号、货币、活动入口、本地化标签、奖励数值、地图勋章、星光和动态徽章不会成为页面 ID 或页面识别锚点。

每个模板记录来源 `evidenceId`、来源图片 SHA-256、裁剪矩形和模板 SHA-256。每页还必须绑定一组 Oracle 可用的正向留出证据，且其图片哈希不能与任何制模源图相同。`catalog` 和 `baseline build` 会重新校验完整证据链。识别代码只依赖 Game Driver 自己的目录和 Pillow，不加载 BetterBTD 截图、OCR 模板、OpenCvSharp 资源或运行时状态。

当前页面识别要求 `16:9` 画面，并把画面规范化到 `1920 x 1080` 后进行固定区域多锚点比较。锚点的 `pageAnchor` 缺省为 `true`；`false` 只用于元素可见性，不影响页面分数和最少锚点数。至少命中页面声明数量的页面锚点并超过总分阈值才返回 `matched`；两个候选分差小于 `0.02` 时返回 `ambiguous`，其余情况返回 `unknown`。只有完整证据链无警告且识别状态为 `matched` 时，识别结果才标记为 Oracle 可用。

## 当前限制

- `desktop-gdi-bitblt` 捕获的是用户真实可见桌面，因此要求窗口未被遮挡、未最小化、完整位于虚拟桌面内且会话未锁屏。
- 通知、顶置窗口和覆盖层会进入截图。这是可见证据的一部分，但测试编排需要据此判定现场是否有效。
- 连续帧等待目前只用于 `click` 操作，采用规范化全画面差异阈值；不同分辨率、动画强度和显示布局仍需要真实校准。
- `click` 在第一个明显变化且连续稳定的画面停止；若操作经过可稳定停留的加载页，它不会自动跨越中间态等待更后的目标页。
- 真实游戏视觉验证目前覆盖中文冷启动、修改客户端警告、主菜单、初学者地图选择首个轮播页、三种难度模式页、Quincy/Gwendolin 英雄选择与恢复、简单/困难标准关卡、暂停继续和暂停返回主页；其他英雄滚动、活动回合、其他地图、胜负、奖励和通用弹窗仍需要独立采集与标注。
- `inLevel.health` 已检测公共 HUD 图标，但生命、金币和回合数值尚未解析；跨地图、受限模式和已开局动态画面仍需扩大真实样本。
- 当前 69 个目录元素具备独立可见性探测器，其中 57 个具备可点击动作点；其他元素明确返回 `notEvaluated`，不能由 `click` 操作控制。
- 当前只实现按元素 ID 左键点击和画面变化/稳定等待；客户区原始坐标点击、键盘、文本输入、滚动及元素出现/消失等通用等待尚未实现。

## 测试

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.GameDriver\test.ps1
```
