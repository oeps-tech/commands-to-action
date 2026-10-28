# OEPS Command to Action

A compact Windows desktop application based on OEPS Raw Material Sticker. It receives scanner commands and raw labels through the bundled client SDK and types actions into the application that currently has keyboard focus.

## Install and run

Build output is in `artifacts/`. Install `Oeps.CommandToAction-0.1.4-setup-win-x64.msi` and launch **OEPS Command to Action** from the Start menu or desktop shortcut. The per-user installer includes the .NET 10 desktop runtime. It can coexist with Raw Material Sticker.

For development, open `Oeps.CommandToAction.sln` with a .NET 10 SDK or double-click `Run.cmd`. The script uses `.tools/dotnet`, the existing SDK in the sibling `raw-material-sticker` repository, or the system SDK. `Run.cmd` runs source directly; use the installed shortcut for update installation.

1. Choose **Local computer**, or **External** and enter the scanner server's IP address or host name.
2. Leave **Use Reader** checked. **Use Camera** is unchecked and disabled.
3. Select a command, then click **Connect**. The button becomes **Disconnect**.
4. Focus the destination field in Odoo or another application and scan labels.

The initial command is **Disable** on every launch. The reader uses port **8766**; an `appsettings.json` override can change `readerPort`. The camera port is never opened. The **i** button describes the current command. The **gear** button edits shared timing values, saved per user.

The third **barcode** button copies a Data Matrix image for the currently selected command, then shows “Data Matrix code copied to clipboard.” Paste it into your document or label layout. Every command, including Disable, has its own code. The code contains `C*<exact command name>*<CRC-32>` so scanning it through the scanner server selects that command in this app. Copying works while disconnected and does not execute an action.

Images are 1024 × 1024 pixels at 300 DPI, with a white quiet zone and integer-sized black modules. The clipboard carries both bitmap and PNG formats. Keep the white border intact and avoid stretching or blurring the code when laying out printed labels. Generation is local, using ZXing.Net 0.16.11; no label data is uploaded.

## Commands and labels

The application accepts exact, case-sensitive command names from version C messages:

```json
{"label_version":"C","command":"Odoo IN Moves"}
{"label_version":"X","raw":"AB12123456"}
```

A recognized C message immediately updates the dropdown. Unknown commands leave the selection unchanged. Versions 1, 2 and 3 are ignored. Raw X messages are parsed as JSON so Unicode and escaped controls are decoded before use. Only C messages change the selected command; command-looking raw text does not.

| Command | Action |
| --- | --- |
| Odoo IN | Types the full OEPS product or four-character prefix, waits INIT, then Tab. |
| Odoo IN Moves | Types the product, moves through four fields, optionally fills serial/batch after the first Tab, sets quantity 1, then Enter. |
| Odoo OUT Moves | Types the product, moves through three fields, optionally fills serial/batch after the second Tab, sets quantity 1, then Enter. |
| Add Enter and Up | Types raw content, Enter, Up. |
| Add Right | Types raw content, Right. |
| Direct | Types raw content without added keys. |
| Disable | Ignores new X labels; C labels remain active. |

The three Odoo commands require an exact match to one of these formats:

- `OEPS-yyyy`: four ASCII letters or digits after the uppercase `OEPS-`.
- `yyyyxxxxxxxx`: four ASCII letters or digits followed by eight digits.
- `yyyyxxxxxx`: four ASCII letters or digits followed by six digits.

Whitespace, extra characters, non-ASCII letters/digits and trailing newlines fail validation. Invalid labels produce no keyboard action. The other text commands do not apply this format validation.

The exact Odoo sequences are below; each timing token means a delay at that position. `product` means the full `OEPS-yyyy` value or the first four characters of a serial/batch label.

```text
Odoo IN:
  product INIT Tab

Odoo IN Moves / OEPS product:
  label INIT Tab TAB Tab TAB Tab TAB Tab TAB 1 CHAR Enter
Odoo IN Moves / serial or batch:
  prefix INIT Tab TAB label SN Tab TAB Tab TAB Tab TAB 1 CHAR Enter

Odoo OUT Moves / OEPS product:
  label INIT Tab TAB Tab TAB Tab TAB 1 CHAR Enter
Odoo OUT Moves / serial or batch:
  prefix INIT Tab TAB Tab TAB label SN Tab TAB 1 CHAR Enter
```

## Timing and queue behavior

| Setting | Default | Meaning |
| --- | ---: | --- |
| INIT | 1,000 ms | After typing the product. |
| SN | 100 ms | After typing the serial number or batch. |
| TAB | 200 ms | After each Tab marked with TAB in the move commands. |
| CHAT (CHAR in sequences) | 100 ms | After quantity 1, before Enter. |
| BETWEEN_ACTION | 250 ms | Minimum time from one job finishing to the next starting. |

Settings accept 0–60,000 milliseconds. Saved timing values are retained on upgrade; use **Defaults** in the timing dialog or edit TAB to apply the new 200 ms default. Every accepted label retains the command and timings selected when it arrived. One worker executes the queue in receipt order; repeated identical scans are separate jobs. Changing the command, including selecting Disable, affects future labels and leaves accepted jobs intact. **Discard Queue** stops the current action and clears pending jobs while keeping the reader connected for new scans. **Disconnect** and closing the application also cancel the current action and clear pending jobs. Characters already typed cannot be undone. A failed partial action is reported and never automatically retried; later jobs continue.

Keyboard output uses Windows `SendInput`, without the clipboard or `SendKeys` syntax. Version 0.1.1 sends normal key-down/key-up events for characters available on the destination application's keyboard layout, preserving case with Caps Lock on or off. This allows spreadsheet grids to recognize typing before the cell is in edit mode, like a scanner in keyboard mode. A short 5 ms interval separates characters. Characters requiring dead keys or Ctrl/Alt combinations, and characters absent from the layout, use literal Unicode fallback; a Unicode-only first character may still require entering cell edit mode. Characters such as `+`, `%`, `^` and braces remain literal. Actual tabs and line breaks become Tab and Enter; CRLF is one Enter. Unicode surrogate pairs remain together. Output waits while this application's main window or dialogs have focus, or while Shift/Ctrl/Alt/Windows is held. The application never activates the destination window. Keep the destination application and field focused throughout each action. Windows may block input into a target running with higher privileges; partial input is reported without replay.

Connection loss clears the queue. Reconnect with **Connect**; the SDK does not replay missed scans.

## Settings and updates

Preferences and shared timing values are stored in:

```text
%LOCALAPPDATA%\OEPS\CommandToAction\user-settings.json
```

Optional per-user `appsettings.json` in the same directory overrides the packaged configuration. `--data-dir <directory>` selects isolated settings for development.

The update architecture follows Raw Material Sticker: a separate launcher, immutable version directories, SHA-256 package verification, startup-ready confirmation and recovery to the last working version. The app checks stable GitHub releases at startup and every 30 minutes, displaying **Update available** at the bottom right. Restart from the installed shortcut to install an available update, or choose **Not now**. Update lookup failures leave the installed version usable.

Update releases come from `oeps-tech/commands-to-action` and need both `Oeps.CommandToAction-<version>-win-x64.zip` and its `.sha256` asset. The MSI is for first installation; future updates use the smaller application ZIP. No release is published by a local build.

## Build and verification

```powershell
dotnet build Oeps.CommandToAction.sln -c Release
dotnet run --project tests/Oeps.CommandToAction.Tests -c Release
powershell -ExecutionPolicy Bypass -File scripts/package.ps1 -Version 0.1.4
```

Pass `-Dotnet <path-to-dotnet.exe>` to the packaging script to use a private SDK. Packaging runs the test suite and produces the MSI, application ZIP and checksums under `artifacts/`. WiX 4.0.6 is used to build the MSI. Reusing an existing package version requires archiving the existing output first.

The test suite covers validation, every exact key/delay sequence, FIFO order, timing snapshots, cancellation, failure handling, literal Unicode input encoding, SDK loopback WebSocket delivery and update verification/recovery. It does not type into the desktop. An isolated GUI check is also available:

```powershell
dotnet run --project src/Oeps.CommandToAction.App -c Release -- --ui-smoke --data-dir .local/ui-smoke
```

That check uses a mock keyboard, does not connect to the scanner, and saves a report and window previews. See `docs/manual-verification.md` for real reader and Odoo checks.

The GitHub Actions workflow builds reviewable artifacts for branches and pull requests. Pushing a stable `v<major>.<minor>.<patch>` tag publishes a release with the packaged artifacts.
