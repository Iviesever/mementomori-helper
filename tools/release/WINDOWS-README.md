# Windows x64 exporter preview

Extract the entire ZIP before running `START-EXPORTER.cmd`. It includes its .NET runtime; no SDK, git checkout or separately installed .NET runtime is needed. This is an unsigned Windows preview, not an Authenticode-signed installer.

Normal use needs your existing private desktop login configuration at `%LOCALAPPDATA%\MementoMoriExporter\appsettings.user.json`. It must contain a valid AuthOption. Portable mode forces GameConfig.AutoJob.DisableAll=true independently of the original file. Alternatively run `Start-Exporter.ps1 -ConfigPath <your-private-path>`. Never send this file to chat, GitHub or a bug report.

The launcher copies only AuthOption into a per-launch private working directory and forces automatic jobs and battle-log reporting off. It binds to 127.0.0.1, opens the local export UI, and discards inherited protocol console output. Close via the UI shutdown button. The launcher removes its own temporary working directory after the child exits; it never removes your original configuration or exports. Closing/killing the launcher abruptly can leave the private session directory behind. Master data is currently fetched again for each isolated session.

The existing source-based launcher remains available in the repository. This packaged launcher does not rely on its cached runtime, so it cannot silently use an older shared assembly after an update.

## Offline check

`START-EXPORTER.cmd -OfflineCheck` launches the actual bundled executable and export UI without constructing game services, reading the saved login configuration, or contacting game servers. Export deliberately returns HTTP 503 with `offline_validation_mode`. This validates packaging, the apphost, local UI, and shutdown, NOT real login, account data or battle calculations.

CI also runs the PowerShell launcher with `-OfflineCheck -NoBrowser`, checks the explicit offline response header and export refusal, then requests graceful shutdown. See BUILD-INFO.json and FILE-SHA256SUMS.txt for this package's exact source/build revision and per-file hashes.

The package does not add direct transfer-code login to the desktop UI. Phone transfer-code login, saved accounts, all eight default export selections and JSON/ZIP behavior remain unchanged.
