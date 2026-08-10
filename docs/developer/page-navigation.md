# 页面导航目录

## 目录职责

`tools/BetterBTD.GameDriver/navigation/page-navigation.json` 只描述逻辑页面之间经过验证的跨页动作。视觉 catalog 继续负责页面锚点、元素位置、元素可见性和 `viewState` 识别；页面对象负责同页准备。

每条边包含：

```text
sourcePage
actionMethod
element 或 elementTemplate
parameters / allowedParameters
conditions
allowedTargetPages
settleRule
retryPolicy
sideEffects
evidence
```

加载器会拒绝以下关系：

- `sourcePage` 出现在 `allowedTargetPages` 中；
- 源页或目标页不在视觉 catalog；
- 固定动作元素不属于源页；
- 没有已标记 `verified` 的真实证据；
- 参数化动作没有参数声明或参数值超出验证集合。

## 执行循环

一次 `navigate` 调用内部执行如下闭环：

```text
capture + recognize
    -> BFS 选择当前页面到目标页面的下一条边
    -> 页面对象 prepare 同页状态
    -> 页面对象 leave 执行一条跨页边
    -> 等待 changedStable
    -> capture + recognize
    -> 按实际 Oracle 页面重新规划
```

路线不是预先承诺的脚本。目标页、允许目标页和实际识别页始终由独立 Game Driver 证据决定。

## CLI 示例

```powershell
tools\BetterBTD.GameDriver\game-driver.ps1 navigate --phase arrange --page settings
tools\BetterBTD.GameDriver\game-driver.ps1 navigate --phase arrange --page hotkeys
tools\BetterBTD.GameDriver\game-driver.ps1 navigate --phase arrange --map monkeyMeadow
tools\BetterBTD.GameDriver\game-driver.ps1 navigate --phase arrange --map monkeyMeadow --difficulty easy
tools\BetterBTD.GameDriver\game-driver.ps1 navigate --phase arrange --hero Quincy
tools\BetterBTD.GameDriver\game-driver.ps1 navigate --phase arrange --page mainMenu --hero Quincy
```

`--map`、`--difficulty`、`--mode` 和 `--hero` 是页面对象的目标参数，不是导航图节点。默认目标页面分别推导为 `difficultySelect`、对应模式页、`inLevel` 或 `heroSelect`；需要更精确的目标时使用 `--page`。
指定 `--page mainMenu --hero ...` 时，导航器会先进入英雄页、完成页面内选择，再执行已验证的返回边。

## 证据要求

`evidence` 中的 `afterEvidenceId` 必须来自真实 Game Driver 采集的 after evidence，且对应操作轨迹的最终页面必须是 Oracle 合格的 `matched` 页面。视觉 holdout 只能证明页面识别，不能单独证明导航边。

当前目录的地图参数只包含六张真实验证过的地图。新增地图或边时，先在真实 BTD6 中重复操作并保存 before/after/operation 证据，再更新页面对象、导航目录和本文件；未完成验证的关系只能标记为候选，不得加入默认图。
