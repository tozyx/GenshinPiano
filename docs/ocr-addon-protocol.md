# OCR add-on protocol

OCR is an optional component and is not linked into the main executable. Install
an engine under `addons/ocr/` next to `GenshinPiano.exe`:

```text
addons/ocr/
  manifest.json
  GenshinPiano.Ocr.Engine.exe
  models/...
```

Manifest schema 1:

```json
{
  "schemaVersion": 1,
  "protocolVersion": 1,
  "engineVersion": "0.1.0",
  "executable": "GenshinPiano.Ocr.Engine.exe"
}
```

The host starts the executable with `--stdio`, writes one UTF-8 JSON request to
standard input, then closes input. The engine writes one JSON result to standard
output and exits. Diagnostics belong on standard error; standard output must
contain JSON only.

Requests contain `protocolVersion`, the absolute `imagePath`, a `notationHint`
(`auto`, `numbered`, or `staff`), a UI language, and an optional `watermarkMode`
(`auto`, `strong`, or `off`; omitted values default to `auto`). The optional
`includeAccompaniment` flag defaults to `true`; engines may still analyze all
voices for layout reconstruction when it is false, but should only return the
primary voice. Successful responses contain
a complete `.gpiano`-compatible `score`, confidence in the range 0–1, and optional
warnings. Failed responses set `success` to false and should provide stable
`errorCode` and user-readable `errorMessage` fields.

Engines may report real processing progress on standard error using one line per
update: `OCR_PROGRESS|StageName|0.0-1.0`. Supported stage names are `Preparing`,
`WatermarkSuppression`, `TextDetection`, `SuperResolution`, and
`ScoreReconstruction`. Other standard-error lines remain diagnostics.

The host rejects incompatible manifests, executable paths escaping the add-on
directory, non-zero exit codes, timeouts, malformed JSON, mismatched protocol
versions, and successful responses without a score.
