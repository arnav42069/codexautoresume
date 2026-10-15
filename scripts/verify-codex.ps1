param([string]$Codex = 'codex.exe')
$ErrorActionPreference = 'Stop'
& $Codex --version
if ($LASTEXITCODE -ne 0) { throw 'Cannot execute the native Codex CLI.' }
$schema = Join-Path $PSScriptRoot '../artifacts/codex-schema'
& $Codex app-server generate-json-schema --out $schema
if ($LASTEXITCODE -ne 0) { throw 'This Codex build does not expose the required schema generator.' }
Write-Output "Protocol schemas saved to $schema. No prompts submitted or configuration changed."
