# Automatic update offers · 2026-10-24

Prepared for the next release from `main` / v1.0.57 (`6dc72e31d0ed67e47bd69a79c29082c5aa62d15f`) in the isolated integration checkout. The primary dirty checkout and installed user application were not modified.

## Behavior

- Check on startup and every six hours; never overlap checks or install attempts.
- A verified newer release queues one modal offer per version per session. The offer waits until the main window is active, processing/import has finished, menus/drags have ended and owned windows (including startup notes) are closed.
- Update/Later labels are localized. Later, Escape and the close button dismiss the offer. Manual checks can offer the same version again; a later release can produce a new automatic offer.
- Up-to-date/offline automatic checks remain silent. Closing the app stops both timers and late replies cannot open a modal.
- Download/install still requires confirmation and uses the existing digest verification and safe close/helper path. Development builds ask before opening the release page.

## Local verification

Release solution build passed with zero warnings/errors. `PixelArtAlignment.Tests --updates` passed (six top-level checks, including the dedicated updater group). The updater group covers pending requests and deduplication, no-update/offline startup, shutdown before reply, periodic scheduling, busy/disabled/owned/minimized guards, modal blocking, decline, same-version suppression, manual retry, a newer release, RU/EN labels and the pre-existing installer close/failure/retry guards. No real update was downloaded or installed.

Logs: ignored `artifacts/automatic-updates/build.log` and `focused.log`. The final repeated-tag rollback scenario was added after the native interruption and is checked by PR CI; no further local UI run was started. The full required WPF/processing/CLI/plugin regression also runs in that Windows CI.

## Native Windows scope and interruption

A separate preview used the actual MainWindow/TesseraDialog code, its own synthetic library and a fake newer-version response `v99.0.0`. The real first-launch patch notes stayed in front; no update offer covered them. A real mouse click on Continue closed the notes, then the Update/Later modal appeared with the correct version, readable text and buttons.

The user stopped Computer Use with physical Escape during the follow-up. Further native input stopped immediately. A real Later/Escape dismissal and native English/theme checks were therefore not completed; their programmatic WPF counterparts passed. No download, installation or browser opening was attempted. The preview is not a release build and its fake version is not published.

No screenshot containing unrelated foreground application content is included. A successful automated CI must not be described as completion of the interrupted native input checks.
