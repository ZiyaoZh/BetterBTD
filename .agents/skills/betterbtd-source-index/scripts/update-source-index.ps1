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

function Get-DocumentKey {
    param([pscustomobject]$Entry)

    if ($Entry.Root -eq 'Repository') { return 'repository' }
    if ($Entry.Root -eq 'BetterBTD.Tests') { return 'betterbtd-tests' }
    if ($Entry.Root -like 'Fischless.*') { return 'fischless-libraries' }
    if ($Entry.Root -eq 'GameDriver tooling') { return 'game-driver-tooling' }
    if ($Entry.Root -eq 'ScriptTest tooling') { return 'script-test-tooling' }

    if ($Entry.Root -eq 'BetterBTD') {
        if ($Entry.Path -match '^BetterBTD/Core/' -or $Entry.Path -match '^BetterBTD/Helpers/') {
            return 'betterbtd-core'
        }
        if ($Entry.Path -match '^BetterBTD/Models/') { return 'betterbtd-models' }
        if ($Entry.Path -match '^BetterBTD/Services/') { return 'betterbtd-services' }
        if ($Entry.Path -match '^BetterBTD/Views/' -or $Entry.Path -match '^BetterBTD/ViewModels/') {
            return 'betterbtd-presentation'
        }
        return 'betterbtd-application'
    }

    throw "No source-index document mapping for $($Entry.Root): $($Entry.Path)"
}

$documentDefinitions = [ordered]@{
    'repository' = [pscustomobject]@{
        Title = 'Repository Metadata'
        FileName = 'repository.md'
        Summary = 'Solution, platform, and package-version entry points.'
        RelatedKeys = @('betterbtd-application', 'betterbtd-tests')
    }
    'betterbtd-application' = [pscustomobject]@{
        Title = 'BetterBTD Application and Assets'
        FileName = 'betterbtd-application.md'
        Summary = 'Application startup files, project definition, and source data assets.'
        RelatedKeys = @('repository', 'betterbtd-presentation', 'betterbtd-services')
    }
    'betterbtd-core' = [pscustomobject]@{
        Title = 'BetterBTD Core and Helpers'
        FileName = 'betterbtd-core.md'
        Summary = 'UI-independent execution, automatic tasks, simulation, control leases, and helpers.'
        RelatedKeys = @('betterbtd-models', 'betterbtd-services', 'betterbtd-tests')
    }
    'betterbtd-models' = [pscustomobject]@{
        Title = 'BetterBTD Models'
        FileName = 'betterbtd-models.md'
        Summary = 'Configuration, game elements, scripts, task contracts, and runtime DTOs.'
        RelatedKeys = @('betterbtd-core', 'betterbtd-services', 'betterbtd-tests')
    }
    'betterbtd-services' = [pscustomobject]@{
        Title = 'BetterBTD Services'
        FileName = 'betterbtd-services.md'
        Summary = 'Capture, recognition, persistence, settings, editor, task adapters, and protocols.'
        RelatedKeys = @('betterbtd-core', 'betterbtd-models', 'betterbtd-presentation', 'betterbtd-tests')
    }
    'betterbtd-presentation' = [pscustomobject]@{
        Title = 'BetterBTD Presentation'
        FileName = 'betterbtd-presentation.md'
        Summary = 'WPF pages, windows, controls, ViewModels, and presentation support.'
        RelatedKeys = @('betterbtd-application', 'betterbtd-models', 'betterbtd-services')
    }
    'betterbtd-tests' = [pscustomobject]@{
        Title = 'BetterBTD Tests'
        FileName = 'betterbtd-tests.md'
        Summary = 'xUnit behavior, compatibility, protocol, and test-double coverage.'
        RelatedKeys = @('betterbtd-core', 'betterbtd-models', 'betterbtd-services')
    }
    'fischless-libraries' = [pscustomobject]@{
        Title = 'Fischless Platform Libraries'
        FileName = 'fischless-libraries.md'
        Summary = 'Windows game capture, global hot-key, and input simulation libraries.'
        RelatedKeys = @('betterbtd-application', 'betterbtd-core')
    }
    'game-driver-tooling' = [pscustomobject]@{
        Title = 'BetterBTD Game Driver Tooling'
        FileName = 'game-driver-tooling.md'
        Summary = 'Independent Python and PowerShell tooling for observing and controlling a real BTD6 client.'
        RelatedKeys = @('script-test-tooling', 'betterbtd-tests')
    }
    'script-test-tooling' = [pscustomobject]@{
        Title = 'BetterBTD Script Test Tooling'
        FileName = 'script-test-tooling.md'
        Summary = 'Scenario validation and black-box orchestration around the Test API and Game Driver.'
        RelatedKeys = @('game-driver-tooling', 'betterbtd-tests')
    }
}

foreach ($documentKey in $documentDefinitions.Keys) {
    $documentDefinitions[$documentKey] | Add-Member -NotePropertyName Key -NotePropertyValue $documentKey
}

foreach ($entry in $entries) {
    $entry | Add-Member -NotePropertyName DocumentKey -NotePropertyValue (Get-DocumentKey $entry)
}

function Get-DocumentEntries {
    param([string]$DocumentKey)

    return @($entries | Where-Object { $_.DocumentKey -eq $DocumentKey } | Sort-Object Root, Path)
}

function Write-MarkdownFile {
    param(
        [string]$Path,
        [System.Collections.Generic.List[string]]$Lines
    )

    while ($Lines.Count -gt 0 -and [string]::IsNullOrEmpty($Lines[$Lines.Count - 1])) {
        $Lines.RemoveAt($Lines.Count - 1)
    }

    $outputDirectory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    [System.IO.File]::WriteAllText($Path, ($Lines -join [Environment]::NewLine) + [Environment]::NewLine, $utf8NoBom)
}

$referenceDirectory = Split-Path -Parent $outputPath
$documentDirectory = Join-Path $referenceDirectory 'source-index'
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)

$mainLines = [System.Collections.Generic.List[string]]::new()
$mainLines.Add('# BetterBTD Source Index')
$mainLines.Add('')
$mainLines.Add('Generated by `.agents/skills/betterbtd-source-index/scripts/update-source-index.ps1`. This page is the entry point; detailed inventories live in the linked documents below.')
$mainLines.Add('')
$mainLines.Add("The index covers **$($entries.Count)** maintainable source-like files. Read only the child document that matches the current task to keep context focused.")
$mainLines.Add('')
$mainLines.Add('## Document Map')
$mainLines.Add('')
$mainLines.Add('| Document | Scope | Files |')
$mainLines.Add('| --- | --- | ---: |')
foreach ($definition in $documentDefinitions.Values) {
    $documentEntries = @(Get-DocumentEntries $definition.Key)
    $link = 'source-index/' + $definition.FileName
    $mainLines.Add(('| [{0}]({1}) | {2} | {3} |' -f $definition.Title, $link, $definition.Summary, $documentEntries.Count))
}
$mainLines.Add('')
$mainLines.Add('Every child document links back here and to related boundaries. Source-file links inside child documents point to the repository root.')
$mainLines.Add('')
$mainLines.Add('## Quick Navigation')
$mainLines.Add('')
$mainLines.Add('| Task | Start with | Continue with |')
$mainLines.Add('| --- | --- | --- |')
$mainLines.Add('| Application startup and shell | [Application](source-index/betterbtd-application.md), [Presentation](source-index/betterbtd-presentation.md) | `BetterBTD/Program.cs`, `BetterBTD/App.xaml.cs`, `BetterBTD/MainWindow.xaml(.cs)` |')
$mainLines.Add('| Target-window capture | [Services](source-index/betterbtd-services.md) | [Models](source-index/betterbtd-models.md), `BetterBTD/Services/Tasks/CaptureAnalysis/` |')
$mainLines.Add('| Script editing and execution | [Services](source-index/betterbtd-services.md), [Core](source-index/betterbtd-core.md) | [Models](source-index/betterbtd-models.md), [Tests](source-index/betterbtd-tests.md) |')
$mainLines.Add('| Automatic tasks and input ownership | [Core](source-index/betterbtd-core.md), [Services](source-index/betterbtd-services.md) | [Models](source-index/betterbtd-models.md), [Fischless libraries](source-index/fischless-libraries.md) |')
$mainLines.Add('| Robot or Test API | [Core](source-index/betterbtd-core.md), [Services](source-index/betterbtd-services.md) | [Tests](source-index/betterbtd-tests.md), [Script Test tooling](source-index/script-test-tooling.md) |')
$mainLines.Add('| WPF pages and controls | [Presentation](source-index/betterbtd-presentation.md) | [Application](source-index/betterbtd-application.md), [Services](source-index/betterbtd-services.md) |')
$mainLines.Add('| External black-box testing | [Game Driver tooling](source-index/game-driver-tooling.md), [Script Test tooling](source-index/script-test-tooling.md) | `docs/developer/game-driver.md`, `docs/developer/script-test-scenario.md` |')
$mainLines.Add('| Unit and regression tests | [Tests](source-index/betterbtd-tests.md) | Match the test directory to the production boundary |')
$mainLines.Add('')
$mainLines.Add('## Layer Map')
$mainLines.Add('')
$mainLines.Add('```text')
$mainLines.Add('Views (XAML / Window / Page)')
$mainLines.Add('        | binding')
$mainLines.Add('ViewModels (UI state and commands)')
$mainLines.Add('        |')
$mainLines.Add('Services (capture, recognition, storage, settings, protocols)')
$mainLines.Add('        |')
$mainLines.Add('Core (script execution, automatic tasks, simulators)')
$mainLines.Add('        |')
$mainLines.Add('Windows and BTD6')
$mainLines.Add('```')
$mainLines.Add('')
$mainLines.Add('## Navigation Rules')
$mainLines.Add('')
$mainLines.Add('- Start from this page, then load one child inventory that matches the concern.')
$mainLines.Add('- Verify the linked source file and its registrations before editing; the index is not an implementation authority.')
$mainLines.Add('- For capture, input, automatic tasks, scripts, or Test API changes, read the matching `docs/developer/` protocol and preserve shared-resource serialization.')
$mainLines.Add('- Run the updater after adding, removing, or moving maintainable source files.')
Write-MarkdownFile $outputPath $mainLines

foreach ($definition in $documentDefinitions.Values) {
    $documentEntries = @(Get-DocumentEntries $definition.Key)
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("# $($definition.Title)")
    $lines.Add('')
    $lines.Add("$($definition.Summary)")
    $lines.Add('')
    $lines.Add('[Back to BetterBTD Source Index](../source-index.md)')
    $lines.Add('')
    $lines.Add('## Related Indexes')
    $lines.Add('')
    foreach ($relatedKey in $definition.RelatedKeys) {
        $related = $documentDefinitions[$relatedKey]
        $lines.Add(('- [{0}](./{1})' -f $related.Title, $related.FileName))
    }
    if ($definition.Key -eq 'game-driver-tooling') {
        $lines.Add('- [Game Driver developer protocol](../../../../../docs/developer/game-driver.md)')
    }
    if ($definition.Key -eq 'script-test-tooling') {
        $lines.Add('- [Script Test scenario protocol](../../../../../docs/developer/script-test-scenario.md)')
        $lines.Add('- [Test API protocol](../../../../../docs/developer/test-api.md)')
    }
    $lines.Add('')
    $lines.Add('## Directory Summary')
    $lines.Add('')
    $lines.Add('| Directory | Files |')
    $lines.Add('| --- | ---: |')
    foreach ($directoryGroup in ($documentEntries | Group-Object Directory | Sort-Object Name)) {
        $directory = $directoryGroup.Name
        if ([string]::IsNullOrWhiteSpace($directory)) { $directory = '(root)' }
        $lines.Add(('| `{0}` | {1} |' -f $directory, $directoryGroup.Count))
    }
    $lines.Add('')
    $lines.Add('## File Inventory')
    $lines.Add('')
    $lines.Add('Paths are relative to the repository root. Open the linked file and verify current behavior before editing.')
    $lines.Add('')
    foreach ($directoryGroup in ($documentEntries | Group-Object Directory | Sort-Object Name)) {
        $directory = $directoryGroup.Name
        if ([string]::IsNullOrWhiteSpace($directory)) { $directory = '(root)' }
        $lines.Add(('### `{0}`' -f $directory))
        $lines.Add('')
        $lines.Add('| File | Description |')
        $lines.Add('| --- | --- |')
        foreach ($entry in ($directoryGroup.Group | Sort-Object Path)) {
            $link = '../../../../../' + $entry.Path
            $fileName = [System.IO.Path]::GetFileName($entry.Path)
            $escapedDescription = $entry.Description.Replace('|', '\|')
            $lines.Add(('| [{0}]({1}) | {2} |' -f $fileName, $link, $escapedDescription))
        }
        $lines.Add('')
    }
    Write-MarkdownFile (Join-Path $documentDirectory $definition.FileName) $lines
}

Write-Output "Wrote $($entries.Count) source entries across $($documentDefinitions.Count) linked documents to $referenceDirectory"
