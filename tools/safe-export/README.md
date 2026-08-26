# Local sanitized account export

This fork adds a loopback-only account export endpoint and a browser UI for selecting which account sections to export.

## Fastest Windows workflow

From the repository root, double-click:

```text
START-ACCOUNT-EXPORTER.vbs
```

This is the preferred launcher because it does not leave a terminal window open. If Windows has VBScript disabled, use the fallback:

```text
START-ACCOUNT-EXPORTER.bat
```

The browser UI opens automatically when the local exporter is ready.

### V3.2 UX behavior

- the launcher is in the repository root; no need to find `tools\safe-export\...`
- normal launch runs without a persistent console window
- the runtime cache is reused when it is current
- if source files changed, the launcher rebuilds the runtime once instead of silently using an old cached UI
- the exporter now stays open after a download by default
- the Export button is re-enabled after each successful download
- you can export repeatedly in the same session without restarting and repeating the helper login / initialization each time
- when finished, click **用完后关闭导出器** in the browser to stop the local server and remove the temporary runtime login config

The first launch after pulling source changes can still take longer because the modified WebUI must be published once. Subsequent launches reuse the cached runtime until the source changes again.

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

`characters` and `equipment` are intentionally separate. This keeps routine account snapshots smaller while preserving full gear/rune detail when needed.

## Browser presets

- **Quest analysis** — player, progress, Level Link, characters, equipment, decks and items; no gacha request
- **Character development** — player, Level Link, characters, equipment and items
- **Light account** — player, progress, Level Link, characters, decks and items
- **Gacha analysis** — player, characters, items and gacha
- **All** — every section

You can also select sections individually.

### Output formats

- **Compact JSON (recommended)** — smallest file and best for sending to ChatGPT
- **Pretty JSON** — indented human-readable JSON
- **ZIP split by section** — `manifest.json` plus one JSON file per selected section

## Advanced / legacy launchers

The older UI launcher remains available:

```text
tools\safe-export\Open-MementoMori-Export-UI.bat
```

The original one-click full export is also still available:

```text
tools\safe-export\Export-MementoMori-Account.bat
```

It exports all sections as compact JSON and writes:

```text
<working-root>\mementomori-account.json
```

PowerShell can be run directly as well:

```powershell
& .\tools\safe-export\Export-MementoMori-Account.ps1 -Interactive -RestartOriginal
```

## Local release/config layout

The script derives the source repository root from its own location. By default it expects the original release beside the source checkout at:

```text
<working-root>\MementoMoriHelper-v1.14.2\app\publish-win-x64
```

The release `appsettings.user.json` is copied only into the temporary runtime. Credential values are never printed, and the temporary copy is deleted during cleanup.

Generated account snapshots, runtime directories, backup directories, and logs are ignored by git.
