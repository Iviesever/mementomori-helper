# Exporter 0.4.0 release preparation

This is a development PR, not an assertion that real game login or production signing was tested.

## Implemented in this change

- MessagePack and Annotations 2.5.192 -> 2.5.302, retaining the existing v2 protocol surface. The upstream v2.5.302 release combines security fixes from 2.5.205 and 2.5.301. Fixed standard-wire and actual game DTO regression tests cover integer precision, enum values, null, unknown fields, compression and read-only master-data constructors. The tests are synthetic, not captured user sessions; they do not prove every game-server payload compatible.
- Android moved from unsupported MAUI 9 to net10.0-android / MAUI 10.0.101. Run Android dotnet commands FROM MementoMori.Exporter.Android so its nested global.json selects .NET 10. Existing shared/desktop projects remain net9.0; the original desktop MAUI project and release workflow are not migrated by this change. Desktop .NET 9 migration is due before November 10, 2026.
- CI checks direct and transitive vulnerability reports. A failed/incomplete scan or known vulnerable package blocks the job; warnings are not suppressed.
- A self-contained win-x64 distribution includes an isolated launcher, file hashes and source/build provenance. CI runs the actual packaged launcher/apphost with an explicit offline check, checks the actual export UI and HTTP 503 refusal, and requests shutdown. This intentionally does NOT run account initialization, game networking or scheduled jobs.
- A separate manually dispatched workflow builds Release without the stable key, then signs in an android-release environment job. The key is supplied only to the signing step, stored temporarily outside artifacts and removed in finally. Certificate SHA256 is pinned and apksigner verification, including v2, is required. Debuggable APKs are refused. There is no private-key cache, repository key or fallback key.

## One-time stable-signing provisioning (not done by this PR)

Create a PRIVATE long-lived Android signing keystore using JDK keytool; keep an independent secure backup. Do not commit it, upload it as an artifact, or send it to a chat. Configure the GitHub environment named android-release with required review and deployment branch restrictions limited to master and feature/android-account-exporter. Store these as ENVIRONMENT secrets, not repository-wide secrets:

- ANDROID_KEYSTORE_BASE64
- ANDROID_KEYSTORE_PASSWORD
- ANDROID_KEY_ALIAS
- ANDROID_KEY_PASSWORD

Set environment variable ANDROID_SIGNING_CERT_SHA256 to the 64-character SHA256 digest of the expected public signing certificate. Configure the environment in GitHub settings BEFORE dispatching. Declaring an environment in YAML does not itself create required reviewers/protection rules. The workflow does not accept arbitrary source refs and does not run on PR events. After this reviewed workflow reaches the default branch, manually dispatch it on an approved branch at a commit whose CI passed. The result is a signed artifact only; it does not publish a GitHub Release automatically.

The installed 0.3.x preview certificates were ephemeral. This PR cannot recover their private keys or make a new key overwrite them. First migration to a new stable certificate may still require uninstalling ONLY the exporter after retaining your private transfer details. Future updates must retain the same key, app ID and increasing versionCode. Stable builds use 100000 + this signing workflow's run_number; preview CI builds use their own smaller run_number. Changing workflow identity/resetting history requires explicit versionCode migration.

## Runtime acceptance still requiring a real device/account

Record commit/version, device/OS and pass/fail locally; never include passwords, private IDs, raw protocol logs or configuration in a public report. Verify transfer login; restart/credential recovery; switching account/world; airplane-mode failure then retry; all eight sections selected; JSON/ZIP save/cancel/retry/share; clear account; and upgrading two consecutive stable-signed builds without losing saved accounts. Windows also needs a real private-config login/export check. Offline smoke checks are NOT substitutes for these steps.

No user exports or private credentials are added to the repository or used in CI. A missing translation remains marked unresolved, not invented. Existing export counts, character calculations, explicit-refresh behavior and saved-account semantics remain unchanged.

## Primary references checked September 12, 2026

- https://github.com/MessagePack-CSharp/MessagePack-CSharp/releases/tag/v2.5.302
- https://dotnet.microsoft.com/en-us/platform/support/policy/maui
- https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core
- https://learn.microsoft.com/en-us/dotnet/maui/android/deployment/publish-cli
- https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/manage-environments

CI results and artifact IDs belong in the PR validation record after the exact commit completes. Do not reuse prior PR test counts as this change's result.
