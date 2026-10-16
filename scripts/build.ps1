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
        $fixture = Join-Path $PWD 'artifacts/ui-codex-fixture'
        New-Item -ItemType Directory -Path $fixture -Force | Out-Null
        Copy-Item 'tests/CodexAutoResume.Tests/bin/Release/net8.0/*' -Destination $fixture -Recurse -Force
        Copy-Item (Join-Path $fixture 'CodexAutoResume.Tests.exe') (Join-Path $fixture 'codex.exe') -Force
        $smokeArgs = @('--self-test', '--fixture-directory', ('"' + $fixture + '"'), '--screenshots', ('"' + $PWD + '/artifacts/ui-screenshots"'))
        $process = Start-Process -FilePath "$PWD/artifacts/$Runtime/CodexAutoResume.exe" -ArgumentList $smokeArgs -PassThru
        if (-not $process.WaitForExit(60000)) { $process.Kill(); throw 'Windows UI startup check timed out.' }
        if ($process.ExitCode -ne 0) {
            $errorPath = "$PWD/artifacts/$Runtime/ui-smoke-error.txt"
            $detail = if (Test-Path $errorPath) { Get-Content $errorPath -Raw } else { 'No managed exception report.' }
            $escaped = $detail.Replace('%','%25').Replace("`r",'%0D').Replace("`n",'%0A')
            Write-Output "::error title=WPF smoke test::$escaped"
            throw "Windows UI startup check failed: $($process.ExitCode): $detail"
        }
    }
    Copy-Item README.md, LICENSE -Destination "artifacts/$Runtime"
    '@echo off', '"%~dp0CodexAutoResume.exe" --create-shortcut' | Set-Content "artifacts/$Runtime/Create desktop shortcut.cmd" -Encoding ascii
    New-Item -ItemType Directory -Path "artifacts/$Runtime/docs" -Force | Out-Null
    Copy-Item docs/* -Destination "artifacts/$Runtime/docs"
    Compress-Archive -Path "artifacts/$Runtime/*" -DestinationPath "artifacts/CodexAutoResume-$Runtime.zip" -Force
    Get-FileHash "artifacts/$Runtime/CodexAutoResume.exe" -Algorithm SHA256
} finally { Pop-Location }
