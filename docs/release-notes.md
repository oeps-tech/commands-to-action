# OEPS Command to Action 0.1.4

First public release of the Windows application for scanner-driven keyboard actions.

- Receive C command labels and X raw labels through the barcode reader server.
- Seven selectable commands, strict Odoo label validation, and queued execution with configurable delays.
- Keyboard-style typing compatible with selected Google Sheets cells; behavior confirmed by the user.
- Discard Queue stops active and waiting actions while keeping the reader connected.
- Copy a validated 1024 × 1024 Data Matrix image for any command using the barcode button.
- Compact interface with disabled camera selection, command help, and an update indicator beside the version.

Install **Oeps.CommandToAction-0.1.4-setup-win-x64.msi** for Windows x64. It includes the .NET 10 desktop runtime. The smaller **Oeps.CommandToAction-0.1.4-win-x64.zip** is used by the application's updater. Both downloads include SHA-256 checksum files.

Validation: clean release build, 39 automated tests passed, and 49 GUI checks passed on the preceding feature build. Real clipboard pasting and printed Data Matrix scanning remain manual verification steps.

### Final wording changes

- Update timing dialog wording to “TAB — After send a tab” and “CHAT — After send character(s)”.
- Capitalize the first word of all timing descriptions. This is a display-only change; stored timing keys and execution behavior are unchanged.

## 0.1.3

- Remove the Configurable intervals lists from command info while keeping the exact sequences.
- Add a third, barcode-icon button beside info and settings. It copies a Data Matrix for the selected command and confirms success in a popup.
- Generate a distinct 1024 × 1024 image at 300 DPI for each command, including Disable. Each code contains the scanner's C command envelope and CRC-32 checksum.
- Copy both bitmap and PNG clipboard formats, with a white quiet zone and crisp, unblurred modules.
- Verify all seven generated codes by decoding their images and validating them with the supplied scanner SDK.

## 0.1.2

- Match the info and gear button heights to the command dropdown.
- Show the exact Odoo product and serial/batch sequences in the info dialog, with applicable interval names and no timing values.
- Add a white Discard Queue button below Connect/Disconnect. It cancels the active action and clears waiting jobs while keeping the reader connected.
- Set the TAB default to 200 ms. Existing saved timing values are retained; use Defaults in the timing dialog or change TAB to apply the new value.
- Move the version to the bottom right beside the update indicator.

## 0.1.1

Improve typing into spreadsheet cells selected with a single click.

- Normal characters now generate keyboard key-down/key-up events using the destination application's keyboard layout, instead of Unicode packets for every character.
- Preserve exact case with Caps Lock on or off. Use literal Unicode fallback for characters that need dead keys or Ctrl/Alt combinations, or are absent from the layout.
- Add a short 5 ms interval per character for browser editors.
- Release any injected modifier keys after partial input failure without replaying text.

The user confirmed that the revised keyboard input works in Google Sheets. Automated checks verify event encoding and native Windows layout translation without typing into the desktop.

## 0.1.0

Initial Windows application for scanner-driven keyboard actions.

- Reader connection using the bundled .NET 10 client SDK; camera selection is disabled.
- Seven commands selectable locally or through version C labels.
- Version X labels execute in order with strict Odoo format validation and configurable delays.
- Literal Unicode keyboard output, with no SendKeys command interpretation.
- Disconnect cancels the current action and clears pending labels.
- Per-user MSI installation, update notification, checksum verification and last-working-version recovery.
