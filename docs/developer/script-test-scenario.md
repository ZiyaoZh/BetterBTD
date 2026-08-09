# BetterBTD 脚本测试场景协议

`betterbtd/script-test-scenario` v1 是独立 Game Driver 与 BetterBTD Test API 之间的测试编排契约。场景文件只描述前置状态、脚本执行参数、外部断言、输入交还门槛和失败证据策略；它不包含底层截图、识别或输入实现。

规范 schema、校验器和示例位于 `tools/BetterBTD.ScriptTest/`。场景使用 camelCase 字段和稳定英文枚举/ID，未知字段、未知版本、重复 JSON 属性及 `NaN` / `Infinity` 等非标准 JSON 常量全部 fail closed。

## Oracle 边界

通过条件必须来自 Game Driver 对真实 BTD6 客户区的独立观察。一个可用于断言的采样必须同时满足：

- PNG、元数据和 `.complete.json` 三件套完整且哈希一致。
- 捕获来源为 `BetterBTD.GameDriver`，没有捕获警告，宽高比适用于当前视觉目录。
- `recognition.status=matched`，页面及所需视口/元素结果均为 Oracle eligible。
- `unknown`、`ambiguous`、标注图和控制操作中的 `expectationProbe` 不能作为通过证据。

Test API 的 operation 状态、脚本进度、检查点、重试、结果和日志只能进入 `nonOracleDiagnostics`。`Completed` 是继续外部断言的 Act 门槛，不是测试通过条件。BetterBTD 自身截图、OCR 或 UI 状态不得填充 Game Driver 证据字段，也不得在独立 Oracle 不可用时作为回退。

## 场景结构

完整示例见 [`easy-standard-victory.scenario.json`](../../tools/BetterBTD.ScriptTest/examples/easy-standard-victory.scenario.json)。v1 根对象包含：

| 字段 | 语义 |
| --- | --- |
| `schema` / `schemaVersion` | 固定为 `betterbtd/script-test-scenario` / `1`。 |
| `id` | 稳定场景 ID，不使用本地化显示文本。 |
| `requiredCapabilities` | 场景断言需要的独立 Game Driver 能力。 |
| `script` | 脚本相对路径、API 执行超时和执行间隔参数。 |
| `arrange` | Agent 要建立的地图、难度、模式、英雄及独立就绪断言。 |
| `act` | 固定只读观察策略、采样间隔和期望的 API 终态。 |
| `assert` | 必须满足的正向外部断言及 Act/Assert 采样中不得命中的断言。 |
| `recover` | Game Driver 重新取得输入前的固定 API 门槛及恢复目标。 |
| `failureArtifacts` | 失败时必须保留的截图、识别、轨迹、API 状态和日志。 |

`script.path` 相对场景文件解析，必须留在仓库内。`failureArtifacts.directory` 是以 `artifacts/` 开头的正斜杠路径模板，只允许安全静态段及完整路径段 `{scenarioId}`、`{runId}`，两个占位符都必须且只能出现一次。编排器在运行开始时用已校验的场景 ID 和新生成的 run ID 展开模板；run ID 必须匹配 `[a-z0-9][a-z0-9._-]{0,127}`。展开后再次规范化和检查路径仍位于仓库 `artifacts/` 下，之后才能写入证据；未知占位符、反斜杠和 `artifacts/../` 等路径无效。

场景不保存任何 Token、Authorization header、密码、credential、secret、API key 或 `expectedSha256`。校验器拒绝敏感字段名和常见凭据值签名，编排器还必须对报告输出脱敏。每次运行必须调用 `/scripts/validate`，再把当次返回的 SHA-256 传给 `/scripts/execute.expectedSha256`。

`arrange.gameState` 的 map、difficulty、mode 和 hero 必须来自 `game-state-catalog.json` 中复制的稳定 BetterBTD 枚举名，mode 还必须属于所选 difficulty。该目录描述 BetterBTD 当前脚本格式支持面，不等同于 Game Driver 的全部视觉目录；例如 Game Driver 已观察但 BetterBTD 枚举尚未包含的 `Ascent` 和 `DanDeMonk` 不能写入 v1 场景。`/scripts/validate.nonOracleDiagnostics` 返回脚本摘要后，编排器必须逐项与场景比较；不一致时在 execute 前以 `scriptMetadataMismatch` 失败。摘要只用于验证被测输入配置一致，不是游戏结果 Oracle。

`act.gameDriverInput` 固定为 `ObserveOnly`，`recover.inputGate` 固定要求：

```json
{
  "operationTerminal": true,
  "inputOwner": "None",
  "inputControlReleased": true,
  "canGameDriverRecover": true
}
```

场景不能放宽这些字段。cancel 返回 `202 Accepted` 后仍处于 Act；只有轮询到 operation 终态并同时满足四项输入门槛，才能执行第一个 Recover 点击、按键、滚动或拖动。若 `inputReleaseFailure` 导致门槛不能满足，运行结果不得进入 Recover，必须保存诊断并按 Test API 约定重启 BetterBTD 才能再次交还输入。

## 断言词汇

每个 predicate 都有全场景唯一的 `id`。`assert.all` 至少包含一个正向断言，仅有“未观察到失败”不能通过测试。每个正向断言还必须显式声明 `quantifier: Eventually` 和 `observationWindow`；后者为 `ActAndAssert` 或 `Assert`。这些字段不能用于 Arrange、`neverObserved` 或 Recover predicate。

| `kind` | `operator` | 字段 | 能力 |
| --- | --- | --- | --- |
| `Page` | `Equals` | `pageId` | `PageRecognition` |
| `Page` | `OneOf` | `pageIds` | `PageRecognition` |
| `ViewState` | `Equals` | `pageId`, `viewStateId` | `PageRecognition`, `ViewStateRecognition` |
| `Element` | `Visible` | `elementId` | `PageRecognition`, `ElementVisibility` |
| `ElementState` | `Equals` | `elementId`, `state` | `PageRecognition`, `ElementVisibility`, `ElementState` |
| `ElementNumber` | `Equals` / `GreaterThanOrEqual` / `LessThanOrEqual` | `elementId`, `value` | `PageRecognition`, `ElementNumber` |

页面、视口、元素和状态 ID 必须存在于当前 Game Driver catalog，视口必须归属指定页面。`Element Visible` 只接受已有独立可见性 detector 的元素；仅声明几何边界或动作点不够。`ElementNumber` 只接受 role 为 `value` 且带有效 schema v3/v4 `number` 声明的元素；边界、value 角色或 BetterBTD 内部 OCR 都不能单独提供该能力。

胜利可表达为 `Page OneOf [victoryPlayerStats, victorySummary]`，失败页为 `defeatSummary`。`assert.all` 中每个 `Eventually` predicate 独立求值，必须在自身 `observationWindow` 内至少由一个 Oracle-eligible 采样满足；不同 predicate 可以引用不同采样，因此能组合执行中回合数与执行后的胜利页面。两个 predicate 表示两者都必须分别发生；互斥页面的“任一”关系必须写成一个 `Page OneOf`。

例如，“执行中曾达到第 40 回合，完成后观察到胜利”由两个正向断言组成：`inLevel.round GreaterThanOrEqual 40` 使用 `ActAndAssert`，`Page OneOf [victoryPlayerStats, victorySummary]` 使用 `Assert`。这不会把不同帧伪装成同一游戏状态；报告必须分别引用两个实际证据采样。

`assert.neverObserved` 在整个 Act 和 Assert 采样时间线上求值；其准确语义是“按配置的采样间隔未观察到”，不能表述为连续时间上从未出现。任一合格采样命中 predicate 时立即失败；证据损坏、有捕获警告或识别为 `unknown` / `ambiguous` 的采样不能证明负向条件，最终结果必须是 `InfrastructureError`，不得忽略该采样后通过。

当前 `ElementNumber` 支持 `inLevel`、`sandbox` 和 `sandboxTower` 各自的 `health`、`cash`、`round`。数值识别必须返回 `status=matched` 且 `oracleEligible=true` 才可求值；`unknown`、`ambiguous` 或来自非 Oracle 证据的匹配都属于不可求值采样。schema v3/v4 预检会拒绝不完整字形集、非法匹配参数、越出参考空间或元素边界的数值区域，以及带视口 placement 的数值元素，避免把 Game Driver 无法加载的目录误报为能力可用；Game Driver 自身还会在 schema v4 目录加载时校验字形 alpha mask。`defeatSummary.roundReached`、`victorySummary.reward` 等尚未声明独立数值模型的 value 会在场景预检中以“没有独立数值识别”拒绝，编排器不得改用 BetterBTD OCR、脚本进度或日志判定。

## 编排状态机

```text
Validate scenario/script
        |
        v
Arrange -- Game Driver input allowed, every action re-observed
        |
        v
Act ----- POST execute; Game Driver capture/recognize only
        |                    |
        | timeout/failure    | expected terminal status
        v                    v
Cancel and poll          Assert -- Game Driver capture/recognize only
        |                    |
        +---------+----------+
                  v
Wait for terminal + input release gate
                  |
                  v
Recover -- Game Driver input allowed
```

Arrange 开始前不得存在脚本 operation。正式场景预检必须先运行 Game Driver `catalog`，由 Game Driver 自己校验 schema、模板与来源证据完整性；场景校验器只建立所需 ID 索引，不能替代该命令。达到 `arrange.readyWhen` 后，编排器保存一组新的独立证据，再调用 `/scripts/validate`、比较脚本摘要并调用 `/scripts/execute`。Act/Assert 期间只能周期性调用 Game Driver `capture` 与 `recognize`；禁止 `click`、`click-point`、`scroll-point`、`drag-point` 和 `press-key`。

Act 应持续分页收集 operation 日志，使用 `nextSequence` 作为下一游标，并记录 `isTruncated` 与 `firstAvailableSequence`。完成后立即收集最终状态和剩余日志，以免超过 Test API 的 20 个 operation / 10000 条日志保留窗口。日志始终放在 `nonOracleDiagnostics`。

Assert 在 operation 达到 `Completed` 后继续采样至所有正向 predicate 已在各自窗口中满足，或等待至超时。`ActAndAssert` predicate 可引用此前 Act 时间线，`Assert` predicate 只能引用终态后的新采样；完整但未满足 predicate 的 Oracle-eligible 采样可继续等待，超时后为 `Failed`。若某 predicate 在自身窗口中没有任何可求值采样则为 `InfrastructureError`。`neverObserved` 同时检查此前 Act 观察时间线和 Assert 新采样。每个断言结果必须引用实际 Game Driver 证据三件套和识别输出，不能只保存布尔值。

通过场景也必须保留所有正向断言引用的原始证据与识别输出；`failureArtifacts` 额外规定失败和基础设施错误时不可省略的完整诊断集合。

## 结果规则

后续编排器生成的运行结果至少区分 `Passed`、`Failed` 和 `InfrastructureError`：

| Test API Act | 外部断言 | 结果 |
| --- | --- | --- |
| `Completed` | 全部由合格 Game Driver 证据满足 | `Passed` |
| `Completed` | 任一不满足或命中 `neverObserved` | `Failed` |
| `Failed` / `Cancelled` / `TimedOut` | 任意 | `Failed`，外部观察只作附加证据 |
| 任意 | 证据损坏、有警告、`unknown` / `ambiguous` 或缺少所需能力 | `InfrastructureError`，不得通过 |

API `Completed` 但真实游戏失败必须是 `Failed`。真实画面看似成功但 API operation 失败也不能通过，因为被测 Act 没有按场景完成；外部截图仍应保留用于解释实际游戏状态。

## 失败证据

`failureArtifacts` 的所有布尔字段在 v1 中固定为 `true`。失败或基础设施错误必须保留：

- Act/Assert 的 Game Driver 原始截图三件套及独立 recognition 输出。
- 采样时间线，包括 UTC 时间、证据引用、页面/视口/元素摘要和 Oracle 资格。
- Arrange/Recover 的 Game Driver `operation.json` 输入轨迹；若 Recover 未获授权，记录“未执行”，不能伪造轨迹。
- Test API 最终 operation status 与完整可获取日志，整体标记为 `nonOracleDiagnostics`。

报告不得包含 Bearer Token。BetterBTD 日志发生截断时必须显式记录，不能把缺失日志当作完整日志。

## 校验

首次建立独立环境：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.ScriptTest\setup.ps1
```

校验结构、路径、catalog 引用和当前 Oracle 能力：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.ScriptTest\validate-scenario.ps1 `
  tools\BetterBTD.ScriptTest\examples\easy-standard-victory.scenario.json
```

正式执行预检应增加 `--check-script-path`。退出码 `0` 表示格式有效且能力满足，`2` 表示场景无效，`3` 表示格式有效但缺少 Oracle 能力。该校验不启动 BetterBTD、不连接 Test API，也不捕获或控制游戏。

协议 v1 的字段和语义冻结。新增可选实现私有数据只能放入 `extensions`，但字段名仍不得包含 Token、Authorization、credential、password、secret、API key 等敏感凭据，也不能保存动态脚本哈希；新增必填字段或改变既有判定语义必须升级 `schemaVersion`。

返回 [开发者文档](README.md) · [BetterBTD Test API](test-api.md) · [独立 BTD6 Game Driver](game-driver.md)
