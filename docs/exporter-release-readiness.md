# Exporter release readiness

This document distinguishes executable automation from a completed production release. A passing compile or emulator run is not evidence of real-game login on a physical phone.

## Build and verification paths

- `Android account exporter (preview)` runs the no-account C# suite, restores with transitive NuGet auditing, compiles the complete WebUI, tests signing preflight with a disposable key, publishes a Windows x64 self-contained ZIP, starts the shipped Windows launcher in an isolated offline directory, checks the real export UI and shutdown, and builds an ARM64 Android preview.
- `Android exporter runtime checks` builds a separate Debug-only `.devicetest` package and runs it on an Android emulator. Synthetic credentials exercise the real Android SecureStorage across process restart, export copying/clear, repeated system document-picker cancellation, and native UI startup. The test entry point is not compiled into normal packages; Release builds reject the test flag.
- `Android exporter protected signing` is manual-only on the repository's master/development branch with the `android-release` environment. It requires exact-source device approval and a pinned certificate. Missing or mismatched inputs fail; there is no fallback to a newly generated debug key.

Every artifact must be tied to its exact source/build commit and checksum. The PR's final verification comment identifies the run actually inspected. Do not use a prior commit's green check as evidence for later executable changes.

## Runtime and protocol security

Android targets .NET/MAUI 10 (MAUI 9 support ended 2026-05-12). Shared libraries and existing desktop projects retain net9.0; the Android directory selects SDK 10 independently. MessagePack and its annotations are consistently pinned to 2.5.302, the upstream security backport, to preserve the existing MagicOnion 5 ABI. This is not a claim of migration to the actively developed 3.x serializer. Its future migration is a separate compatibility project, not a reason to suppress today's vulnerability diagnostics.

Protocol defaults use MessagePackSecurity.UntrustedData with a graph-depth limit of 128. The Standard resolver/map representation is retained; typeless deserialization is not enabled. Tests cover Int64 amounts, unknown server fields, equipment/master DTOs, compression, truncated input and depth rejection. They do not establish every live server schema or battle formula. NuGet audit failures and low/moderate/high/critical known vulnerabilities fail CI. Other compiler warnings remain. A clear audit means no advisory detected by that restore, not no possible flaw.

Primary references:
- https://dotnet.microsoft.com/en-us/platform/support/policy/maui
- https://github.com/MessagePack-CSharp/MessagePack-CSharp/releases/tag/v2.5.302
- https://learn.microsoft.com/en-us/dotnet/maui/android/deployment/publish-cli?view=net-maui-10.0
- https://developer.android.com/tools/apksigner

## Activate production signing (repository-owner administration required)

1. Create GitHub environment `android-release`, restrict deployment branches to master and feature/android-account-exporter, and require a trusted reviewer. An environment name in YAML does not prove that protection rules were configured.
2. Generate a long-lived Android signing keystore outside the repository with keytool. Keep encrypted offline backups of the keystore/password. Use the same password for the store/key with the provided script. Never put keys in a PR, artifact, source ZIP, chat or public issue.
3. Store ANDROID_KEYSTORE_BASE64 and ANDROID_KEYSTORE_PASSWORD as environment secrets. Set environment variables ANDROID_KEY_ALIAS and ANDROID_CERT_SHA256 to the alias and SHA256 of the DER-encoded signing certificate, not the APK or keystore hash.
4. Complete the physical-device matrix below against the exact revision. Record evidence without credentials. A trusted reviewer sets DEVICE_VALIDATED_SOURCE_SHA to the complete commit SHA. This is a maintainer attestation, not an automated physical-device test.
5. Run the manual signing workflow on the approved revision. The key is available only in the signing step, outside the checkout, without diagnostic/binlogs; its temporary directory is removed in finally. Verify the APK v2 signature, pinned identity, non-debuggable manifest, version and checksum before distribution. Never distribute the disposable preflight key.

Current connector access does not provision environment secrets or reviewer protection rules. Do not describe signing as activated just because the workflow exists.

The earlier 0.2/0.3 preview certificates were ephemeral and their private keys were not retained. A new stable certificate cannot cover-install over them. Preserve your own transfer code/password before intentionally uninstalling the exporter only, then use the stable identity for subsequent upgrades. Never uninstall the game for this process.

## Physical-device acceptance record (not automatically completed)

Record device/OS, source SHA, APK SHA256, certificate SHA256, versionCode, timestamp and outcome for fresh install; transfer login/world selection; restart/saved account restore; account/world switching; network failure/retry; all-eight-section JSON/ZIP; local document save; cancelled save/retry; share/return; explicit account removal; upgrade retaining credentials with the same stable certificate.

Never send passwords or raw login configuration with evidence. Emulator checks do not cover a physical vendor keystore, actual document-provider write, another app's receipt of sharing or a real game-server session. No real account is supplied to CI.

## Windows package

Extract the entire MementoMori-Exporter-win-x64-preview.zip and run START-EXPORTER.cmd. The .NET runtime is included. Existing private login configuration stays in %LOCALAPPDATA%/MementoMoriExporter/appsettings.user.json; only whitelisted fields are read. Portable mode uses in-memory runtime options, disables automatic jobs/report uploads, binds only 127.0.0.1, and does not expose full Helper routes. No user configuration is included in the ZIP.

Start-Exporter.ps1 -OfflineCheck serves the real UI without reading credentials or initializing the game network. Export requests deliberately return 503. Only this isolated offline mode enables console diagnostics. CI checks the shipped launcher from a path with spaces and validates shutdown/checksums. This is not live Windows login, a GUI/browser acceptance test or Authenticode signing.
