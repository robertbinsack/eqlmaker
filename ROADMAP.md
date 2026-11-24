<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](ROADMAP.md) · [Deutsch](ROADMAP.de.md)

# Roadmap

This page says what EQLMaker can do today, what is being worked on and what is planned. The
three lists below are written from [docs/status.json](docs/status.json) by
`scripts/update-status.ps1`; nothing is listed as available without a test of this repository
that proves it. The roadmap is a plan, not a commitment.

<!-- status:begin release -->
> [!NOTE]
> **Status:** The source code of version 0.1.0 is published in this repository and is tested; the table under [Features](README.en.md#features) names the tests. There is no release package yet: the first release is in preparation. Until then, build from source, see [Quick start](README.en.md#quick-start).
<!-- status:end -->

## Available and tested (v0.1)

<!-- status:begin summary -->
17 of 18 features of version 0.1 are available and tested, 1 in development. The test column names the tests of the repository that prove each feature.
<!-- status:end -->

<!-- status:begin available -->
| Feature | Description | Since | Test |
|---|---|---|---|
| Record usage | Windows programs and browser applications: screen video, and every user action with time, window and control when EQLMaker operates the program (scenario file or MCP client); video and database events also while a person works by hand | 0.1.0 | `PipelineTests.SampleEndToEndOnPlainPostgres`<br>`RecordCliTests.StartAndStopWhileSomeoneElseWorks` |
| Measured database changes | Statements, parameters, transactions and changed rows using PostgreSQL's built-in means – no server-wide changes, no restart | 0.1.0 | `PostgresSourceTests.CapturesStatementsParametersErrorsAndChangedRows`<br>`PostgresSourceTests.RoleCaptureAndResetOfAnAbortedCapture`<br>`LogParserTests.DefaultPrefixStatementsParametersErrors`<br>`TestDecodingTests.InsertUpdateDeleteWithQuotingAndTypes` |
| Open event format | Database accesses as JSON Lines, documented, version 0.1 | 0.1.0 | `EventFormatTests.FileRoundTripWithAllFields`<br>`EventFormatTests.NewerMajorVersionIsRefused`<br>`EventFormatTests.SortIsStable` |
| Read the manual | PDF, HTML, DOCX, text or Markdown; chapters are linked to screens | 0.1.0 | `ManualTests.PdfCompressedWithCharacterMapAndObjectStream`<br>`ManualTests.PdfPrintedByABrowser`<br>`ManualTests.Docx`<br>`ManualTests.Html`<br>`ManualTests.MarkdownAndTheSampleHandbook`<br>`ManualTests.PlainText`<br>`ManualTests.RunWritesIntoTheProjectAndSpecLinksIt` |
| Timestamp overlay | Small program showing date and time with milliseconds on top of all windows | 0.1.0 | `TimecodeTests.OverlayProgramRendersTheSameStrip`<br>`TimecodeTests.ScaledAndCompressedFramesAreRead`<br>`TimecodeTests.DamagedPatternFallsBackToTheDigits` |
| Add video with audio later | The timestamp is read from the image and matched with the database changes | 0.1.0 | `VideoImportTests.ImportAt15FramesPerSecond`<br>`VideoImportTests.ImportAt30FramesPerSecond`<br>`VideoImportTests.ImportAt60FramesPerSecond`<br>`ClockMapTests.CutsAreFoundAndMappedOnBothSides`<br>`ClockMapTests.OffsetOfAWrongClockIsEstimated` |
| Transcript with Whisper | Speech recognition via whisper.cpp, fully offline. The recogniser is part of the release package; from source, name your own `whisper-cli.exe` in the configuration. The speech test runs where a recogniser is present | 0.1.0 | `SpeechTests.SpokenSentenceIsTranscribed`<br>`VideoImportTests.TranscriptFormatsAreRead` |
| Timeline and specification | Click, screen, statement and transaction on one timeline; screens, queries and tables derived from it by fixed rules | 0.1.0 | `TimelineTests.EventsGoToTheActionBeforeThem`<br>`TimelineTests.TransactionIdsGiveExactTies`<br>`TimelineTests.TransactionsAreTiedByTimeAndTableWithoutIds`<br>`TimelineTests.SameInputGivesSameOutput`<br>`SpecTests.StepsMasksQueriesTablesAndManualLinks` |
| Rebuild as web app | Guided step with web template and instructions for a connected AI assistant | 0.1.0 | `GuidedTests.GenerateIsAWorkspaceAndInstructionsNotAnApplication`<br>`src/templates/web/test/template.test.js: server: API and files through HTTP with a stand-in pool` |
| Read-only archive clone | Guided step with reference images per screen | 0.1.0 | `GuidedTests.CloneScaffoldShowsTheReferenceAndGreysOutWritingControls` |
| Take over findings from the video | Guided step: every finding is decided as adopted, rejected or open, with a report | 0.1.0 | `GuidedTests.EnrichRecordsDecisionsAndReportsEveryFinding` |
| Comparison tests | Visual comparison and comparison of measured database changes between two recordings | 0.1.0 | `VerifyScreensTests.EqualDifferentToleranceIgnoreAndSize`<br>`VerifyScreensTests.FoldersArePairedByName`<br>`VerifyDbTests.SameChangesWithOtherGeneratedKeysAreEqual`<br>`VerifyDbTests.DeliberateDifferencesAreFound`<br>`VerifyDbTests.MissingStepAndEmptyRecordingsAreNotEqual`<br>`VerifyDbTests.AWrongReferenceIsNotHiddenByTheKeyRule` |
| Comparison video with player | Original and rebuild on one timeline | 0.1.0 | `PluginTests.VideoLayoutPluginIsUsedByVideo`<br>`PipelineTests.SampleEndToEndOnPlainPostgres` |
| MCP server and CLI | The same steps as tools for any MCP client and as commands | 0.1.0 | `McpTests.ProtocolToolsAndPrompts`<br>`PipelineTests.CommandLineReportsUnknownCommandsAndMissingArguments` |
| Plugin interfaces | For event sources, web templates, clone renderers and video layouts, documented | 0.1.0 | `PluginTests.ExamplesAreValidAndOfEveryType`<br>`PluginTests.SourcePluginDeliversEventsThroughTheSameInterface`<br>`PluginTests.WebTemplatePluginIsUsedByGenerate`<br>`PluginTests.CloneRendererPluginIsUsedByClone`<br>`PluginTests.VideoLayoutPluginIsUsedByVideo`<br>`PluginTests.BrokenManifestsAreRefusedWithTheReason` |
| Sample application with end-to-end run | An own sample application as Windows program and in the browser; `eqlmaker sample` runs the whole pipeline on plain PostgreSQL and finds the deliberate defects | 0.1.0 | `PipelineTests.SampleEndToEndOnPlainPostgres`<br>`examples/sample-app/web/test/http.test.js: saving an invoice writes invoice, lines, stock movements, stock and counter`<br>`examples/sample-app/web/test/calc.test.js: the deliberate defect changes at least one rounding vector, and only the tax` |
| German and English | Programs, documentation and web template in both languages | 0.1.0 | `I18nTests.TranslationsHaveTheSameKeysAndPlaceholders`<br>`I18nTests.GermanIsUsedWhenAskedFor`<br>`I18nTests.EnglishFileIsTheExportOfTheCode`<br>`src/templates/web/test/template.test.js: languages: English and German resources have the same keys` |
<!-- status:end -->

The tests named `Class.Method` belong to `bin\eqlmaker-tests.exe` (sources in `tests/`); the
others are Node tests, named by file and test. How to run them: [CONTRIBUTING.md](CONTRIBUTING.md).

## In development

<!-- status:begin in-development -->
| Feature | Description |
|---|---|
| Setup without admin rights | One batch file sets everything up in its own folder: no installer, no registry, no service. The setup script is in the repository; the release package it belongs to is in preparation |
<!-- status:end -->

Next steps of version 0.1:

- First release as a Windows package (`eqlmaker-windows-x64.zip` with checksum), built by the
  release workflow from a version tag.
- A short demo animation for the README.

## Planned

<!-- status:begin planned -->
- **From June 2027 · planned**
  - [ ] Recognize screen elements and link them to data changes
  - [ ] Live dialogue via text, later via voice
  - [ ] Detect and replace personal data
  - [ ] Derive rules from answers and check them against existing data
  - [ ] Illustrated manual, German and English
  - [ ] Tests with users, case study on a free application
- **Later · no date**
  - [ ] Event format as a standalone specification 1.0 for other tools
  - [ ] More open sources: MySQL/MariaDB and SQL Server
  - [ ] Accessibility of generated web apps (WCAG 2.1 AA)
  - [ ] Template gallery for contributors
<!-- status:end -->

The items "from June 2027" are one clearly scoped new module. So far there is only the concept,
no code. Funding is being applied for; there is no commitment yet.

The items "later" have no date. Among them: sources for MySQL/MariaDB and SQL Server need a way
to measure changed rows on those servers; until then, applications on other database servers can
be reached by replacing PostgreSQL with EQLServer, a commercial product by the same author that
is not part of EQLMaker (see the README, "Other databases").
