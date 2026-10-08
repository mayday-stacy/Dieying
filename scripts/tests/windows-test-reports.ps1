#Requires -Version 5.1
# Exercises the production TRX checks without loading .NET test assemblies or
# changing Windows security settings. Run with powershell -File this-script.ps1.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot '../windows.ps1'
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile(
    [IO.Path]::GetFullPath($source), [ref] $tokens, [ref] $parseErrors)
if ($parseErrors.Count -ne 0) { throw ($parseErrors | Out-String) }
# Load only the functions under test: importing windows.ps1 itself would start
# its default Build action and model checks.
foreach ($name in @('Assert-TestReport', 'Invoke-WindowsTests')) {
    $definition = $ast.Find({ param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true)
    if (-not $definition) { throw "Missing production function: $name" }
    . ([scriptblock]::Create($definition.Extent.Text))
}

$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$fixtureRoot = Join-Path $workspace "artifacts/script-tests/$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
$report = Join-Path $fixtureRoot 'fixture.trx'
$verified = New-Object 'System.Collections.Generic.List[string]'

function Write-FixtureTrx([string] $Path, [hashtable] $Overrides = @{},
        [string[]] $Results = @('Passed', 'NotExecuted'), [string] $Outcome = 'Completed') {
    $counts = [ordered]@{
        total = 2; executed = 1; passed = 1; failed = 0; error = 0; timeout = 0
        aborted = 0; notRunnable = 0; notExecuted = 1; disconnected = 0
    }
    foreach ($key in $Overrides.Keys) { $counts[$key] = $Overrides[$key] }
    $attributes = ($counts.Keys | ForEach-Object { "$_=`"$($counts[$_])`"" }) -join ' '
    $resultXml = ($Results | ForEach-Object { "<UnitTestResult outcome=`"$_`" />" }) -join ''
    $xml = "<TestRun xmlns=`"http://microsoft.com/schemas/VisualStudio/TeamTest/2010`"><Results>$resultXml</Results><ResultSummary outcome=`"$Outcome`"><Counters $attributes /></ResultSummary></TestRun>"
    [IO.File]::WriteAllText($Path, $xml)
}

function Expect-Failure([string] $Name, [scriptblock] $Run, [string] $MessagePattern) {
    $failure = $null
    try { & $Run | Out-Null } catch { $failure = $_ }
    if (-not $failure -or $failure.Exception.Message -notmatch $MessagePattern) {
        throw "$Name did not fail as expected ($MessagePattern). Actual: $failure"
    }
    $verified.Add($Name)
}

Write-FixtureTrx $report
$counts = Assert-TestReport $report 0
if ($counts.passed -ne 1 -or $counts.notExecuted -ne 1) { throw 'Valid counts were not retained.' }
$verified.Add('valid passing report with a skipped test')
Expect-Failure 'nonzero process exit' { Assert-TestReport $report 3 } 'exit.*3'
Expect-Failure 'missing report' { Assert-TestReport (Join-Path $fixtureRoot 'missing.trx') 0 } 'No TRX'
Write-FixtureTrx $report @{ total = 0; executed = 0; passed = 0; notExecuted = 0 } @()
Expect-Failure 'zero tests even with exit zero' { Assert-TestReport $report 0 } 'Zero tests'
foreach ($field in @('failed', 'error', 'timeout', 'aborted', 'notRunnable', 'disconnected')) {
    Write-FixtureTrx $report @{ $field = 1 }
    Expect-Failure "nonzero $field counter" { Assert-TestReport $report 0 } $field
}
Write-FixtureTrx $report @{} @('Passed', 'NotExecuted') 'Aborted'
Expect-Failure 'aborted summary' { Assert-TestReport $report 0 } 'did not complete'
Write-FixtureTrx $report @{} @('Passed')
Expect-Failure 'incomplete individual results' { Assert-TestReport $report 0 } 'individual results'
Write-FixtureTrx $report @{ executed = 'bad' }
Expect-Failure 'invalid counter' { Assert-TestReport $report 0 } 'invalid.*executed'
[IO.File]::WriteAllText($report, '<TestRun>')
Expect-Failure 'malformed XML' { Assert-TestReport $report 0 } 'end of file|unclosed|unexpected|Load'

# Test the actual orchestration too: the fake runner writes exactly what the
# command's --results-directory requests. It does not launch any application.
$repoRoot = Join-Path $fixtureRoot 'fake-repository'
$Configuration = 'Release'
$dotnetPath = 'Invoke-FakeDotNet'
$script:scenario = 'success'
$script:projectsRun = @()
function Invoke-FakeDotNet {
    $arguments = @($args)
    $project = [IO.Path]::GetFileNameWithoutExtension($arguments[1])
    $script:projectsRun += $project
    $directoryIndex = [array]::IndexOf($arguments, '--results-directory')
    if ($directoryIndex -lt 0) { throw 'No explicit report directory.' }
    $path = Join-Path $arguments[$directoryIndex + 1] 'results.trx'
    $global:LASTEXITCODE = 0
    if ($script:scenario -eq 'missing-app' -and $project -eq 'Composa.App.Tests') {
        Write-Output 'Simulated test host produced no report.'
        return
    }
    if ($script:scenario -eq 'core-zero' -and $project -eq 'Composa.Core.Tests') {
        Write-FixtureTrx $path @{ total = 0; executed = 0; passed = 0; notExecuted = 0 } @()
    } else {
        Write-FixtureTrx $path
    }
    if ($script:scenario -eq 'process-failure' -and $project -eq 'Composa.App.Tests') {
        $global:LASTEXITCODE = 7
    }
    Write-Output "Simulated $project output for $script:scenario."
}
Invoke-WindowsTests
$verified.Add('both projects pass')
$firstRun = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'artifacts/windows-tests') -Directory)
Invoke-WindowsTests
$allRuns = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'artifacts/windows-tests') -Directory)
if ($firstRun.Count -ne 1 -or $allRuns.Count -ne 2) { throw 'Runs reused a report directory.' }
$verified.Add('successive runs have separate directories')
foreach ($scenarioName in @('core-zero', 'missing-app', 'process-failure')) {
    $script:scenario = $scenarioName
    $script:projectsRun = @()
    Expect-Failure "orchestration rejects $scenarioName" { Invoke-WindowsTests } 'verification failed.*|Log:'
    if (($script:projectsRun -join ',') -ne 'Composa.Core.Tests,Composa.App.Tests') {
        throw 'An early failure prevented running the other project.'
    }
}
$logs = @(Get-ChildItem -LiteralPath $repoRoot -Filter console.log -Recurse -File)
if ($logs.Count -ne 10 -or @($logs | Where-Object { $_.Length -eq 0 }).Count -ne 0) {
    throw 'A project console log is missing or empty.'
}
$verified.Add('all project console logs retained')
$verified | ForEach-Object { Write-Host "PASS: $_" }
Write-Host "$($verified.Count) checks passed. Fixtures and logs: $fixtureRoot"
# The final fixture deliberately sets the native exit code to 7. Do not let the
# GitHub Actions PowerShell wrapper mistake that simulated failure for ours.
$global:LASTEXITCODE = 0
