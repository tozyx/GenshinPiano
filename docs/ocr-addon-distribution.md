# OCR add-on distribution and update flow

The OCR engine is an optional component with an independent version. It is not bundled into the
application ZIP and it does not participate in the application updater's restart/rollback state
machine.

## Published assets

The application launchers `tools\build-release.bat` and `tools\build-release-framework.bat` build only application
packages. Use `tools\build-ocr-addon.bat` to package OCR independently. All three launchers are
tracked in Git and resolve the repository root automatically. The OCR launcher requires PowerShell 7, the .NET 10 SDK, and
`GENSHINPIANO_UPDATE_SIGNING_KEY` pointing to the existing private signing key outside the repository.
The first argument sets the OCR release version, defaulting to `1.0.0` in the BAT. You can edit that
default for double-click publishing. The build, packaged manifest, and ZIP name use the selected
version; source project and manifest versions are left unchanged.

```cmd
tools\build-ocr-addon.bat
tools\build-ocr-addon.bat 1.0.1
tools\build-ocr-addon.bat 1.0.1 "D:\ocr-assets" "D:\vc-runtime-x64"
```

The second and third arguments override the SMT resource and x64 VC runtime directories. Defaults are
`publish\smt-experiment` and Windows `System32`. Output is `publish\addons\ocr` and the three assets
below. To invoke the publisher directly, see the PowerShell command in
[Building](BUILDING.md#9-发布输出).

Attach these three files to a GitHub and/or GitCode release:

- `ocr-addons-<version>-win-x64.zip`
- `ocr-addons-<version>-win-x64.zip.sha256`
- `ocr-addons-<version>-win-x64.zip.sig`

The release tag may be the application version. Component discovery reads the version from the OCR
asset name, so the OCR engine and application can be released independently. The ZIP must contain
`addons/ocr/manifest.json` and the executable named by that manifest.

## Runtime flow

Manual **Check for updates** in the main window checks application and OCR metadata in parallel,
then presents installed/latest versions with separate application and OCR update actions and Cancel.
Checking alone never downloads a package. A failure checking one component does not block the other.
OCR can be installed when missing; current components have their update action disabled.

Automatic updates retain the existing application-only background download and exit/restart installation
flow. Manual checking, choosing OCR, or cancelling the overview does not cancel that background task.
Choosing the same application update waits for and reuses its verified download; an older selection
cannot replace a newer prepared update. Turning off automatic updates cancels the automatic operation;
turning off network access also cancels manual network operations.

The OCR window checks metadata on opening when network access is allowed, reusing a check less than
five minutes old, and displays current/update-available/unknown status. Its check action does not
download; after discovery, the button offers installation/update. Status is shared with the main
overview and refreshed after installation. Recognition and runtime replacement are mutually exclusive.

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

Since 0.9.0, the add-on contains a native CPU SMT runtime and quantized model under
`addons/ocr/staff-omr/smt`, including app-local x64 VC runtime DLLs. Python/Oemer is no
longer shipped. Numbered notation retains its existing .NET pipeline and models.
`Publish-OcrSmtRuntime.ps1` verifies pinned archive/model hashes and copies only the
required native files, model, licenses, and provenance. `requiredFiles` in the manifest
enables resource validation before and after installation. Schema and IPC protocol
remain version 1; legacy manifests and hosts remain compatible.

Staff recognition supports clear, flat piano scans and rendered images. Photographs
are unsupported, and unresolved structural anomalies cause recognition to fail rather
than returning an incomplete score. See [SMT integration](SMT_INTEGRATION.md).

Network access remains governed by the application's existing **Allow network access** setting.
OCR component downloads are user initiated and do not silently download with automatic application
updates.

## Contributor bootstrap

The runtime and model are deliberately excluded from Git. Place the downloaded CPU archive and
quantized model in `publish/smt-experiment`, then run `tools/Setup-OcrDevelopment.ps1` from the
repository root. The script verifies hashes and deploys a Debug-ready add-on without Python.
Override the resource location with `-SmtTrialDirectory`. Ordinary .NET builds and CI do not
download OCR assets. The legacy Oemer/Python setup and packaging scripts have been removed; offline evaluation tools remain under `tools/ocr/research`.
