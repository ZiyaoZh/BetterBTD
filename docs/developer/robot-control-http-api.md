# 机器人控制 HTTP 消息协议

本文档描述 BetterBTD 机器人控制任务的本地 HTTP 消息协议。机器人控制任务在“自动任务”页面中以独立卡片启动；只有任务启动后，HTTP 监听才会开启。

当前版本只定义通用注册、派发、状态跟踪和结果反馈协议。具体游戏动作执行逻辑、具体 UI 自动处理逻辑会在后续补充。

> [!WARNING]
> 默认服务只应监听回环地址。该协议当前不提供身份认证、用户权限或公网暴露所需的安全边界。

## 基本规则

- 默认监听地址：`http://127.0.0.1:18766/`
- 默认只面向本机 HTTP 桥接调用。
- 请求和响应均使用 JSON。
- HTTP 层不处理群聊 ID、用户 ID、权限校验等机器人侧逻辑。
- 动作请求不排队。
- 当前正在执行任意动作时，新动作会立即返回 `Busy`。
- 当前 UI 有高优先级自动处理规则需要执行时，机器人派发动作会立即返回 `UiAutomationRequired`。
- 当前游戏状态不满足动作要求时，动作会立即返回 `InvalidGameState`。
- 具体动作完成后，接口返回统一的结果 envelope。

## 路由

### 查询当前状态

```http
GET /api/robot-task/status
```

返回机器人任务运行状态、监听地址、当前操作、最后一次操作结果和最近一次游戏 UI 状态摘要。

### 查询动作列表

```http
GET /api/robot-task/actions
```

返回当前已注册动作的元数据，包括动作 key、显示名、参数定义、允许状态和超时时间。

### 执行动作

```http
POST /api/robot-task/actions/{actionKey}/execute
Content-Type: application/json
```

`actionKey` 必须是已注册动作 key。动作请求不会排队；如果当前不能执行，会立即返回拒绝结果。

### 查询当前操作

```http
GET /api/robot-task/operations/current
```

如果当前没有执行中的操作，返回 `null`。

### 查询最后一次操作

```http
GET /api/robot-task/operations/last
```

如果尚未执行过操作，返回 `null`。

## 已注册动作

### 创建多人房间

```text
actionKey: create_multiplayer_room
```

输入参数：

```json
{
  "map": "MonkeyMeadow",
  "difficulty": "Easy",
  "mode": "Standard"
}
```

预期成功结果：

```json
{
  "roomCode": "ABCDXY"
}
```

当前执行逻辑尚未实现，会返回 `NotImplemented`。

### 加入多人房间

```text
actionKey: join_multiplayer_room
```

输入参数：

```json
{
  "roomCode": "ABCDXY"
}
```

当前执行逻辑尚未实现，会返回 `NotImplemented`。

### 选择英雄

```text
actionKey: select_hero
```

输入参数：

```json
{
  "hero": "Quincy"
}
```

当前执行逻辑尚未实现，会返回 `NotImplemented`。

### 开始挑战

```text
actionKey: start_challenge
```

输入参数：

```json
{}
```

当前执行逻辑尚未实现，会返回 `NotImplemented`。

### 打钱

```text
actionKey: send_money
```

输入参数：

```json
{
  "player": "p2"
}
```

`player` 可取值：

```text
p1
p2
p3
p4
```

当前执行逻辑尚未实现，会返回 `NotImplemented`。

### 关闭自动开始

```text
actionKey: disable_auto_start
```

输入参数：

```json
{}
```

当前执行逻辑尚未实现，会返回 `NotImplemented`。

### 开始下一回合

```text
actionKey: start_next_round
```

输入参数：

```json
{}
```

当前执行逻辑尚未实现，会返回 `NotImplemented`。

## 执行动作请求格式

```json
{
  "requestId": "msg-001",
  "parameters": {
    "map": "MonkeyMeadow",
    "difficulty": "Easy",
    "mode": "Standard"
  }
}
```

字段说明：

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `requestId` | string | 否 | 调用方自定义请求 ID。服务端会原样带回，便于机器人侧关联消息。 |
| `parameters` | object | 否 | 动作参数。无参数动作可传 `{}` 或省略。 |

## 统一响应格式

所有动作执行接口返回同一类 envelope：

```json
{
  "requestId": "msg-001",
  "operationId": "robot-20260607123456-000001",
  "action": "create_multiplayer_room",
  "accepted": true,
  "status": "Failed",
  "code": "NotImplemented",
  "message": "Action 'create_multiplayer_room' is registered but its execution logic is not implemented yet.",
  "data": {},
  "state": {
    "capturedAt": "2026-06-07T08:00:00Z",
    "uiState": "Unknown",
    "confidence": 0,
    "summary": "No game UI state has been captured yet."
  }
}
```

字段说明：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `requestId` | string | 请求中的 `requestId`，如果请求未提供则为空字符串。 |
| `operationId` | string | 服务端生成的操作 ID。拒绝请求也会生成。 |
| `action` | string | 动作 key。 |
| `accepted` | bool | 是否通过派发前检查并进入动作执行阶段。 |
| `status` | string | 操作状态。 |
| `code` | string | 结果码或错误码。 |
| `message` | string | 面向调用方的结果说明。 |
| `data` | object | 动作自定义结果数据，例如创建房间返回 `roomCode`。 |
| `state` | object | 当前或最近一次游戏 UI 状态摘要。 |

## 状态值

### `status`

```text
Rejected
Running
Completed
Failed
Cancelled
TimedOut
```

### `code`

```text
Ok
Busy
InvalidAction
InvalidParameter
InvalidGameState
UiAutomationRequired
NotImplemented
TaskNotRunning
Failed
Cancelled
TimedOut
```

含义：

| code | 含义 |
| --- | --- |
| `Ok` | 成功。 |
| `Busy` | 当前已有动作执行中，动作不排队，直接拒绝。 |
| `InvalidAction` | 未注册的动作 key。 |
| `InvalidParameter` | 参数缺失、为空或枚举值不合法。 |
| `InvalidGameState` | 当前游戏 UI 状态不满足动作要求。 |
| `UiAutomationRequired` | 当前 UI 命中了高优先级自动处理规则，机器人动作被拒绝。 |
| `NotImplemented` | 动作已注册，但具体执行逻辑尚未实现。 |
| `TaskNotRunning` | 机器人控制任务尚未启动。 |
| `Failed` | 动作执行失败。 |
| `Cancelled` | 动作被取消。 |
| `TimedOut` | 动作执行超时。 |

## HTTP 状态码

| 场景 | HTTP 状态码 |
| --- | --- |
| 动作被接受并完成处理 | `200 OK` |
| 参数错误 | `400 Bad Request` |
| 动作不存在 | `404 Not Found` |
| busy、状态不匹配、UI 自动动作抢占 | `409 Conflict` |
| 机器人任务未运行 | `503 Service Unavailable` |
| 未处理异常 | `500 Internal Server Error` |

注意：动作通过派发检查后，即使具体执行结果是 `Failed` 或 `NotImplemented`，HTTP 状态码仍可能是 `200 OK`。业务结果应以响应体中的 `accepted`、`status`、`code` 为准。

## 示例

### 查询动作列表

```http
GET http://127.0.0.1:18766/api/robot-task/actions
```

示例响应：

```json
{
  "actions": [
    {
      "key": "create_multiplayer_room",
      "displayName": "Create multiplayer room",
      "description": "Create a multiplayer room and return the room code.",
      "parameters": [
        {
          "key": "map",
          "displayName": "Map",
          "type": "Enum",
          "isRequired": true,
          "allowedValues": ["MonkeyMeadow", "TreeStump"]
        }
      ],
      "allowedUiStates": [],
      "timeoutMs": 30000
    }
  ]
}
```

`allowedValues` 会根据项目内枚举输出，实际返回列表会比示例更完整。

### 创建多人房间

```http
POST http://127.0.0.1:18766/api/robot-task/actions/create_multiplayer_room/execute
Content-Type: application/json
```

```json
{
  "requestId": "msg-create-room-001",
  "parameters": {
    "map": "MonkeyMeadow",
    "difficulty": "Easy",
    "mode": "Standard"
  }
}
```

当前占位响应：

```json
{
  "requestId": "msg-create-room-001",
  "operationId": "robot-20260607123456-000001",
  "action": "create_multiplayer_room",
  "accepted": true,
  "status": "Failed",
  "code": "NotImplemented",
  "message": "Action 'create_multiplayer_room' is registered but its execution logic is not implemented yet.",
  "data": {},
  "state": {
    "uiState": "Unknown",
    "confidence": 0,
    "summary": "No game UI state has been captured yet."
  }
}
```

后续实现完成后，成功响应中的 `data` 应包含：

```json
{
  "roomCode": "ABCDXY"
}
```

### busy 拒绝

```json
{
  "requestId": "msg-002",
  "operationId": "robot-20260607123457-000002",
  "action": "send_money",
  "accepted": false,
  "status": "Rejected",
  "code": "Busy",
  "message": "Robot task is busy and does not queue action requests.",
  "data": {},
  "state": {
    "uiState": "InLevel",
    "confidence": 1,
    "summary": "Test state."
  }
}
```

### 参数错误

```json
{
  "requestId": "msg-003",
  "operationId": "robot-20260607123458-000003",
  "action": "send_money",
  "accepted": false,
  "status": "Rejected",
  "code": "InvalidParameter",
  "message": "Required parameter 'player' is missing or empty.",
  "data": {},
  "state": {
    "uiState": "InLevel",
    "confidence": 1,
    "summary": "..."
  }
}
```

## 状态查询响应

```json
{
  "isRunning": true,
  "runState": "Listening",
  "listeningUrl": "http://127.0.0.1:18766/",
  "currentOperation": null,
  "lastResult": {
    "operationId": "robot-20260607123456-000001",
    "action": "create_multiplayer_room",
    "accepted": true,
    "status": "Failed",
    "code": "NotImplemented",
    "message": "Action 'create_multiplayer_room' is registered but its execution logic is not implemented yet.",
    "data": {}
  },
  "gameState": {
    "uiState": "Unknown",
    "confidence": 0,
    "summary": "No game UI state has been captured yet."
  },
  "lastUpdatedAt": "2026-06-07T08:00:00Z"
}
```

`runState` 可取值：

```text
Stopped
Starting
Listening
BusyWithUiAutomation
BusyWithRobotAction
Stopping
```

## 后续扩展点

### 实现具体机器人动作

为对应 action 实现 `IRobotGameAction`：

- `CheckAsync`：检查参数和当前 UI 状态。
- `ExecuteAsync`：执行具体游戏操作。
- 返回 `RobotActionResult.Completed(...)` 或 `RobotActionResult.Failed(...)`。

### 实现高优先级 UI 自动动作

实现 `IRobotUiAutomationRule`：

- `CanHandle(GameUiSnapshot snapshot)` 判断当前 UI 是否需要优先处理。
- `ExecuteAsync(...)` 执行具体点击或输入。
- 规则按 `Priority` 从高到低匹配。

当某个规则匹配当前 UI 时，普通机器人动作会被拒绝，不会排队等待。

返回 [开发者文档](README.md) · [自动任务架构](reference/auto-task-architecture.md)
