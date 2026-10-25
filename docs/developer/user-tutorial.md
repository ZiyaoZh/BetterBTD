# 网页版用户教程维护

教程站点地址为 <https://ziyaozh.github.io/BetterBTD/>，源码位于 [`docs/site/`](../site/index.html)。这是无构建依赖的静态站点，不依赖 CDN、运行时 Markdown 渲染或前端包管理器；直接打开 `index.html` 也能阅读，禁用 JavaScript 时目录和全部指令仍可用。JavaScript 仅提供章节搜索、深链接展开、阅读位置和打印展开。

## 本地预览与验证

直接在浏览器打开 `docs/site/index.html`，或在仓库根目录运行：

```powershell
python -m http.server 8080 --directory docs/site --bind 127.0.0.1
```

访问 <http://127.0.0.1:8080>。检查桌面/窄屏布局、深浅主题、键盘操作、搜索、全部任务深链接及打印。执行静态校验：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-tutorial.ps1
node --check docs/site/tutorial.js
git diff --check
```

校验脚本核对本地资源、重复 ID、所有页内跳转、编辑器指令覆盖与应用中的任务链接，不检查外部网站是否在线。

## 发布

仓库管理员需在 GitHub 的 **Settings → Pages → Build and deployment → Source** 选择 **GitHub Actions**。[`User tutorial` 工作流](../../.github/workflows/docs.yml)在教程变更推送到 `main` 后验证并发布，也支持手动运行；PR 只校验，不发布。首次启用需符合仓库 Pages 权限与环境审批设置。

本地提交和修改工作流不会自动启用 Pages，也不会推送或发布。发布后确认站点首页，以及 `#collection`、`#golden-bloon`、`#black-border`、`#farming`、`#race`、`#odyssey`、`#robot-control` 能访问，再发行带新入口的应用。默认站点地址依赖仓库所有者和仓库名，变更地址时同步修改 `DocumentationLinks.Home`、README、用户文档入口及校验脚本。

## 内容维护

- `index.html` 保存用户教程；`styles.css` 负责响应式和主题；`tutorial.js` 负责渐进增强。不要把开发者协议内容复制进普通用户的操作步骤。
- 修改脚本指令时，同时更新对应 `data-command` 条目；非执行指令（注释、自由游戏分界点）也必须覆盖。新增自动任务时保留稳定英文锚点，并更新 `DocumentationLinks` 映射。
- 先核对处理器、任务策略、UI 和测试，再与 Wiki 对照。Wiki 解释游戏机制，不能证明 BetterBTD 实现了某项能力；记录核查日期和已知版本差异。
- 当前已知边界包括：英雄工具未实现实际计算；模范度采用 PC v57.0 规则且未计入特殊活动的 Paragon Boost；竞速需手动进入；收集按专家图、金气球按新手图解析。这些实现变化后应同步更新教程。
- 当前网页正文为简体中文；中英文应用界面的“教程”入口都使用同一稳定地址。英文界面不会改变链接 ID。
- 网页以当前主分支源码为依据，没有针对每个历史发行版保存快照。更新教程时注明对游戏版本或旧版应用的影响，必要时同步维护 `docs/user/` 的简要说明。
