[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$site = Join-Path $repository 'docs/site'
$html = Get-Content (Join-Path $site 'index.html') -Raw -Encoding UTF8
$ids = @([regex]::Matches($html, '\bid="([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
$duplicateIds = @($ids | Group-Object | Where-Object Count -gt 1)
if ($duplicateIds.Count -gt 0) { throw "Duplicate HTML IDs: $($duplicateIds.Name -join ', ')" }

foreach ($match in [regex]::Matches($html, '(?:href|src)="([^"]+)"')) {
    $target = $match.Groups[1].Value
    if ($target.StartsWith('#')) {
        if ($ids -cnotcontains $target.Substring(1)) { throw "Missing tutorial anchor: $target" }
    }
    elseif ($target -match '^https?://') {
        $uri = $null
        if (-not [Uri]::TryCreate($target, [UriKind]::Absolute, [ref]$uri)) { throw "Invalid URL: $target" }
    }
    elseif (-not (Test-Path -LiteralPath (Join-Path $site $target) -PathType Leaf)) {
        throw "Missing site asset: $target"
    }
}

# Every editor command must have exactly one explanation, including non-executable markers.
$model = Get-Content (Join-Path $repository 'BetterBTD/Models/ScriptEditor/ScriptInstructionModels.cs') -Raw
$enum = [regex]::Match($model, '(?s)public enum ScriptCommandType\s*\{([^}]+)\}').Groups[1].Value
$commands = @($enum -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$documented = @([regex]::Matches($html, 'data-command="([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
if ($commands.Count -eq 0 -or $documented.Count -ne $commands.Count) { throw 'Instruction coverage count differs from ScriptCommandType.' }
foreach ($command in $commands) {
    if (@($documented | Where-Object { $_ -ceq $command }).Count -ne 1) { throw "Missing or duplicate instruction: $command" }
}

$links = Get-Content (Join-Path $repository 'BetterBTD/Helpers/DocumentationLinks.cs') -Raw
foreach ($match in [regex]::Matches($links, '=>\s*"([a-z-]+)"')) {
    if ($ids -cnotcontains $match.Groups[1].Value) { throw "Application link has no tutorial section: $($match.Groups[1].Value)" }
}
$tutorialHomeUrl = [regex]::Match($links, 'Home\s*=\s*"([^"]+)"').Groups[1].Value
if ($tutorialHomeUrl -cne 'https://ziyaozh.github.io/BetterBTD/') { throw 'Review the Pages address and tutorial entry links together.' }
if ($html -match '<!-- (instruction-reference|task-sections|reference-sources) -->') { throw 'Unfinished tutorial placeholder.' }
Write-Host "Tutorial verified: $($ids.Count) unique IDs, $($commands.Count) instructions, all local assets and application anchors exist."
