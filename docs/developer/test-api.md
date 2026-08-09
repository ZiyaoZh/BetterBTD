# BetterBTD Test API

BetterBTD Test API 是测试编排 Agent 控制 BetterBTD 脚本执行器的本地控制面。它只负责应用生命周期和内部诊断，不提供通用游戏点击、按键或坐标输入。

## Oracle 边界

Test API 返回的捕获状态、步骤、检查点、重试、日志和脚本结果都位于 `nonOracleDiagnostics`。这些字段用于定位 BetterBTD 行为，不能证明真实游戏结果通过。

黑盒断言仍以独立 Game Driver 保存的真实 BTD6 客户区截图及其独立识别结果为准。`Completed` 只表示 BetterBTD 脚本执行器完成，不表示测试 `passed`。

## 启用和认证

Test API 默认关闭。启动方必须为每次 BetterBTD 进程生成新的至少 32 字符 Token，并显式传入 `--test-api`：

```powershell
$tokenBytes = New-Object byte[] 32
$tokenGenerator = [Security.Cryptography.RandomNumberGenerator]::Create()
$tokenGenerator.GetBytes($tokenBytes)
$tokenGenerator.Dispose()
$env:BETTERBTD_TEST_API_TOKEN = [Convert]::ToBase64String($tokenBytes)
Start-Process .\BetterBTD.exe -ArgumentList '--test-api'
```

默认地址是 `http://127.0.0.1:18767/`。可通过 `--test-api-url http://127.0.0.1:19001/` 修改端口，也可用 `--test-api-token <token>` 直接传递 Token。推荐环境变量，避免 Token 出现在进程命令行中。

监听地址必须是数值回环 IP 和根路径。服务拒绝 `localhost`、通配地址、非回环地址、HTTPS、userinfo、query 和 fragment。Token 只保存在本次进程内存中，不写入 BetterBTD 配置或响应；服务停止后立即失效。

所有接口都要求：

```http
Authorization: Bearer <token>
```

缺失或错误 Token 返回 `401 Unauthorized`。响应带 `Cache-Control: no-store`，不提供 CORS 头。带请求体的写接口只接受 `application/json`，请求体上限为 1 MiB。

## 接口

所有路径以 `/api/test/v1` 开头，JSON 使用 camelCase，枚举使用稳定英文名。

| 方法 | 路径 | 用途 |
| --- | --- | --- |
| `GET` | `/health` | 查询应用版本、配置白名单、捕获器及脚本执行器状态。 |
| `POST` | `/capture/start` | 启动 BetterBTD 自身的游戏捕获器。 |
| `POST` | `/scripts/validate` | 无输入副作用地校验脚本并返回 SHA-256。 |
| `POST` | `/scripts/execute` | 创建异步脚本 operation，立即返回 `202 Accepted`。 |
| `GET` | `/operations/status` | 按 operation ID 查询生命周期状态。 |
| `GET` | `/operations/logs` | 按游标读取结构化诊断日志。 |
| `POST` | `/operations/pause` | 请求在下一个安全检查点暂停。 |
| `POST` | `/operations/resume` | 恢复已请求暂停或已暂停的 operation。 |
| `POST` | `/operations/cancel` | 请求取消 operation。 |

### 健康状态

```http
GET /api/test/v1/health
```

响应中的 `nonOracleDiagnostics` 包含配置白名单、捕获模式/窗口和执行器进度。配置不会返回游戏安装路径等无关字段。

### 启动捕获器

```http
POST /api/test/v1/capture/start
Content-Type: application/json

{
  "windowHandle": 123456,
  "captureModeName": "WindowsGraphicsCapture",
  "captureIntervalMs": 50
}
```

字段均可省略；省略 `windowHandle` 时按 BetterBTD 配置的目标标题查找窗口。已使用相同窗口和设置运行时为幂等成功，不同设置返回 `409 Conflict`。API 关闭时只停止由 API 启动的捕获会话，不启动遮罩窗口。

### 校验脚本

```http
POST /api/test/v1/scripts/validate
Content-Type: application/json

{
  "scriptPath": "E:\\tests\\scripts\\example.json"
}
```

API 会规范化路径、限制脚本为 16 MiB、检查当前脚本格式、命令类型和处理器注册，并确认文件在读取期间没有变化。成功响应返回 `sha256` 和 `nonOracleDiagnostics` 下的脚本摘要。

### 执行脚本

```http
POST /api/test/v1/scripts/execute
Content-Type: application/json

{
  "scriptPath": "E:\\tests\\scripts\\example.json",
  "expectedSha256": "<validate 返回的 64 位十六进制摘要>",
  "startStepIndex": 0,
  "intervalStrategy": "InstructionCustom",
  "commonOperationIntervalMs": 200,
  "timeoutMs": 600000
}
```

`expectedSha256` 可省略，但测试编排应传入校验阶段返回值，防止两次调用之间脚本被替换。`timeoutMs` 范围为 1000 至 86400000；省略时由调用方通过 cancel 管理超时。

成功仅表示 operation 已创建：

```http
HTTP/1.1 202 Accepted
Location: /api/test/v1/operations/status?operationId=test-...
```

BetterBTD 不排队执行。已有 Test API operation、直接脚本、自动任务或 Robot 任务运行时返回 `409 Busy`。

### 查询状态

```http
GET /api/test/v1/operations/status?operationId=test-20260809120000-000001
```

省略 `operationId` 时优先返回当前 operation；没有运行中的 operation 时返回最近完成的一次。明确传入 ID 可查询最近保留的 20 个 operation。

状态值：

```text
Starting
Running
PauseRequested
Paused
Cancelling
Completed
Failed
Cancelled
TimedOut
```

步骤、检查点、尝试次数和脚本结果位于 `nonOracleDiagnostics`。只有同时满足以下字段时，Game Driver 才能进入 Recover 并重新发送游戏输入：

```json
{
  "inputOwner": "None",
  "inputControlReleased": true,
  "canGameDriverRecover": true
}
```

收到 cancel 的 `202` 响应并不代表脚本已经停止；编排层必须继续轮询到终态和上述恢复门槛。

恢复门槛还要求 Test API operation 的进程级游戏控制租约已经释放，且脚本执行器、自动任务和 Robot 均未运行。如果最终按键释放失败，operation 转为 `Failed`，`nonOracleDiagnostics.inputReleaseFailure` 保留原因，`canGameDriverRecover` 保持 `false`；此时必须重启 BetterBTD 后才能再次交还游戏输入。

### 暂停、恢复和取消

三个接口使用相同请求体：

```json
{
  "operationId": "test-20260809120000-000001"
}
```

pause 接受后先进入 `PauseRequested`，脚本只在安全检查点进入 `Paused`。控制请求成功返回 `202 Accepted`；未知 ID 返回 `404`，非法状态转换返回 `409`。重复 cancel 是幂等操作。

### 查询日志

```http
GET /api/test/v1/operations/logs?operationId=test-...&afterSequence=0&limit=200
```

`limit` 范围为 1 至 1000。客户端把响应的 `nextSequence` 作为下一次 `afterSequence`。每个 operation 最多保留最近 10000 条日志；发生截断时 `isTruncated=true`，`firstAvailableSequence` 指向最早仍可读取的条目。全部日志都属于 `nonOracleDiagnostics`。

## 输入所有权

| 阶段 | 输入方 | Test API 约束 |
| --- | --- | --- |
| Arrange | Game Driver | 尚未创建脚本 operation。 |
| Act | BetterBTD | execute 后 Game Driver 只观察，禁止输入。 |
| Assert | 无或 BetterBTD | Game Driver 用独立截图判断可见结果。 |
| Recover | Game Driver | operation 终态且 `canGameDriverRecover=true`。 |

测试运行期间不得从 BetterBTD UI 手工启动脚本、自动任务或 Robot 控制。Test API operation、直接脚本、自动任务和 Robot 通过同一个进程级游戏控制租约原子互斥；自动任务内嵌脚本使用引用计数加入同一租约，捕获器设置变更也与这些控制器串行执行。

每个输入 owner 都必须在释放最后一个租约引用前等待其后台动作退出并成功执行按键释放。释放失败会 poison 全局输入控制并阻止新的 owner；同一 owner 只有在仍持有租约时成功重试按键释放，才能清除 poison。应用退出也会先取消自动任务和直接/嵌入脚本，停止 Test API 与 Robot，等待租约空闲，再关闭捕获和输入服务。

## 错误响应

```json
{
  "code": "invalidRequest",
  "message": "..."
}
```

常用状态码：`400` 请求或脚本无效，`401` Token 无效，`404` 路由或 operation 不存在，`409` 共享资源忙或状态转换无效，`500` 未预期服务错误。

返回 [开发者文档](README.md) · [独立 BTD6 Game Driver](game-driver.md) · [脚本测试场景协议](script-test-scenario.md) · [脚本执行引擎](reference/script-engine.md)
