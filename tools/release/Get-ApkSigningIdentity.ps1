param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Report)
$ErrorActionPreference = 'Stop'
# Only verified APK signer certificates, never source-stamp or public-key digests.
# Supported labels include traditional, SDK-ranged, and the SDK 36 V3.0 label.
if ($Report -notmatch '(?m)^Verifies\s*\r?$' -or
    $Report -notmatch '(?m)^Number of signers:\s*1\s*\r?$' -or
    $Report -notmatch '(?m)^Verified using v2 scheme \(APK Signature Scheme v2\):\s*true\s*\r?$') {
    throw 'A verified, single-signer APK with a v2 signature is required.'
}
$pattern = '(?m)^(?:Signer (?:#\d+|\(minSdkVersion=[^\r\n]+\))|V3\.0 Signer:) certificate SHA-256 digest:\s*([A-Fa-f0-9]{64})\s*\r?$'
$digests = @([regex]::Matches($Report, $pattern) | ForEach-Object { $_.Groups[1].Value.ToLowerInvariant() } | Sort-Object -Unique)
if ($digests.Count -ne 1) { throw 'APK must have exactly one unambiguous signing-certificate SHA256. Key rotation requires a separate reviewed policy.' }
$digests[0]
