# OCR add-on distribution and update flow

The OCR engine is an optional component with an independent version. It is not bundled into the
application ZIP and it does not participate in the application updater's restart/rollback state
machine.

## Published assets

Attach these three files to a GitHub and/or GitCode release:

- `ocr-addons-<version>-win-x64.zip`
- `ocr-addons-<version>-win-x64.zip.sha256`
- `ocr-addons-<version>-win-x64.zip.sig`

The release tag may be the application version. Component discovery reads the version from the OCR
asset name, so the OCR engine and application can be released independently. The ZIP must contain
`addons/ocr/manifest.json` and the executable named by that manifest.

## Runtime flow

1. The OCR dialog detects `addons/ocr/manifest.json` through `IOcrAddonService`.
2. On demand, `OcrAddonReleaseSource` queries GitCode and GitHub; `RacingUpdateSource` selects the
   newest component version returned during the mirror grace window.
3. `ResumableUpdatePackageDownloader` stores a partial download under
   `update-cache/downloads/ocr` and resumes it after cancellation or restart.
4. `SignedUpdatePackageVerifier` validates both SHA-256 and the RSA signature using the same trusted
   public key as application updates.
5. `OcrAddonPackageManager` extracts to staging, validates the signed version against
   `manifest.json`, then swaps `addons/ocr`. The old directory is restored if installation fails.

The application updater preserves the complete `addons` directory. Updating or rolling back the
application therefore does not downgrade or delete independently installed components.

Network access remains governed by the application's existing **Allow network access** setting.
OCR component downloads are user initiated and do not silently download with automatic application
updates.
