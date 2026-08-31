# GenshinPiano v3 Roadmap

## Completed in the current iteration

- [x] Establish the optional OCR add-on boundary.
  - Keep OCR runtimes and models outside the lightweight main package.
  - Discover a versioned engine manifest under `addons/ocr`.
  - Exchange one UTF-8 JSON request and response through an isolated process.
  - Reject path traversal, incompatible protocols, malformed results, and invalid scores.

- [x] Harden simulated-key release safety.
  - Track every key pressed by the Windows input adapter with thread-safe state.
  - Release tracked keys after every normal, cancelled, or failed playback run.
  - Send a best-effort key-up sweep during managed crashes and application/process exit.
  - Keep cleanup idempotent so overlapping shutdown paths are safe.

- [x] Authenticate update packages with an embedded RSA public key.
  - Generate detached RSA-3072/SHA-256 signatures during both release builds.
  - Keep the private signing key outside the repository and release archives.
  - Require the ZIP, SHA-256 sidecar, and signature sidecar on each mirror.
  - Verify both the package hash and signature before an update becomes ready.
  - Validate required application, updater, and bundled-song output before packaging.

- [x] Add explicit `.gpiano` schema migrations.
  - Treat schema-less early score JSON as version 0 and migrate it to version 1.
  - Apply migrations sequentially before typed deserialization and validation.
  - Reject files created by a newer application to prevent destructive downgrades.
  - Always write the current schema version and validate malformed null sections safely.

- [x] Add non-destructive score quality analysis and 21-key range shifting.
  - Report unmapped 21-key notes, exact duplicates, same-pitch overlaps, and very short notes.
  - Shift by adjacent natural-note keys or octaves as a single undoable edit.
  - Keep every shifted note visible and directly mappable inside the 21-key editor range.
  - Let the user explicitly choose duplicate removal, same-pitch overlap trimming, and very-short-note removal.
  - Keep potentially audible cleanup rules disabled by default and group each cleanup run into one undo step.

- [x] Add per-Windows-session single-instance coordination.
  - Use a named mutex to prevent concurrent writers to settings, recovery data, and update caches.
  - Forward supported score paths from a second launch to the existing instance through a named pipe.
  - Restore and activate the existing main window when a forwarded open request arrives.
  - Detect the normal/elevated privilege boundary and show a clear instruction when forwarding is blocked.

- [x] Improve update-source racing when GitHub and GitCode publish different latest versions.
  - Keep the low-latency first-valid-response behavior.
  - After the first valid manifest arrives, allow the other source a short grace period (initial target: 500–1000 ms).
  - If both sources respond during that window, select the higher semantic version.
  - If the other source fails or exceeds the grace period, immediately use the first valid result.
  - Add diagnostics for source latency, selected mirror, selected version, and fallback reason.
### Numbered-notation OCR baseline

- [x] Preserve source detail with overlapping high-resolution OCR tiles.
- [x] Reconstruct notation rows from character coordinates and remove tile duplicates.
- [x] Infer a basic second voice from bimodal vertical spacing between notation rows.
- [x] Convert and integrate the MIT-licensed OrpheusNet middle-symbol CNN as ONNX.
- [x] Reimplement OrpheusNet-style middle-band vertical projection for note candidates.
- [x] Detect octave dots, rhythm underlines and augmentation dots geometrically.
- [ ] Detect accidentals, extension dashes and ties geometrically.
- [ ] Add an OCR correction preview before importing the generated score.
