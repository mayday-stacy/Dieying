<#
.SYNOPSIS
Build, test, run, or publish the local Windows edition without Git Bash.
.EXAMPLE
.\scripts\windows.ps1 -Action Run
.EXAMPLE
.\scripts\windows.ps1 -Action Publish -Runtime win-x64 -Zip
.NOTES
Requires the .NET SDK selected by global.json (except Models). Models are verified
before each action; only MODNet, which is not in git, is downloaded. -SkipModels
allows offline development with the bundled models. Publish always includes all
three models and their notices. The application itself never downloads models.

Every action uses the local update channel. Update tests inject their channel and
mock releases explicitly, so a test build cannot contact the upstream release service.
Test writes a fresh artifacts/windows-tests directory for each run, retaining
console logs and TRX reports. Both projects must actually execute tests; a test
host blocked by Windows security cannot be reported as a successful test run.
Run isolates settings and recovery under artifacts/local-data unless the caller
already set DIEYING_DATA_DIR (or the legacy IMAGE_EDITOR_DEV_DATA_DIR). Publish creates a fresh portable directory each time
and optionally a zip; it does not build an installer or modify existing packages.
#>

#Requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('Build', 'Test', 'Run', 'Models', 'Publish')]
    [string] $Action = 'Build',

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [ValidateSet('win-x64', 'win-arm64')]
    [string] $Runtime = 'win-x64',

    [string] $OutputDirectory,
    [switch] $Zip,
    [switch] $SkipModels
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$appProject = Join-Path $repoRoot 'src/Composa.App/Composa.App.csproj'
$solution = Join-Path $repoRoot 'Composa.slnx'
$modelDirectory = Join-Path $repoRoot 'src/Composa.Core/Models'

if ($SkipModels -and $Action -in @('Models', 'Publish')) {
    throw '-SkipModels is only available for Build, Test and Run.'
}
if ($Zip -and $Action -ne 'Publish') {
    throw '-Zip is only available for Publish.'
}
if ($Action -eq 'Publish') {
    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
        $OutputDirectory = Join-Path $repoRoot 'dist'
    }
    # Resolve while still in the caller's directory, before entering the repo.
    $outputRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
}

# Keep these pins in sync with scripts/models/fetch.sh and the Vision catalogs.
# The two committed files are checked, never silently replaced from the network.
$models = @(
    @{
        File = 'u2netp.onnx'
        Sha256 = '309c8469258dda742793dce0ebea8e6dd393174f89934733ecc8b14c76f4ddd8'
        Bytes = 4574861
        Url = $null
    },
    @{
        File = 'realesr-general-x4v3.onnx'
        Sha256 = '09b757accd747d7e423c1d352b3e8f23e77cc5742d04bae958d4eb8082b76fa4'
        Bytes = 4871181
        Url = $null
    },
    @{
        File = 'modnet.onnx'
        Sha256 = '07c308cf0fc7e6e8b2065a12ed7fc07e1de8febb7dc7839d7b7f15dd66584df9'
        Bytes = 25888640
        Url = 'https://huggingface.co/Xenova/modnet/resolve/main/onnx/model.onnx'
    }
)

function Test-ModelFile([string] $Path, [hashtable] $Model) {
    return (Test-Path -LiteralPath $Path -PathType Leaf) -and
        (Get-Item -LiteralPath $Path).Length -eq $Model.Bytes -and
        (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq $Model.Sha256
}

function Initialize-Models {
    foreach ($model in $models) {
        $path = Join-Path $modelDirectory $model.File
        if (Test-ModelFile $path $model) {
            Write-Host "Verified $($model.File)"
            continue
        }
        if (-not $model.Url) {
            throw "The committed model $path is missing or invalid. Restore it from git before building."
        }

        # Download next to the final file so promotion stays on the same volume.
        # A failed request or checksum leaves the previous file untouched.
        $partial = "$path.$([guid]::NewGuid().ToString('N')).part"
        $oldProgress = $ProgressPreference
        $oldSecurityProtocol = [Net.ServicePointManager]::SecurityProtocol
        try {
            $ProgressPreference = 'SilentlyContinue'
            [Net.ServicePointManager]::SecurityProtocol = $oldSecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
            Write-Host "Downloading $($model.File) from $($model.Url)"
            Invoke-WebRequest -Uri $model.Url -OutFile $partial -UseBasicParsing -TimeoutSec 180
            if (-not (Test-ModelFile $partial $model)) {
                throw "The download of $($model.File) did not match its pinned size and SHA-256."
            }
            if (Test-Path -LiteralPath $path -PathType Leaf) {
                [IO.File]::Replace($partial, $path, $null)
            } else {
                [IO.File]::Move($partial, $path)
            }
            Write-Host "Verified $($model.File)"
        } finally {
            $ProgressPreference = $oldProgress
            [Net.ServicePointManager]::SecurityProtocol = $oldSecurityProtocol
            if (Test-Path -LiteralPath $partial -PathType Leaf) {
                Remove-Item -LiteralPath $partial
            }
        }
    }
}

function Invoke-DotNet([string[]] $Arguments) {
    & $dotnetPath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE."
    }
}

function Assert-TestReport([string] $ReportPath, [int] $ExitCode) {
    if ($ExitCode -ne 0) {
        throw "dotnet test exited with code $ExitCode."
    }
    if (-not (Test-Path -LiteralPath $ReportPath -PathType Leaf)) {
        throw 'No TRX report was produced; test execution could not be verified.'
    }

    $xml = New-Object System.Xml.XmlDocument
    $xml.XmlResolver = $null
    $readerSettings = New-Object System.Xml.XmlReaderSettings
    $readerSettings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $readerSettings.XmlResolver = $null
    $reader = [System.Xml.XmlReader]::Create($ReportPath, $readerSettings)
    try { $xml.Load($reader) } finally { $reader.Dispose() }
    $namespaces = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
    $namespaces.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
    $summary = $xml.SelectSingleNode('/t:TestRun/t:ResultSummary', $namespaces)
    $countersNode = $xml.SelectSingleNode('/t:TestRun/t:ResultSummary/t:Counters', $namespaces)
    if (-not $summary -or -not $countersNode) {
        throw 'The TRX report has no valid result summary and counters.'
    }

    $counters = @{}
    foreach ($name in @('total', 'executed', 'passed', 'failed', 'error', 'timeout',
            'aborted', 'notRunnable', 'notExecuted', 'disconnected')) {
        [long] $value = 0
        if (-not [long]::TryParse($countersNode.GetAttribute($name), [ref] $value) -or $value -lt 0) {
            throw "The TRX report has a missing or invalid '$name' counter."
        }
        $counters[$name] = $value
    }
    if ($counters.executed -eq 0) {
        throw 'Zero tests executed. A blocked test host or discovery failure is not a passing run.'
    }
    foreach ($name in @('failed', 'error', 'timeout', 'aborted', 'notRunnable', 'disconnected')) {
        if ($counters[$name] -ne 0) {
            throw "The TRX report contains $($counters[$name]) '$name' results."
        }
    }
    if ($summary.GetAttribute('outcome') -notin @('Completed', 'Passed')) {
        throw "The TRX run did not complete successfully: $($summary.GetAttribute('outcome'))."
    }
    $results = $xml.SelectNodes('/t:TestRun/t:Results/t:UnitTestResult', $namespaces)
    $passedResults = @($results | Where-Object { $_.GetAttribute('outcome') -eq 'Passed' }).Count
    $skippedResults = @($results | Where-Object { $_.GetAttribute('outcome') -eq 'NotExecuted' }).Count
    if ($counters.passed -ne $counters.executed -or
        $counters.total -ne $counters.executed + $counters.notExecuted -or
        $results.Count -ne $counters.total -or $passedResults -ne $counters.passed -or
        $skippedResults -ne $counters.notExecuted) {
        throw 'The TRX counters and individual results do not describe a complete passing run.'
    }
    return $counters
}

function Invoke-WindowsTests {
    # Each project gets a distinct report path even when both target the same
    # framework. A fresh parent prevents a previous passing TRX hiding a failure.
    $runName = "$(Get-Date -Format 'yyyyMMdd-HHmmss')-$([guid]::NewGuid().ToString('N'))"
    $runDirectory = Join-Path $repoRoot "artifacts/windows-tests/$runName"
    New-Item -ItemType Directory -Path $runDirectory | Out-Null
    Write-Host "Test results: $runDirectory"
    $failures = @()
    foreach ($project in @('Composa.Core.Tests', 'Composa.App.Tests')) {
        $projectDirectory = Join-Path $runDirectory $project
        New-Item -ItemType Directory -Path $projectDirectory | Out-Null
        $reportPath = Join-Path $projectDirectory 'results.trx'
        $logPath = Join-Path $projectDirectory 'console.log'
        $testArguments = @('test', (Join-Path $repoRoot "tests/$project/$project.csproj"),
            '-c', $Configuration, '-p:UpdateChannel=local',
            '--results-directory', $projectDirectory, '--logger', 'trx;LogFileName=results.trx')
        try {
            # Windows PowerShell turns native stderr into ErrorRecords. Preserve
            # it in the log and check the real process status plus TRX ourselves.
            $previousErrorAction = $ErrorActionPreference
            try {
                $ErrorActionPreference = 'Continue'
                & $dotnetPath @testArguments 2>&1 | Tee-Object -FilePath $logPath | Out-Host
                $testExitCode = $LASTEXITCODE
            } finally {
                $ErrorActionPreference = $previousErrorAction
            }
            $counts = Assert-TestReport $reportPath $testExitCode
            Write-Host "$project verified: $($counts.passed) passed, $($counts.notExecuted) skipped."
        } catch {
            $failures += "${project}: $($_.Exception.Message) Log: $logPath; report: $reportPath"
        }
    }
    if ($failures.Count -ne 0) {
        throw ("Test verification failed. No overall pass is recorded.`n" + ($failures -join "`n"))
    }
    Write-Host "Both test projects passed verification. Results: $runDirectory"
}

if (-not $SkipModels) { Initialize-Models }
if ($Action -eq 'Models') { return }

$dotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue
if (-not $dotnetCommand) {
    throw 'Install the .NET 10 SDK selected by global.json, then reopen PowerShell.'
}
$dotnetPath = $dotnetCommand.Source

# The SDK resolves global.json from the working directory, not from --project.
Push-Location -LiteralPath $repoRoot
try {
    switch ($Action) {
        'Build' {
            Invoke-DotNet @('build', $solution, '-c', $Configuration, '-p:UpdateChannel=local')
        }
        'Test' {
            Invoke-WindowsTests
        }
        'Run' {
            $previousDataDirectory = $env:DIEYING_DATA_DIR
            try {
                if ([string]::IsNullOrWhiteSpace($previousDataDirectory) -and
                    [string]::IsNullOrWhiteSpace($env:IMAGE_EDITOR_DEV_DATA_DIR)) {
                    $env:DIEYING_DATA_DIR = Join-Path $repoRoot 'artifacts/local-data'
                }
                Invoke-DotNet @('run', '--project', $appProject, '-c', $Configuration, '-p:UpdateChannel=local')
            } finally {
                $env:DIEYING_DATA_DIR = $previousDataDirectory
            }
        }
        'Publish' {
            # Restore first: MinVer's target only exists after NuGet has restored it.
            Invoke-DotNet @('restore', $appProject, '-r', $Runtime)
            $version = (Invoke-DotNet @('msbuild', $appProject, '-t:MinVer', '-getProperty:MinVerVersion', '-v:q', '-nologo') | Out-String).Trim()
            if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$') {
                throw "MinVer did not return a valid version: $version"
            }
            $version = ($version -split '\+')[0]
            $applicationId = (Invoke-DotNet @('msbuild', $appProject, '-getProperty:AssemblyName', '-v:q', '-nologo') | Out-String).Trim()
            if ($applicationId -notmatch '^[a-z][a-z0-9-]+$' -or $applicationId -eq 'composa') {
                throw 'Portable development packages require a separate application identity, not the upstream Composa executable.'
            }
            $name = "$applicationId-$version-local-$Runtime-$(Get-Date -Format 'yyyyMMdd-HHmmss')-$([guid]::NewGuid().ToString('N').Substring(0, 6))"
            $publishDirectory = Join-Path $outputRoot $name
            if (Test-Path -LiteralPath $publishDirectory) {
                throw "Refusing to overwrite an existing publish directory: $publishDirectory"
            }
            New-Item -ItemType Directory -Path $publishDirectory | Out-Null
            Invoke-DotNet @('publish', $appProject, '-c', $Configuration, '-r', $Runtime,
                '--self-contained', 'true', '-p:DebugType=none', '-p:PublishSingleFile=false',
                '-p:UpdateChannel=local', '-o', $publishDirectory)

            foreach ($file in @('LICENSE', 'README.md', 'README.zh-CN.md', 'CHANGELOG.md')) {
                Copy-Item -LiteralPath (Join-Path $repoRoot $file) -Destination $publishDirectory
            }
            foreach ($file in @("$applicationId.exe", 'THIRD-PARTY-NOTICES.txt', 'ImageMagick-NOTICE.txt')) {
                $publishedFile = Join-Path $publishDirectory $file
                if (-not (Test-Path -LiteralPath $publishedFile -PathType Leaf) -or
                    (Get-Item -LiteralPath $publishedFile).Length -eq 0) {
                    throw "Required file is missing or empty in the published build: $file"
                }
            }
            foreach ($model in $models) {
                if (-not (Test-ModelFile (Join-Path $publishDirectory "models/$($model.File)") $model)) {
                    throw "Published model did not match its pin: $($model.File)"
                }
            }
            if (Get-ChildItem -LiteralPath $publishDirectory -Filter '*.pdb' -Recurse -File) {
                throw 'The published build contains debug symbols; check LeaveOutDebugSymbols in Composa.App.csproj.'
            }
            if ($Zip) {
                $zipPath = Join-Path $outputRoot "$name.zip"
                Compress-Archive -LiteralPath $publishDirectory -DestinationPath $zipPath -CompressionLevel Optimal
                $digest = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
                [IO.File]::WriteAllText("$zipPath.sha256", "$digest  $([IO.Path]::GetFileName($zipPath))`n", [Text.UTF8Encoding]::new($false))
                Write-Host "Portable zip: $zipPath"
            }
            Write-Host "Portable application: $(Join-Path $publishDirectory "$applicationId.exe")"
        }
    }
} finally {
    Pop-Location
}
