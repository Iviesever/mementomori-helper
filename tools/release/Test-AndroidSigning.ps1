param(
    [Parameter(Mandatory = $true)][string]$InputApk,
    [Parameter(Mandatory = $true)][string]$ReleaseApk
)
$ErrorActionPreference = 'Stop'
$names = @('ANDROID_KEYSTORE_BASE64','ANDROID_KEYSTORE_PASSWORD','ANDROID_KEY_ALIAS','ANDROID_KEY_PASSWORD','ANDROID_SIGNING_CERT_SHA256')
foreach ($name in $names) {
    if (-not [string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($name))) { throw 'Never run disposable signing tests in a credential-bearing environment.' }
}
$base = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
$root = Join-Path $base ('mm-sign-test-' + [Guid]::NewGuid().ToString('N'))
$script = Join-Path $PSScriptRoot 'Sign-AndroidApk.ps1'
New-Item -ItemType Directory -Path $root | Out-Null
try {
    $key = Join-Path $root 'test.keystore'
    & "$env:JAVA_HOME/bin/keytool.exe" -genkeypair -keystore $key -alias fixture -keyalg RSA -keysize 2048 -validity 2 -dname 'CN=Disposable CI Test' -storepass fixture-test-only -keypass fixture-test-only -noprompt 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the disposable signing fixture.' }
    $cert = Join-Path $root 'public.cer'
    & "$env:JAVA_HOME/bin/keytool.exe" -exportcert -keystore $key -alias fixture -storepass fixture-test-only -file $cert 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not export the public test certificate.' }
    $expected = (Get-FileHash $cert -Algorithm SHA256).Hash.ToLowerInvariant()
    $env:ANDROID_KEYSTORE_BASE64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($key))
    $env:ANDROID_KEYSTORE_PASSWORD = 'fixture-test-only'
    $env:ANDROID_KEY_PASSWORD = 'fixture-test-only'
    $env:ANDROID_KEY_ALIAS = 'fixture'
    $env:ANDROID_SIGNING_CERT_SHA256 = $expected
    & $script -InputApk $InputApk -OutputDirectory (Join-Path $root 'first') -AllowDebuggableForTest
    & $script -InputApk $InputApk -OutputDirectory (Join-Path $root 'second') -AllowDebuggableForTest
    # Exercise the actual Release path with debuggable rejection enabled, but a disposable key.
    & $script -InputApk $ReleaseApk -OutputDirectory (Join-Path $root 'release') -DisposableKeyForTest
    foreach ($name in @('first','second','release')) {
        $info = Get-Content (Join-Path $root "$name/BUILD-INFO.json") -Raw | ConvertFrom-Json
        if ($info.certificateSha256 -ne $expected -or $info.signing -ne 'disposable-test-key') { throw 'Signing fixture identity mismatch.' }
        $package = if ($name -eq 'release') { 'io.github.iviesever.mementomori.exporter' } else { 'io.github.iviesever.mementomori.exporter.dev' }
        if ($info.packageName -ne $package -or [long]$info.applicationVersion -lt 1 -or [string]::IsNullOrWhiteSpace($info.displayVersion)) { throw 'Signed APK version provenance is missing.' }
        $sourceApk = if ($name -eq 'release') { $ReleaseApk } else { $InputApk }
        if ($info.inputSha256 -ne (Get-FileHash $sourceApk -Algorithm SHA256).Hash) { throw 'Signed APK input provenance mismatch.' }
    }
    $rejected = $false
    try { & $script -InputApk $InputApk -OutputDirectory (Join-Path $root 'debug-rejected') -DisposableKeyForTest }
    catch { $rejected = $true }
    if (-not $rejected) { throw 'Stable signing accepted a debuggable APK.' }
    $env:ANDROID_SIGNING_CERT_SHA256 = '0' * 64
    $rejected = $false
    try { & $script -InputApk $InputApk -OutputDirectory (Join-Path $root 'wrong-cert') -AllowDebuggableForTest }
    catch { $rejected = $true }
    if (-not $rejected -or (Test-Path (Join-Path $root 'wrong-cert/MementoMori-Exporter-android-arm64.apk'))) {
        throw 'Mismatched certificate was not rejected and cleaned up.'
    }
    $env:ANDROID_SIGNING_CERT_SHA256 = $expected
    $env:ANDROID_KEY_ALIAS = $null
    $rejected = $false
    try { & $script -InputApk $InputApk -OutputDirectory (Join-Path $root 'missing-key') -AllowDebuggableForTest }
    catch { $rejected = $true }
    if (-not $rejected) { throw 'Missing signing configuration was accepted.' }
    Write-Host 'PASS: two preview signatures and a Release signature use one pinned identity; debug APKs, wrong certificates and missing inputs fail closed.'
    Write-Host 'Disposable signing tests do not provision a stable release key or prove device update compatibility.'
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $null) }
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
