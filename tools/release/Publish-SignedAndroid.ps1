param([switch]$ValidateOnly)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
foreach ($name in @('ANDROID_KEYSTORE_BASE64','ANDROID_KEYSTORE_PASSWORD','ANDROID_KEY_ALIAS','ANDROID_CERT_SHA256')) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) { throw "Missing protected signing input: $name. No debug-key fallback is allowed." }
}
if ($env:ANDROID_KEYSTORE_BASE64.Length -gt 131072) { throw 'Signing file exceeds the configured limit.' }
$fingerprint = $env:ANDROID_CERT_SHA256.Replace(':','').ToLowerInvariant()
if ($fingerprint -notmatch '^[a-f0-9]{64}$') { throw 'Invalid pinned signing-certificate fingerprint.' }
if ($env:ANDROID_KEY_ALIAS -notmatch '^[A-Za-z0-9_.-]{1,64}$') { throw 'Unsupported signing alias.' }
$temp = Join-Path ([IO.Path]::GetTempPath()) ('mm-signing-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $temp | Out-Null
try {
    # Private file stays outside the checkout/artifact tree and only this account can read it.
    if ($IsWindows) {
        $acl = [Security.AccessControl.DirectorySecurity]::new()
        $acl.SetAccessRuleProtection($true,$false)
        $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User
        $rule = [Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow')
        $acl.AddAccessRule($rule)
        Set-Acl -LiteralPath $temp -AclObject $acl
    } else { & chmod 700 $temp; if ($LASTEXITCODE -ne 0) { throw 'Cannot restrict signing-directory access.' } }
    $keystore = Join-Path $temp 'release.keystore'
    [IO.File]::WriteAllBytes($keystore,[Convert]::FromBase64String($env:ANDROID_KEYSTORE_BASE64))
    $certificate = Join-Path $temp 'signer.der'
    & keytool -exportcert -keystore $keystore -alias $env:ANDROID_KEY_ALIAS -storepass:env ANDROID_KEYSTORE_PASSWORD -file $certificate 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Unable to open the protected signing key.' }
    if ((Get-FileHash $certificate -Algorithm SHA256).Hash.ToLowerInvariant() -ne $fingerprint) { throw 'The signing certificate does not match the pinned identity.' }
    if ($ValidateOnly) { Write-Host 'Protected signing inputs validated; no APK produced.'; return }
    Push-Location "$root/MementoMori.Exporter.Android"
    try {
        # Do not enable MSBuild diagnostic/binlogs: they can record environment secrets.
        dotnet build -c Release -f net10.0-android -t:SignAndroidPackage -p:RuntimeIdentifier=android-arm64 -p:ExporterDeviceTests=false -p:AndroidKeyStore=true "-p:AndroidSigningKeyStore=$keystore" "-p:AndroidSigningKeyAlias=$env:ANDROID_KEY_ALIAS" -p:AndroidSigningKeyPass=env:ANDROID_KEYSTORE_PASSWORD -p:AndroidSigningStorePass=env:ANDROID_KEYSTORE_PASSWORD -p:NuGetAudit=true -p:NuGetAuditMode=all '-warnaserror:NU1900,NU1901,NU1902,NU1903,NU1904' '-clp:ErrorsOnly;Summary'
        if ($LASTEXITCODE -ne 0) { throw 'Signed release build failed.' }
        $apks = @(Get-ChildItem bin/Release -Recurse -Filter '*-Signed.apk')
        if ($apks.Count -ne 1) { throw 'Expected exactly one release APK.' }
        $tools = @(Get-ChildItem "$env:ANDROID_HOME/build-tools" -Directory | Where-Object Name -match '^\d+\.\d+\.\d+$' | Sort-Object { [version]$_.Name } -Descending)
        if ($tools.Count -eq 0) { throw 'Android build-tools unavailable.' }
        $verify = & "$($tools[0].FullName)/apksigner.bat" verify --verbose --print-certs $apks[0].FullName 2>&1
        if ($LASTEXITCODE -ne 0) { throw 'APK signature verification failed.' }
        $text = $verify -join "`n"
        if ($text -notmatch 'Verified using v2 scheme.*true' -or $text -notmatch [regex]::Escape($fingerprint)) { throw 'APK signature identity or v2 verification mismatch.' }
        $manifest = & "$($tools[0].FullName)/aapt.exe" dump xmltree $apks[0].FullName AndroidManifest.xml
        if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect signed APK manifest.' }
        if (($manifest -join "`n") -match 'android:debuggable[^\r\n]*0xffffffff') { throw 'Refusing a debuggable release APK.' }
        $out = "$root/artifacts/signed-android"
        New-Item -ItemType Directory $out -Force | Out-Null
        Copy-Item $apks[0].FullName "$out/MementoMori-Exporter-android-arm64.apk"
        $hash = (Get-FileHash "$out/MementoMori-Exporter-android-arm64.apk" -Algorithm SHA256).Hash
        $commit = git rev-parse HEAD
        if ($LASTEXITCODE -ne 0) { throw 'Cannot identify release source.' }
        [xml]$project = Get-Content MementoMori.Exporter.Android.csproj -Raw
        @{ sourceCommit=$commit; sha256=$hash; certificateSha256=$fingerprint; signing='pinned-protected-key'; applicationVersion=[string]$project.Project.PropertyGroup[0].ApplicationVersion; realDeviceEvidenceCommit=$env:DEVICE_VALIDATED_SOURCE_SHA } | ConvertTo-Json | Set-Content "$out/BUILD-INFO.json" -Encoding utf8
        "$hash  MementoMori-Exporter-android-arm64.apk" | Set-Content "$out/SHA256SUMS.txt" -Encoding ascii
    } finally { Pop-Location }
} finally {
    if (Test-Path $temp) { Remove-Item -LiteralPath $temp -Recurse -Force }
}
