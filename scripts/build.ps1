param([ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    dotnet run --project tests/CodexAutoResume.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Automated tests failed.' }
    dotnet publish src/CodexAutoResume.Windows -c Release -r $Runtime --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
        -o "artifacts/$Runtime"
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    if ($IsWindows -or $env:OS -eq 'Windows_NT') {
        $process = Start-Process -FilePath "$PWD/artifacts/$Runtime/CodexAutoResume.exe" -ArgumentList '--self-test' -PassThru
        if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'Windows UI startup check timed out.' }
        if ($process.ExitCode -ne 0) { throw "Windows UI startup check failed: $($process.ExitCode)" }
    }
    Copy-Item README.md, LICENSE -Destination "artifacts/$Runtime"
    New-Item -ItemType Directory -Path "artifacts/$Runtime/docs" -Force | Out-Null
    Copy-Item docs/* -Destination "artifacts/$Runtime/docs"
    Compress-Archive -Path "artifacts/$Runtime/*" -DestinationPath "artifacts/CodexAutoResume-$Runtime.zip" -Force
    Get-FileHash "artifacts/$Runtime/CodexAutoResume.exe" -Algorithm SHA256
} finally { Pop-Location }
