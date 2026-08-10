[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$RepositoryRoot,

    [Parameter(Mandatory = $false)]
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$repository = (Resolve-Path $RepositoryRoot).Path.TrimEnd('\', '/')
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repository '.agents\skills\betterbtd-source-index\references\source-index.md'
}

if (-not [System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath = Join-Path $repository $OutputPath
}
$outputPath = [System.IO.Path]::GetFullPath($OutputPath)

$sourceExtensions = @(
    '.cs', '.xaml', '.csproj', '.props', '.targets',
    '.py', '.ps1', '.json', '.schema', '.txt', '.md'
)
$excludedDirectoryNames = @(
    'bin', 'obj', '.vs', '.venv', '__pycache__', 'artifacts',
    'visual-baselines', 'templates', 'samples'
)

$roots = @(
    [pscustomobject]@{ Name = 'BetterBTD'; RelativePath = 'BetterBTD'; Kind = 'application' },
    [pscustomobject]@{ Name = 'BetterBTD.Tests'; RelativePath = 'BetterBTD.Tests'; Kind = 'tests' },
    [pscustomobject]@{ Name = 'Fischless.GameCapture'; RelativePath = 'Fischless.GameCapture'; Kind = 'platform' },
    [pscustomobject]@{ Name = 'Fischless.HotkeyCapture'; RelativePath = 'Fischless.HotkeyCapture'; Kind = 'platform' },
    [pscustomobject]@{ Name = 'Fischless.WindowsInput'; RelativePath = 'Fischless.WindowsInput'; Kind = 'platform' },
    [pscustomobject]@{ Name = 'GameDriver tooling'; RelativePath = 'tools\BetterBTD.GameDriver'; Kind = 'tooling' },
    [pscustomobject]@{ Name = 'ScriptTest tooling'; RelativePath = 'tools\BetterBTD.ScriptTest'; Kind = 'tooling' }
)

function Test-IncludedFile {
    param([System.IO.FileInfo]$File)

    if ($sourceExtensions -notcontains $File.Extension.ToLowerInvariant()) {
        return $false
    }

    $relative = $File.FullName.Substring($repository.Length).TrimStart('\', '/')
    foreach ($segment in ($relative -split '[\\/]')) {
        if ($excludedDirectoryNames -contains $segment) {
            return $false
        }
    }

    return $true
}

function Get-RepositoryRelativePath {
    param([string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $prefix = $repository + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside the repository: $Path"
    }

    return $fullPath.Substring($prefix.Length) -replace '\\', '/'
}

function Get-RoleDescription {
    param(
        [string]$RelativePath,
        [string]$Extension,
        [string]$RootKind
    )

    $normalizedPath = '/' + ($RelativePath -replace '\\', '/')
    if ($RootKind -eq 'tests') {
        return 'xUnit behavior, regression, protocol, or test-double code'
    }
    if ($RootKind -eq 'tooling') {
        if ($Extension -eq '.py') { return 'External black-box tool implementation or test' }
        if ($Extension -eq '.ps1') { return 'External tool entry point, setup, or test script' }
        if ($Extension -eq '.json') { return 'External tool scenario, schema, catalog, or test data' }
        if ($Extension -eq '.md') { return 'External tool developer documentation' }
        return 'External tool configuration or runtime input'
    }

    $roles = [ordered]@{
        '/Core/AutoTasks/Strategies/' = 'Concrete automatic-task strategy'
        '/Core/AutoTasks/' = 'Automatic-task coordination, sessions, and registry'
        '/Core/GameControl/' = 'Shared game-control lease and input ownership'
        '/Core/RobotControl/' = 'Robot-control actions, registry, and coordination'
        '/Core/ScriptExecution/Handlers/' = 'Script instruction handlers and support code'
        '/Core/ScriptExecution/' = 'UI-independent script execution, sessions, and scheduling'
        '/Core/Simulator/' = 'Windows mouse/keyboard simulation and message dispatch'
        '/Core/TestApi/' = 'Internal black-box test-control coordinator'
        '/Core/Config/' = 'Core hot-key and key-binding configuration'
        '/Helpers/' = 'Cross-layer helpers, extensions, paths, and platform adapters'
        '/Models/AutoTasks/' = 'Automatic-task configuration and runtime models'
        '/Models/GameElements/' = 'Stable game-element, map, hero, and tower identifiers'
        '/Models/MyScripts/' = 'Script-library and script-document models'
        '/Models/ScriptEditor/' = 'Script-editor input and presentation models'
        '/Models/ScriptExecution/' = 'Script instruction, log, and session models'
        '/Models/TestApi/' = 'Test API request, response, and operation models'
        '/Models/' = 'Application, task, input, and tool data models'
        '/Services/Start/Capture/' = 'Target-window discovery, capture sessions, and capture diagnostics'
        '/Services/Start/' = 'Startup flow and start-page services'
        '/Services/Tasks/CaptureAnalysis/' = 'Screenshot analysis, OCR, matching, and game-state recognition'
        '/Services/Tasks/Input/' = 'Script input, hardware input, and coordinate transforms'
        '/Services/Tasks/AutoTasks/' = 'Automatic-task UI state, navigation, actions, and adapters'
        '/Services/Tasks/ScriptExecution/' = 'Services adapted for the core script executor'
        '/Services/Tasks/RobotControl/' = 'Robot-control service adapters'
        '/Services/Tasks/TestApi/' = 'Test API HTTP transport and service adapters'
        '/Services/Tasks/' = 'Services used by automated task runtimes'
        '/Services/MyScripts/' = 'Script documents, library, compatibility conversion, and bindings'
        '/Services/Editor/' = 'Script-editor instruction, option, and sequence services'
        '/Services/Shell/Localization/' = 'Localization resources and display text'
        '/Services/Shell/' = 'Application shell, navigation, and shared UI services'
        '/Services/Settings/' = 'Configuration, theme, and device settings'
        '/Services/Tools/' = 'Round, hero, collection, save, and other tool services'
        '/Services/Updates/' = 'Application update checks and downloads'
        '/Services/Shared/' = 'Services shared across pages and task flows'
        '/Services/Diagnostics/' = 'Capture and runtime diagnostics'
        '/Services/' = 'Application services and infrastructure'
        '/ViewModels/' = 'Page, window, and tool binding state and commands'
        '/Views/Pages/' = 'WPF pages and page-level code-behind'
        '/Views/Controls/' = 'Reusable WPF controls, behaviors, converters, and styles'
        '/Views/Windows/' = 'WPF windows, dialogs, and overlays'
        '/Views/' = 'WPF views and interface components'
    }

    foreach ($entry in $roles.GetEnumerator()) {
        if ($normalizedPath.IndexOf($entry.Key, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return $entry.Value
        }
    }

    switch ($Extension) {
        '.xaml' { return 'WPF view, window, control, or resource definition' }
        '.csproj' { return 'MSBuild project definition' }
        '.props' { return 'Shared MSBuild properties or package versions' }
        '.targets' { return 'MSBuild target definition' }
        '.slnx' { return 'Solution and platform configuration' }
        '.json' { return 'Application data, catalog, or configuration' }
        default { return 'Application source code' }
    }
}

function Get-DeclaredSymbols {
    param([System.IO.FileInfo]$File)

    $text = Get-Content -LiteralPath $File.FullName -Raw -ErrorAction SilentlyContinue
    if ([string]::IsNullOrWhiteSpace($text)) {
        return @()
    }

    $symbols = [System.Collections.Generic.List[string]]::new()
    if ($File.Extension -eq '.cs') {
        $matches = [regex]::Matches($text, '(?m)\b(?:class|record|struct|interface|enum|delegate)\s+([A-Za-z_][A-Za-z0-9_]*)')
        foreach ($match in $matches) {
            if (-not $symbols.Contains($match.Groups[1].Value)) { $symbols.Add($match.Groups[1].Value) }
            if ($symbols.Count -ge 5) { break }
        }
    }
    elseif ($File.Extension -eq '.xaml') {
        $match = [regex]::Match($text, 'x:Class\s*=\s*"([^"]+)"')
        if ($match.Success) { $symbols.Add($match.Groups[1].Value) }
        $rootMatch = [regex]::Match($text, '<(?:[\w]+:)?([A-Za-z]+)\b')
        if ($rootMatch.Success -and -not $symbols.Contains($rootMatch.Groups[1].Value)) { $symbols.Add($rootMatch.Groups[1].Value) }
    }
    elseif ($File.Extension -eq '.py') {
        $matches = [regex]::Matches($text, '(?m)^\s*(?:async\s+)?def\s+([A-Za-z_][A-Za-z0-9_]*)')
        foreach ($match in $matches) {
            if (-not $symbols.Contains($match.Groups[1].Value)) { $symbols.Add($match.Groups[1].Value) }
            if ($symbols.Count -ge 5) { break }
        }
    }
    elseif ($File.Extension -eq '.ps1') {
        $matches = [regex]::Matches($text, '(?im)^\s*function\s+([A-Za-z_][A-Za-z0-9_-]*)')
        foreach ($match in $matches) {
            if (-not $symbols.Contains($match.Groups[1].Value)) { $symbols.Add($match.Groups[1].Value) }
            if ($symbols.Count -ge 5) { break }
        }
    }

    return $symbols.ToArray()
}

function Get-FileDescription {
    param(
        [System.IO.FileInfo]$File,
        [string]$RelativePath,
        [string]$RootKind
    )

    $role = Get-RoleDescription -RelativePath $RelativePath -Extension $File.Extension.ToLowerInvariant() -RootKind $RootKind
    $symbols = @(Get-DeclaredSymbols -File $File)
    if ($symbols.Count -gt 0) {
        return "$role; primary symbols: $($symbols -join ', ')"
    }

    return $role
}

$entries = [System.Collections.Generic.List[object]]::new()
foreach ($root in $roots) {
    $rootPath = Join-Path $repository $root.RelativePath
    if (-not (Test-Path -LiteralPath $rootPath -PathType Container)) { continue }

    foreach ($file in (Get-ChildItem -LiteralPath $rootPath -Recurse -File -Force | Where-Object { Test-IncludedFile $_ })) {
        $relativePath = Get-RepositoryRelativePath -Path $file.FullName
        $directory = [System.IO.Path]::GetDirectoryName($relativePath)
        if ([string]::IsNullOrWhiteSpace($directory)) { $directory = '' }
        $entries.Add([pscustomobject]@{
            Root = $root.Name
            RootKind = $root.Kind
            Path = $relativePath
            Directory = ($directory -replace '\\', '/')
            Description = Get-FileDescription -File $file -RelativePath $relativePath -RootKind $root.Kind
        })
    }
}

foreach ($metadataPath in @('BetterBTD.slnx', 'Directory.Packages.props')) {
    $filePath = Join-Path $repository $metadataPath
    if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) { continue }

    $file = Get-Item -LiteralPath $filePath
    $entries.Add([pscustomobject]@{
        Root = 'Repository'
        RootKind = 'metadata'
        Path = Get-RepositoryRelativePath -Path $file.FullName
        Directory = ''
        Description = Get-FileDescription -File $file -RelativePath $metadataPath -RootKind 'metadata'
    })
}

$entries = @($entries | Sort-Object Root, Path)
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('# BetterBTD Source Index')
$lines.Add('')
$lines.Add('Generated by `.agents/skills/betterbtd-source-index/scripts/update-source-index.ps1`. This is an Agent navigation map, not a replacement for source, tests, or the protocol documents under `docs/developer/`.')
$lines.Add('')
$lines.Add("The index contains **$($entries.Count)** maintainable source-like files. Entries are sorted by project and path; C#, XAML, Python, and PowerShell entries include extracted primary symbols.")
$lines.Add('')
$lines.Add('## Quick Navigation')
$lines.Add('')
$lines.Add('| Task | Start here | Continue with |')
$lines.Add('| --- | --- | --- |')
$lines.Add('| Application startup and shell | `BetterBTD/Program.cs`, `BetterBTD/App.xaml.cs`, `BetterBTD/MainWindow.xaml(.cs)` | `BetterBTD/ViewModels/MainWindowViewModel.cs`, `BetterBTD/Views/Pages/` |')
$lines.Add('| Target-window capture | `BetterBTD/Services/Start/Capture/` | `BetterBTD/Services/Tasks/CaptureAnalysis/`, `BetterBTD/Models/GameWindowInfo.cs` |')
$lines.Add('| Game UI, OCR, and map recognition | `BetterBTD/Services/Tasks/CaptureAnalysis/` | `BetterBTD/Models/GameElements/`, `BetterBTD/Assets/` |')
$lines.Add('| Script editing and library | `BetterBTD/Services/MyScripts/`, `BetterBTD/Services/Editor/` | `BetterBTD/Models/MyScripts/`, `BetterBTD/Views/Pages/ScriptEditorPage.xaml` |')
$lines.Add('| Script execution | `BetterBTD/Core/ScriptExecution/` | `BetterBTD/Core/ScriptExecution/Handlers/`, `BetterBTD/Services/Tasks/ScriptExecution/` |')
$lines.Add('| Automatic tasks | `BetterBTD/Core/AutoTasks/` | `BetterBTD/Services/Tasks/AutoTasks/`, `BetterBTD/Models/AutoTasks/` |')
$lines.Add('| Input and shared ownership | `BetterBTD/Core/GameControl/`, `BetterBTD/Services/Tasks/Input/` | `BetterBTD/Core/Simulator/`, `Fischless.WindowsInput/` |')
$lines.Add('| Robot or Test API | `BetterBTD/Core/RobotControl/`, `BetterBTD/Core/TestApi/` | `BetterBTD/Services/Tasks/RobotControl/`, `BetterBTD/Services/Tasks/TestApi/`, `BetterBTD/Models/TestApi/` |')
$lines.Add('| WPF pages and controls | `BetterBTD/Views/`, `BetterBTD/ViewModels/` | `BetterBTD/Services/Shell/Localization/` |')
$lines.Add('| External black-box tools | `tools/BetterBTD.GameDriver/`, `tools/BetterBTD.ScriptTest/` | `docs/developer/game-driver.md`, `docs/developer/script-test-scenario.md` |')
$lines.Add('| Unit and regression tests | `BetterBTD.Tests/` | Match the test directory to the production boundary |')
$lines.Add('')
$lines.Add('## Layer Map')
$lines.Add('')
$lines.Add('```text')
$lines.Add('Views (XAML / Window / Page)')
$lines.Add('        | binding')
$lines.Add('ViewModels (UI state and commands)')
$lines.Add('        |')
$lines.Add('Services (capture, recognition, storage, settings, protocols)')
$lines.Add('        |')
$lines.Add('Core (script execution, automatic tasks, simulators)')
$lines.Add('        |')
$lines.Add('Windows and BTD6')
$lines.Add('```')
$lines.Add('')
$lines.Add('| Project / directory | Responsibility |')
$lines.Add('| --- | --- |')
$lines.Add('| `BetterBTD/Core` | UI-independent script, task, input, robot, and test-control core |')
$lines.Add('| `BetterBTD/Services` | Capture, recognition, persistence, settings, editor, task adapters, and protocols |')
$lines.Add('| `BetterBTD/Models` | Configuration, scripts, tasks, game elements, and runtime DTOs |')
$lines.Add('| `BetterBTD/Views` / `ViewModels` | WPF views, page state, and commands |')
$lines.Add('| `BetterBTD.Tests` | xUnit behavior, protocol tests, and test doubles |')
$lines.Add('| `Fischless.*` | Windows capture, global hot-key, and input libraries |')
$lines.Add('| `tools/*` | External Game Driver and Script Test tools with no BetterBTD assembly reference |')
$lines.Add('')
$lines.Add('## Project Counts')
$lines.Add('')
$lines.Add('| Project | Files |')
$lines.Add('| --- | ---: |')
foreach ($group in ($entries | Group-Object Root)) {
    $lines.Add(('| `{0}` | {1} |' -f $group.Name, $group.Count))
}
$lines.Add('')
$lines.Add('## File Inventory')
$lines.Add('')
$lines.Add('Paths are relative to the repository root. Open the linked file and verify current behavior before editing.')
$lines.Add('')

foreach ($rootGroup in ($entries | Group-Object Root)) {
    $lines.Add("### $($rootGroup.Name)")
    $lines.Add('')
    foreach ($directoryGroup in ($rootGroup.Group | Group-Object Directory | Sort-Object Name)) {
        $directory = $directoryGroup.Name
        if ([string]::IsNullOrWhiteSpace($directory)) { $directory = '(root)' }
        $lines.Add(('#### `{0}`' -f $directory))
        $lines.Add('')
        $lines.Add('| File | Description |')
        $lines.Add('| --- | --- |')
        foreach ($entry in ($directoryGroup.Group | Sort-Object Path)) {
            $link = '../../../../' + $entry.Path
            $fileName = [System.IO.Path]::GetFileName($entry.Path)
            $escapedDescription = $entry.Description.Replace('|', '\|')
            $lines.Add(('| [{0}]({1}) | {2} |' -f $fileName, $link, $escapedDescription))
        }
        $lines.Add('')
    }
}

while ($lines.Count -gt 0 -and [string]::IsNullOrEmpty($lines[$lines.Count - 1])) {
    $lines.RemoveAt($lines.Count - 1)
}

$outputDirectory = Split-Path -Parent $outputPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($outputPath, ($lines -join [Environment]::NewLine) + [Environment]::NewLine, $utf8NoBom)
Write-Output "Wrote $($entries.Count) source entries to $outputPath"
