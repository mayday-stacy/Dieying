# Settings and updates

## What Dieying remembers

Between launches, Dieying keeps the window size and maximized state, recent files, language, separate JPEG and WebP export quality, view options, Auto Select, subject detection and resampling choices, the History panel layout, changed keyboard shortcuts and whether AI control is allowed. Tool settings and colors carry from tab to tab within a session but start fresh at the next launch.

## Where files live

| Data | Windows | Linux defaults | macOS defaults |
| --- | --- | --- | --- |
| Settings | `%APPDATA%\dieying` | `~/.config/dieying` | `~/Library/Application Support/dieying` |
| Cache | `%LOCALAPPDATA%\dieying` | `~/.cache/dieying` | `~/Library/Caches/dieying` |

Recovery copies live in the cache folder's `recovery` subfolder. Linux respects `XDG_CONFIG_HOME` and `XDG_CACHE_HOME`. The platform paths describe the source implementation; Windows portable builds are the current distribution focus.

Set `DIEYING_DATA_DIR` to an **absolute path** to keep settings and cache in its `config` and `cache` subfolders. Relative paths are rejected. `scripts/windows.ps1 -Action Run` uses `artifacts/local-data` unless the caller supplies an override, so source development does not use the normal profile. The previous `IMAGE_EDITOR_DEV_DATA_DIR` override remains a fallback when the new variable is unset.

Dieying does not read `COMPOSA_DATA_DIR` or copy Composa's settings. When the new default profile has no settings file, it may read preferences from the adjacent `image-editor-dev` profile once; subsequent saves go to `dieying` without changing the old file. Existing development recovery caches stay in their original folder: use the older editor to recover and save those documents, then open the `.cmps` files in Dieying. Recovery caches are never silently migrated or removed by renaming the application.

The MCP pipe is separate from Composa's: `dieying-mcp` on Windows, and `mcp.sock` in Dieying's cache folder on Linux/macOS. See [AI control](ai-control.md) for custom connection settings.

## Updates

Current Dieying builds use the **local** update channel. They do not query, download or install Composa releases, and Help > Check for Updates explains that a source build must be rebuilt. The inherited GitHub update implementation remains covered by tests using fake releases and network responses; it is not an enabled Dieying distribution service.

To update a portable copy, save and close your documents, extract a newly verified package into a new folder and run its `dieying.exe`. Keep the whole package together. Settings stay in the profile above; an older application folder is not that profile. There is no automatic file replacement or installer in the portable workflow.

A future public update channel needs Dieying's own repository, asset names, verification and release tests. See [the release procedure](../RELEASING.md); do not point the current build at an upstream Composa download.

## If something goes wrong

An unexpected error shows a "Something went wrong" dialog with the error and advice to save a copy with File > Save As. The document stays open. A crash leaves recovery copies, which the next launch offers to restore. Recovery is a fallback rather than a replacement for saving your project regularly.
