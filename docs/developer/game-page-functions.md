# 页面功能文档

## 页面对象协议

页面对象负责页面内部状态，不把内部状态展开为导航节点。每个页面对象提供以下语义：

```text
prepare(target, observation, actionRunner) -> observation, completed
leave(edge, target, actionRunner) -> observation
```

`prepare` 可以执行有限的同页操作，并且每个操作都必须重新识别页面。`leave` 只能执行导航目录中声明的跨页方法。两者都不能把“点击成功”当作事实，最终页面必须由下一次独立视觉证据确认。

## 已实现页面对象

### `CatalogPageObject`

通用页面对象使用导航边上的固定 catalog 元素执行跨页点击。它不提供同页状态操作。

### `MapSelectPage`

内部状态是地图分类和页码，目标地图通过 `mapTargets` 映射到一个 `mapSelect.*` view state。

- `ensureMapVisible(mapId)`：必要时点击分类，再逐页点击 `nextPage` 或 `previousPage`；
- `enterMap(mapId)`：只在地图可见并由独立元素检测确认后点击地图卡，唯一导航目标是 `difficultySelect`；
- 地图翻页和分类切换不进入页面导航图；
- 默认目录只接受已验证的 `monkeyMeadow`、`treeStump`、`frozenOver`、`spiceIslands`、`ascent` 和 `midnightMansion`。

### `HeroSelectPage`

内部状态是英雄列表滚动位置和当前选择。目标英雄不拆成页面节点。

- `ensureHeroVisible(heroId)`：必要时在英雄列表区域滚动到目标 view state；
- `selectHero(heroId)`：点击英雄元素并重新确认仍处于 `heroSelect`；
- `leavePage()`：通过已验证的 `heroSelect.back` 边返回 `mainMenu`；
- 目前只把从主菜单进入英雄页、英雄页返回主菜单加入默认图。

## 失败规则

- 初始识别若为 `unknown`、`ambiguous` 或非 Oracle 证据，禁止发送输入；
- 同页准备动作没有得到预期的源页或 view state，立即失败；
- 跨页动作仍停留源页时，只按边上的 `retryPolicy` 有限重试；
- 到达未列入 `allowedTargetPages` 的已识别页面，立即停止；
- 任何跨页动作之后不能确认 Oracle 合格页面，都不继续下一条边。

新增页面对象时，应同时补充页面用途、内部状态、方法前置条件、失败表现和对应真实证据。
