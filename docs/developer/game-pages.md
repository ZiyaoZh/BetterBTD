# BTD6 游戏界面总览

Game Driver 将稳定的逻辑页面 ID 与页面内部的 `viewState` 分开。页面 ID 参与页面导航，`viewState` 只用于页面对象识别内部状态，例如地图页码和英雄列表滚动位置。

## 页面清单

| 分类 | 稳定页面 ID | 状态 | 说明 |
| --- | --- | --- | --- |
| 启动 | `welcome`, `modifiedClientWarning` | 已建模 | 启动页和修改客户端阻断页 |
| 主流程 | `mainMenu`, `mapSelect`, `difficultySelect`, `easyModeSelect`, `mediumModeSelect`, `hardModeSelect`, `heroSelect` | 视觉已覆盖；部分边已验证 | 地图、难度、模式和英雄选择 |
| 游戏 | `inLevel`, `stageSettings` | 视觉已覆盖；部分边已验证 | 游戏内 HUD 和暂停设置 |
| 设置 | `settings`, `hotkeys`, `accessibility`, `extras` | 视觉已覆盖；部分边已验证 | 设置及其子页面 |
| 结算 | `defeatSummary`, `postGameMapReview`, `victoryPlayerStats`, `victorySummary`, `freeplayPrompt` | 视觉已覆盖；条件边有限 | 胜负和自由游戏分支 |
| 模态 | `overwriteSaveConfirmation`, `chimpsModeInfo`, `retryLastRoundConfirmation`, `restartGameConfirmation`, `sandboxIntro`, `sandboxHealthEditor`, `sandboxCashEditor` | 视觉已覆盖；条件边有限 | 只在出现对应模态时可操作 |
| Sandbox | `sandbox`, `sandboxTower` | 视觉已覆盖；条件边有限 | Sandbox 两种页面状态 |

当前页面识别以 `tools/BetterBTD.GameDriver/visual-baselines/catalog.json` 为唯一视觉来源。页面内的 `viewState` 包括：

- `mapSelect.beginner01` 到 `mapSelect.expert03`：地图分类和页码；
- `heroSelect.top`、`heroSelect.bottom`：英雄列表滚动位置；
- `extras.top`、`extras.bottom`：设置列表滚动位置；
- `inLevel.roundReady`、`inLevel.roundActive`：同一游戏页的回合状态。

## 当前默认主图

默认页面导航目录位于 `tools/BetterBTD.GameDriver/navigation/page-navigation.json`。它目前覆盖以下已确认主链路：

```text
mainMenu -> mapSelect -> difficultySelect
mainMenu -> settings -> hotkeys
mainMenu -> heroSelect -> mainMenu
difficultySelect -> easyModeSelect -> inLevel
mapSelect -> mainMenu
settings -> mainMenu
```

目录只收录操作后离开源页面、目标页面经过独立 Game Driver Oracle 确认的边。同页翻页、分类切换、英雄选择、开关和滚动都不进入这张图。

未在真实 BTD6 轨迹中确认的关系只保留在页面功能文档或视觉 catalog 中，不会成为默认自动导航路线。
