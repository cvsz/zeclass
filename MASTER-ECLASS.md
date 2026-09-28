\# zEClass — Production-Grade Autonomous Engineering Master Prompt



\## Mission



You are the principal engineer responsible for taking:



\*\*Repository:\*\* `https://github.com/cvsz/zeclass`



from its current state to a \*\*verified production-grade release candidate\*\*.



Do not merely review, summarize, recommend, or generate TODO lists.



\*\*Inspect → plan → implement → test → scan → review → fix → re-test → document → release-gate.\*\*



You are authorized to make all necessary code, test, configuration, documentation, CI/CD, security, reliability, performance, packaging, and release-hygiene changes required to achieve the acceptance criteria below.



Do not claim something is fixed, secure, production-ready, tested, or verified unless you actually have evidence from the repository, commands, CI, or other authoritative execution results.



\---



\# 1. NON-NEGOTIABLE OPERATING RULES



\## 1.1 Start from reality



Before changing anything:



```bash

git fetch --all --prune

git status --short --branch

git branch -a

git log --oneline --decorate -20

git remote -v

```



Determine:



\* current branch;

\* current commit;

\* upstream;

\* dirty working tree;

\* open/merged/relevant branches if available;

\* tags;

\* existing release state;

\* existing CI state.



Do not assume the repository matches an earlier review.



Re-read the current repository.



\---



\# 2. EXISTING PLANNING/DOCUMENTATION FIRST



Before implementation inspect:



```text

AGENTS.md

CLAUDE.md

GEMINI.md

README.md

ROADMAP.md

docs/\*\*

.github/\*\*

Directory.Build.\*

Directory.Packages.\*

global.json

\*.sln

\*.csproj

\*.props

\*.targets

```



Also inspect:



```text

tests/\*\*

src/\*\*

scripts/\*\*

tools/\*\*

installer/\*\*

```



If planning documents exist, treat them as authoritative project intent unless they conflict with actual implementation.



Identify:



\* completed work;

\* stale claims;

\* incomplete work;

\* contradictory documentation;

\* obsolete TODOs;

\* missing execution records.



Do not delete useful historical evidence merely because it is inconvenient.



\---



\# 3. ESTABLISH A BASELINE



Before modifying code, execute the strongest available validation.



At minimum attempt:



```bash

dotnet --info

dotnet --list-sdks

dotnet --list-runtimes

dotnet restore

dotnet build

dotnet test

dotnet format --verify-no-changes

```



Also run available:



```text

CodeQL

dependency review

NuGet audit

dotnet list package --vulnerable

PSScriptAnalyzer

YAML validation

PowerShell syntax checks

packaging checks

publish checks

```



If Windows-specific commands are unavailable in the current environment:



\* do not fake them;

\* document the limitation;

\* execute every equivalent validation available;

\* create CI jobs/tests that will perform the missing validation on Windows.



Record baseline results.



\---



\# 4. ONE COMPLETE VERTICAL SLICE AT A TIME



Work in complete vertical slices.



For each slice:



1\. inspect;

2\. understand root cause;

3\. implement;

4\. add regression tests;

5\. run targeted tests;

6\. run relevant static/security checks;

7\. run full suite;

8\. review diff;

9\. update documentation;

10\. update roadmap/execution record;

11\. commit;

12\. prepare PR-ready state.



Do not create half-implemented abstractions for future work.



Do not leave:



```text

TODO

FIXME

NotImplementedException

throw new NotSupportedException

placeholder

stub

fake success

ignored exception

```



unless there is a documented and intentionally accepted reason.



\---



\# 5. PRIORITY ORDER



Use this order unless new evidence proves a higher-risk issue exists.



\## P0 — Release/security/reliability blockers



1\. Real Authenticode release signing

2\. CI execution verification

3\. `main` branch protection/repository governance

4\. Bounded PDF decompression

5\. Bounded Office ZIP extraction

6\. Streaming/bounded screen recording



\## P1 — Runtime correctness



7\. Fix Win32 DPI P/Invoke

8\. Aggregate board/document resource limits

9\. Save-As transactional state semantics

10\. Autosave revision ordering

11\. Autosave settings correctness

12\. Office media format correctness

13\. Office extension/support contract

14\. Correct PDF page mapping

15\. Import staging cleanup/retention

16\. NDI executable trust validation

17\. Privacy-safe diagnostics



\## P1 — Release lifecycle



18\. .NET LTS migration assessment and implementation

19\. Release/tag/application-version synchronization

20\. Signed artifact manifest

21\. SBOM-to-artifact binding

22\. Explicit license



\## P2 — Production validation



23\. Physical hardware matrix

24\. DPI/multi-monitor testing

25\. accessibility

26\. localization

27\. long-duration soak

28\. update/uninstall/rollback

29\. performance/load testing



If implementation reveals a more severe issue, reprioritize based on:



```text

security impact

data loss

RCE/process execution

resource exhaustion

release integrity

correctness

reliability

performance

maintainability

UX

```



\---



\# 6. P0 — RELEASE SIGNING



\## Problem



Do not confuse:



```text

Strong-name assembly signing

```



with:



```text

Windows Authenticode signing

```



The release must use an actual supported Authenticode signing mechanism.



Inspect the existing Azure signing implementation.



Determine whether the repository intends to use:



\* Azure Trusted Signing;

\* SignTool;

\* certificate-store signing;

\* another Microsoft-supported signing mechanism.



Do not invent credentials.



Do not commit certificates or private keys.



Do not commit secrets.



\---



\## Required release flow



The final release pipeline must conceptually be:



```text

build

&#x20; ↓

test

&#x20; ↓

publish

&#x20; ↓

Authenticode sign

&#x20; ↓

timestamp

&#x20; ↓

verify signature

&#x20; ↓

verify certificate identity

&#x20; ↓

package

&#x20; ↓

generate hashes

&#x20; ↓

generate SBOM

&#x20; ↓

generate release manifest

&#x20; ↓

verify package contents

&#x20; ↓

publish release

```



Verify:



\* PE signature exists;

\* signature is valid;

\* timestamp exists where supported;

\* expected signer identity is present;

\* expected certificate/thumbprint is matched where applicable;

\* unsigned executable is rejected when signing is mandatory.



Support an explicit development/pilot mode if necessary, but production release must fail closed when required signing configuration is absent.



Never allow:



```text

production tag

→ unsigned artifact

```



unless the release policy explicitly marks it as a non-production prerelease.



Add automated tests/checks for the release gate.



\---



\# 7. P0 — CI VERIFICATION



Inspect all workflows.



Ensure workflows cover at minimum:



```text

build

test

format

static analysis

dependency audit

CodeQL

PowerShell analysis

publish

artifact validation

SBOM

release verification

```



Ensure Windows-specific jobs run on Windows.



Ensure failures propagate correctly.



Ensure:



```yaml

continue-on-error: true

```



is not masking production-critical failures.



Check:



\* permissions;

\* token scope;

\* artifact retention;

\* dependency pinning;

\* action versions;

\* concurrency;

\* cancellation;

\* secrets;

\* environment protection.



Use least privilege.



Then trigger/verify CI where repository permissions allow.



Do not claim CI is passing merely because YAML parses.



Record actual run evidence.



\---



\# 8. P0 — BRANCH PROTECTION



The production branch must not permit uncontrolled direct pushes.



Establish or document a ruleset requiring:



\* pull request;

\* successful required CI;

\* security checks;

\* appropriate approval;

\* conversation resolution;

\* no force pushes;

\* no branch deletion;

\* controlled merge policy.



If repository administration cannot be modified from the current environment:



\* create the required repository configuration documentation;

\* create a machine-checkable governance checklist;

\* clearly mark the external administrative action as unverified.



Never claim branch protection exists unless verified.



\---



\# 9. P0 — PDF SECURITY HARDENING



Treat PDFs as hostile input.



Never perform unbounded decompression.



Replace patterns such as:



```csharp

stream.CopyTo(output);

```



with bounded streaming.



Implement configurable limits:



```text

MaxPdfFileBytes

MaxCompressedStreamBytes

MaxInflatedStreamBytes

MaxTotalInflatedBytes

MaxImageCount

MaxProcessingTime

MaxPages

```



Use overflow-safe arithmetic.



Reject malformed inputs.



Avoid integer overflow.



Avoid uncontrolled allocations.



Avoid repeated copying.



Add regression tests for:



\* oversized PDF;

\* compressed bomb;

\* malformed stream;

\* truncated stream;

\* invalid filters;

\* huge declared length;

\* nested/hostile content;

\* valid normal PDF;

\* multiple pages;

\* page/image mapping.



\---



\# 10. P0 — OFFICE ZIP SECURITY



DOCX/PPTX/etc. are ZIP containers and must be treated as hostile input.



Implement:



```text

MaxArchiveBytes

MaxEntryCount

MaxCompressedEntryBytes

MaxUncompressedEntryBytes

MaxTotalUncompressedBytes

MaxCompressionRatio

MaxProcessingTime

```



Never extract unbounded data into memory.



Use bounded streaming.



Reject:



\* ZIP bombs;

\* path traversal;

\* absolute paths;

\* `../`;

\* invalid entry names;

\* excessive entry counts;

\* oversized files;

\* unsupported formats.



Ensure temporary extraction directories are isolated and random.



Add cleanup on:



```text

success

failure

cancellation

startup orphan recovery

```



Add malicious ZIP regression tests.



\---



\# 11. P0 — SCREEN RECORDER



Do not buffer an entire recording in:



```csharp

List<byte\[]>

```



Design the recorder to stream frames to disk.



Required behavior:



```text

Start

→ create bounded temporary/output file

→ encode/write frames incrementally

→ flush safely

→ Stop

→ finalize container

```



Add:



```text

MaxRecordingDuration

MaxRecordingBytes

MinimumFreeDiskSpace

MaxFrameRate

MaxResolution

```



On resource exhaustion:



\* stop gracefully;

\* preserve valid output if possible;

\* report explicit error;

\* do not crash the application.



Add tests proving:



```text

memory usage does not grow linearly with duration

```



Test:



\* start/stop;

\* cancellation;

\* disk-full;

\* insufficient disk;

\* encoder failure;

\* corrupted frame;

\* long recording;

\* invalid dimensions;

\* invalid FPS.



\---



\# 12. P1 — WIN32 INTEROP



Audit every P/Invoke.



For every declaration verify against official Windows API definitions:



```text

name

calling convention

return type

parameter types

BOOL

HANDLE

HWND

DWORD

ULONG

SIZE\_T

LPVOID

struct layout

packing

character set

SetLastError

```



Pay particular attention to:



```text

EnableNonClientDpiScaling

SetupAPI

HID

user32

gdi32

NDI/native bridge

pointer/input APIs

```



Remove unused incorrect declarations.



Where possible use:



```text

LibraryImport

```



instead of legacy `DllImport`.



Add architecture validation for:



```text

x86

x64

ARM64

```



where the application officially supports those targets.



\---



\# 13. P1 — DIGITIZER/HID



Do not silently turn native errors into:



```text

no devices

```



Return structured diagnostics.



Distinguish:



```text

NoDevice

PermissionDenied

EnumerationFailed

MalformedDevice

UnsupportedDevice

NativeApiFailure

Success

```



Capture useful non-secret diagnostic data:



```text

vendor

product

usage page

usage

device class

capabilities

```



Avoid exposing unique hardware identifiers in shareable diagnostics.



Add tests using mocked/native abstraction boundaries.



\---



\# 14. P1 — BOARD MODEL LIMITS



Create centralized validation constants.



At minimum:



```text

MaxPages

MaxPageNameBytes

MaxStrokesPerPage

MaxTotalStrokes

MaxPointsPerStroke

MaxTotalPoints

MaxImagesPerPage

MaxTotalImages

MaxTextBytes

MaxBoardBytes

```



Do not rely only on a total JSON file-size limit.



Validation must occur:



```text

before deserialization where possible

during deserialization where practical

after deserialization

before rendering

before saving

```



Reject maliciously oversized structures.



Avoid quadratic behavior.



Add fuzz/property-style tests where practical.



\---



\# 15. P1 — SERIALIZATION



Audit all JSON serialization.



Prefer safe defaults.



Remove:



```csharp

UnsafeRelaxedJsonEscaping

```



unless there is a demonstrated compatibility requirement.



Document:



\* schema version;

\* migration policy;

\* backwards compatibility;

\* corruption handling.



Implement schema migration if needed.



A malformed board file must never crash the application.



\---



\# 16. P1 — SAVE-AS TRANSACTIONAL CORRECTNESS



Never mutate authoritative application state before persistence succeeds.



Required pattern:



```text

candidate path

candidate metadata

&#x20;      ↓

write temporary file

&#x20;      ↓

flush

&#x20;      ↓

atomic replace/move

&#x20;      ↓

commit in-memory state

```



If saving fails:



```text

old path remains authoritative

old document name remains authoritative

dirty state remains correct

user receives explicit failure

```



Add regression tests.



Test:



\* invalid path;

\* permission denied;

\* disk full;

\* locked destination;

\* cancellation;

\* replacement failure.



\---



\# 17. P1 — AUTOSAVE



Implement explicit revision semantics.



Every mutation should advance:



```text

DocumentRevision

```



Autosave snapshots must record:



```text

revision

timestamp

```



A stale snapshot must never overwrite a newer snapshot.



Implement:



```text

dirty revision

saved revision

autosaved revision

```



Honor:



```text

AutoSaveEnabled

AutoSaveInterval

```



dynamically.



Avoid unnecessary deep cloning when no changes occurred.



Do not block the UI thread with large snapshot operations if avoidable.



Handle:



```text

shutdown

crash recovery

write failure

cancellation

disk full

concurrent mutation

```



Add tests for out-of-order completion.



\---



\# 18. P1 — IMPORTER CONTRACT



Ensure `IsSupported()` exactly matches what the implementation can actually import.



If:



```text

.doc

.ppt

```



are unsupported, do not advertise them as supported.



If supported formats include:



```text

.docx

.docm

.pptx

.pptm

```



ensure each has tests.



No extension may be accepted solely because it looks related.



\---



\# 19. P1 — OFFICE MEDIA FORMAT



Never save arbitrary bytes using the wrong extension.



Detect actual content using:



```text

magic bytes

MIME/content signature

container metadata

```



Map correctly:



```text

JPEG → .jpg

PNG → .png

EMF → .emf

WMF → .wmf

```



Reject unknown content.



Test each format.



\---



\# 20. P1 — OFFICE PAGE PREVIEW CORRECTNESS



Determine whether the product requirement is:



```text

extract embedded media

```



or:



```text

render each Office page/slide

```



If the requirement is actual page preview, do not pretend that selecting the first embedded image is equivalent.



Implement the correct page/slide mapping.



If rendering requires a platform dependency, make that dependency explicit and tested.



\---



\# 21. P1 — PDF PAGE CORRECTNESS



If the UI says:



```text

Import PDF pages

```



then implement actual:



```text

PDF page → rendered page image

```



mapping.



Do not simply extract arbitrary PDF image streams and label them as pages.



Each generated page must retain:



```text

source page number

```



and preserve ordering.



Add tests with:



\* text-only pages;

\* image pages;

\* mixed content;

\* multiple images;

\* repeated images;

\* rotated pages;

\* unusual page sizes.



\---



\# 22. P1 — TEMP FILE LIFECYCLE



Create a central temporary-storage manager.



Track:



```text

owner

created

size

purpose

source operation

```



Cleanup:



```text

after success

after failure

after cancellation

on startup

on application shutdown

by age

by total size

```



Never delete arbitrary files.



Use a dedicated application-owned directory.



\---



\# 23. P1 — NDI PROCESS TRUST



Environment variables must not be treated as trust.



Before executing external native binaries:



verify:



```text

expected path

trusted directory

file exists

file type

Authenticode signature where available

expected publisher

optional SHA-256 allowlist

```



Prevent arbitrary execution in production mode.



Keep development override explicit and visible.



Never log secrets.



\---



\# 24. P1 — DIAGNOSTICS PRIVACY



Create:



```text

FullDiagnostics

SafeDiagnostics

```



Safe diagnostics must redact:



```text

username

home directory

full file paths

machine identifiers

unique hardware IDs

secrets

tokens

environment variables

```



Add tests that assert no sensitive values appear in safe reports.



\---



\# 25. RELEASE VERSIONING



Make the release tag authoritative.



Example:



```text

v1.2.3

```



must produce:



```text

Version       = 1.2.3

ProductVersion= 1.2.3

FileVersion   = 1.2.3.0

AssemblyVersion according to policy

```



Do not leave hard-coded release versions in multiple locations.



Validate tag/version consistency in CI.



Reject mismatched release builds.



\---



\# 26. RELEASE MANIFEST



Every release should contain:



```text

release-manifest.json

SHA256SUMS

SBOM

artifacts

signature verification information

```



Manifest fields should include:



```json

{

&#x20; "product": "zEClass",

&#x20; "version": "...",

&#x20; "commit": "...",

&#x20; "rid": "...",

&#x20; "artifact": "...",

&#x20; "sha256": "...",

&#x20; "size": 0,

&#x20; "signed": true,

&#x20; "signer": "...",

&#x20; "sbomSha256": "...",

&#x20; "buildWorkflow": "...",

&#x20; "buildRunId": "..."

}

```



Do not invent values.



Populate from actual build results.



\---



\# 27. SBOM



Generate a CycloneDX SBOM from the exact release build.



Bind it to:



```text

artifact hash

commit

version

RID

```



Validate that the SBOM is actually produced.



Fail release if mandatory SBOM generation fails.



\---



\# 28. DEPENDENCY SECURITY



Run:



```bash

dotnet list package --vulnerable --include-transitive

```



and the repository's dependency-review workflow.



Inspect:



\* direct dependencies;

\* transitive dependencies;

\* abandoned libraries;

\* known CVEs;

\* licenses;

\* native binaries.



Do not blindly upgrade everything.



For every upgrade:



```text

upgrade

→ build

→ test

→ security scan

→ behavior validation

```



Avoid unnecessary dependency churn.



\---



\# 29. .NET VERSION



Evaluate migration from:



```text

net8.0-windows

```



to the current supported LTS release.



Do not migrate merely because a newer version exists.



First identify:



```text

WPF compatibility

System.Drawing behavior

native API compatibility

third-party packages

Windows SDK requirements

runtime behavior

publish behavior

```



Then migrate as a dedicated vertical slice.



The target must be a supported production runtime.



\---



\# 30. PERFORMANCE



Measure before optimizing.



Add benchmarks or repeatable measurements for:



```text

startup

document open

document save

large board rendering

large stroke set

PDF import

Office import

autosave

recording

NDI startup

```



Track:



```text

latency

CPU

memory

GC

disk I/O

```



Set realistic regression budgets.



Do not optimize by speculation.



\---



\# 31. CONCURRENCY



Audit:



```text

UI thread

dispatcher

timers

background tasks

cancellation tokens

file writes

autosave

recording

NDI

audio

imports

shutdown

```



Every background operation must have:



```text

CancellationToken

timeout where appropriate

exception handling

lifecycle ownership

```



Avoid:



```text

async void

fire-and-forget

Task.Run without ownership

unobserved exceptions

cross-thread UI mutation

```



unless explicitly justified.



\---



\# 32. SHUTDOWN



Application shutdown must be deterministic.



Order:



```text

stop accepting new work

cancel background operations

stop recorder

stop NDI

flush autosave

persist necessary state

dispose native resources

dispose timers

dispose streams

exit

```



No background process should survive unexpectedly.



No temporary file should remain unnecessarily.



No data should be silently lost.



Add shutdown tests where practical.



\---



\# 33. ACCESSIBILITY



Verify:



```text

keyboard navigation

focus order

focus visibility

automation names

button labels

menu semantics

contrast

tooltips

screen reader compatibility

high DPI

text scaling

```



Add automated checks where possible.



Document manual checks where automation is unavailable.



\---



\# 34. MULTI-DPI / DISPLAY



Test:



```text

100%

125%

150%

175%

200%

```



and:



```text

single monitor

mixed DPI

monitor unplug/replug

window move between monitors

fullscreen

projector

touch

pen

mouse

```



Do not assume DPI correctness from code inspection.



\---



\# 35. HARDWARE MATRIX



Document and test supported input devices.



At minimum categorize:



```text

mouse

touch

stylus

digitizer

external pen tablet

unsupported HID

```



Test on actual Windows hardware where possible.



If hardware cannot be tested in the current environment:



\* build automated abstractions;

\* add mocks;

\* add a manual test matrix;

\* mark physical verification explicitly as pending.



Never claim it passed without evidence.



\---



\# 36. TEST REQUIREMENTS



Increase test coverage around the highest-risk code.



Required regression suites:



```text

Board serialization

Board limits

Save/SaveAs

Autosave

PDF parser

Office parser

ZIP security

NDI process lifecycle

Native interop

Recording limits

Release versioning

Release signing

Safe diagnostics

```



Tests must prove failure behavior, not only happy paths.



Use:



```text

unit tests

integration tests

property/fuzz tests where useful

filesystem tests

resource-limit tests

architecture tests

```



\---



\# 37. STATIC ANALYSIS



Enable and enforce:



```text

dotnet format

Roslyn analyzers

nullable

warnings-as-errors where practical

CodeQL

dependency review

NuGet vulnerability scan

PSScriptAnalyzer

YAML validation

PowerShell syntax validation

```



Do not suppress warnings without:



```text

justification

scope

tracking issue or documentation

```



Never use broad suppression to make CI green.



\---



\# 38. SECRETS



Search repository history and current tree for:



```text

API keys

tokens

passwords

private keys

certificates

PFX

connection strings

GitHub tokens

Azure credentials

NDI secrets

```



Use:



```text

gitleaks

GitHub secret scanning

```



where available.



Never commit:



```text

.pfx

.p12

.key

.pem

.env with credentials

```



If a real secret is discovered:



1\. stop;

2\. identify exposure;

3\. remove from current tree;

4\. rotate/revoke it;

5\. document remediation;

6\. verify history where appropriate.



Never merely delete a secret and declare the incident fixed.



\---



\# 39. DOCUMENTATION



Update:



```text

README.md

docs/ROADMAP.md

docs/ARCHITECTURE.md

docs/SECURITY.md

docs/RELEASE.md

docs/TESTING.md

docs/PRODUCTION-READINESS.md

docs/EXECUTION-LOG.md

CHANGELOG.md

```



Only create files that fit the repository's existing documentation structure.



Documentation must distinguish:



```text

VERIFIED

```



from:



```text

IMPLEMENTED BUT NOT VERIFIED

```



from:



```text

REQUIRES PHYSICAL/MANUAL VALIDATION

```



Never write:



```text

production ready

```



unless all release gates are actually satisfied.



\---



\# 40. ROADMAP / EXECUTION RECORD



Maintain an explicit status table:



```text

ID

Priority

Problem

Root Cause

Implementation

Tests

Security Validation

Performance Validation

Evidence

Status

Commit

PR

```



Statuses:



```text

DISCOVERED

PLANNED

IMPLEMENTED

TESTED

VERIFIED

BLOCKED

DEFERRED

```



Never mark:



```text

VERIFIED

```



without evidence.



\---



\# 41. CHANGELOG



Every meaningful production fix must be reflected in the changelog.



Use categories:



```text

Added

Changed

Fixed

Security

Performance

Release

Breaking

```



Security fixes must describe impact without exposing exploitable implementation details unnecessarily.



\---



\# 42. GIT DISCIPLINE



Never work directly on production `main` unless repository policy explicitly requires it.



Create focused branches:



```text

codex/p0-release-signing

codex/p0-import-security

codex/p0-recorder

codex/p1-persistence

...

```



Prefer one logical vertical slice per PR.



Do not create giant unrelated commits.



Commit messages should explain intent.



Before each commit:



```bash

git diff --check

git status

```



Review the entire diff.



\---



\# 43. PR REQUIREMENTS



Every PR must contain:



```text

Problem

Root cause

Implementation

Tests

Security impact

Performance impact

Compatibility impact

Documentation updates

Validation evidence

Remaining limitations

```



Do not claim CI passes until it actually passes.



\---



\# 44. FAILURE POLICY



If a test fails:



Do not simply disable it.



Determine:



```text

real bug

test bug

environment issue

unsupported platform

flaky behavior

expected behavior mismatch

```



Fix the root cause.



If genuinely environment-specific:



\* isolate;

\* document;

\* add appropriate CI coverage;

\* do not weaken production checks.



Never:



```text

delete test

skip test

increase timeout blindly

catch exception

return success

```



just to make CI green.



\---



\# 45. SECURITY FAILURE POLICY



For security-sensitive failures:



```text

STOP

INVESTIGATE

FIX

ADD REGRESSION TEST

RE-SCAN

```



Do not continue piling unrelated changes onto a known security regression.



\---



\# 46. PRODUCTION READINESS GATES



The repository may only be declared production-ready when all applicable gates are satisfied.



\## Build



\* \[ ] clean restore

\* \[ ] clean build

\* \[ ] no unexpected warnings

\* \[ ] release build succeeds

\* \[ ] all intended RIDs publish successfully



\## Tests



\* \[ ] unit tests pass

\* \[ ] integration tests pass

\* \[ ] regression tests pass

\* \[ ] security tests pass

\* \[ ] release tests pass

\* \[ ] no unexplained skipped tests



\## Security



\* \[ ] CodeQL passes

\* \[ ] dependency review passes

\* \[ ] dependency vulnerability scan reviewed

\* \[ ] secret scan clean

\* \[ ] no known unaccepted critical/high vulnerability

\* \[ ] hostile archive handling bounded

\* \[ ] hostile PDF handling bounded

\* \[ ] native execution trust validated

\* \[ ] diagnostics do not leak secrets



\## Persistence



\* \[ ] save is transactional

\* \[ ] save-as is transactional

\* \[ ] corruption is handled

\* \[ ] schema is versioned

\* \[ ] migration is defined

\* \[ ] autosave ordering is deterministic

\* \[ ] crash recovery tested



\## Runtime



\* \[ ] no unbounded memory growth

\* \[ ] recording is streaming/bounded

\* \[ ] import processing is bounded

\* \[ ] background work is cancellable

\* \[ ] shutdown is deterministic

\* \[ ] native handles/resources are disposed



\## Native Windows



\* \[ ] all P/Invokes audited

\* \[ ] x64 validated

\* \[ ] x86 validated if supported

\* \[ ] ARM64 validated if supported

\* \[ ] DPI behavior verified

\* \[ ] HID behavior verified



\## Release



\* \[ ] version derives from tag

\* \[ ] artifact hashes generated

\* \[ ] SBOM generated

\* \[ ] SBOM tied to release artifact

\* \[ ] Authenticode signature verified

\* \[ ] signer identity verified

\* \[ ] timestamp verified where applicable

\* \[ ] release manifest generated

\* \[ ] package contents verified

\* \[ ] unsigned production release impossible



\## Governance



\* \[ ] main branch protected

\* \[ ] required CI checks configured

\* \[ ] security checks required

\* \[ ] force pushes prevented

\* \[ ] release permissions minimized



\## Documentation



\* \[ ] README accurate

\* \[ ] roadmap accurate

\* \[ ] architecture accurate

\* \[ ] security documentation accurate

\* \[ ] release documentation accurate

\* \[ ] changelog updated

\* \[ ] execution evidence recorded

\* \[ ] known limitations documented



\---



\# 47. EVIDENCE STANDARD



For every completed production gate provide evidence such as:



```text

command

exit code

test count

workflow run

artifact

hash

scan result

file

commit

PR

```



Example:



```text

PASS

dotnet test

460 passed

1 skipped

0 failed

commit abc1234

```



Do not say:



```text

"looks good"

"should work"

"CI should pass"

"production ready"

```



without evidence.



\---



\# 48. FINAL DEEP REVIEW



After all implementation work:



Perform another independent review as if you did not write the changes.



Look specifically for:



```text

security regressions

TOCTOU

path traversal

resource exhaustion

race conditions

deadlocks

UI freezes

data loss

stale state

native ABI mistakes

wrong assumptions

incorrect release gates

CI bypasses

secret leaks

documentation lies

```



Search again for:



```text

TODO

FIXME

HACK

NotImplemented

NotSupported

catch (Exception)

async void

Task.Run

fire-and-forget

CopyTo(

ReadAllBytes

ReadAllText

GetBytes

List<byte\[]>

Unsafe

Marshal

DllImport

Process.Start

```



Every hit must be reviewed.



Do not blindly eliminate legitimate uses.



\---



\# 49. FINAL CLEAN BUILD



Run the complete validation suite again.



At minimum:



```bash

git diff --check

dotnet restore

dotnet format --verify-no-changes

dotnet build

dotnet test

dotnet list package --vulnerable --include-transitive

```



Then execute Windows-specific validation through CI:



```text

build

test

publish

CodeQL

dependency review

PowerShell analysis

SBOM

artifact validation

signing

release verification

```



Run the full suite after the final code change.



\---



\# 50. FINAL REPORT



At the end produce a concise but complete report containing:



\## Executive status



```text

PRODUCTION READY

CONDITIONAL

NO-GO

```



Never choose PRODUCTION READY without evidence.



\## Changes completed



List every vertical slice.



\## Security



List:



```text

fixed

verified

remaining

```



\## Tests



Provide exact:



```text

passed

failed

skipped

not run

reason

```



\## CI



Provide actual workflow evidence.



\## Release



Provide:



```text

version

commit

RID

artifact

SHA256

signature status

SBOM

```



\## Remaining blockers



Only actual blockers.



\## Deferred items



Explicitly explain why they are deferred.



\## Manual validation



List physical/manual tests that cannot be executed automatically.



\## Final decision



One of:



```text

GO

CONDITIONAL GO

NO-GO

```



with objective justification.



\---



\# 51. ABSOLUTE PROHIBITIONS



Never:



\* invent test results;

\* invent CI results;

\* invent security findings;

\* invent release signatures;

\* invent certificates;

\* invent production traffic;

\* invent hardware verification;

\* claim a vulnerability is fixed without a regression test where practical;

\* disable security checks to make CI pass;

\* silently weaken validation;

\* commit credentials;

\* commit certificates/private keys;

\* bypass branch protections;

\* delete failing tests;

\* suppress warnings globally without justification;

\* mark unverified work as complete;

\* rewrite project history destructively;

\* force-push;

\* publish an unsigned production artifact;

\* introduce unrelated feature work while production blockers remain.



\---



\# 52. DEFINITION OF DONE



The task is complete only when:



```text

Current repository inspected

&#x20;       ↓

Baseline established

&#x20;       ↓

Highest-risk defects fixed

&#x20;       ↓

Regression tests added

&#x20;       ↓

Full test suite passes

&#x20;       ↓

Security scans pass

&#x20;       ↓

CI executes successfully

&#x20;       ↓

Release build succeeds

&#x20;       ↓

Artifacts are verified

&#x20;       ↓

Authenticode signing is verified

&#x20;       ↓

SBOM is generated and bound

&#x20;       ↓

Version/tag consistency verified

&#x20;       ↓

Documentation updated

&#x20;       ↓

Roadmap/execution evidence updated

&#x20;       ↓

Final independent review completed

&#x20;       ↓

No unexplained production blockers remain

```



Only then may you recommend:



```text

GO

```



Otherwise return:



```text

CONDITIONAL GO

```



or:



```text

NO-GO

```



with exact blockers.



\---



\# 53. START NOW



Do not ask me to manually break the work into smaller tasks.



Begin autonomously.



Start with:



```text

1\. Revalidate repository state.

2\. Read all applicable instructions.

3\. Establish baseline.

4\. Build a prioritized execution plan.

5\. Fix P0 release/security/runtime blockers.

6\. Add regression tests.

7\. Run validation.

8\. Fix failures.

9\. Continue through P1/P2.

10\. Update documentation and execution records.

11\. Prepare focused PRs.

12\. Perform final independent production-readiness review.

```



\*\*Implement the work. Do not merely tell me what should be implemented.\*\*



If an external permission, GitHub administration setting, physical Windows device, signing service, certificate, or secret is genuinely required and unavailable, do not fabricate success. Implement everything possible, create the required automation/checks, clearly identify the external blocker, and continue with all independent work.



The final output must be evidence-based.



