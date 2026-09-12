# Actual activation evidence (in progress)

Local checkout: D:/program/memento/mementomori-helper. Resumed clean PR #9 branch at bf199f6afa2cbc8b2cde28a4d8bf748487b7b8ed, without resetting any ref. Since #8 and #7 are already merged, retargeted #9 to feature/android-account-exporter; GitHub reports mergeable. Master remains untouched.

GitHub CLI uses the owner's Windows keyring login with repo/workflow scopes and this repository's admin permission. Created android-release and read it back: required reviewer Iviesever, can_admins_bypass=false, custom branch policy permitting only master and feature/android-account-exporter. No device approval was supplied. Repository secrets, variables, environments and stable releases were absent at initial inspection. No signing key exists in the inspected project/default Android locations or tracked Git history. This is a bounded inspection, not a scan of unrelated private disks.

Tools reused: Git, GitHub CLI, .NET SDK 9.0.317/10.0.400, Microsoft JDK 17 keytool, Python 3.13, PowerShell 7, Android SDK build-tools 36/37 and adb. No local MAUI workload or JDK 21 was found; existing CI supplies the release build toolchain, so the whole environment was not reinstalled. adb reports no attached device.

Actual local checks:
- dotnet test tests/Exporter.Android.Tests/Exporter.Android.Tests.csproj -c Release: 138 passed, 0 failed, 0 skipped; 180 compiler warnings, no errors. TRX: artifacts/resume/local-tests/resume-tests.trx.
- Python update/identity suite: 12 passed.
- Test-ApkSigningIdentity.ps1: 4 accepted fixtures and 10 rejection cases passed.
- Initializer exercised with a disposable local PKCS12: create, private-key CSR proof, verified backup, reuse without key replacement. Found and fixed newline handling for DPAPI files. No test key was uploaded to GitHub or used for a daily APK.
- Password UI permission setup exposed PowerShell Set-Acl requesting SeSecurityPrivilege; changed to DACL-only icacls and verified only the owner/SYSTEM have access, without elevation.

Downloaded and inspected exact bf199f6 emulator run 34678231014: API35 Android15 x86_64, isolated .devicetest; actual adb install -r 1001->1002, same certificate, distinct APK hashes; no uninstall/clear between versions; saved synthetic credentials and previous export readable. Six phase reports pass. physicalDevice=false and realGameLogin=false. These are prior exact-source executed CI results, not a new physical-phone test.

Downloaded old preview from run 34677465273 and independently ran aapt, apksigner and SHA256: normal package io.github.iviesever.mementomori.exporter; versionCode 64, display 0.4.0, ARM64, debuggable; certificate 93302217987f99128f72e208b5855da57919fd2bb3905ed613ecbd5848f68656; APK SHA256 54d17c252a0f3175204cfeec6b37cd677bcaf699912946b86c3f58631334be63. Metadata identifies ephemeral-development-key. This does not identify the user's unattached phone. Existing preview workflow sequence is 68; no stable workflow runs/releases exist, so initial stable 100001 is above inspected preview versions.

Owner delegated choosing a backup folder: D:/MementoMori-Signing-Backup, created with owner/SYSTEM-only ACL. Primary directory: %LOCALAPPDATA%/MementoMoriExporterSigning. Owner entered the password in the masked local window; only current-user DPAPI ciphertext is retained. Created exporter.p12 once, verified its backup bytes and a private-key CSR operation from the backup. Public certificate SHA256: c8432c06c843476a846eeb23eaf5b85ec27733e1829b0eb3d2c444c10af1e891. Uploaded all four ANDROID secrets through stdin and pinned this certificate in android-release; read-back verified names/timestamps and the public pin. No device approval was set. Backup on this same computer is not an independent disaster-recovery copy. Portable PKCS12 recovery also requires the separately retained password. Both private folders contain RECOVERY.txt without secret content.

Added a manual-only, default-off physical acceptance artifact option to the existing preview workflow, using the already isolated .dev APK. It binds the exact dispatch source, clearly labels PHONE-ACCEPTANCE-ONLY, and never exposes the stable key. This closes the bootstrap gap where physical acceptance was required before a daily signed APK could exist. It does not claim normal-package upgrade preservation.

The existing master push workflow could automatically publish public desktop releases and Docker images. For this fork, both publication jobs now require explicit manual publish_release=true; upstream behavior is retained. This allows requested master workflow registration without implicitly publishing an unapproved release. The normal exporter runtime/package and Windows outer launcher are unchanged.

Remaining: CI and development integration, explicit master approval to register workflow, precise-source physical acceptance, legitimate environment approval, stable APK construction and verification. Signing inputs are provisioned, but no daily APK has yet exercised the protected workflow. Do not deliver the old/test APK as the requested result.
