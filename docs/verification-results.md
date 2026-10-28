# Verification — 28 October 2026

## 0.1.3 command Data Matrix copy

- **39 automated tests passed**; all seven generated 1024px Data Matrix images decode to unique command envelopes that pass the bundled scanner SDK's checksum validation and select the expected command.
- **49 GUI smoke assertions passed**, including the third button's position and sizing, its copy/confirmation wiring, and PNG dimensions/resolution. Actual saved PNGs also decode correctly after downsampling to 256px.
- Main window, shortened command info, and generated images inspected in `.local/ui-smoke-013/`.
- The GUI check substitutes a file-output clipboard sink and a confirmation recorder. It does not modify the user's clipboard, send desktop keystrokes, or contact a scanner. Actual clipboard pasting and printed-label scanning remain manual checks.

## 0.1.2 layout and queue controls

- Release build: **0 warnings, 0 errors**; automated suite: **36 passed**.
- GUI smoke check confirms matching dropdown/info/gear heights and top alignment, white Discard Queue below Connect, version at the bottom right, and the 200 ms TAB default.
- A blocked mock action and queued follow-up were discarded through the button. The connection remained active, the remaining key was not sent, and a fresh label executed without reconnecting.
- Main window and Odoo IN Moves info dialog previews were inspected in `.local/ui-smoke-012/`. The dialog shows both exact sequences and the applicable interval names without timing values.
- Saved timing values remain intact on upgrade; the new default applies to fresh settings and the Defaults button.

## 0.1.1 keyboard compatibility change

- **36 tests passed**, including normal key-event encoding, native Windows layout translation with Caps Lock on/off, Unicode fallback, and cleanup of partially injected Shift/key sequences.
- No desktop keystrokes were emitted during tests. The single-click Google Sheets behavior remains to be confirmed in the user's browser.

## 0.1.0 initial build

- Release solution build with .NET SDK 10.0.400: **0 warnings, 0 errors**.
- Automated test suite: **32 passed, 0 failed**.
- GUI smoke check: **21 assertions passed**, including disabled camera, dropdown ordering, C-driven selection, literal repeated X readings, local selection, input validation, control bounds and timing values.
- GUI main window and timing dialog previews were inspected at the current Windows display scale; controls and footer fit without clipping.
- Saved settings were checked for all five default timing values and reader/server preferences.
- A real loopback WebSocket connection exercised the supplied Scanner.Client and Scanner.Contracts DLLs, including C/X messages, repeated scans, Unicode/control decoding and closure.
- The application and launcher were packaged into an application ZIP with SHA-256 checksum and a per-user Windows x64 MSI with bundled .NET 10 runtime.

The keyboard encoding and native struct layout were tested without emitting desktop input. The GUI check used a mock keyboard and no scanner server. Physical-reader delivery, native typing into Odoo, and the actual Odoo form's field order and response times require the checks in `manual-verification.md`. The installer was built, but was not installed on this workstation. No GitHub release was published.

Local GUI report and previews: `.local/ui-smoke/`.
