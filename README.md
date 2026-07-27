<div align="center">
  <img src="BetterBTD/Assets/AppIcon.ico" alt="BetterBTD" width="112" height="112">
  <h1>BetterBTD</h1>
  <p>面向《气球塔防 6》玩家的 Windows 自动化、脚本管理与实用工具箱。</p>

  <p>
    <a href="https://github.com/ZiyaoZh/BetterBTD/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/ZiyaoZh/BetterBTD?include_prereleases&sort=semver"></a>
    <a href="https://github.com/ZiyaoZh/BetterBTD/actions/workflows/ci.yml"><img alt="CI" src="https://github.com/ZiyaoZh/BetterBTD/actions/workflows/ci.yml/badge.svg"></a>
    <a href="LICENSE.txt"><img alt="License" src="https://img.shields.io/github/license/ZiyaoZh/BetterBTD"></a>
    <img alt="Platform" src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D4">
    <img alt=".NET" src="https://img.shields.io/badge/.NET-8.0-512BD4">
  </p>

  <p>
    <a href="https://github.com/ZiyaoZh/BetterBTD/releases/latest/download/BetterBTD.Install.exe"><strong>下载最新版</strong></a>
    ·
    <a href="docs/user/README.md">用户手册</a>
    ·
    <a href="docs/developer/README.md">开发者文档</a>
    ·
    <a href="https://github.com/ZiyaoZh/BetterBTD/issues">问题反馈</a>
  </p>
</div>

> [!IMPORTANT]
> BetterBTD 仍在持续开发，BTD6 更新、窗口比例、游戏语言和热键配置都可能影响自动化稳定性。首次使用请先验证截图和单个脚本，再启动长时间自动任务。

## 功能概览

| 模块 | 能力 |
| --- | --- |
| 游戏捕获 | 自动或手动选择 BTD6 窗口，提供截图测试、遮罩和多种捕获模式 |
| 脚本编辑器 | 可视化编排放置、升级、等待、点击、技能、回合控制等指令 |
| 我的脚本 | 导入、导出、筛选、运行和管理 `.btd`、`.btd6`、`.btd6s` 脚本 |
| 自动任务 | 支持收集活动、金气球、黑框、循环刷关、竞速、奥德赛和机器人控制入口 |
| 输入模拟 | 支持 Windows `SendInput`，也可选用 Interception 硬件输入模式 |
| 实用工具 | 提供回合收益、英雄等级、模范度、模范属性和存档查看工具 |
| 桌面体验 | 深浅色主题、中英文界面、按键绑定、版本检查与自动更新 |

BetterBTD 不是简单的点击录制器。脚本包含地图、难度、模式、英雄和标签等元数据，并通过托管脚本库与自动任务建立稳定绑定。

## 快速开始

### 1. 安装

1. 从 [GitHub Releases](https://github.com/ZiyaoZh/BetterBTD/releases/latest) 下载 `BetterBTD.Install.exe`。
2. 安装 [.NET 8 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/8.0)；已安装可跳过。
3. 运行安装程序并启动 BetterBTD。

> [!NOTE]
> 当前发布包面向 Windows x64，依赖系统中的 .NET 8 Desktop Runtime，不支持 macOS 或 Linux。

### 2. 首次配置

1. 启动 BTD6，并将游戏窗口设置为 16:9，推荐 `1920 × 1080`。
2. 在 BetterBTD 的“设置”页确认界面语言、游戏语言和游戏热键。
3. 返回“开始”页，优先选择 `WindowsGraphicsCapture` 并执行“测试图像捕获”。
4. 如果没有找到游戏，使用“手动选择窗口”。
5. 截图正常后启动截图器，再导入并试运行一个脚本。

完整步骤见 [快速上手](docs/user/getting-started.md)。

## 推荐工作流

```text
启动 BTD6
   ↓
验证截图与热键
   ↓
导入或编写脚本
   ↓
单独运行并检查日志
   ↓
绑定并启动自动任务
```

- 普通用户应先阅读 [用户手册](docs/user/README.md)。
- 脚本作者可继续阅读 [脚本与脚本库](docs/user/scripts.md)。
- 自动任务需要先完成脚本绑定，详见 [自动任务指南](docs/user/auto-tasks.md)。
- 参与开发、构建或协议集成请前往 [开发者文档](docs/developer/README.md)。

## 兼容性与限制

- 支持 Windows 10/11 x64。
- 坐标以 `1920 × 1080` 为参考系；非 16:9 窗口可能发生偏移。
- 自动任务和脚本执行共享截图与输入资源，同一时间只运行一个流程。
- 普通输入模式无需驱动；硬件输入模式需要自行安装 Interception 驱动。
- 奥德赛目前需要用户先进入对应活动流程，再交由 BetterBTD 接管。
- 游戏版本更新后，OCR 模板和界面规则可能需要同步维护。

## 本地数据

| 数据 | 默认位置 |
| --- | --- |
| 应用配置 | `%LocalAppData%\BetterBTD\appsettings.json` |
| 用户数据根目录 | `<BetterBTD 安装目录>\User\` |
| 托管脚本库 | `<安装目录>\User\MyScripts\` |
| 自动任务规则 | `<安装目录>\User\AutoTasks\` |
| 诊断与运行日志 | `<安装目录>\User\Logs\` |

升级或迁移前，建议同时备份应用配置和整个 `User` 目录。

## 从源码构建

需要 Windows、.NET 8 SDK 和 x64 构建环境：

```powershell
dotnet restore BetterBTD.slnx
dotnet build BetterBTD.slnx -c Release /p:Platform=x64
dotnet test BetterBTD.slnx -c Release --no-build /p:Platform=x64
```

详细环境、目录结构和发布流程见 [开发指南](docs/developer/development.md)。

## 贡献

提交问题前，请附上 BetterBTD 版本、Windows 版本、BTD6 分辨率、捕获模式、复现步骤和相关日志。代码贡献应先通过仓库的 Release 构建与测试命令，并保持改动范围清晰。

- [提交 Issue](https://github.com/ZiyaoZh/BetterBTD/issues/new)
- [查看开发者文档](docs/developer/README.md)
- [查看 CI](https://github.com/ZiyaoZh/BetterBTD/actions/workflows/ci.yml)

## 许可证与声明

本项目以 [GNU General Public License v3.0](LICENSE.txt) 发布。

BetterBTD 是非官方第三方项目，与 Ninja Kiwi 无隶属或背书关系。《Bloons TD 6》及相关商标、素材归其权利人所有。使用自动化功能前，请自行确认其符合你所在平台和游戏环境的规则，并自行承担使用风险。
