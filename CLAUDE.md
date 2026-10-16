# PC Clean — project notes

## Stack

- C# / .NET 9, **WinForms** (`net9.0-windows`), single project `PcClean`.
- No NuGet packages, no designer files — UI is built in code in `MainForm.cs`.
- `OutputType=WinExe`, `ApplicationHighDpiMode=PerMonitorV2`.

## Commands

- Build: `dotnet build PcClean\PcClean.csproj -c Release`
- Run:   `PcClean\bin\Release\net9.0-windows\PcClean.exe`
- Test:  `PcClean.exe --selftest`  (un-elevated, prints ALL TESTS PASSED)
- Inspect: `PcClean.exe --cli <path>` (exact per-node byte counts)

Note: the exe is a GUI-subsystem binary, so its console output only appears when
launched from a real terminal, or captured via `Start-Process -RedirectStandardOutput`.
`cmd` does not wait for GUI apps — use `powershell Start-Process -Wait` in scripts.

## Files

- `Scanner.cs` — `FileNode` model + recursive scan. Aggregates sizes/file counts
  bottom-up, sorts children by size descending, skips reparse points, tolerates
  access-denied per entry.
- `DeleteService.cs` — `CollectChecked` (top-most checked nodes only, so a checked
  folder covers its subtree) and permanent delete + `Detach` (removes node from the
  model and subtracts its size/count from ancestors). Delete is recursive, reports
  `DeleteProgress` (bytes/files/current path), and honors a `CancellationToken`
  (returns a partial `DeleteOutcome` with `Cancelled = true`).
- `CheckLogic.cs` — check/uncheck cascade rules (down on check, clear ancestors on
  uncheck).
- `MainForm.cs` — window wiring: toolbar (path field + Browse + Scan/Cancel), summary
  strip, size tree, footer (status + Delete). Validation/scan errors show inline in
  the summary strip, not as pop-ups.
- `Theme.cs` — dark palette, fonts, drawn glyphs (folder/file/chevron/checkbox),
  dark DWM title bar.
- `Widgets.cs` — `FlatButton`, `PathField`, `ProgressLine` (painted controls).
- `SummaryStrip.cs` — scanned-folder headline + drive meter (ticked bytes in red).
- `SizeTree.cs` — owner-drawn virtualised tree with share-of-folder bars, own
  scrollbar, keyboard nav (arrows, Home/End, PgUp/PgDn, Space ticks).
- `ThemedDialog.cs` — dark modal used for delete confirm, delete errors, admin notice.
- `Program.cs` — entry point: `--selftest` / `--cli` modes first, otherwise
  elevate-if-needed (`runas`) and show the form.
- `SelfTest.cs` — end-to-end checks used to verify the project.
- `Util.cs` — `FormatSize`, `Count` (pluralised counts), `NormalizeFolder`, `GetVolume`.

## Key decisions

- **Elevation at runtime, not via manifest.** `requireAdministrator` in a manifest
  would UAC-prompt on every launch including the test modes. Instead `Program.cs`
  checks admin and relaunches itself with `runas`; CLI/selftest modes skip elevation.
  Same end result for the user (app runs as admin), but keeps the logic testable.
- **Exact sizes.** Enumeration uses `AttributesToSkip = 0` (hidden + system files
  included) and `IgnoreInaccessible = false` so denials are counted, not hidden.
- **Volume roots are normalized (`Util.NormalizeFolder`).** A bare `L:` is
  drive-relative and resolves to the current directory *on that drive* (which,
  when elevated, is the exe folder) — so `L:` and `L:\` are both rewritten to
  `L:\` before scanning. Without this, scanning a partition silently scanned the
  wrong folder and appeared to show no folders.
- **Skip reparse points** (junctions/symlinks/mount points) — prevents loops and
  double counting; this is why totals can differ from other tools on such trees.
- **Permanent delete** was chosen deliberately (frees space immediately; Recycle Bin
  would not). Always confirmed via `ThemedDialog.Confirm`.
- **Checkbox cascade** lives in `CheckLogic` and is done against the model; the tree
  only reads `FileNode.Checked`, so folders that were never expanded work too.
  Checking cascades **down only** — checking a subfolder never checks its parent, so only the subfolder is a delete target.
  Unchecking also clears ancestors, so a checked parent can't cover an unchecked child.
- **Delete confirmation** lists each top-most target with its size and type (folder +
  file count, or file) plus the overall totals, so you see what and how heavy it is.
- **Progress + cancel.** Scan runs an indeterminate sweep on the progress rail (total
  unknown up front); delete fills the rail driven by `DeleteProgress`. The same **Cancel** button stops either
  operation. Delete is done by hand (not `Directory.Delete(path, true)`) so it can
  report progress and stop, clearing the ReadOnly bit first and **never following
  reparse points** (deletes the junction/symlink itself, not its target).
- **Volume readout** (`Util.GetVolume`) shows the target drive's free/total space and
  refreshes after a delete, so the freed bytes are visible as reclaimed space.
- **Custom dark UI** — everything is painted (no stock TreeView/MessageBox) so the
  whole app shares one palette. Amber = size weight, red = marked for delete.
  Feature set is still scan + inspect + delete only.
- **Delete confirm defaults to Cancel** — Enter never deletes by accident.
- `Util.FormatSize` uses 3 significant digits (1.53 GB / 15.3 GB / 153 GB) so
  columns don't jitter.
