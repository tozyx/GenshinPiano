# OCR add-on protocol

OCR is an optional component and is not linked into the main executable. Install
an engine under `addons/ocr/` next to `GenshinPiano.exe`:

```text
addons/ocr/
  manifest.json
  GPianoOcrEngine.exe
  models/...
```

Manifest schema 1:

```json
{
  "schemaVersion": 1,
  "protocolVersion": 1,
  "engineVersion": "0.1.0",
  "executable": "GPianoOcrEngine.exe",
  "launchMode": "file"
}
```

`launchMode` may be `file` or omitted. New bundled engines use `file`: the host
writes a UTF-8 JSON request file, starts the executable without redirected
standard streams, then reads the response/progress files. The executable first
acts as a short-lived launcher and detaches the long-running OCR worker so it is
shown as a background process rather than altering the main application's Task
Manager group. Older
engines without `launchMode` are still started with `--stdio`: the host writes
one UTF-8 JSON request to standard input, then closes input. The engine writes
one JSON result to standard output and exits. Diagnostics belong on standard
error; standard output must contain JSON only.

Requests contain `protocolVersion`, the absolute `imagePath`, a `notationHint`
(`auto`, `numbered`, or `staff`), a UI language, and an optional `watermarkMode`
(`auto`, `strong`, or `off`; omitted values default to `auto`). The optional
`includeAccompaniment` flag defaults to `true`; engines may still analyze all
voices for layout reconstruction when it is false, but should only return the
primary voice. `preferGpuAcceleration` defaults to `true`. It is a preference,
not a requirement: engines should use CUDA when it is available and healthy,
otherwise retry with the CPU provider in the same request. Successful responses contain
a complete `.gpiano`-compatible `score`, confidence in the range 0–1, and optional
warnings. Failed responses set `success` to false and should provide stable
`errorCode` and user-readable `errorMessage` fields.

Engines may report real processing progress on standard error using one line per
update: `OCR_PROGRESS|StageName|0.0-1.0`. Supported stage names are `Preparing`,
`WatermarkSuppression`, `TextDetection`, `SuperResolution`, and
`ScoreReconstruction`. In `file` launch mode, progress lines are appended to the
progress file instead. Other standard-error lines remain diagnostics.

The host rejects incompatible manifests, executable paths escaping the add-on
directory, non-zero exit codes, timeouts, malformed JSON, mismatched protocol
versions, and successful responses without a score.
