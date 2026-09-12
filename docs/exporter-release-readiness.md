# Exporter 0.4.1 release readiness

This document distinguishes executable automation from an activated production release. A passing compile or emulator run is not evidence of real-game login on a physical phone. Exact CI outcomes belong in the PR validation record, not an assumed test count here.

## Build and verification

- `Android account exporter (preview)` executes the no-account C# suite, transitive NuGet audit, and self-contained Windows publication. It tests the shipped launcher/apphost before packaging AND after extracting the final ZIP into a path with spaces: actual export UI, offline export refusal, graceful shutdown and checksums. It then builds an isolated `.dev` ARM64 APK and the real Release signing input for internal verification only. It does not upload either as a daily-use Android package. Disposable-key tests exercise the actual signing script and failure cleanup; test keys are deleted, not distributed.
- `Android exporter runtime checks` builds a separate Debug-only `.devicetest` package. It builds different versionCode 1001/1002 APKs, writes synthetic credentials and files in the old version, installs the new version with `adb install -r` without uninstall/data clear, and verifies old credentials can still be decrypted and old export bytes read. Synthetic data also exercises Android SecureStorage across process restart, private export copying/clear, repeated system document-picker cancellation and native UI startup. Reports include the source/build commits, test APK hash, Android API/ABI and explicit emulator/real-account flags. Normal packages do not compile this entry point; Release rejects the device-test flag.
- `Exporter protected stable signing` (`exporter-stable-signing.yml`) is the ONLY stable signing workflow. It is manual-only on master or feature/android-account-exporter. A key-free build job runs the regression suite, Release compilation and audit. A separate android-release job verifies same-run source/hash provenance and exact-source device approval before accessing the pinned key. Missing/mismatched inputs fail, debuggable input is rejected, and there is no fallback key. Stable versionCode uses one sequence: 100000 + this workflow's run number. The superseded key-in-build workflow/script and preflight-only test were removed.

Every artifact identifies its exact source/build commit and checksum. A temporary PR merge commit is not evidence that the PR has merged. Do not reuse a prior commit's green checks after executable changes.

## Runtime and protocol security

Android targets net10.0-android / MAUI 10.0.101; run Android dotnet commands from its directory so the nested global.json selects SDK 10. Shared libraries and desktop projects remain net9.0. This is not a whole-repository .NET 10 migration.

MessagePack and its annotations use the upstream 2.5.302 security backport, retaining the existing MagicOnion 5 ABI. Protocol defaults apply UntrustedData with a graph-depth limit of 128, retaining the Standard resolver/map representation without typeless deserialization. Synthetic tests cover Int64 values, unknown fields/enums, DTOs, read-only master constructors, compression, truncation and depth rejection; they do not prove every live server schema or battle formula.

NU1900 through NU1904 block exporter CI, including audit failures and low/moderate/high/critical known vulnerabilities. Direct/transitive scan JSON is retained. A clean report means that scan found no known advisory, not that no potential vulnerability exists. Other compiler warnings remain visible. The original desktop MAUI project and its release workflow were not migrated; net9.0 planning and the eventual MessagePack 3 migration remain separate maintenance tasks.

Primary references checked September 12, 2026:
- https://github.com/MessagePack-CSharp/MessagePack-CSharp/releases/tag/v2.5.302
- https://dotnet.microsoft.com/en-us/platform/support/policy/maui
- https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core
- https://developer.android.com/tools/apksigner
- https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/manage-environments

## Activate stable signing: repository-owner administration required

1. Configure GitHub environment `android-release` with a trusted required reviewer and deployment branches restricted to master and feature/android-account-exporter. A YAML environment name does NOT configure these protections.
2. Use `tools/release/SETUP-ANDROID-UPDATES.cmd` locally (see `tools/release/ANDROID-UPDATES.md`), or generate a long-lived Android signing keystore outside the checkout with keytool. Keep encrypted independent backups. Never send private keys/passwords to chat, PRs, source ZIPs or artifacts.
3. Set environment secrets ANDROID_KEYSTORE_BASE64, ANDROID_KEYSTORE_PASSWORD, ANDROID_KEY_ALIAS and ANDROID_KEY_PASSWORD. Set environment variable ANDROID_SIGNING_CERT_SHA256 to the 64-character SHA256 of the DER public certificate, not the APK or keystore. Existing alias/certificate variables are supported for migration; an omitted key password uses the store password.
4. After physical-device acceptance of an exact revision, a trusted reviewer sets environment variable DEVICE_VALIDATED_SOURCE_SHA to its full commit SHA. This is a human attestation, not a flag the CI invents.
5. Once this reviewed workflow is on the default branch, dispatch the sole stable-signing workflow on an approved branch/revision. Review pinned-certificate, version, non-debuggable manifest, APK v2 signature and checksum output before distribution. The workflow produces an artifact, not an automatic GitHub Release.

The current connector does not provision environment secrets/reviewer rules. Implementing and testing the signer does not activate the real stable identity. Do not alternate workflows/versionCode schemes or reset run history without an explicit increasing-version migration.

Earlier 0.2/0.3 preview certificates were ephemeral and their private keys were not retained. A new stable identity cannot overwrite those packages in place. Before intentionally uninstalling ONLY the exporter, retain your private transfer details. Never uninstall the game. Subsequent stable updates must keep the key/app ID and increase versionCode.

## Physical acceptance not replaced by automation

Record source/version, APK/certificate hashes, device/OS, timestamp and pass/fail without credentials. Check transfer login/world selection; saved account after restart; account/world switches; network failure/retry; all eight JSON/ZIP categories; actual local document write; cancel/retry; share/return; explicit account removal; and upgrade of two stable-signed versions without losing saved accounts.

Emulator tests do not cover a physical vendor keystore, successful writes by every document provider, another app receiving the share, or a live game-server session. No real account is supplied to CI. Windows also requires live private-config login/export acceptance; offline checks do not establish it.

## Windows distribution

Extract the complete `MementoMori-Exporter-windows-x64-preview.zip`, then run `START-EXPORTER.cmd`. Its .NET runtime is included; this is an unsigned portable preview, not an Authenticode-signed installer. Private config normally stays at `%LOCALAPPDATA%/MementoMoriExporter/appsettings.user.json`, or can be passed using `Start-Exporter.ps1 -ConfigPath`.

The portable host applies the credential whitelist, keeps writable runtime options in memory, forces jobs/report uploads off, binds only 127.0.0.1 and omits full Helper routes. The launcher uses its own temporary session directory and never changes the original private config. Normal shutdown deletes that session directory; abrupt termination can leave it behind. Master data is currently fetched per isolated session.

`Start-Exporter.ps1 -OfflineCheck` starts the actual UI before constructing game services, without reading credentials or contacting game servers. Export deliberately returns 503. Only this offline mode enables bounded diagnostic output. No user's private configuration, export or signing key is included in either distribution.
