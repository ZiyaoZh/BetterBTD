# 开发指南

## 环境要求

- Windows 10/11 x64
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 或可运行 .NET CLI 的等效环境
- Windows 11 SDK `10.0.22621.0` 对应的目标包
- Git

BetterBTD 是 WPF 桌面应用，目标框架为 `net8.0-windows10.0.22621.0`，因此不能在 macOS 或 Linux 上完成完整构建和运行验证。

## 获取与构建

```powershell
git clone https://github.com/ZiyaoZh/BetterBTD.git
cd BetterBTD
dotnet restore BetterBTD.slnx
dotnet build BetterBTD.slnx -c Release /p:Platform=x64
```

运行主程序：

```powershell
dotnet run --project BetterBTD\BetterBTD.csproj -c Debug /p:Platform=x64
```

运行全部测试：

```powershell
dotnet test BetterBTD.slnx -c Release /p:Platform=x64
```

CI 使用相同的 Release/x64 构建路径，工作流定义在 `.github/workflows/ci.yml`。

## 解决方案组成

| 项目 | 作用 |
| --- | --- |
| `BetterBTD` | WPF 主程序、业务模型、自动化运行时和资源 |
| `BetterBTD.Tests` | xUnit 单元与回归测试 |
| `Fischless.GameCapture` | 游戏窗口捕获抽象与实现 |
| `Fischless.HotkeyCapture` | 全局热键支持 |
| `Fischless.WindowsInput` | Windows 输入模拟基础库 |

更详细的分层说明见 [项目架构](architecture.md)。

## 开发约定

- 目标平台固定为 x64。
- UI 使用 WPF、WPF UI 和 CommunityToolkit.Mvvm；业务逻辑应放在 ViewModel、Service 或 Core 中。
- 用户可见文字需要同时维护中文和英文资源。
- 游戏元素使用稳定枚举或键值持久化，不使用本地化显示文本作为配置 ID。
- OCR 图标放在 `BetterBTD/Assets/OcrIcons/`，文件名应与代码中的稳定标识一致。
- 坐标规则默认基于 `1920 × 1080`，修改后需要验证缩放和非 16:9 警告路径。
- 不提交 `bin/`、`obj/`、本地配置、用户脚本或诊断日志。

仓库中的 `.github/skills/` 记录了 WPF 页面布局、控件、主题和交互约定。修改相关 UI 前应阅读对应说明。

## 提交前检查

```powershell
dotnet build BetterBTD.slnx -c Release /p:Platform=x64
dotnet test BetterBTD.slnx -c Release --no-build /p:Platform=x64
git diff --check
git status --short
```

涉及识图或自动任务时，还应在真实 BTD6 窗口中验证：

- `1920 × 1080` 的基准路径
- 中文和英文游戏界面
- 截图器重启与手动选窗
- 任务停止、取消和失败恢复
- 诊断日志是否能定位失败阶段

## 发布流程

发布由 `.github/workflows/release.yml` 完成：

1. 推送形如 `v1.2.3` 或 `v1.2.3-beta.1` 的标签。
2. GitHub Actions 恢复、构建并测试 Release/x64。
3. `dotnet publish` 生成依赖 .NET 8 Desktop Runtime 的 `win-x64` 应用。
4. Kachina Builder 打包 `BetterBTD.Install.exe` 和内置更新器。
5. 工作流创建 GitHub Release、生成发行说明并上传安装包。

版本号来自 Git 标签；不要只修改项目文件中的默认 `Version` 后手工发布。

## 提交问题与贡献

Issue 至少应包含：

- BetterBTD 版本和提交号
- Windows 与 BTD6 版本
- 游戏分辨率、语言、捕获模式和输入模式
- 最小复现步骤
- `User\Logs\` 中相关日志；上传前移除个人信息

Pull Request 应保持单一目的，补充与风险相匹配的测试，并说明真实游戏验证范围。涉及脚本格式、绑定文件或 HTTP 协议的破坏性变更，需要同步更新对应开发者文档。

返回 [开发者文档](README.md) · [项目首页](../../README.md)
