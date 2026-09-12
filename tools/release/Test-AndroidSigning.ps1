param([Parameter(Mandatory = $true)][string]$InputApk)
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
    # A disposable CI key with a deliberately non-secret test password. Never upload either.
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
    foreach ($name in @('first','second')) {
        $info = Get-Content (Join-Path $root "$name/BUILD-INFO.json") -Raw | ConvertFrom-Json
        if ($info.certificateSha256 -ne $expected -or $info.signing -ne 'disposable-test-key') { throw 'Signing fixture identity mismatch.' }
    }
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
    Write-Host 'PASS: two signatures use one pinned identity; wrong certificate and missing inputs fail closed.'
    Write-Host 'Disposable signing tests do not provision a stable release key or prove device update compatibility.'
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $null) }
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
