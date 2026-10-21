<#
.SYNOPSIS
    Builds NowPlaying and its installer.

.DESCRIPTION
    Output in the dist\ folder:
      app\                             the app (NowPlaying.exe...) and the widget of the Windows Widgets board
                                       (NowPlaying.Widgets.exe), framework-dependent (.NET 10 Desktop Runtime),
                                       compiled ahead of time (ReadyToRun: lighter and quicker to start)
      NowPlaying.Widgets.msix          the "sparse" package that lists the widget in the Widgets board
      NowPlaying-Setup-<version>.exe   the installer (needs Inno Setup 6)

    Prerequisites: see "Development" in the README.

.PARAMETER NoInstaller
    Do not build the installer (useful without Inno Setup).

.PARAMETER SkipTests
    Do not run the unit tests.

.EXAMPLE
    .\scripts\build.ps1
#>
[CmdletBinding()]
param(
    [switch]$NoInstaller,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist'
$appDir = Join-Path $dist 'app'

function Write-Step([string]$Text) { Write-Host "`n==> $Text" -ForegroundColor Cyan }

function Invoke-Native([scriptblock]$Command, [string]$What) {
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$What failed (exit code $LASTEXITCODE)." }
}

$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version
Write-Host "NowPlaying $version" -ForegroundColor Green

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Tool not found: dotnet. See the prerequisites in the README.' }
# makeappx (Windows SDK): on the PATH in a Developer prompt, else the latest SDK installed.
$makeappx = @(
    (Get-Command makeappx -ErrorAction SilentlyContinue).Source
    Get-ChildItem (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin\*\x64\makeappx.exe') -ErrorAction SilentlyContinue |
        Sort-Object { [version]$_.Directory.Parent.Name } -Descending | Select-Object -ExpandProperty FullName
) | Where-Object { $_ } | Select-Object -First 1
if (-not $makeappx) { throw 'Tool not found: makeappx (Windows SDK). See the prerequisites in the README.' }

# Start from an empty dist folder (build output only).
if ([IO.Directory]::Exists($dist)) { [IO.Directory]::Delete($dist, $true) }
New-Item -ItemType Directory -Force $appDir | Out-Null

if (-not $SkipTests) {
    Write-Step 'Tests'
    Invoke-Native { dotnet test (Join-Path $root 'tests\NowPlaying.Tests') -c Release --nologo } 'The tests'
}

Write-Step 'Application (.NET)'
Invoke-Native {
    dotnet publish (Join-Path $root 'src\NowPlaying') -c Release -r win-x64 --self-contained false `
        -p:DebugType=none -o $appDir --nologo
} 'Building the application'

Write-Step 'Widget of the Widgets board (.NET)'
# Same folder as the app: the two share their Windows files (WinRT.Runtime, Microsoft.Windows.SDK.NET).
Invoke-Native {
    dotnet publish (Join-Path $root 'src\NowPlaying.Widgets') -c Release -r win-x64 --self-contained false `
        -p:DebugType=none -o $appDir --nologo
} 'Building the widget'

Write-Step 'Sparse package (Widgets board)'
Invoke-Native {
    & $makeappx pack /o /nv /d (Join-Path $root 'src\NowPlaying.Widgets\package') /p (Join-Path $dist 'NowPlaying.Widgets.msix')
} 'makeappx'

if (-not $NoInstaller) {
    Write-Step 'Installer (Inno Setup)'
    $iscc = @(
        (Get-Command iscc -ErrorAction SilentlyContinue).Source
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if ($iscc) {
        Invoke-Native { & $iscc /Q "/DAppVersion=$version" (Join-Path $root 'installer\NowPlaying.iss') } 'Inno Setup'
    }
    else {
        Write-Warning 'Inno Setup 6 not found: installer not built (winget install JRSoftware.InnoSetup).'
    }
}

Write-Step 'Done'
Get-ChildItem $dist | ForEach-Object { Write-Host "  dist\$($_.Name)" }
