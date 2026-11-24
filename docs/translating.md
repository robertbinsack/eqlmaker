<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](translating.md) · [Deutsch](de/translating.md)

# Translating

EQLMaker is written in English and translated into German. This page says where the texts are,
how to change a translation and how to add a language. English is the reference: when two
versions disagree, the English one counts.

## What exists in which language

| What | English | German | Other languages |
|---|---|---|---|
| Documentation | `docs/*.md` | `docs/de/*.md` | `docs/<lang>/*.md` |
| README, ROADMAP, CONTRIBUTING | see `docs/languages.json` | see `docs/languages.json` | `README.<lang>.md` and so on |
| Program texts: help, messages, descriptions of MCP tools | in the code, exported to `src/i18n/en.json` | `src/i18n/de.json` | `src/i18n/<lang>.json` |
| Status tables in README and ROADMAP | `labels.en` and the `en` texts in `docs/status.json` | `labels.de` and the `de` texts | the same keys for the language |
| Web template | `src/templates/web/public/i18n/en.json` | `de.json` in the same folder | a further file there |
| Messages of the package setup | `scripts/setup/strings.en.json` | `strings.de.json` | a further file there |

Not translated, in any language: file formats and their keys, identifiers, names of commands,
options and MCP tools, error messages of the engine, and the instructions of the guided steps
(`src/prompts`).

## The setting file

`docs/languages.json` says which language is the reference and which file is the README and
the ROADMAP of each language:

```json
{
  "canonical": "en",
  "languages": ["en", "de"],
  "readme": { "de": "README.md", "en": "README.en.md" },
  "roadmap": { "en": "ROADMAP.md", "de": "ROADMAP.de.md" }
}
```

The checks and the status tables read this file. To make another language the one that the
repository page shows, rename the two README files and change `readme` here; then correct the
links of the language switch lines.

## Changing a translation

1. Change the English page first, then the same place in every translation.
2. Keep the structure: the same headings in the same order, the same number of links, the same
   tables with the same number of rows.
3. Keep code blocks identical. Commands, JSON and file names are not translated. What needs a
   translated comment goes into the text around the block.
4. Run the check.

## The check

```text
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\check-docs.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\check-docs.ps1 -Language de -Table translation-status.md
```

It compares every original with its translation: the sequence of heading levels, the code
blocks, the number of links and pictures, and the number of rows of every table. It prints one
line per file with `in step`, `drift` (and what differs), `missing` or `no original`, and ends
with exit code 1 when something is not in step. `-Table <file>` also writes that list as a
Markdown table; the file is a report for you and is not committed.

The check sees structure, not meaning. A translation that passes can still be wrong.

A translation that has a different number of links on purpose says so in a comment, with the
difference to the original:

```text
<!-- check-docs: links -1 -->
```

Program texts are checked by tests:

```text
bin\eqlmaker-tests.exe I18n
```

`I18nTests.TranslationsHaveTheSameKeysAndPlaceholders` fails when a translation lacks a key,
has an unknown key, or lost a placeholder (`{0}`), an option name (`--name`) or an identifier
with an underscore (`record_start`). `I18nTests.EnglishFileIsTheExportOfTheCode` fails when
`en.json` is not the export of the code.

## Adding a language

Use the two-letter code of the language, here `xx`.

1. **Documentation.** Copy `docs/*.md` to `docs/xx/` and translate the text. In every page,
   extend the language switch line at the top, in all languages.
2. **README, ROADMAP, CONTRIBUTING.** Add `README.xx.md`, `ROADMAP.xx.md` and
   `CONTRIBUTING.xx.md`. Add `xx` to `languages`, `readme` and `roadmap` in
   `docs/languages.json`.
3. **Status tables.** Add `labels.xx` to `docs/status.json` and an `xx` text to the `name` and
   `description` of every feature. Run `scripts\update-status.ps1`; it fills the status blocks
   of the new README and ROADMAP.
4. **Program texts.** Export the English texts and translate the values, not the keys:

   ```text
   bin\eqlmaker i18n-export src\i18n\en.json
   copy src\i18n\en.json src\i18n\xx.json
   ```

   In the usage text (key `usage`), translate the description of each line and keep the column
   with the commands as it is. Build again (`build.cmd`) so that the file is copied next to the
   programs, then try it: `bin\eqlmaker help --lang xx`.
5. **Web template**, if you want generated applications in the language: add
   `src/templates/web/public/i18n/xx.json` with the keys of `en.json` there; the Node tests of
   the template compare the keys.
6. Run the check and the tests above.

A language is listed as complete when the check passes for it and the program texts have every
key. A partial translation is welcome too: say in the pull request what is missing.

## Wording

- Plain, short sentences. Say what to do.
- One word for one thing, everywhere: recording, time line, specification, mask, statement,
  guided step, comparison test. Look how the German pages name them before you choose words.
- `generate`, `clone` and `enrich` are guided steps in every language. Do not translate them
  into words that mean "automatic".
