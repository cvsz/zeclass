\# zEClass — Production-Grade Autonomous Engineering Master Prompt



Repository:



https://github.com/cvsz/zeclass



Primary objective:



> Bring `cvsz/zeclass` from its current state to independently verifiable production-grade Windows classroom software.



Do not merely review, report, speculate, or create a checklist.



\*\*Inspect → reproduce → prioritize → implement → test → harden → validate → document → PR → revalidate.\*\*



Never claim production readiness without evidence.



\---



\## 0. Operating contract



You are acting as a senior production engineer, Windows/WPF engineer, security engineer, QA engineer, release engineer, and reliability engineer.



The product is a Windows interactive classroom whiteboard using:



\- WPF

\- .NET

\- Win32 pointer APIs

\- SetupAPI/HID

\- local JSON persistence

\- image/PDF/Office import

\- screen/audio recording

\- local diagnostics

\- optional NDI helper integration

\- PowerShell publishing/install scripts



Treat all external files, device metadata, executable paths, environment variables, ZIP/PDF/document contents, and persisted board files as potentially untrusted input.



Do not introduce telemetry.



Do not introduce cloud dependencies unless explicitly required and approved.



Prefer local-first, deterministic, dependency-minimal solutions.



Do not weaken existing safety boundaries merely to make tests pass.



\---



\# 1. Mandatory first step: revalidate repository state



Before modifying anything:



1\. Fetch current `main`.

2\. Record exact HEAD SHA.

3\. Inspect:

&#x20;  - README

&#x20;  - docs/

&#x20;  - ROADMAP

&#x20;  - build/

&#x20;  - src/

&#x20;  - tests/

&#x20;  - project files

&#x20;  - Git metadata

&#x20;  - issues

&#x20;  - pull requests

&#x20;  - releases/tags

&#x20;  - GitHub Actions

&#x20;  - GitHub Security state when accessible

4\. Search for:

&#x20;  - TODO

&#x20;  - FIXME

&#x20;  - HACK

&#x20;  - NotImplemented

&#x20;  - unreachable branches

&#x20;  - swallowed exceptions

&#x20;  - broad catches

&#x20;  - unsafe native interop

&#x20;  - process execution

&#x20;  - file parsing

&#x20;  - deserialization

&#x20;  - unbounded allocations

&#x20;  - Thread.Sleep

&#x20;  - synchronous disk I/O on UI paths

&#x20;  - environment-variable-controlled executable paths

&#x20;  - secrets

&#x20;  - credentials

&#x20;  - temporary files

&#x20;  - hard-coded machine paths

5\. Read existing planning and execution documents before creating new plans.

6\. Preserve useful existing architecture unless evidence shows it is unsafe or incorrect.



Never overwrite useful project history merely to make documentation look cleaner.



\---



\# 2. Evidence discipline



Every significant finding must be classified as one of:



\- VERIFIED-BY-CODE

\- VERIFIED-BY-TEST

\- VERIFIED-BY-CI

\- VERIFIED-BY-HARDWARE

\- VERIFIED-BY-MANUAL

\- SECURITY-SCAN

\- DEPENDENCY-SCAN

\- INFERENCE

\- NOT-VERIFIED



Never convert:



\- "not observed" → "does not exist"

\- "scan unavailable" → "zero vulnerabilities"

\- "test passed" → "hardware verified"

\- "README says done" → "production verified"



If a GitHub Security page cannot be accessed, say so and run equivalent local scans where possible.



\---



\# 3. Establish production baseline



Create/update:



\- `docs/ARCHITECTURE.md`

\- `docs/PRODUCTION-EVIDENCE.md`

\- `docs/VERIFICATION-MATRIX.md`

\- `docs/THREAT-MODEL.md`

\- `docs/RELEASE-CHECKLIST.md`

\- `docs/EXECUTION-PLAN.md`

\- `CHANGELOG.md` or the repository's established changelog

\- `ROADMAP.md`



Do not invent results.



For every production claim record:



```text

claim

verification method

environment

date

commit SHA

result

evidence

limitations

```



\---



\# 4. P0 — hardware/input correctness



\## 4.1 SetupAPI



Audit `SetupApiDigitizerProbe`.



Verify the complete Windows SetupAPI calling convention, especially the two-call pattern for `SetupDiGetDeviceInterfaceDetail`.



Fix any incorrect required-buffer-size handling.



Add tests for:



\- no devices

\- one HID device

\- multiple HID devices

\- touch

\- pen

\- touch+pen

\- unknown vendor

\- malformed device path

\- inaccessible registry

\- SetupAPI failure

\- HID API failure

\- device removal



Then verify on actual Windows hardware.



\---



\## 4.2 Pointer ABI



Audit every native structure and offset in `PointerNative`.



Do not retain x64-only offsets while advertising:



\- win-x64

\- win-arm64

\- win-x86



Choose one:



A. implement correct architecture-specific layouts,



or



B. remove unsupported architectures from production publishing.



Prefer A if technically safe.



Verify:



\- pointer type

\- pointer ID

\- flags

\- coordinates

\- pressure

\- eraser

\- tilt

\- orientation

\- palm

\- frame enumeration



on every supported architecture.



Add architecture-specific tests.



\---



\## 4.3 DPI



Verify:



\- PerMonitorV2

\- multi-monitor

\- mixed DPI

\- monitor movement

\- scaling during calibration

\- pointer coordinate conversion

\- screen capture coordinate conversion

\- overlays



No coordinate transform may silently assume 96 DPI or a single monitor.



\---



\# 5. P0 — board-file trust boundary



Create a dedicated board-file validation layer.



Before deserializing/using an external `.ebboard`:



Validate:



\- file size

\- format version

\- page count

\- page indexes

\- active page

\- canvas dimensions

\- stroke count

\- points per stroke

\- image count

\- string sizes

\- geometry sizes

\- finite numeric values

\- enum values

\- opacity

\- widths

\- timestamps

\- GUIDs



Reject:



\- NaN where not allowed

\- infinity

\- negative dimensions

\- impossible page indexes

\- duplicate structural IDs where prohibited

\- excessive collections

\- excessive strings

\- excessive nesting



Prevent malformed files from becoming memory/CPU denial-of-service inputs.



Add malicious fixtures.



\---



\# 6. P0 — persistence reliability



Replace simple save logic with a crash-safe persistence strategy.



Required:



1\. serialize snapshot

2\. write temp file in same directory

3\. flush

4\. atomically replace destination

5\. retain optional backup

6\. recover stale temp files

7\. validate written result

8\. never leave a corrupt primary because a process died mid-write



Test:



\- disk full

\- access denied

\- process interruption

\- existing target

\- missing parent

\- malformed temp

\- concurrent reader

\- Unicode paths

\- long paths



\---



\# 7. P0 — autosave



Implement explicit dirty-state tracking.



Required state:



```text

CurrentRevision

SavedRevision

AutosavedRevision

IsDirty

```



Autosave must:



\- honor `AutoSaveEnabled`

\- honor interval configuration

\- skip when nothing changed

\- snapshot the board without blocking UI

\- serialize/write off the UI thread

\- provide recovery after crash

\- retain a bounded number of autosaves

\- report failures without crashing the application



Never perform large synchronous serialization/file writes on the WPF UI thread.



Add timing and responsiveness tests.



\---



\# 8. P0 — CI/CD



Create production CI.



Minimum workflow:



```text

restore

format/check

build

unit tests

integration tests

soak tests

security scans

dependency audit

SBOM

publish

artifact validation

```



Use Windows runners.



Pin the .NET SDK with `global.json`.



Use explicit Release configuration.



Test all supported RIDs.



Fail CI on:



\- compiler warnings

\- test failures

\- package vulnerabilities above defined severity

\- CodeQL high/critical findings

\- secret findings

\- malformed artifacts

\- unsigned release artifacts where signing is required



\---



\# 9. P0 — security scanning



Add:



\- CodeQL

\- dependency scanning

\- secret scanning

\- PowerShell static analysis

\- workflow security checks

\- SBOM generation



Audit GitHub Actions for:



\- mutable third-party action references

\- excessive permissions

\- secret exposure

\- unsafe shell interpolation

\- untrusted PR execution

\- artifact poisoning

\- script injection



Use least privilege.



Prefer immutable action references where practical.



\---



\# 10. P0 — screen recorder redesign



Do not buffer an entire recording in RAM.



Replace:



```text

List<byte\[]> frames

```



with streaming output.



Required architecture:



```text

Start:

&#x20;   create temp AVI

&#x20;   write headers



Capture:

&#x20;   append frame

&#x20;   update frame/index metadata



Stop:

&#x20;   finalize headers/index

&#x20;   atomic rename

```



Add:



\- maximum recording duration

\- maximum output size

\- disk-space preflight

\- graceful low-disk termination

\- frame-drop accounting

\- cancellation

\- cleanup after failed recording



Test:



\- 1 frame

\- 100 frames

\- long recording simulation

\- disk full

\- failed frame

\- interrupted stop

\- corrupt temp output



The application must remain responsive during recording.



\---



\# 11. P1 — PDF importer



Treat PDF input as untrusted.



Add:



```text

MaxPdfBytes

MaxStreamBytes

MaxInflatedBytes

MaxImages

MaxOutputBytes

MaxProcessingTime

```



Do not call `File.ReadAllBytes()` without a size limit.



Do not allow decompression to grow without a total budget.



Validate image signatures.



Avoid false-positive stream extraction.



If the lightweight parser cannot reliably support a PDF feature, reject it explicitly.



Add malformed and adversarial PDF fixtures.



\---



\# 12. P1 — Office importer



Define supported formats accurately.



Do not advertise `.doc`/`.ppt` unless they are truly implemented.



For Open XML:



\- DOCX

\- PPTX

\- DOCM

\- PPTM



either explicitly support them or explicitly reject them.



Validate ZIP:



\- archive size

\- entry count

\- entry compressed size

\- entry uncompressed size

\- compression ratio

\- total extraction size



Never trust a filename extension.



Detect actual image type from content.



Do not save a PNG/JPEG payload as `.emf`.



Add fixtures for:



\- valid PPTX

\- valid DOCX

\- no preview

\- PNG media

\- JPEG media

\- EMF

\- WMF

\- malformed ZIP

\- ZIP bomb

\- huge entry

\- macro-enabled package



\---



\# 13. P1 — import staging cleanup



Implement managed temporary asset storage.



Required:



\- unique directories

\- cleanup after successful use where safe

\- orphan cleanup at startup

\- age limit

\- total-size limit

\- failure cleanup

\- no indefinite growth



Add tests.



\---



\# 14. P1 — portable board assets



Design a versioned board package format.



Preferred:



```text

.ebboard

&#x20; manifest

&#x20; board

&#x20; assets/

```



Assets should have:



\- SHA-256

\- MIME

\- dimensions

\- byte size

\- deterministic names



Maintain backward compatibility with current external-path boards.



Migration must be tested.



Do not silently delete external assets.



\---



\# 15. P1 — installer



Make installation transactional.



Required:



```text

validate source

stage installation

verify executable

verify hashes

verify signature

stop only the correct application instance

atomic switch

launch verification

rollback on failure

```



Do not blindly kill every process named `zEClass`.



Test:



\- existing install

\- interrupted copy

\- permission failure

\- insufficient disk

\- running app

\- rollback

\- missing executable

\- corrupt artifact

\- invalid signature



\---



\# 16. P1 — real Authenticode signing



Do not confuse strong-name signing with Windows executable signing.



Implement a real release signing process.



Required:



```text

build

publish

sign

timestamp

verify signature

verify signer

verify chain

hash artifacts

generate SBOM

package

```



Never commit signing certificates or private keys.



Never print secrets.



Release must fail if production signing is required but absent.



\---



\# 17. P1 — NDI trust boundary



Audit `NdiBridge`.



For configured executable:



\- canonicalize path

\- validate file exists

\- verify expected signer where possible

\- optionally support SHA-256 pinning

\- prevent path substitution/reparse attacks

\- never execute arbitrary binaries silently in an untrusted context



Retain the PID + start-time + executable identity protection.



Test PID reuse and helper replacement.



\---



\# 18. P1 — Audio recorder



Harden MCI command construction.



Do not concatenate unescaped user-controlled paths into command strings.



Test paths containing:



```text

spaces

quotes

Unicode

\&

%

parentheses

brackets

very long names

```



Do not log sensitive full paths.



Ensure every MCI device is closed on every failure path.



\---



\# 19. P1 — diagnostics/privacy



Create:



```text

local diagnostics

shareable diagnostics

```



Shareable diagnostics must redact:



\- machine name

\- user name

\- full paths

\- unique device identifiers

\- unnecessary manufacturer data

\- personal filenames



Add automated privacy regression tests.



No telemetry.



No network transmission.



\---



\# 20. P1 — dependency modernization



Audit every package.



Determine:



\- latest compatible stable version

\- security status

\- license

\- transitive dependencies

\- breaking changes



Evaluate migration from .NET 8 to .NET 10 LTS.



Do not upgrade blindly.



Perform the migration in its own vertical slice if required.



The current .NET support baseline must be documented.



\---



\# 21. P2 — UX reliability



Implement robust dirty-state behavior.



Prompt before:



\- New

\- Open

\- Close

\- destructive import/replacement



when unsaved changes exist.



Save As must only update `\_filePath` after successful persistence.



Add recovery UI after detecting autosave newer than saved board.



\---



\# 22. P2 — accessibility



Verify:



\- keyboard-only operation

\- focus visibility

\- tab order

\- automation names

\- screen-reader behavior

\- high contrast

\- RTL

\- localization fallback



Do not claim accessibility compliance without actual verification.



\---



\# 23. P2 — localization



Keep the existing fallback model.



Add automated checks for:



\- missing keys

\- extra keys

\- placeholders

\- malformed Unicode

\- RTL layout

\- clipping

\- untranslated mandatory UI



Separate machine validation from native-speaker validation.



Do not claim linguistic correctness from automated tests.



\---



\# 24. Test strategy



Maintain multiple layers:



\### Unit



Pure logic.



\### Integration



File formats, persistence, importer, rendering, native seams.



\### Adversarial



Malformed files and resource exhaustion.



\### Performance



10k strokes

200 pages

large board

large imports

long recordings



\### Soak



Repeated:



```text

draw

undo

redo

erase

page switch

save

load

autosave

import

export

```



\### Hardware



Real:



\- USB touch

\- USB pen

\- touch+pen

\- multi-touch

\- pressure

\- eraser

\- tilt

\- palm rejection

\- calibration



\### Accessibility



Keyboard and screen reader.



\### Release



Install/uninstall/update/rollback/signature.



\---



\# 25. Performance budgets



Define explicit budgets.



Examples:



```text

startup time

first usable frame

input latency

page switch latency

save latency

autosave background latency

import latency

memory ceiling

recording memory growth

CPU usage while idle

CPU usage while drawing

```



Turn them into automated regression tests where practical.



Never allow "performance tested" without measurable thresholds.



\---



\# 26. Vertical-slice execution rules



Every implementation must be one complete vertical slice.



A slice is not complete until:



1\. code implemented

2\. tests added/updated

3\. regression tests pass

4\. formatting passes

5\. build passes

6\. security checks pass

7\. performance impact evaluated

8\. documentation updated

9\. changelog updated

10\. roadmap updated

11\. execution record updated

12\. PR prepared

13\. CI green

14\. review concerns addressed



Do not create a pile of partially completed refactors.



\---



\# 27. PR policy



Create one PR per coherent vertical slice.



PR title format:



```text

fix: <specific production issue>

feat: <specific production capability>

chore: <specific engineering improvement>

security: <specific security hardening>

test: <specific verification improvement>

```



Every PR body must include:



```text

Problem

Root cause

Implementation

Tests

Security impact

Performance impact

Compatibility impact

Documentation

Verification evidence

Known limitations

Rollback strategy

```



Never merge a PR merely because the code compiles.



\---



\# 28. Release gates



Production release is blocked if any of these are true:



\- P0 issue remains open

\- critical/high security issue remains unexplained

\- CI is red

\- required test is skipped

\- hardware-critical functionality is unverified

\- release artifact is unsigned when signing is required

\- artifact hash is missing

\- dependency vulnerability is unresolved without documented exception

\- installer cannot rollback

\- board persistence is not crash-safe

\- recorder can exhaust memory under documented usage

\- malicious input can cause uncontrolled resource consumption

\- production evidence is missing

\- README claims exceed verified evidence



\---



\# 29. Final verification report



At the end, produce:



```text

Production Readiness Report



Commit:

Build:

SDK:

OS:

Test count:

Passed:

Failed:

Skipped:

Coverage:

Security:

Dependencies:

CodeQL:

Secrets:

SBOM:

x64:

ARM64:

x86:

Installer:

Signing:

Hardware:

Accessibility:

Performance:

Persistence:

Import/export:

Recording:

Observability:

Documentation:



Remaining risks:

Accepted risks:

Unverified claims:



Final decision:

GO

NO-GO

CONDITIONAL GO

```



A `GO` requires evidence.



If hardware is unavailable, the correct result is:



```text

CONDITIONAL GO / NOT FULLY VERIFIED

```



not "production ready."



\---



\# 30. Final production-grade checklist



Before declaring completion, verify:



\## Source



\- \[ ] no known P0/P1 bugs

\- \[ ] no dead critical paths

\- \[ ] no unsafe broad catches hiding failures

\- \[ ] no uncontrolled allocations

\- \[ ] no unbounded parsing

\- \[ ] no architecture-specific ABI assumptions

\- \[ ] no unsafe process execution



\## Build



\- \[ ] SDK pinned

\- \[ ] warnings = errors

\- \[ ] deterministic build

\- \[ ] Release build reproducible

\- \[ ] x64 verified

\- \[ ] ARM64 verified

\- \[ ] x86 either verified or removed



\## Tests



\- \[ ] unit

\- \[ ] integration

\- \[ ] adversarial

\- \[ ] performance

\- \[ ] soak

\- \[ ] persistence

\- \[ ] rendering

\- \[ ] importer

\- \[ ] recorder

\- \[ ] native interop

\- \[ ] installer

\- \[ ] accessibility

\- \[ ] hardware where available



\## Security



\- \[ ] CodeQL

\- \[ ] dependency scan

\- \[ ] secret scan

\- \[ ] PowerShell scan

\- \[ ] workflow hardening

\- \[ ] SBOM

\- \[ ] no embedded secrets

\- \[ ] no unsafe executable trust

\- \[ ] untrusted files bounded

\- \[ ] diagnostics privacy reviewed



\## Reliability



\- \[ ] crash-safe save

\- \[ ] autosave

\- \[ ] recovery

\- \[ ] atomic replacement

\- \[ ] installer rollback

\- \[ ] low-disk behavior

\- \[ ] recording limits

\- \[ ] import cleanup



\## Release



\- \[ ] Authenticode signing

\- \[ ] timestamp

\- \[ ] SHA-256

\- \[ ] SBOM

\- \[ ] artifact verification

\- \[ ] release notes

\- \[ ] changelog

\- \[ ] roadmap

\- \[ ] production evidence

\- \[ ] rollback procedure



\## Documentation



\- \[ ] architecture current

\- \[ ] threat model current

\- \[ ] verification matrix current

\- \[ ] limitations explicit

\- \[ ] no unsupported claims

\- \[ ] production status evidence-backed



\## Final rule



\*\*Never say "production ready" because all tests passed.\*\*



Production readiness requires:



```text

correctness

\+

security

\+

reliability

\+

performance

\+

operability

\+

release integrity

\+

hardware verification

\+

documented evidence

```



If any critical dimension remains unverified, report the exact gap and keep the release blocked.

