<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](CONTRIBUTING.md) · [Deutsch](CONTRIBUTING.de.md)

# Contributing

Contributions are welcome: bug reports, fixes, tests, translations, plugins and documentation.
This page says how to build, how to test, and what a change needs before it can be merged.

## Build

```text
build.cmd
build.cmd -Lint
```

`build.cmd` needs nothing but Windows: it uses the C# compiler of the .NET Framework 4 that is
part of every Windows. There is no SDK, no package manager and no project file. The result is
the folder `bin` with `eqlmaker.exe`, `eqlmaker-timestamp.exe`, `sample-desktop.exe`,
`eqlmaker-tests.exe` and the resources in `bin\share`. `-Lint` also checks the JavaScript,
PowerShell and JSON files for syntax errors (JavaScript needs Node.js on the `PATH`).

## Tests

```text
bin\eqlmaker-tests.exe
bin\eqlmaker-tests.exe Timeline
bin\eqlmaker-tests.exe --list
bin\eqlmaker-tests.exe --json test-results.json
```

A word after the program name is a filter on the test names. A test that lacks what it needs is
reported as skipped, with the reason. To run everything, give the tests:

| What | How |
|---|---|
| A PostgreSQL 15 or later for the database tests | Set `EQLMAKER_TEST_PG` to the connection string of a superuser, for example `host=127.0.0.1 port=5432 user=postgres dbname=postgres password='...'`. The server needs `wal_level = logical` and `logging_collector = on`. Use a server for tests: the tests create and drop databases and roles. |
| FFmpeg for the video tests | `ffmpeg` on the `PATH`, or its path in `EQLMAKER_TEST_FFMPEG` |
| A speech recogniser for the speech test | path of a `whisper-cli.exe` in `EQLMAKER_TRANSCRIBE` (the release package has one) |
| Microsoft Edge or Chrome | for the tests that operate a web page; Edge is part of Windows |

The tests of the web template and of the browser variant of the sample run with Node.js 18 or
later:

```text
cd src\templates\web
node --test
cd ..\..\..\examples\sample-app\web
npm ci
node --test
```

The checks of the repository:

```text
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\check-spdx.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\check-docs.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\update-status.ps1 -Check
```

The workflow `.github/workflows/ci.yml` runs all of this on every pull request, against a plain
PostgreSQL.

## Style

- **C# 5.** The compiler of the .NET Framework knows no newer language version: no string
  interpolation, no `?.`, no expression-bodied members, no `out var`.
- **Warnings are errors.** The compiler is the linter (`/warn:4 /warnaserror+`).
- **No dependencies in the engine.** Everything in `src/core` is built from the class library
  of the .NET Framework. Do not add packages or binaries.
- **Licence header.** Every source and script file starts with

  ```text
  SPDX-License-Identifier: AGPL-3.0-or-later
  Copyright (c) 2026 Robert Binsack
  ```

  in the comment syntax of the file; Markdown files start with the first line as an HTML
  comment. `scripts\check-spdx.ps1` checks it.
- **Texts.** Plain, short sentences. English is the reference for program texts and
  documentation. File formats, identifiers, tool names and error messages of the engine stay
  English in every language.
- **No secrets, no real data.** No passwords, no recordings of real applications, no names of
  real customers in code, tests or examples. Test data is invented.

## Sign-off

Every commit carries a `Signed-off-by` line (`git commit -s`). With it you state that you may
contribute the change under the licence of the project. That is the Developer Certificate of
Origin; there is no contributor licence agreement and nobody gets special rights to your
contribution. The text and the how-to are in [DCO.md](DCO.md).

## How a feature becomes "available"

The README and the roadmap list a feature as available only when a test proves it:

1. Write the feature and at least one test that fails without it.
2. Add or change its entry in [docs/status.json](docs/status.json): `status` `available`, the
   test names under `tests` (`Class.Method` for the test program, `<file>: <test name>` for
   Node tests and the tests of the package), `since` and `date`.
3. Run `scripts\update-status.ps1`. It writes the status tables into the README and ROADMAP
   files of every language and fails when a named test does not exist.
4. Describe it in `docs/` in English and in every language that is complete (see below).

Work that is not finished is listed as `in development`, ideas as `planned`, both without tests.

## Translations

The documentation is in English (`docs/`) and German (`docs/de/`), with the same file names.
A change of an English page needs the same change in the German page; code blocks stay
identical. `scripts\check-docs.ps1` compares the structure. Program texts are in
`src/i18n/<language>.json`. How to add a language: [docs/translating.md](docs/translating.md).

If you do not speak German, say so in the pull request and mark the German text you could not
write; somebody else completes it before the merge.

## Plugins

Event sources, web templates, clone renderers and video layouts are plugins: a folder with
`plugin.json`, described in [docs/plugins.md](docs/plugins.md). Examples that belong to the
project go into `plugins/examples/<type>-<name>` and get a test in `tests/PipelineTests.cs`
(`PluginTests`). A plugin for one product or one site is better kept in its own repository.

## Pull requests

- One topic per pull request, with a description of what changes and why.
- Tests pass, the checks above pass, commits are signed off.
- Questions belong into the Discussions of the repository, bugs and proposals into issues.
- Security problems: see [SECURITY.md](SECURITY.md). Behaviour: [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).
