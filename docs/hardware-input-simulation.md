# 硬件模拟说明

BetterBTD 现已支持两种键鼠模拟方式：

- 普通模拟：沿用当前 `SendInput` 方案，无需额外驱动。
- 硬件模拟：通过项目内置的 Interception 适配层向驱动发送硬件级键鼠输入。

## 前提

启用硬件模拟前，需要先安装 Interception 驱动。

当前项目移植的是 `C:\Users\Administrator\Downloads\interception\Interception` 的用户态能力，驱动本体仍需按上游项目方式安装。

## 使用建议

- 第一次启用硬件模拟后，先实际移动一次鼠标并按一次键盘。
- 应用会在后台识别当前活跃的键盘/鼠标设备，并优先向这些设备发送后续输入。

## 回退策略

如果配置中选择了硬件模拟，但运行时未检测到 Interception 驱动，BetterBTD 会自动回退到普通模拟，避免脚本完全失效。
