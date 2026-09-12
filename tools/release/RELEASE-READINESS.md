# Exporter 0.4.0 release preparation

The canonical acceptance and signing instructions are in [docs/exporter-release-readiness.md](../../docs/exporter-release-readiness.md).

There is only one stable-signing entry: `.github/workflows/exporter-stable-signing.yml`. The older `android-exporter-signed.yml`, key-in-build `Publish-SignedAndroid.ps1` and preflight-only test were removed. Do not recreate a second versionCode sequence. The retained `Test-AndroidSigning.ps1` tests real Debug and Release signing inputs with disposable keys, including pinned identity, input/version provenance, debuggable rejection and failure cleanup.

The stable workflow separates compilation from private-key access, requires the android-release environment and exact-source DEVICE_VALIDATED_SOURCE_SHA attestation, and refuses a missing or wrong identity. Actual environment required reviewers/branch restrictions, private long-lived key provisioning and physical-device approval remain repository-owner actions; neither YAML nor synthetic tests perform them. Never send the key or account passwords to chat or GitHub comments.

Normal preview CI verifies dependency reports, the no-account suite, Windows directory and final ZIP startup, Android preview/Release compilation, and the disposable signer. The emulator workflow tests actual Android storage/restart/picker cancellation in a separately named test package. Each CI artifact must be matched to its exact source/build revision and checksum before reporting success.

Keep the distinction between these automated checks and live game login, physical document-provider/sharing behavior, and stable-certificate cover installation. Historical preview private keys cannot be recovered by this workflow. No production release is implied by a passing PR.
