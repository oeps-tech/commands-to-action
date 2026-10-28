# Reader and destination-application verification

Automated checks use a loopback scanner server and a mock keyboard. Test these steps with the actual reader and destination application before production use.

1. Install the MSI, open the shortcut, and confirm the initial command is Disable. Confirm Use Camera cannot be checked.
2. Connect to the reader server on port 8766. Focus an empty text-editor document.
3. Select Direct, scan Unicode text and text containing `+^%{}`. Confirm literal output and no added key. Repeat the same label twice and confirm both are typed.
   In Google Sheets, single-click an empty cell without entering edit mode, then scan an ASCII product or serial label such as `OEPS-AB12`. Confirm the first character starts editing and the complete label appears. Repeat with Caps Lock on and off and with each Windows keyboard layout in use. Repeat Add Right and Add Enter and Up and confirm the destination cell after each action. Characters requiring Unicode fallback may still need the cell editor active. If typing stalls, record the exact queue count and status shown by OEPS before double-clicking the cell.
4. Scan a C command for Add Right and confirm the dropdown changes without taking focus away from the destination. Scan an X value and confirm the Right key. Repeat for Add Enter and Up.
5. Scan an unknown C command; confirm the current command stays selected. Scan Disable and confirm subsequent X labels have no effect; scan another C command to resume.
6. Use a disposable Odoo test record. For each Odoo command, verify an `OEPS-AB12`, a ten-character `AB12123456`, and a twelve-character `AB1212345678` label. Check the product, serial/batch field, quantity 1 and final Enter against the requested move layout. Invalid or whitespace-padded labels should produce no action.
7. Scan several labels before the first action finishes. Confirm actions execute in order, with the configured minimum interval after each completed job. Change commands between scans; already queued labels should retain their original action.
8. Open the timing dialog during a queued action. Confirm typing waits while an OEPS window has focus. Change INIT, SN, TAB and CHAR; save, restart and confirm persistence. Use the appropriate timings for Odoo's actual field response times.
9. Click Disconnect during a delayed action. Confirm no further steps occur and pending labels are cleared. Reconnect and verify only newly scanned labels execute. Repeat by closing the app and by stopping the scanner server.
   Also test **Discard Queue** with one active and several waiting labels: the active action should stop, waiting labels should disappear, and new scans should work over the same connection. Confirm the button sits below Disconnect with a white background.
10. Once a newer stable release exists with its ZIP and checksum, confirm Update available at bottom right. Restart using the installed shortcut, test Not now, then accept the update and verify settings are retained.
11. Select each command and click the barcode button. Confirm the copy-success popup, paste the image into your label/document tool, and scan it with the reader. The dropdown should select the corresponding command, including Disable. Verify both on-screen and printed codes at the intended label size. Confirm the image can still be pasted after closing OEPS Command to Action. Keep its white border intact.

The app types into the foreground application. Keep the intended destination field focused for the whole action. Test in a normal-privilege target; Windows rejects input into higher-privilege targets.
