param([string]$OutputDirectory = 'artifacts/windows')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$output = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
$stage = Join-Path $output 'MementoMori-Exporter-win-x64'
New-Item -ItemType Directory -Path (Join-Path $stage 'app') -Force | Out-Null
Push-Location $root
try {
    dotnet publish MementoMori.WebUI/MementoMori.WebUI.csproj -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false -p:NuGetAudit=true -p:NuGetAuditMode=all '-warnaserror:NU1900,NU1901,NU1902,NU1903,NU1904' -o "$stage/app" '-clp:ErrorsOnly;Summary'
    if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed.' }
    if (-not (Test-Path "$stage/app/MementoMori.WebUI.exe")) { throw 'Publish produced no executable.' }
    if (@(Get-ChildItem $stage -Recurse -File -Filter 'appsettings.user.json').Count -ne 0) { throw 'Refusing to package private account configuration.' }
    # Only the CMD entry belongs beside app/, never beside the runtime DLLs.
    Remove-Item "$stage/Start-Exporter.ps1","$stage/app/START-EXPORTER.cmd" -Force -ErrorAction SilentlyContinue
    Copy-Item "$PSScriptRoot/Start-Exporter.ps1" -Destination "$stage/app"
    Copy-Item "$PSScriptRoot/START-EXPORTER.cmd" -Destination $stage
    Copy-Item "$root/LICENSE" -Destination $stage
    @'
MementoMori Exporter - Windows x64 preview
Extract the whole ZIP, then open the outer START-EXPORTER.cmd beside the app folder.
Runtime files and Start-Exporter.ps1 are inside app/. Do not move the CMD away from app/.
No .NET SDK/runtime install is needed.
Use your existing private %LOCALAPPDATA%\MementoMoriExporter\appsettings.user.json.
Only whitelisted login fields are used; automatic jobs and report uploads are disabled.
The package does not contain login credentials. Account configuration and master cache stay in your private local app-data directory, not beside the executable.
Offline installation check: START-EXPORTER.cmd -OfflineCheck
This shows the real local UI but intentionally rejects exports and never logs into the game.
Offline checks do not prove real game login. This preview is not Authenticode signed.
'@ | Set-Content "$stage/README.txt" -Encoding utf8
    $commit = git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify build commit.' }
    @{ sourceCommit=$env:SOURCE_COMMIT; buildCommit=$commit; runtime='win-x64'; selfContained=$true; signing='unsigned-windows-preview' } | ConvertTo-Json | Set-Content "$stage/BUILD-INFO.json" -Encoding utf8
    $hashes = @(Get-ChildItem $stage -Recurse -File | Where-Object { $_.FullName -ne (Join-Path $stage 'SHA256SUMS.txt') } | Sort-Object FullName | ForEach-Object {
        $name = $_.FullName.Substring($stage.Length + 1).Replace('\','/')
        "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash)  $name"
    })
    $hashes | Set-Content "$stage/SHA256SUMS.txt" -Encoding ascii
    Compress-Archive -Path "$stage/*" -DestinationPath "$output/MementoMori-Exporter-win-x64-preview.zip" -Force
} finally { Pop-Location }
