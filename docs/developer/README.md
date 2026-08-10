# BetterBTD 开发者文档

本目录面向构建项目、贡献代码、维护识别规则或调用本地协议的开发者。

## 开发指南

- [开发环境、构建与发布](development.md)
- [项目架构](architecture.md)
- [脚本文件格式](script-file-format.md)
- [机器人控制 HTTP 协议](robot-control-http-api.md)
- [BetterBTD Test API](test-api.md)
- [独立 BTD6 Game Driver](game-driver.md)
- [脚本测试场景协议](script-test-scenario.md)
- [新增地图维护流程](map-update-workflow.md)

## Agent Skills

- [`btd6-game-driver`](../../.agents/skills/btd6-game-driver/SKILL.md)：独立观察和操控真实 BTD6，只在 Arrange/Recover 取得输入。
- [`betterbtd-script-test`](../../.agents/skills/betterbtd-script-test/SKILL.md)：按场景协议编排 Test API 与 Game Driver，生成黑盒证据报告。
- [`betterbtd-source-index`](../../.agents/skills/betterbtd-source-index/SKILL.md)：按项目结构和文件级索引快速定位 BetterBTD 源文件。

## 内部参考

以下文档记录自动化模块的设计约束和基于 `1920 × 1080` 的识别参数，主要用于维护现有实现：

- [自动任务架构](reference/auto-task-architecture.md)
- [脚本库设计](reference/script-library-design.md)
- [脚本执行引擎](reference/script-engine.md)
- [收集活动 UI 策略](reference/collection-ui-strategy.md)
- [地图徽章识别](reference/badge-recognition.md)
- [UI 状态规则](reference/ui-state-rules.md)

返回 [项目首页](../../README.md) · [全部文档](../README.md) · [用户手册](../user/README.md)
