# Run locally as the repository owner. This script never accepts passwords on its command line.
[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository = 'Iviesever/mementomori-helper',
    [string]$KeyPath = '',
    [ValidatePattern('^[A-Za-z0-9_.-]+$')][string]$KeyAlias = 'exporter',
    [switch]$CreateNew,
    [switch]$UploadToGitHub
)
$ErrorActionPreference = 'Stop'
if ($env:CI -or $env:GITHUB_ACTIONS) { throw 'Long-lived signing setup must run on the owner computer, never in CI.' }
if ([string]::IsNullOrWhiteSpace($KeyPath)) {
    $KeyPath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'MementoMoriExporterSigning/exporter.p12'
}
$KeyPath = [IO.Path]::GetFullPath($KeyPath)
# Walk ancestors instead of assuming a particular checkout layout. Keys never belong in source control.
$ancestor = [IO.Path]::GetDirectoryName($KeyPath)
while ($ancestor) {
    if (Test-Path (Join-Path $ancestor '.git')) { throw 'Choose a key location outside every Git checkout.' }
    $parent = [IO.Directory]::GetParent($ancestor)
    $ancestor = if ($parent) { $parent.FullName } else { $null }
}
$keytool = if ($env:JAVA_HOME -and (Test-Path "$env:JAVA_HOME/bin/keytool.exe")) {
    "$env:JAVA_HOME/bin/keytool.exe"
} else { (Get-Command keytool -ErrorAction Stop).Source }
if (-not (Test-Path -LiteralPath $KeyPath) -and -not $CreateNew) {
    throw 'No local key. Restore your existing key, or deliberately pass -CreateNew for a first-time identity.'
}
$creating = -not (Test-Path -LiteralPath $KeyPath)
if ($creating) {
    Write-Host 'A NEW long-lived identity cannot update old randomly signed preview APKs.'
    if ((Read-Host 'Type CREATE to create your first permanent signing key') -cne 'CREATE') { throw 'Setup canceled.' }
} else { Write-Host 'Reusing the existing local key; it will NOT be replaced.' }
$storeSecure = Read-Host 'Keystore password (retain it in your password manager)' -AsSecureString
$keySecure = $null
$storePlain = $null; $keyPlain = $null
$oldStore = $env:MM_SIGNING_SETUP_STORE; $oldKey = $env:MM_SIGNING_SETUP_KEY
$work = Join-Path ([IO.Path]::GetTempPath()) ('mm-public-cert-' + [guid]::NewGuid().ToString('N'))
function Get-PlainText([Security.SecureString]$Secret) {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Secret)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}
function Invoke-OwnerCommand([string]$Executable, [string[]]$Arguments, [AllowNull()][string]$InputText = $null) {
    # Secret bytes go to stdin, not argv, console, a temporary file or a transcript.
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $Executable
    foreach ($arg in $Arguments) { if ($arg -match '["\r\n]') { throw 'Invalid CLI argument.' } }
    $info.Arguments = ($Arguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $info.UseShellExecute = $false
    $info.RedirectStandardInput = $true; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $info
    try {
        [void]$process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
        if ($null -ne $InputText) {
            $bytes = [Text.Encoding]::UTF8.GetBytes($InputText)
            try { $process.StandardInput.BaseStream.Write($bytes, 0, $bytes.Length); $process.StandardInput.BaseStream.Flush() }
            finally { [Array]::Clear($bytes, 0, $bytes.Length) }
        }
        $process.StandardInput.Close()
        if (-not $process.WaitForExit(120000)) { $process.Kill(); throw 'Owner tool timed out.' }
        if ($process.ExitCode -ne 0) { throw 'Owner tool failed; check prerequisites, input and owner permissions. No private output was printed.' }
        $stdout.GetAwaiter().GetResult()
    } finally { $process.Dispose() }
}
function Invoke-OwnerGh([string[]]$Arguments, [AllowNull()][string]$InputText = $null) {
    Invoke-OwnerCommand -Executable (Get-Command gh -ErrorAction Stop).Source -Arguments $Arguments -InputText $InputText
}
try {
    $storePlain = Get-PlainText $storeSecure
    if ($storePlain.Length -lt 12 -and $creating) { throw 'Use a new keystore password of at least 12 characters.' }
    if ($storePlain.Contains("`n") -or $storePlain.Contains("`r")) { throw 'A one-line password is required.' }
    $keyPlain = $storePlain
    if (-not $creating -and (Read-Host 'Does this key have a DIFFERENT password? Type YES only if so') -ceq 'YES') {
        $keySecure = Read-Host 'Private key password' -AsSecureString
        $keyPlain = Get-PlainText $keySecure
    }
    $env:MM_SIGNING_SETUP_STORE = $storePlain; $env:MM_SIGNING_SETUP_KEY = $keyPlain
    New-Item -ItemType Directory -Path $work | Out-Null
    if ($creating) {
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($KeyPath)) -Force | Out-Null
        Invoke-OwnerCommand -Executable $keytool -Arguments @('-genkeypair','-storetype','PKCS12','-keystore',$KeyPath,'-alias',$KeyAlias,'-keyalg','RSA','-keysize','3072','-validity','10000','-dname','CN=MementoMori Exporter','-storepass:env','MM_SIGNING_SETUP_STORE','-keypass:env','MM_SIGNING_SETUP_KEY','-noprompt') | Out-Null
    }
    # A certificate export alone does not prove possession of a working private key/password.
    Invoke-OwnerCommand -Executable $keytool -Arguments @('-certreq','-keystore',$KeyPath,'-alias',$KeyAlias,'-storepass:env','MM_SIGNING_SETUP_STORE','-keypass:env','MM_SIGNING_SETUP_KEY','-file',"$work/proof.csr") | Out-Null
    Invoke-OwnerCommand -Executable $keytool -Arguments @('-exportcert','-keystore',$KeyPath,'-alias',$KeyAlias,'-storepass:env','MM_SIGNING_SETUP_STORE','-file',"$work/public.cer") | Out-Null
    $fingerprint = (Get-FileHash "$work/public.cer" -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "Public certificate SHA256: $fingerprint"
    Write-Host "Keep independent encrypted backups of $KeyPath and its password before publishing any APK."
    if (-not $UploadToGitHub) { return }
    if ((Read-Host "Confirm the independent key backup, then type UPLOAD to configure $Repository / android-release") -cne 'UPLOAD') { throw 'Upload canceled; your local key is retained.' }
    $environment = Invoke-OwnerGh -Arguments @('api',"repos/$Repository/environments/android-release") | ConvertFrom-Json
    $reviewers = @($environment.protection_rules | Where-Object { $_.type -eq 'required_reviewers' -and @($_.reviewers).Count -gt 0 })
    if ($reviewers.Count -eq 0 -or $environment.deployment_branch_policy.custom_branch_policies -ne $true) {
        throw 'Configure android-release required reviewers and custom branch restrictions first. This script does not weaken those protections.'
    }
    $policy = Invoke-OwnerGh -Arguments @('api',"repos/$Repository/environments/android-release/deployment-branch-policies?per_page=100") | ConvertFrom-Json
    if ($policy.total_count -lt 1 -or $policy.total_count -gt 2 -or @($policy.branch_policies).Count -ne $policy.total_count) { throw 'Expected only explicitly allowed release branches.' }
    foreach ($branch in $policy.branch_policies) {
        if ($branch.name -notin @('master','feature/android-account-exporter') -or ($branch.type -and $branch.type -ne 'branch')) { throw 'Signing environment permits unexpected refs; review its restrictions.' }
    }
    $variables = @(Invoke-OwnerGh -Arguments @('variable','list','--repo',$Repository,'--env','android-release','--json','name,value') | ConvertFrom-Json)
    $pins = @($variables | Where-Object { $_.name -in @('ANDROID_SIGNING_CERT_SHA256','ANDROID_CERT_SHA256') })
    foreach ($pin in $pins) {
        if ($pin.value -notmatch '^[A-Fa-f0-9]{64}$' -or $pin.value.ToLowerInvariant() -ne $fingerprint) { throw 'Pinned GitHub certificate differs. Restore the matching key; automatic key rotation is forbidden.' }
    }
    $existingSecrets = @(Invoke-OwnerGh -Arguments @('secret','list','--repo',$Repository,'--env','android-release','--json','name') | ConvertFrom-Json)
    if ($pins.Count -eq 0 -and @($existingSecrets | Where-Object { $_.name -like 'ANDROID_*' }).Count -gt 0) {
        throw 'Existing Android secrets have no public identity pin. Verify and pin their certificate manually before setup.'
    }
    # Establish the public pin first. Interrupted setup can be resumed only with the SAME key.
    Invoke-OwnerGh -Arguments @('variable','set','ANDROID_SIGNING_CERT_SHA256','--repo',$Repository,'--env','android-release','--body',$fingerprint) | Out-Null
    $secretValues = @{
        ANDROID_KEYSTORE_BASE64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($KeyPath))
        ANDROID_KEYSTORE_PASSWORD = $storePlain; ANDROID_KEY_PASSWORD = $keyPlain; ANDROID_KEY_ALIAS = $KeyAlias
    }
    foreach ($entry in $secretValues.GetEnumerator()) {
        Invoke-OwnerGh -Arguments @('secret','set',$entry.Key,'--repo',$Repository,'--env','android-release') -InputText $entry.Value | Out-Null
    }
    Write-Host 'Signing inputs uploaded. Existing exact-source device approval is unchanged; no workflow was dispatched and no APK was published.'
} finally {
    $env:MM_SIGNING_SETUP_STORE = $oldStore; $env:MM_SIGNING_SETUP_KEY = $oldKey
    $storePlain = $null; $keyPlain = $null; $secretValues = $null
    if ($storeSecure) { $storeSecure.Dispose() }; if ($keySecure) { $keySecure.Dispose() }
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
    # Never delete the permanent key, including after an upload failure.
}
