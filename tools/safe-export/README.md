# Local sanitized account export

This fork branch adds a local `GET /safe-export` endpoint to the WebUI and a reusable Windows launcher.

## Important safety notes

- This project is an unofficial client/helper. Using it may violate the game's rules or terms and can carry account risk.
- The export endpoint is designed as a whitelist serializer and does **not** export `ClientKey`, transfer passwords, auth/session tokens, player/user IDs, or raw character/equipment GUIDs.
- The endpoint still performs the normal unofficial login flow if the account is not already logged in.
- The gacha section performs `Gacha/GetList`, a list/read request. It does not issue `DrawRequest`.
- `GameConfig.AutoJob.DisableAll` must be `true`; the supplied PowerShell script refuses to continue otherwise.
- Manual mutating buttons in the WebUI still exist. This branch should not be described as making the whole helper read-only.

## Exported data

Schema: `mementomori-safe-account-export-v2`

The JSON includes:

- player name/rank/exp/VIP (no player ID)
- main quest progress and resolved quest memo
- Level Link state and members
- character raw level and effective Level Link level
- current rarity, base rarity, element, job, battle power and battle parameters
- equipment, reinforcement, sacred/magic treasure state, runes with resolved names/levels/types
- saved decks
- inventory with resolved names and rarity where available
- current gacha cases, draw count, ceiling count, bonus count, selected characters and costs

Duplicate character instances receive a snapshot-local `instanceIndex`. Decks and Level Link can reference this index without exposing raw GUIDs.

## Windows usage

The scripts are in this directory:

- `Export-MementoMori-Account.ps1`
- `Export-MementoMori-Account.bat`

The PowerShell script derives the repository root from its own path. By default it expects the original release beside the source checkout at:

```text
<working-root>\MementoMoriHelper-v1.14.2\app\publish-win-x64
```

and writes the sanitized snapshot to:

```text
<working-root>\mementomori-account.json
```

Double-click the BAT for the normal workflow. It tries the existing published runtime first and automatically retries with a full publish when necessary.

You can also run the PowerShell script directly:

```powershell
& .\tools\safe-export\Export-MementoMori-Account.ps1 -RestartOriginal
```

Optional flags:

```powershell
-SkipPublish
-KeepHistory
-RestartOriginal
```

If your original release lives elsewhere, pass `-ReleaseDir` explicitly.

## Local config

The script uses the existing release `appsettings.user.json` only by copying it into a temporary runtime directory. It never prints credential values and deletes the temporary copy in `finally` cleanup.

`appsettings.user.json`, export JSON snapshots, runtime directories, and logs are ignored by git.
