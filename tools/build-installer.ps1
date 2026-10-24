# Gera o instalador: publica o app autocontido em dist/app e compila installer/Bergamota.iss.
# Saída: dist/Bergamota-Setup-<versão>.exe
# Uso: powershell -ExecutionPolicy Bypass -File tools/build-installer.ps1
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root

Remove-Item dist\app -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish src\Bergamota\Bergamota.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true -p:DebugType=none -o dist\app -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "publish falhou" }

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 não encontrado (winget install JRSoftware.InnoSetup)" }
& $iscc /Q installer\Bergamota.iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup falhou" }
Get-ChildItem dist\Bergamota-Setup-*.exe | Select-Object Name, @{ n = "MB"; e = { [math]::Round($_.Length / 1MB, 1) } }
