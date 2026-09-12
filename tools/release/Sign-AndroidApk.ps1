param(
    [Parameter(Mandatory = $true)][string]$InputApk,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [switch]$AllowDebuggableForTest,
    [switch]$DisposableKeyForTest
)
$ErrorActionPreference = 'Stop'
foreach ($name in @('ANDROID_KEYSTORE_BASE64','ANDROID_KEYSTORE_PASSWORD','ANDROID_KEY_ALIAS','ANDROID_KEY_PASSWORD','ANDROID_SIGNING_CERT_SHA256')) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) { throw "Missing signing input: $name" }
}
if ($env:ANDROID_SIGNING_CERT_SHA256 -notmatch '^[A-Fa-f0-9]{64}$') { throw 'Expected certificate SHA256 must be 64 hex characters.' }
if ($env:ANDROID_KEYSTORE_BASE64.Length -gt 14000000) { throw 'Signing key input is unexpectedly large.' }
$inputPath = (Resolve-Path -LiteralPath $InputApk).Path
$tools = Get-ChildItem "$env:ANDROID_HOME/build-tools" -Filter apksigner.bat -Recurse | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $tools) { throw 'Android SDK apksigner is required.' }
$java = Join-Path $env:JAVA_HOME 'bin/java.exe'
$jar = Join-Path $tools.Directory.FullName 'lib/apksigner.jar'
$aapt = Join-Path $tools.Directory.FullName 'aapt.exe'
foreach ($file in @($java,$jar,$aapt)) { if (-not (Test-Path -LiteralPath $file)) { throw 'Complete JDK and Android SDK signing tools are required.' } }
$badging = (& $aapt dump badging $inputPath) -join "`n"
if ($LASTEXITCODE -ne 0) { throw 'Input APK manifest could not be checked.' }
if (-not $AllowDebuggableForTest -and $badging.Contains('application-debuggable')) { throw 'Stable signing refuses debuggable APKs.' }
$expectedPackage = if ($AllowDebuggableForTest) { 'io.github.iviesever.mementomori.exporter.dev' } else { 'io.github.iviesever.mementomori.exporter' }
$packagePattern = [regex]::Escape($expectedPackage)
if ($badging -notmatch "(?m)^package: name='$packagePattern' versionCode='(?<code>[0-9]+)' versionName='(?<version>[^']+)'") { throw 'Unexpected Android package identity or version.' }
$versionCode = [long]$Matches['code']
$displayVersion = $Matches['version']
if ($versionCode -lt 1 -or $versionCode -gt 2100000000) { throw 'Invalid Android versionCode.' }
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$output = Join-Path $outputRoot 'MementoMori-Exporter-android-arm64.apk'
if (Test-Path -LiteralPath $output) { throw 'Refusing to overwrite an existing signed APK.' }
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$tempRoot = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
$keyDirectory = Join-Path $tempRoot ('mm-sign-' + [Guid]::NewGuid().ToString('N'))
$keyPath = Join-Path $keyDirectory 'signing.keystore'
$ok = $false
try {
    New-Item -ItemType Directory -Path $keyDirectory | Out-Null
    [IO.File]::WriteAllBytes($keyPath, [Convert]::FromBase64String($env:ANDROID_KEYSTORE_BASE64))
    # Invoke the SDK's actual apksigner engine directly, avoiding batch-wrapper argument handling.
    $signOutput = (& $java -jar $jar sign --ks $keyPath --ks-key-alias $env:ANDROID_KEY_ALIAS --ks-pass env:ANDROID_KEYSTORE_PASSWORD --key-pass env:ANDROID_KEY_PASSWORD --out $output $inputPath 2>&1) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw 'APK signing failed.' }
    $verification = (& $java -jar $jar verify --verbose --print-certs $output 2>&1) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw 'Signed APK verification failed.' }
    $actual = & (Join-Path $PSScriptRoot 'Get-ApkSigningIdentity.ps1') -Report $verification
    if ($actual -ne $env:ANDROID_SIGNING_CERT_SHA256.ToLowerInvariant()) { throw 'Signing certificate does not match the pinned identity.' }
    $hash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  MementoMori-Exporter-android-arm64.apk" | Set-Content (Join-Path $outputRoot 'SHA256SUMS.txt') -Encoding ascii
    $verification | Set-Content (Join-Path $outputRoot 'APK-SIGNATURE.txt') -Encoding utf8
    @{
        sourceCommit = $env:SOURCE_COMMIT; buildCommit = $env:BUILD_COMMIT
        workflowRun = $env:GITHUB_RUN_ID; certificateSha256 = $actual; sha256 = $hash
        packageName = $expectedPackage
        applicationVersion = $versionCode; displayVersion = $displayVersion
        inputSha256 = (Get-FileHash $inputPath -Algorithm SHA256).Hash
        signing = $(if ($AllowDebuggableForTest -or $DisposableKeyForTest) { 'disposable-test-key' } else { 'protected-stable-key' })
        physicalDeviceValidation = 'not-performed-by-this-workflow'
    } | ConvertTo-Json | Set-Content (Join-Path $outputRoot 'BUILD-INFO.json') -Encoding utf8
    $ok = $true
} catch {
    throw 'Protected APK signing failed. Verify key configuration and pinned certificate; private diagnostics were not printed.'
} finally {
    $signOutput = $null
    if (Test-Path -LiteralPath $keyDirectory) { Remove-Item -LiteralPath $keyDirectory -Recurse -Force }
    if (-not $ok -and (Test-Path -LiteralPath $output)) { Remove-Item -LiteralPath $output -Force }
}
Write-Host 'APK signed and verified against the pinned public certificate fingerprint.'
