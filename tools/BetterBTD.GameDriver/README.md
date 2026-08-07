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

Game Driver 在当前线程启用 Per-Monitor V2 坐标语义，以读取真实物理像素。这不会修改 Windows 的分辨率、缩放或其他系统显示设置。基准坐标仍按 BetterBTD 现有算法在代码中换算：

```text
clientX = clamp(round(referenceX / 1920 * clientWidth), 0, clientWidth - 1)
clientY = clamp(round(referenceY / 1080 * clientHeight), 0, clientHeight - 1)
screenX = clientOriginOnScreenX + clientX
screenY = clientOriginOnScreenY + clientY
```

点坐标和矩形统一采用左闭右开的客户区边界。

## 视觉目录协议

版本化目录位于 `visual-baselines/`。首个纵向切片覆盖中文 `mainMenu`：一份制模证据、一份不同动画时刻的真实正向留出证据、一份真实启动画面负向证据、4 个非文本锚点和 19 个目录元素。模板来自设置、成就、退出和开始图标；玩家名、货币、活动入口、本地化标签及动态徽章不会成为页面 ID 或锚点。

每个模板记录来源 `evidenceId`、来源图片 SHA-256、裁剪矩形和模板 SHA-256。`catalog` 和 `baseline build` 都会重新校验这些值。识别代码只依赖 Game Driver 自己的目录和 Pillow，不加载 BetterBTD 截图、OCR 模板、OpenCvSharp 资源或运行时状态。

当前页面识别要求 `16:9` 画面，并把画面规范化到 `1920 x 1080` 后进行固定区域多锚点比较。至少命中页面声明数量的锚点并超过总分阈值才返回 `matched`；两个候选分差小于 `0.02` 时返回 `ambiguous`，其余情况返回 `unknown`。只有完整证据链无警告且识别状态为 `matched` 时，识别结果才标记为 Oracle 可用。

## 当前限制

- `desktop-gdi-bitblt` 捕获的是用户真实可见桌面，因此要求窗口未被遮挡、未最小化、完整位于虚拟桌面内且会话未锁屏。
- 通知、顶置窗口和覆盖层会进入截图。这是可见证据的一部分，但测试编排需要据此判定现场是否有效。
- 当前只在激活并等待指定时间后截取单帧，尚未实现连续帧稳定性判断。
- 当前只覆盖中文主菜单页面；地图选择、难度、模式、英雄、关卡内、暂停、胜负和弹窗仍需要独立采集与标注。
- 当前只有设置、成就、退出和开始元素具备独立可见性探测器；其他目录元素明确返回 `notEvaluated`，尚未实现其遮挡、文本、数值或选中状态检测。
- 当前未实现鼠标键盘输入和等待条件。

## 测试

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.GameDriver\test.ps1
```
