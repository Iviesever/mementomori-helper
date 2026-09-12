# Desktop export projection v3.1.1

Follow-up to Android 0.3.1 / PR #5. This revision connects the desktop export path to the same metadata corrections. The earlier Android changelog describes the historical 0.3.1 delivery; its desktop-pending note is superseded by this follow-up once merged.

## Corrected desktop output

- `source.exporter` identifies `selective-ui-v3.1.1`; `helperCommitKind=upstream-baseline` makes clear that helperCommit is not the exporter build revision.
- Missing sphere records retain sphereId and return null for unknown level/isAttackType, with `metadataStatus=unavailable`. A missing translation preserves the actual level, type, rarity and attack flag and is marked unresolved.
- Equipment slot and rarity are resolved before translating the equipment name, so a translation failure does not erase known metadata.
- EquipmentFragment rarity resolves the composite record's EquipmentId before looking up equipment. A missing composite does not fall back to an unrelated same-numbered equipment record.
- Inventory retains original type, itemId, count and name/resource key, adding resolved/unresolved metadataStatus. This flags missing translation rather than inventing names or dropping inventory entries.
- Missing gacha response/list is distinct from a valid explicitly empty list. Read failures retain the exception type only; cancellation propagates rather than producing a successful partial export.

## Compatibility and unchanged behavior

The eight-section v3.1 JSON/ZIP contract, link-member file, property names, selection aliases, compact/pretty options and loopback restriction are preserved. Consumers must accept null for missing sphere metadata, as already required by Android 0.3.1. Character stats, inventory counts, login, transfer credentials and automatic-job behavior are not changed in this revision. Exports are not changed to automatically refresh account data.

Android remains version 0.3.1: this follow-up does not change the Android application or require reinstalling it. Any CI-generated APK is still an ephemeral-development-signature validation artifact, not a new stable-signed release.

## Validation

`BuildSnapshotDataAsync` is the actual desktop projection used by the HTTP endpoint. Tests pass synthetic UserSyncData and a controlled list reader, avoiding login or network requests. Temporary synthetic master tables are isolated from concurrent tests and restored afterward.

The new tests cover the actual desktop inventory/equipment/gacha projection, missing and untranslated sphere metadata, real composite-to-equipment mapping, partial list failure/cancellation, section selection, preserving raw IDs/counts in safe projections and excluding private instance GUIDs. These supplement, rather than replace, the existing Android storage/login tests and serializer/ZIP parity tests.

CI also builds the complete MementoMori.WebUI project, not just its linked export source in the test assembly, and rebuilds the Android APK. Test-build, desktop-build and Android-build diagnostics are retained, including on failure. Use the current commit's Actions result for outcomes; this document does not assert a test run has already passed.

## Still outside this validation

No real game account, production master download, battle-math comparison, Windows launcher/device login, Android SecureStorage, save dialog or share target is exercised. This revision does not fix the upstream ItemUtil for unrelated callers; both safe exporters explicitly route fragment rarity through ExportMetadata. Stable signing and dependency-security review remain release prerequisites. No real account exports or credentials are committed as test fixtures.
