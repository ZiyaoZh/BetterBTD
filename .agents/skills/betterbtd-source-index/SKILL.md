---
name: betterbtd-source-index
description: Navigate and explain the BetterBTD source tree before making code changes. Use when an agent needs to locate the right C#, XAML, test, platform-library, Python, or PowerShell file; understand ownership and dependency flow; choose related tests; or update the project source index after files move or new modules are added.
---

# BetterBTD Source Index

Use this skill as the repository map for source-location questions and code changes. Read [the source index](references/source-index.md) before searching the repository broadly; it contains the current project map, architectural entry points, directory responsibilities, and a file-level inventory.

## Navigate A Task

1. Classify the request by concern: WPF presentation, ViewModel orchestration, application service, core runtime, model/configuration, platform library, test, or external tooling.
2. Open the matching section in `references/source-index.md`, then inspect the listed entry points and their neighboring files.
3. Verify the current file contents and registrations before editing. The index is a navigation aid, not a substitute for reading the implementation or the developer documentation.
4. Select tests from the matching `BetterBTD.Tests` area. For behavior that crosses a public boundary, also read the related protocol or architecture document under `docs/developer/`.
5. After adding, removing, or moving maintainable source files, refresh the checked-in inventory from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .agents\skills\betterbtd-source-index\scripts\update-source-index.ps1
```

Do not hand-edit the generated file. Review the generated diff and keep the index update with the source change that caused it.

## Use The Architecture Map

- `BetterBTD\Views` and `BetterBTD\ViewModels` own presentation and page state. Keep business behavior in services or core code, not XAML code-behind.
- `BetterBTD\Services` owns capture, recognition, persistence, settings, localization, update, editor, task adapters, and protocol boundaries.
- `BetterBTD\Core` owns UI-independent script execution, automatic-task orchestration, input simulation, robot control, leases, and the Test API coordinator.
- `BetterBTD\Models` owns persisted configuration, script documents, game-element catalogs, task DTOs, and runtime contracts.
- `Fischless.*` projects are reusable Windows platform libraries. Change them only when the behavior belongs below the BetterBTD application layer.
- `BetterBTD.Tests` mirrors the core/service boundaries and is the first place to look for regression coverage.
- `tools\BetterBTD.GameDriver` and `tools\BetterBTD.ScriptTest` are external Python/PowerShell black-box tooling. They must remain independent from BetterBTD runtime assemblies and preserve the input-ownership and external-Oracle rules in their developer documentation.

## Follow Common Flows

- Startup and shell: `Program.cs` -> `App.xaml.cs` -> `MainWindow.xaml(.cs)` -> `ViewModels\MainWindowViewModel.cs` and `Views\Pages`.
- Capture and recognition: `Services\Start\Capture` -> `Services\Tasks\CaptureAnalysis` -> `Models\GameElements` / task models.
- Script editing and execution: `Services\MyScripts` and `Services\Editor` -> `Core\ScriptExecution` -> `Services\Tasks\ScriptExecution` adapters and input services.
- Automatic tasks: `Core\AutoTasks` contracts/strategies -> `Services\Tasks\AutoTasks` runtime adapters -> capture analysis and script execution.
- Robot/Test API: `Core\RobotControl` or `Core\TestApi` -> `Services\Tasks\RobotControl` / `Services\Tasks\TestApi` -> HTTP or external test tooling.

When a request touches capture, input, automatic tasks, scripts, or the Test API, preserve shared-resource serialization and re-observe UI state after actions. Read the relevant `docs/developer/` protocol before changing a boundary.

## Keep The Index Fresh

The inventory is generated from tracked source-like files and excludes build output, IDE state, virtual environments, visual baseline fixtures, and other generated artifacts. Run the updater after structural changes, then run the skill validator:

```powershell
python C:\Users\95889\.codex\skills\.system\skill-creator\scripts\quick_validate.py .agents\skills\betterbtd-source-index
```

The index intentionally contains path and symbol summaries rather than full source. Always use the repository files as the authority for implementation details.
