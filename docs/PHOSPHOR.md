# Phosphor appearance and Codex discovery

The Windows app stays in WPF. Its dashboard separates the scheduling switch from the verified app-server connection. An ON preference never establishes ownership or proves that a watcher is operational. Failed connections stay visible with their full error and retry instructions.

Appearance lives in the existing per-user `state.json`, alongside the executable cache. The queue and continuation journal keep their existing format and safeguards. The default is SuckerPunch Pink, with Green, Cyan, Ultraviolet and Amber presets, a validated `#RRGGBB` field, and the native Windows color picker. Valid changes preview immediately and save after a brief debounce; invalid input keeps the last valid color. Closing through Exit flushes settings. Window close/minimize behavior is configurable.

**The original SuckerPunch color was not found in the available repository, shared assets or accessible public repository list.** `ThemePalette.SuckerPunchPinkPreview` is deliberately named and marked `BRAND_COLOR_TODO`. Its `#FF4FA3` value is a preview, not an exact brand match. Replace that one token when the original palette is supplied.

`ThemePalette` owns the palette and contrast math; `PhosphorTheme` maps it to WPF resources. Custom colors get a readable accent-text variant with at least 4.5:1 contrast on the primary surface. Semantic success, waiting and failure colors stay independent. Only the terminal mark receives a small optional glow. Button hover transitions take 150 ms. Reduce Motion, Reduce Glow, Windows client animation preferences and high contrast take priority over visual effects. Native window chrome, scrolling, access keys and keyboard focus remain available.

## Executable discovery

Startup revalidates the saved native path first. If an update removed it, detection falls back to PATH, configured npm prefixes and known per-user/system installation directories. PATH enumeration is equivalent to `where codex` and `where codex.exe`, without executing launchers. Standard and nested npm vendor packages are resolved to their native binary. `npm root -g` runs through `node` and `npm-cli.js`, with separate process arguments, never an interpolated shell command. A bounded recursive fallback stays within known `@openai/codex` package directories (eight levels, 512 directories per root); it skips linked directories and never scans a drive.

Every selected candidate must have a native executable header and successfully return a `codex-cli` semantic version within eight seconds. A matching filename alone is insufficient. Missing npm is harmless. An edited or browsed manual override is validated explicitly and cannot silently switch installations during connection. Re-detect clears that override and starts a fresh search. Detected paths and versions are visible and selectable in Connection & Settings. The cache stores the path, not a permanent trust decision: connection validates it again.

Discovery only starts short-lived version commands. Connecting still requires the ownership checkbox and checks for competing Codex clients. Once an owned app-server process exists, executable controls stay locked; reconnection requires restarting the utility. No duplicate app-server is launched. The only transport change replaces the previous version subprocess with the shared bounded validator; scheduling, rate-limit permissions and turn delivery logic are unchanged.

Linux/macOS keep the existing Avalonia frontend and shared backend. The WPF appearance settings are Windows-specific. macOS now normalizes its protected `/var`, `/tmp` and `/etc` system aliases for repository conflict checks; project-created symlinks remain rejected.

## Verification and limits

Core tests exercise actual native child processes in paths containing spaces, cache fallback, explicit overrides, invalid versions, nonzero exits, custom npm trees, duplicate-server rejection, contrast, persisted appearance and watcher health presentation. Windows CI additionally installs the official Codex CLI and discovers it from its npm prefix without a supplied native path, then initializes its app-server and lists sessions in an isolated Codex home. No model turn is sent.

The WPF smoke check renders the real empty, reduced-motion, failed, connected, queue and narrow-window states. It discovers and connects a local protocol fixture through PATH with no manual path entry, checks preview/validation and saves appearance. Screenshots are uploaded as a CI artifact. Core persistence tests reopen the real JSON store in a new scheduler instance.

Physical-monitor DPI, assistive technologies, Windows login startup and actual user-session continuity still need acceptance checks on the user's Windows machine. This cloud host cannot control that machine. Existing Desktop conversation UUIDs must pass the separate continuity diagnostic on the machine holding their history. Desktop UI synchronization remains unsupported.
