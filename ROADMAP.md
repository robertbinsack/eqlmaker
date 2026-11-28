<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](ROADMAP.md) · [Deutsch](ROADMAP.de.md)

# Roadmap

This page says what EQLMaker can do today, what is being worked on and what is planned. The
three lists below are written from [docs/status.json](docs/status.json) by
`scripts/update-status.ps1`; nothing is listed as available without a test of this repository
that proves it. The roadmap is a plan, not a commitment.

<!-- status:begin release -->
> [!NOTE]
> **Status:** Version 0.2.0 was released on 2026-11-25. The Windows package is on the [release page](https://github.com/robertbinsack/eqlmaker/releases/latest); the table under [Features](README.en.md#features) names the tests behind it. See [Quick start](README.en.md#quick-start).
<!-- status:end -->

## Available and tested (v0.2)

<!-- status:begin summary -->
23 of 23 features of version 0.2 are available and tested, 0 in development. The test column names the tests of the repository that prove each feature.
<!-- status:end -->

<!-- status:begin available -->
| Feature | Description | Since | Test |
|---|---|---|---|
| Record usage | Windows programs and browser applications: screen video, and every user action with time, window and control when EQLMaker operates the program (scenario file or MCP client); video and database events also while a person works by hand | 0.1.0 | `PipelineTests.SampleEndToEndOnPlainPostgres`<br>`RecordCliTests.StartAndStopWhileSomeoneElseWorks`<br>`RecordCliTests.StartTakesLoginsAndDatabaseFromTheCommandLine` |
| Measured database changes | Statements, parameters, transactions and changed rows using PostgreSQL's built-in means – no server-wide changes, no restart; changed rows also from servers without the module test_decoding (through pgoutput) | 0.1.0 | `PostgresSourceTests.CapturesStatementsParametersErrorsAndChangedRows`<br>`PostgresSourceTests.RoleCaptureAndResetOfAnAbortedCapture`<br>`LogParserTests.DefaultPrefixStatementsParametersErrors`<br>`TestDecodingTests.InsertUpdateDeleteWithQuotingAndTypes`<br>`PgOutputTests.MessagesAreReadAsTransactionsWithRows`<br>`PgOutputTests.CapturesChangedRowsThroughPgoutputAndCleansUp` |
| Open event format | Database accesses as JSON Lines, documented, version 0.1 | 0.1.0 | `EventFormatTests.FileRoundTripWithAllFields`<br>`EventFormatTests.NewerMajorVersionIsRefused`<br>`EventFormatTests.SortIsStable` |
| Read the manual | PDF, HTML, DOCX, text or Markdown; chapters are linked to screens | 0.1.0 | `ManualTests.PdfCompressedWithCharacterMapAndObjectStream`<br>`ManualTests.PdfPrintedByABrowser`<br>`ManualTests.Docx`<br>`ManualTests.Html`<br>`ManualTests.MarkdownAndTheSampleHandbook`<br>`ManualTests.PlainText`<br>`ManualTests.RunWritesIntoTheProjectAndSpecLinksIt` |
| Timestamp overlay | Small program showing date and time with milliseconds on top of all windows | 0.1.0 | `TimecodeTests.OverlayProgramRendersTheSameStrip`<br>`TimecodeTests.ScaledAndCompressedFramesAreRead`<br>`TimecodeTests.DamagedPatternFallsBackToTheDigits` |
| Add video with audio later | The timestamp is read from the image and matched with the database changes | 0.1.0 | `VideoImportTests.ImportAt15FramesPerSecond`<br>`VideoImportTests.ImportAt30FramesPerSecond`<br>`VideoImportTests.ImportAt60FramesPerSecond`<br>`ClockMapTests.CutsAreFoundAndMappedOnBothSides`<br>`ClockMapTests.OffsetOfAWrongClockIsEstimated` |
| Transcript with Whisper | Speech recognition via whisper.cpp, fully offline. The recogniser and a model are part of the Windows package; from source, name your own `whisper-cli.exe` in the configuration. The speech test runs where a recogniser is present; the test of the package transcribes an English and a German sample | 0.1.0 | `SpeechTests.SpokenSentenceIsTranscribed`<br>`VideoImportTests.TranscriptFormatsAreRead` |
| Timeline and specification | Click, screen, statement and transaction on one timeline; screens, queries and tables derived from it by fixed rules | 0.1.0 | `TimelineTests.EventsGoToTheActionBeforeThem`<br>`TimelineTests.TransactionIdsGiveExactTies`<br>`TimelineTests.TransactionsAreTiedByTimeAndTableWithoutIds`<br>`TimelineTests.SameInputGivesSameOutput`<br>`SpecTests.StepsMasksQueriesTablesAndManualLinks` |
| Rebuild as web app | Guided step with web template and instructions for a connected AI assistant | 0.1.0 | `GuidedTests.GenerateIsAWorkspaceAndInstructionsNotAnApplication`<br>`src/templates/web/test/template.test.js: server: API and files through HTTP with a stand-in pool` |
| Read-only archive clone | Guided step with reference images per screen | 0.1.0 | `GuidedTests.CloneScaffoldShowsTheReferenceAndGreysOutWritingControls` |
| Claude app for Claude Desktop | Guided step: own local MCP server and own database per application, page as installed app in Claude Desktop, preview and confirmation before every write, German and English | 0.3.0 | `GuidedTests.ClaudeAppIsOneServerPerAppWithoutPasswordAndPreviewBeforeWrite`<br>`src/templates/claude-app/mcp-server/test/template.test.mjs: database: preview writes nothing, the confirmed token writes once, deleting needs the destructive tool` |
| Take over findings from the video | Guided step: every finding is decided as adopted, rejected or open, with a report | 0.1.0 | `GuidedTests.EnrichRecordsDecisionsAndReportsEveryFinding` |
| Comparison tests | Visual comparison and comparison of measured database changes between two recordings | 0.1.0 | `VerifyScreensTests.EqualDifferentToleranceIgnoreAndSize`<br>`VerifyScreensTests.FoldersArePairedByName`<br>`VerifyDbTests.SameChangesWithOtherGeneratedKeysAreEqual`<br>`VerifyDbTests.DeliberateDifferencesAreFound`<br>`VerifyDbTests.MissingStepAndEmptyRecordingsAreNotEqual`<br>`VerifyDbTests.AWrongReferenceIsNotHiddenByTheKeyRule` |
| Comparison video with player | Original and rebuild on one timeline | 0.1.0 | `PluginTests.VideoLayoutPluginIsUsedByVideo`<br>`PipelineTests.SampleEndToEndOnPlainPostgres` |
| Voice-over for demo videos | A spoken commentary laid over a finished video, segment by segment along its caption timeline, synthesised by a local Fish Speech server with your own voice reference; clips are measured and fitted (slight speed-up at most), optional background music with adjustable volume is lowered under the speech; the picture stays unchanged | 0.3.0 | `NarrationTests.NarrateMixesSpeechAndMusicOverTheVideo`<br>`NarrationTests.NarrateRefusesSpeechThatDoesNotFit`<br>`NarrationTests.FitSpeedsUpSlightlyAndReportsWhatToCut`<br>`NarrationTests.DraftCoversTheWholeVideoAndSplitsLongCaptions` |
| MCP server and CLI | The same steps as tools for any MCP client and as commands | 0.1.0 | `McpTests.ProtocolToolsAndPrompts`<br>`PipelineTests.CommandLineReportsUnknownCommandsAndMissingArguments` |
| Plugin interfaces | For event sources, readers, web templates, clone renderers and video layouts, documented | 0.1.0 | `PluginTests.ExamplesAreValidAndOfEveryType`<br>`PluginTests.SourcePluginDeliversEventsThroughTheSameInterface`<br>`PluginTests.WebTemplatePluginIsUsedByGenerate`<br>`PluginTests.CloneRendererPluginIsUsedByClone`<br>`PluginTests.VideoLayoutPluginIsUsedByVideo`<br>`PluginTests.BrokenManifestsAreRefusedWithTheReason`<br>`ReaderTests.ReaderPluginWritesTheSameFormatAndIsFoundByFileExtension`<br>`ReaderTests.ReaderPluginNeedsACommandAndAValidResult` |
| Sample application with end-to-end run | An own sample application as Windows program and in the browser; `eqlmaker sample` runs the whole pipeline on plain PostgreSQL and finds the deliberate defects | 0.1.0 | `PipelineTests.SampleEndToEndOnPlainPostgres`<br>`examples/sample-app/web/test/http.test.js: saving an invoice writes invoice, lines, stock movements, stock and counter`<br>`examples/sample-app/web/test/calc.test.js: the deliberate defect changes at least one rounding vector, and only the tax` |
| German and English | Programs, documentation and web template in both languages | 0.1.0 | `I18nTests.TranslationsHaveTheSameKeysAndPlaceholders`<br>`I18nTests.GermanIsUsedWhenAskedFor`<br>`I18nTests.EnglishFileIsTheExportOfTheCode`<br>`src/templates/web/test/template.test.js: languages: English and German resources have the same keys` |
| Setup without admin rights | One batch file sets everything up in its own folder: no installer, no registry, no service. Tested with the released package as a user without admin rights on Windows Server 2025; not tested on a clean Windows 10 or 11. The tests of the package run locally before a release, not in the automatic checks of the repository | 0.1.0 | `scripts/setup/tests/test-setup.ps1: the setup process is not elevated (restricted token, evidence taken inside the run)`<br>`scripts/setup/tests/test-setup.ps1: eqlmaker-setup.cmd -unattended as ordinary user: downloads, default PostgreSQL without psql, self test with speech and logical decoding`<br>`scripts/setup/tests/test-setup.ps1: registry autostart and uninstall keys, PATH, environment, services, firewall rules, scheduled tasks: nothing added, removed or changed by the package`<br>`scripts/setup/tests/test-setup.ps1: remove: after eqlmaker-db-stop.cmd the folders can be deleted completely by the ordinary user` |
| Read PowerBuilder libraries | Built-in reader for the compiled libraries (.pbd, .pbl) of PowerBuilder 5 to 9, without source code: menus, windows, labels, SQL statements and references. The result goes into the specification and shows which tables no recording touched | 0.2.0 | `ReaderTests.PowerBuilderReaderReadsObjectsSqlLabelsMenusAndReferences`<br>`ReaderTests.PowerBuilderReaderRefusesUnicodeLibrariesAndOtherFiles`<br>`ReaderTests.FirstLibraryWinsForAnObjectNameThatOccursTwice`<br>`ReaderTests.ReadCommandPicksTheReaderByFileExtensionAndSpecTakesTheResultOver` |
| Statement table and old scenario files | The statements of a recording as the table sql.tsv (the format that tools for replaying statements read; T-SQL batches and calls as tsql-batch and tsql-rpc); scenario files of the earlier recorder are converted; a scenario can name its own logins and databases to capture | 0.2.0 | `ExportTests.SqlTsvHasTheColumnsKindsAndEscapesOfTheReplayFormat`<br>`ExportTests.ExportSqlCommandWritesTheTableOfAnExistingRecording`<br>`ExportTests.ScenarioOfTheEarlierRecorderIsConverted`<br>`ExportTests.ScenarioCaptureObjectReplacesRolesAndDatabasesOfTheConfiguration` |
| Applications on other databases | Applications that speak SQL Server (TDS) are captured through EQLServer, a separate, optional product that stores in PostgreSQL: batches, calls and changed rows on one time line. Tested with an optional end-to-end test against a running EQLServer 1.10.0; the other protocols are not tested. EQLMaker does not need EQLServer | 0.2.0 | `ServerTests.TdsStatementsAndChangedRowsOfAnApplicationReachTheTimeline`<br>`tests/eqlserver/run-eqlserver-e2e.ps1: changed rows: insert, update and delete of the customer table`<br>`TimelineTests.TransactionsAreTiedAcrossTheNamesOfAServerThatKeepsSeveralDatabasesInOne` |
<!-- status:end -->

The tests named `Class.Method` belong to `bin\eqlmaker-tests.exe` (sources in `tests/`); the
others are named by file and test. Those are the Node tests and, for the setup, the tests of the
Windows package in `scripts/setup/tests/test-setup.ps1`, which run locally against the built
package before a release. How to run them: [CONTRIBUTING.md](CONTRIBUTING.md).

## In development

<!-- status:begin in-development -->
Nothing of version 0.2 is in development at the moment: every feature of the list above is available.
<!-- status:end -->

Version 0.2.0 is released as a Windows package (`eqlmaker-windows-x64.zip` with checksum). The
package of this release was built and tested locally on Windows Server 2025, as was the update of
an installed 0.1.0 by copying over it. Version 0.2.0 adds the
reader plugin point with the PowerBuilder reader, the statement table `sql.tsv`, changed rows through
`pgoutput` and the test with EQLServer for applications on other databases. Next steps:

- Test the package on a clean Windows 10 and a clean Windows 11.
- Test the download through a proxy.
- Build the package with the release workflow (PowerShell 7); so far it was built with Windows
  PowerShell 5.1 only.
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
  - [ ] Readers for more kinds of application files
  - [ ] Accessibility of generated web apps (WCAG 2.1 AA)
  - [ ] Template gallery for contributors
<!-- status:end -->

The items "from June 2027" are one clearly scoped new module. So far there is only the concept,
no code. Funding is being applied for; there is no commitment yet.

The items "later" have no date. Among them: sources for MySQL/MariaDB and SQL Server need a way
to measure changed rows on those servers; until then, applications on other database servers can
be reached by replacing PostgreSQL with EQLServer, a commercial product by the same author that
is not part of EQLMaker (see [Applications on other databases](docs/other-databases.md)).
