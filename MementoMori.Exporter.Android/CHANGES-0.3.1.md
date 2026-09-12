# 0.3.1 Android export corrections

This change fixes the Android exporter. An attempted desktop SafeExport.cs write was blocked by the tool safety check; that write was not retried through another route. Desktop projection and ItemUtil remain unchanged. ExportMetadata is placed in the core project for reuse, but only Android calls its corrections in this revision.

## Changes

- Use AppInfo's actual app version for the screen label and source.exporter. source.helperCommit is explicitly identified as an upstream baseline, not the exporter build revision.
- Missing sphere metadata produces null level/isAttackType instead of fabricated zero/false values, with metadataStatus=unavailable. A missing translated name does not erase a known sphere level, rarity or type.
- Inventory names/rarities keep their original values and gain metadataStatus=resolved/unresolved. Missing translations and the localized unknown marker are flagged, not guessed. This does not supply names absent from the game master data.
- EquipmentFragment rarity resolves EquipmentCompositeMB(fragment ID).EquipmentId before reading EquipmentMB. Missing mappings do not fall back to an unrelated equipment record.
- Distinguish a missing gacha response/list from a valid explicitly empty list. Cancellation is propagated instead of being reported as a successful partial export.
- Generated-file summary retains a warning when metadata is unresolved or gacha listing failed. Saving/sharing does not erase that summary.

## Preserved behavior

All eight sections remain selected on launch, including gacha. Transfer-code/password login and secure saved accounts are unchanged; the transfer password is not persisted. Saving JSON/ZIP and sharing still use the generated file. No automatic drawing, reward claiming or battling is added. Exporting does not automatically refresh the previously read account snapshot.

The v3.1 category/field contract is retained, with additive metadataStatus/helperCommitKind fields. Consumers must accept null for unavailable sphere level/isAttackType; old fabricated values must not be interpreted as accurate metadata. This revision does not change character stats, inventory counts or account identifiers.

## Verification boundaries

The new no-account tests exercise metadata helpers, failure semantics, sensitive-property rejection and the real desktop/Android JSON serializers and ZIP packers using synthetic payloads. They do not claim that both live projections are now patched or that game login/Android SecureStorage/file pickers have been tested. Refer to the current commit's CI results for the actual test and packaging outcome, not the preceding 0.3.0 run.

The APK still uses an ephemeral development certificate. Compare actual certificates before assuming an older APK can be updated in place. Uninstalling the exporter deletes its saved credentials; retain your own transfer details first and do not uninstall the game itself.
