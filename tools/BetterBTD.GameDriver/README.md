# BetterBTD.GameDriver

`BetterBTD.GameDriver` 是独立于 BetterBTD 进程和程序集的 Windows x64 Python CLI。当前版本只负责查找或启动真实 BTD6、恢复并激活游戏窗口，以及保存客户区截图和外部证据元数据。

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

## 当前限制

- `desktop-gdi-bitblt` 捕获的是用户真实可见桌面，因此要求窗口未被遮挡、未最小化、完整位于虚拟桌面内且会话未锁屏。
- 通知、顶置窗口和覆盖层会进入截图。这是可见证据的一部分，但测试编排需要据此判定现场是否有效。
- 当前只在激活并等待指定时间后截取单帧，尚未实现连续帧稳定性判断。
- 当前未实现鼠标键盘输入、元素识别、页面状态或等待条件。这些能力将在截图证据链稳定后逐步加入。

## 测试

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.GameDriver\test.ps1
```
