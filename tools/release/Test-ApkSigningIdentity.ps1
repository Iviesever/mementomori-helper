$ErrorActionPreference = 'Stop'
$parser = Join-Path $PSScriptRoot 'Get-ApkSigningIdentity.ps1'
$a = 'a' * 64; $b = 'b' * 64
$header = "Verifies`nVerified using v2 scheme (APK Signature Scheme v2): true`nNumber of signers: 1`n"
$regular = $header + "Signer #1 certificate SHA-256 digest: $a`n"
$ranged = $header + "Signer (minSdkVersion=33, maxSdkVersion=2147483647) certificate SHA-256 digest: $a`nSigner (minSdkVersion=28, maxSdkVersion=32) certificate SHA-256 digest: $a`n"
foreach ($valid in @($regular, $ranged, ($regular + "Source Stamp Signer certificate SHA-256 digest: $b`n"))) {
    if ((& $parser -Report $valid) -ne $a) { throw 'Valid signing report was not parsed exactly.' }
}
$invalidReports = @(
    '',
    ($regular -replace 'Verifies', 'DOES NOT VERIFY'),
    ($regular -replace 'signers: 1', 'signers: 2'),
    ($regular -replace ': true', ': false'),
    ($header + "Source Stamp Signer certificate SHA-256 digest: $a`n"),
    ($header + "Signer #1 public key SHA-256 digest: $a`n"),
    ($regular + "Signer #2 certificate SHA-256 digest: $b`n"),
    ($header + "Signer #1 certificate SHA-256 digest: abcdef`n")
)
foreach ($invalid in $invalidReports) {
    $rejected = $false
    try { & $parser -Report $invalid | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Invalid or ambiguous signing report was accepted.' }
}
Write-Host 'PASS: 3 valid signing-report fixtures and 8 rejection cases; no real keys used.'
