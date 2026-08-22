# Local sanitized account export

This fork adds a loopback-only account export endpoint and a simple browser UI for selecting which account sections to export.

## Important safety notes

- This project is an unofficial client/helper. Using it may violate the game's rules or terms and can carry account risk.
- Export payloads are built from a whitelist and do **not** include `ClientKey`, transfer passwords, auth/session tokens, player/user IDs, or raw character/equipment GUIDs.
- Duplicate character instances use a snapshot-local `instanceIndex` so decks, Level Link, and equipment can distinguish copies without exposing GUIDs.
- The endpoint still performs the helper's normal unofficial login flow if the account is not already logged in.
- The gacha section performs `Gacha/GetList`, a list/read request. It does not issue `DrawRequest`. If the gacha section is not selected, `Gacha/GetList` is not called.
- `GameConfig.AutoJob.DisableAll` must be `true`; the PowerShell launcher refuses to continue otherwise.
- The launcher binds ASP.NET Core to `127.0.0.1`, and the export/UI endpoints additionally reject non-loopback requests.
- Manual mutating buttons elsewhere in the WebUI still exist. This fork does not make the whole helper globally read-only.

## Schema v3.1

Schema: `mementomori-safe-account-export-v3.1`

Selectable sections:

- `player` — player name/rank/exp/VIP
- `progress` — main quest progress and resolved quest memo
- `levelLink` — Level Link state and members
- `characters` — raw/effective level, rarity, element, job, battle power and battle parameters
- `equipment` — equipment, reinforcement, sacred/magic treasure state, additional parameters and resolved runes
- `decks` — saved decks and snapshot-local character instance references
- `items` — inventory/resources with resolved names and rarity where available
- `gacha` — current gacha cases, draw count, ceiling count, bonus count, selected characters and costs

The JSON always includes schema/source metadata plus `selectedSections`; unselected account sections are omitted.

`characters` and `equipment` are intentionally separate in v3.1. This keeps routine account snapshots smaller while preserving the ability to export full gear/rune detail when it is actually needed.

## Recommended UI workflow

Double-click:

```text
tools\safe-export\Open-MementoMori-Export-UI.bat
```

The browser page provides these presets:

- **All** — every section
- **Light account** — player, progress, Level Link, characters, decks and items; no equipment detail and no gacha request
- **Quest analysis** — player, progress, Level Link, characters, equipment, decks and items; no gacha request
- **Character development** — player, Level Link, characters, equipment and items
- **Gacha analysis** — player, characters, items and gacha

You can also select sections individually.

### Output formats

- **Compact JSON (recommended)** — same data as pretty JSON, but without indentation/extra whitespace; best for sending to ChatGPT and smallest on disk
- **Pretty JSON** — indented human-readable JSON
- **ZIP split by section** — `manifest.json` plus one JSON file per selected section; extracted JSON files are pretty-printed for inspection

The UI reports the downloaded file size and can automatically stop the temporary export server after the download.

The UI launcher tries the existing runtime first. If that runtime is older or missing, it automatically retries with a full publish.

## HTTP examples

All sections, compact JSON (default):

```text
GET /safe-export
```

Selected sections, compact JSON:

```text
GET /safe-export?sections=player,progress,characters,decks,items&format=json&style=compact
```

Pretty JSON:

```text
GET /safe-export?sections=characters,equipment&format=json&style=pretty
```

ZIP split by module:

```text
GET /safe-export?sections=characters,equipment,items&format=zip
```

## Automatic full JSON workflow

The original one-click full export is still available:

```text
tools\safe-export\Export-MementoMori-Account.bat
```

It exports all sections as compact JSON and writes:

```text
<working-root>\mementomori-account.json
```

You can also run PowerShell directly:

```powershell
& .\tools\safe-export\Export-MementoMori-Account.ps1 -RestartOriginal
```

Interactive UI mode:

```powershell
& .\tools\safe-export\Export-MementoMori-Account.ps1 -Interactive -RestartOriginal
```

Optional switches:

```text
-SkipPublish
-KeepHistory
-RestartOriginal
-Interactive
-ReleaseDir <path>
-Port <port>
```

## Local release/config layout

The script derives the source repository root from its own location. By default it expects the original release beside the source checkout at:

```text
<working-root>\MementoMoriHelper-v1.14.2\app\publish-win-x64
```

The release `appsettings.user.json` is copied only into the temporary runtime. Credential values are never printed, and the temporary copy is deleted in `finally` cleanup.

`appsettings.user.json`, generated account JSON snapshots, runtime directories, backup directories, and logs are ignored by git.
