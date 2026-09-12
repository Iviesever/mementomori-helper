param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][ValidateRange(1,2100000000)][int]$VersionCode
)
$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
$inputApk = Join-Path $output 'input.apk'
if (Test-Path -LiteralPath $inputApk) { throw 'Refusing to replace an existing release-signing input.' }
New-Item -ItemType Directory $output -Force | Out-Null
$project = (Resolve-Path (Join-Path $PSScriptRoot '../../MementoMori.Exporter.Android')).Path
$logs = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../artifacts/build-logs'))
New-Item -ItemType Directory $logs -Force | Out-Null
Push-Location $project
try {
    # No long-lived key in this step. The resulting Release APK is only an internal signing input.
    dotnet publish MementoMori.Exporter.Android.csproj -c Release -f net10.0-android -p:RuntimeIdentifier=android-arm64 -p:AndroidKeyStore=false -p:ApplicationVersion=$VersionCode -p:PublishTrimmed=false -p:RunAOTCompilation=false "-p:JavaSdkDirectory=$env:JAVA_HOME" -o $output "-clp:ErrorsOnly;Summary" -fl "-flp:LogFile=$logs/release-build.log;Verbosity=normal"
    if ($LASTEXITCODE -ne 0) { throw 'Release APK compilation failed.' }
    $apks = @(Get-ChildItem -LiteralPath $output -Filter '*-Signed.apk' -File)
    if ($apks.Count -ne 1) { throw 'Expected exactly one published Release APK.' }
    Move-Item -LiteralPath $apks[0].FullName -Destination $inputApk
} finally { Pop-Location }
Write-Host 'Release signing input compiled without the stable private key.'
