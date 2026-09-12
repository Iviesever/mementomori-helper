# PR #7 / #8 integration notes

This branch incorporates PR #7 through de4edf0 without overwriting it. The ProtocolSerialization module initializer and its UntrustedData/128-depth tests, portable credential whitelist, and isolated *.devicetest package remain active.

The packaged Windows launcher supports both root/apphost and app/apphost layouts and the existing ConfigPath parameter. Live portable mode exposes export routes only, uses whitelisted in-memory options, and disables background jobs/reporting. Offline checks branch before game-service initialization and accept bare or =true flags. No real credentials are read in offline tests. Final ZIP checks run after extraction to a path containing spaces, in addition to directory-mode startup checks.

The sole stable-signing entry is exporter-stable-signing.yml. The superseded workflow and key-in-build script were removed. Test-AndroidSigning.ps1 exercises the replacement signer with a disposable key, including actual Release input and failure cleanup. Stable builds have one increasing versionCode sequence (100000 + that workflow's run number), run the no-account suite without secrets, and bind same-run source/hash metadata before a separate signing job. Existing alias/certificate environment-variable names remain accepted for migration. Exact-source device approval and environment protections are not relaxed or automatically provisioned.

Emulator reports now include source/build SHA, test APK SHA256 and Android API/ABI, and explicitly distinguish emulator tests from physical-device or real-game validation. Test hooks are not compiled into ordinary APKs. A native UI screenshot alone is not proof of full account acceptance.

The original source-based Windows launcher remains available. Abruptly killed portable sessions can leave their private temporary directory; normal shutdown removes it, and original configurations are never deleted. Per-session master-cache downloads remain documented rather than presented as solved. No actual account export, password or stable signing private key enters this PR.
