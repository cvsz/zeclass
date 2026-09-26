# Localization

25 interface languages ship: English built in, plus 24 catalogues in `src/zEClass\lang`.

## How it works

`Locator.BuiltInEnglish` is the source of truth, defined in code so the app still has a usable
catalogue if every file is deleted. A `lang\*.json` file is *layered over* it, not substituted for
it, so a partial translation degrades one word at a time instead of blanking every untranslated
label. That is the property that makes a 48-key catalogue maintainable by hand.

`LanguageCatalog` is a static table of what the app knows about each language. It exists for two
reasons the JSON files cannot answer: the picker should show `Deutsch` rather than `de`, and a
right-to-left script has to be mirrored, not just translated. `LanguageCatalog.IsRightToLeft` drives
`FlowDirection` on the main window.

A file being present is not the same as a language being ready. The picker only offers codes that
appear in both the table and a file, plus any unknown file is still offered rather than silently
hidden, in case one was dropped in by hand.

## Adding a language

1. Create `src\zEClass\lang\<code>.json` with the shape below, translated.
2. Add a `LanguageInfo` to `LanguageCatalog.All`. Set `RightToLeft: true` for Arabic, Hebrew, Persian
   and Urdu.
3. Run the tests. The audit will tell you what you missed.

```json
{
  "Values": {
    "app.title": "...",
    "tool.pen": "..."
  }
}
```

The file must be UTF-8 **without a BOM**. Save it as plain UTF-8; a test enforces this, because a
BOM makes the first key unparseable by some readers and a mis-encoded file shows mojibake.

Every catalogue must carry all 48 keys. English is the fallback, so a key you leave out still
renders, it just renders in English, which is a worse experience than a translation and is flagged
as a missing key by the audit.

## The audit

`CatalogueAuditTests` runs against the real files, not fixtures, so a translation added carelessly
fails the build. It checks:

| Check | Catches |
| --- | --- |
| `CatalogueIsComplete` | a key left out, so the user sees English mid-menu |
| `CatalogueHasNoUnknownKeys` | a typo'd key, which looks translated but never appears |
| `CataloguePlaceholdersSurviveTranslation` | a dropped or renamed `{0}`, which throws `FormatException` the moment that status line updates |
| `CatalogueIsActuallyTranslated` | a file copied from English and never edited |
| `CatalogueHasNoEmptyValues` | a blank label |
| `FilesAreUtf8WithoutABom` | mojibake and unparseable first keys |
| `EveryLanguageFileIsListedInTheCatalog` | a file that would show as a bare code and never be mirrored |

### Identical strings are not always a bug

`tool.pen` is `Pen` in Dutch. `tool.ellipse` is `Oval` in German. `status.page` is `Page {0} / {1}`
in French. The audit flags any value identical to English, and those are exempted in
`CorrectlyIdentical` as explicit `language:key` entries with a comment saying why.

This is deliberately per-language rather than a blanket rule, because "Oval" is the German word but
"Page" is the French one and neither is true of the other. A blanket exemption would quietly stop
the audit catching a genuinely untranslated catalogue. Every entry is a review decision: if a key
ever gets a real translation, delete the entry and the audit starts checking it again.

## Known gaps

- **No native-speaker review.** The 24 catalogues were produced without one. The audit proves
  nothing is missing, mistyped or untranslated; it cannot prove the wording is idiomatic. Get them
  checked before classroom deployment.
- **Only about 48 strings are covered**: tool names, actions, status text and the calibration
  messages. Help text, the hardware acceptance prompts and the diagnostics report are English-only.
- **No Traditional Chinese.** `zh-TW` resolves to `zh-Hans`, which is documented in `Resolve` rather
  than silently substituted, but it is still the wrong script for a Traditional Chinese classroom.
- **No Persian or Urdu** despite both being right-to-left, so `RightToLeft` has only Arabic and
  Hebrew exercising it today.
