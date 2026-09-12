$ErrorActionPreference = 'Stop'
$names = @('ANDROID_KEYSTORE_BASE64','ANDROID_KEYSTORE_PASSWORD','ANDROID_KEY_ALIAS','ANDROID_CERT_SHA256')
$old = @{}
$work = Join-Path ([IO.Path]::GetTempPath()) ('mm-signing-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $work | Out-Null
try {
    foreach ($n in $names) { $old[$n]=[Environment]::GetEnvironmentVariable($n); [Environment]::SetEnvironmentVariable($n,$null) }
    $failed=$false
    try { & "$PSScriptRoot/Publish-SignedAndroid.ps1" -ValidateOnly } catch { $failed=$true }
    if (-not $failed) { throw 'Missing signing inputs did not fail closed.' }
    # Disposable synthetic key, never used for distribution or retained as an artifact.
    $env:ANDROID_KEYSTORE_PASSWORD=[guid]::NewGuid().ToString('N')
    $env:ANDROID_KEY_ALIAS='synthetic-test'
    & keytool -genkeypair -keystore "$work/test.jks" -alias $env:ANDROID_KEY_ALIAS -keyalg RSA -keysize 2048 -validity 2 -dname 'CN=Synthetic signing preflight only' -storepass:env ANDROID_KEYSTORE_PASSWORD -keypass:env ANDROID_KEYSTORE_PASSWORD 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Synthetic key creation failed.' }
    & keytool -exportcert -keystore "$work/test.jks" -alias $env:ANDROID_KEY_ALIAS -storepass:env ANDROID_KEYSTORE_PASSWORD -file "$work/test.der" 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Synthetic certificate export failed.' }
    $env:ANDROID_KEYSTORE_BASE64=[Convert]::ToBase64String([IO.File]::ReadAllBytes("$work/test.jks"))
    $env:ANDROID_CERT_SHA256='0' * 64
    $failed=$false
    try { & "$PSScriptRoot/Publish-SignedAndroid.ps1" -ValidateOnly } catch { $failed=$true }
    if (-not $failed) { throw 'Mismatched certificate was not rejected.' }
    $env:ANDROID_CERT_SHA256=(Get-FileHash "$work/test.der" -Algorithm SHA256).Hash
    & "$PSScriptRoot/Publish-SignedAndroid.ps1" -ValidateOnly
    Write-Host 'Signing preflight: absent input rejected, wrong certificate rejected, matching synthetic key accepted.'
} finally {
    foreach ($n in $names) { [Environment]::SetEnvironmentVariable($n,$old[$n]) }
    Remove-Item $work -Recurse -Force
}
