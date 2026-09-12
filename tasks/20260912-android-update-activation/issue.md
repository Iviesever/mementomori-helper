# Android update continuity activation

Resume PR #9 at bf199f6afa2cbc8b2cde28a4d8bf748487b7b8ed. The initial local master was clean at 084ea08; origin feature is 4541d338ac55c16bdd3c81297d44932710190920. PR #8 and #7 are already merged; do not replay them.

Deliver an ARM64 daily APK with the normal package, persistent signing identity and increasing versionCode. Configure this repository's protected environment/secrets, retain an owner-side private key and backup, and verify actual APK provenance and account-preserving updates. Preserve all account/export functionality and the outer Windows launcher.

Owner authorizes setup, tooling, fixes, PR updates and development-branch integration. Master merges and public releases need explicit approval. Never fabricate device approval, clear phone data or uninstall the old app. Never store secrets/account data in Git, logs or public artifacts.
