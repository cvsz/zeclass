# Threat model

No telemetry, no network transmission, no cloud. Attack surface is local files,
devices, and adjacent processes.

## Untrusted inputs and mitigations

| Input | Abuse | Mitigation (where) |
|---|---|---|
| `.ebboard` file | memory/CPU DoS (2B pages, giant strings, NaN into WPF) | 256 MB pre-check, `BoardLimits` aggregate caps, finite/enum clamps, truncation (`InkModel.cs`) |
| PDF | decompression bomb, oversized streams, path confusion | `PdfLimits` (file/stream/total/time), bounded copy, capped page count, TOCTOU-safe read, head+tail scan (`PdfImporter.cs`) |
| DOCX/PPTX/DOCM/PPTM (ZIP) | ZIP bomb, traversal, ADS, mislabeled media | `ZipLimits` (archive/entries/sizes/ratio/title/time), `IsSafeEntryName`, content-sniffed extensions (`OfficePreviewExtractor.cs`) |
| Images | decoder bombs, missing files | missing-file placeholder (`InkSurface.cs`); staged copies, never in-place |
| HID device metadata/paths | malformed paths, inaccessible registry | read-only opens, degrade-to-unknown, swallowed-safe probes (`DigitizerService.cs`) |
| NDI helper path (env var) | arbitrary binary execution | canonicalization, existence re-check, optional SHA-256 pin, PID+start-time+path identity (`NdiBridge.cs`) |
| MCI save path | command injection via quotes | quoting, quote refusal, redacted logs (`AudioRecorder.cs`) |
| Staging/temp dirs | disk-fill growth, orphan debris | per-operation GUID folders, startup age+size cleanup (`ImportStaging.cs`) |
| Crash mid-write | corrupt primary board | tmp+flush+atomic rename; Save-As commits state only on success |

## Explicitly accepted risks

- PDF/Office import is preview-extraction, not rendering (documented in README).
- Board assets referenced by absolute path (no portable package format yet).
- `DigitizerService` degrades all native failures to "no devices" (no structured
  error taxonomy; avoids alarming teachers with driver internals).
- Release signing requires a certificate in GitHub secrets; unsigned pilot builds
  are allowed only when `RELEASE_SIGNING_REQUIRED` is not `true`.
- Real touch/pen hardware unverified in this environment (see
  `VERIFICATION-MATRIX.md`).
