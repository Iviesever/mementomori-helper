param([string]$Package = 'artifacts/windows-package/MementoMori-Exporter-windows-x64-preview.zip')
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Test-WindowsPackage.ps1') -Package $Package
