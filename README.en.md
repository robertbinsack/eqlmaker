<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
<p align="right"><a href="README.md">Deutsch</a> · <b>English</b></p>

<h1 align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/media/wordmark-dark.svg">
    <img alt="EQLMaker" src="docs/media/wordmark-light.svg" width="420">
  </picture>
</h1>

<p align="center"><b>Understand legacy software and rebuild it with AI as a modern web application. Without source code.</b></p>

<p align="center">
  <img alt="Status: in development" src="https://img.shields.io/badge/Status-in%20development-orange">
  <img alt="License: AGPL-3.0" src="https://img.shields.io/badge/License-AGPL--3.0-blue">
  <img alt="Platform: Windows" src="https://img.shields.io/badge/Platform-Windows-0078D6">
  <img alt="PostgreSQL 15+" src="https://img.shields.io/badge/PostgreSQL-15%2B-336791">
  <img alt="MCP server" src="https://img.shields.io/badge/MCP-Server-6E56CF">
  <a href="https://github.com/robertbinsack/eqlmaker/actions/workflows/ci.yml"><img alt="CI" src="https://github.com/robertbinsack/eqlmaker/actions/workflows/ci.yml/badge.svg"></a>
  <a href="https://github.com/robertbinsack/eqlmaker/releases"><img alt="Release" src="https://img.shields.io/github/v/release/robertbinsack/eqlmaker?label=Release"></a>
</p>

<!-- docs/media/demo.gif : demo animation follows with the first release (made by scripts/demo/make-demo.ps1) -->

**EQLMaker is the open tool for replacing legacy applications with the help of AI.** It **watches** the old program being used, **measures** what it does in the database on the same time line, **understands** it from the manual, from a narrated video and from the files of the application, **prepares the rebuild** for an AI assistant and **proves** it: comparison tests show whether the rebuild shows the same and changes the same data as the original.

The rebuild steps are guided, not fully automatic: an AI assistant builds, EQLMaker prepares its work and checks the result.

<!-- status:begin release -->
> [!NOTE]
> **Status:** Version 0.2.0 was released on 2026-11-25. The Windows package is on the [release page](https://github.com/robertbinsack/eqlmaker/releases/latest); the table under [Features](README.en.md#features) names the tests behind it. See [Quick start](README.en.md#quick-start).
<!-- status:end -->

---

## The problem

Many businesses, associations and public administrations rely on old software whose vendor no longer exists and whose source code is missing. It no longer receives security updates and becomes a risk. Replacing it usually fails not because of technology, but because **nobody knows exactly everything the program does anymore**.

## What EQLMaker does

EQLMaker captures the knowledge embedded in a running application without needing its source code:

- Someone uses the old program as usual. EQLMaker **records** what happens on screen and at the same time **measures** which data the application reads and changes in the database.
- **The AI can also operate the application itself.** Depending on the prompt, the AI operates the application on its own in order to catalogue the interface and the operation completely. Users can add to the catalogue afterwards.
- The **manual** is added, plus – optionally – a **video with spoken explanation**.
- Whoever has the **files of the application** can have them read: the built-in reader for PowerBuilder libraries lists menus, windows, labels and every SQL statement, also of functions that nobody recorded. Readers for other kinds of files plug in the same way.
- Everything lands on a shared **timeline**: this click, this screen, this database change, this sentence in the manual.
- The timeline produces a **specification**. With it, a connected AI assistant rebuilds the application as a modern web app. EQLMaker provides template and instructions and then verifies with **comparison tests** whether the rebuild shows the same and changes the same data as the original.

## How it works

~~~mermaid
flowchart LR
    subgraph IN["1 · Input"]
        A["Record<br/>usage"]
        B["Measure<br/>database changes"]
        C["Read<br/>manual"]
        D["Video with<br/>timestamp & audio"]
    end
    subgraph MAKE["2 · What EQLMaker builds"]
        E["Timeline"]
        F["Specification"]
        G["Rebuild<br/>(migration / archive)"]
    end
    subgraph PROOF["3 · The proof"]
        H["Comparison tests"]
        I["Comparison video"]
    end
    A --> E
    B --> E
    C --> E
    D --> E
    E --> F --> G --> H --> I
~~~

| Stage | Step | What happens | Command |
|---|---|---|---|
| 1 | Observe usage | Screen video and every user action with time, window and control – for Windows programs and browser applications | `record` |
| 1 | Measure data changes | Statements, parameters, transactions and changed rows, using PostgreSQL's built-in means. Measured, not guessed. | `record` |
| 1 | Read the manual | PDF, HTML, DOCX, text or Markdown; chapters are linked to screens | `manual` |
| 1 | Video with timestamp and audio | An overlay shows date and time with milliseconds on top of all windows; record with any screen recorder; offline transcript via Whisper | `video-import` |
| 2 | Timeline | The on-screen timestamp is matched with the times of the database changes | `correlate` |
| 2 | Specification | Screens, queries, tables read and written, manual references, hints from the video | `spec` |
| 2 | Rebuild | Modern web app (migration) or read-only clone (archive) – guided steps for the AI assistant | `generate` · `clone` · `enrich` |
| 3 | Comparison tests | Visual comparison of screens and comparison of measured database changes; the report shows deviations | `verify` |
| 3 | Comparison video | Original and rebuild side by side on one timeline, with player | `video` |

All steps are also available as tools of an **MCP server**.

## Two modes

| | Mode 1 · Migration | Mode 2 · Archive |
|---|---|---|
| **Result** | New UI that reads and writes | 1:1 clone in the browser, read-only |
| **Basis** | Modern web app from a template, on the same tables as the original | The original's screens; save, post and create stay visible but greyed out |
| **Legacy app** | Can keep running in parallel | Can be switched off |
| **For whom** | Anyone who wants to keep using and evolving their software | Anyone who must retire a legacy system but still needs access to its data |

## Examples: legacy application and rebuild side by side

> [!IMPORTANT]
> These proofs of concept were created with the same method on EQLServer, each by an AI agent, **before EQLMaker became a program of its own**. EQLMaker v0.1 did not produce them. They show what the method can achieve.

| Application | Technology | AI build time, new UI | Coverage |
|---|---|---|---|
| Property management | PowerBuilder 6.5 · SQL Anywhere | 24.5 min | 70 / 70 menu functions (100 %) |
| Inventory management | Delphi · Advantage | 16.9 min + 5 h 17 min extension | 398 / 494 menu functions (81 %) |
| Informix sample application | ASP.NET · Informix | 7.7 min | 8 / 8 pages and actions (100 %) |
| InvoicePlane (open source) | PHP · MySQL/MariaDB | 41 min | 81 / 82 GUI functions (99 %) |
| TurboCASH (open source) | Delphi · Firebird | 42 min | 228 / 266 menu functions (86 %) |
| Northwind (Access template) | Microsoft Access · ODBC | 45 min | 58 / 58 GUI functions (100 %) |
| Contoso University (Microsoft sample) | ASP.NET Core · SQL Server | 14 min | 32 / 32 GUI functions (100 %) |
| Dolibarr ERP/CRM (open source) | PHP · PostgreSQL | 3 h 09 min | 81 / 93 GUI functions (87 %) |

▶️ **Videos (original and rebuild side by side):** [eqlmaker.org](https://eqlmaker.org/) – the demo videos were operated by the AI on its own; the apps came out of them.

<sub>The applications come from third-party vendors or are open source. Product and vendor names of third-party programs are anonymized.</sub>

## Features

### Version 0.2

<!-- status:begin summary -->
21 of 21 features of version 0.2 are available and tested, 0 in development. The test column names the tests of the repository that prove each feature.
<!-- status:end -->

<!-- status:begin features -->
| Feature | Description | Status | Test |
|---|---|---|---|
| Record usage | Windows programs and browser applications: screen video, and every user action with time, window and control when EQLMaker operates the program (scenario file or MCP client); video and database events also while a person works by hand | available · tested | `PipelineTests.SampleEndToEndOnPlainPostgres`<br>`RecordCliTests.StartAndStopWhileSomeoneElseWorks`<br>`RecordCliTests.StartTakesLoginsAndDatabaseFromTheCommandLine` |
| Measured database changes | Statements, parameters, transactions and changed rows using PostgreSQL's built-in means – no server-wide changes, no restart; changed rows also from servers without the module test_decoding (through pgoutput) | available · tested | `PostgresSourceTests.CapturesStatementsParametersErrorsAndChangedRows`<br>`PostgresSourceTests.RoleCaptureAndResetOfAnAbortedCapture`<br>`LogParserTests.DefaultPrefixStatementsParametersErrors`<br>`TestDecodingTests.InsertUpdateDeleteWithQuotingAndTypes`<br>`PgOutputTests.MessagesAreReadAsTransactionsWithRows`<br>`PgOutputTests.CapturesChangedRowsThroughPgoutputAndCleansUp` |
| Open event format | Database accesses as JSON Lines, documented, version 0.1 | available · tested | `EventFormatTests.FileRoundTripWithAllFields`<br>`EventFormatTests.NewerMajorVersionIsRefused`<br>`EventFormatTests.SortIsStable` |
| Read the manual | PDF, HTML, DOCX, text or Markdown; chapters are linked to screens | available · tested | `ManualTests.PdfCompressedWithCharacterMapAndObjectStream`<br>`ManualTests.PdfPrintedByABrowser`<br>`ManualTests.Docx`<br>`ManualTests.Html`<br>`ManualTests.MarkdownAndTheSampleHandbook`<br>`ManualTests.PlainText`<br>`ManualTests.RunWritesIntoTheProjectAndSpecLinksIt` |
| Timestamp overlay | Small program showing date and time with milliseconds on top of all windows | available · tested | `TimecodeTests.OverlayProgramRendersTheSameStrip`<br>`TimecodeTests.ScaledAndCompressedFramesAreRead`<br>`TimecodeTests.DamagedPatternFallsBackToTheDigits` |
| Add video with audio later | The timestamp is read from the image and matched with the database changes | available · tested | `VideoImportTests.ImportAt15FramesPerSecond`<br>`VideoImportTests.ImportAt30FramesPerSecond`<br>`VideoImportTests.ImportAt60FramesPerSecond`<br>`ClockMapTests.CutsAreFoundAndMappedOnBothSides`<br>`ClockMapTests.OffsetOfAWrongClockIsEstimated` |
| Transcript with Whisper | Speech recognition via whisper.cpp, fully offline. The recogniser and a model are part of the Windows package; from source, name your own `whisper-cli.exe` in the configuration. The speech test runs where a recogniser is present; the test of the package transcribes an English and a German sample | available · tested | `SpeechTests.SpokenSentenceIsTranscribed`<br>`VideoImportTests.TranscriptFormatsAreRead` |
| Timeline and specification | Click, screen, statement and transaction on one timeline; screens, queries and tables derived from it by fixed rules | available · tested | `TimelineTests.EventsGoToTheActionBeforeThem`<br>`TimelineTests.TransactionIdsGiveExactTies`<br>`TimelineTests.TransactionsAreTiedByTimeAndTableWithoutIds`<br>`TimelineTests.SameInputGivesSameOutput`<br>`SpecTests.StepsMasksQueriesTablesAndManualLinks` |
| Rebuild as web app | Guided step with web template and instructions for a connected AI assistant | available · tested | `GuidedTests.GenerateIsAWorkspaceAndInstructionsNotAnApplication`<br>`src/templates/web/test/template.test.js: server: API and files through HTTP with a stand-in pool` |
| Read-only archive clone | Guided step with reference images per screen | available · tested | `GuidedTests.CloneScaffoldShowsTheReferenceAndGreysOutWritingControls` |
| Take over findings from the video | Guided step: every finding is decided as adopted, rejected or open, with a report | available · tested | `GuidedTests.EnrichRecordsDecisionsAndReportsEveryFinding` |
| Comparison tests | Visual comparison and comparison of measured database changes between two recordings | available · tested | `VerifyScreensTests.EqualDifferentToleranceIgnoreAndSize`<br>`VerifyScreensTests.FoldersArePairedByName`<br>`VerifyDbTests.SameChangesWithOtherGeneratedKeysAreEqual`<br>`VerifyDbTests.DeliberateDifferencesAreFound`<br>`VerifyDbTests.MissingStepAndEmptyRecordingsAreNotEqual`<br>`VerifyDbTests.AWrongReferenceIsNotHiddenByTheKeyRule` |
| Comparison video with player | Original and rebuild on one timeline | available · tested | `PluginTests.VideoLayoutPluginIsUsedByVideo`<br>`PipelineTests.SampleEndToEndOnPlainPostgres` |
| MCP server and CLI | The same steps as tools for any MCP client and as commands | available · tested | `McpTests.ProtocolToolsAndPrompts`<br>`PipelineTests.CommandLineReportsUnknownCommandsAndMissingArguments` |
| Plugin interfaces | For event sources, readers, web templates, clone renderers and video layouts, documented | available · tested | `PluginTests.ExamplesAreValidAndOfEveryType`<br>`PluginTests.SourcePluginDeliversEventsThroughTheSameInterface`<br>`PluginTests.WebTemplatePluginIsUsedByGenerate`<br>`PluginTests.CloneRendererPluginIsUsedByClone`<br>`PluginTests.VideoLayoutPluginIsUsedByVideo`<br>`PluginTests.BrokenManifestsAreRefusedWithTheReason`<br>`ReaderTests.ReaderPluginWritesTheSameFormatAndIsFoundByFileExtension`<br>`ReaderTests.ReaderPluginNeedsACommandAndAValidResult` |
| Sample application with end-to-end run | An own sample application as Windows program and in the browser; `eqlmaker sample` runs the whole pipeline on plain PostgreSQL and finds the deliberate defects | available · tested | `PipelineTests.SampleEndToEndOnPlainPostgres`<br>`examples/sample-app/web/test/http.test.js: saving an invoice writes invoice, lines, stock movements, stock and counter`<br>`examples/sample-app/web/test/calc.test.js: the deliberate defect changes at least one rounding vector, and only the tax` |
| German and English | Programs, documentation and web template in both languages | available · tested | `I18nTests.TranslationsHaveTheSameKeysAndPlaceholders`<br>`I18nTests.GermanIsUsedWhenAskedFor`<br>`I18nTests.EnglishFileIsTheExportOfTheCode`<br>`src/templates/web/test/template.test.js: languages: English and German resources have the same keys` |
| Setup without admin rights | One batch file sets everything up in its own folder: no installer, no registry, no service. Tested with the released package as a user without admin rights on Windows Server 2025; not tested on a clean Windows 10 or 11. The tests of the package run locally before a release, not in the automatic checks of the repository | available · tested | `scripts/setup/tests/test-setup.ps1: the setup process is not elevated (restricted token, evidence taken inside the run)`<br>`scripts/setup/tests/test-setup.ps1: eqlmaker-setup.cmd -unattended as ordinary user: downloads, default PostgreSQL without psql, self test with speech and logical decoding`<br>`scripts/setup/tests/test-setup.ps1: registry autostart and uninstall keys, PATH, environment, services, firewall rules, scheduled tasks: nothing added, removed or changed by the package`<br>`scripts/setup/tests/test-setup.ps1: remove: after eqlmaker-db-stop.cmd the folders can be deleted completely by the ordinary user` |
| Read PowerBuilder libraries | Built-in reader for the compiled libraries (.pbd, .pbl) of PowerBuilder 5 to 9, without source code: menus, windows, labels, SQL statements and references. The result goes into the specification and shows which tables no recording touched | available · tested | `ReaderTests.PowerBuilderReaderReadsObjectsSqlLabelsMenusAndReferences`<br>`ReaderTests.PowerBuilderReaderRefusesUnicodeLibrariesAndOtherFiles`<br>`ReaderTests.FirstLibraryWinsForAnObjectNameThatOccursTwice`<br>`ReaderTests.ReadCommandPicksTheReaderByFileExtensionAndSpecTakesTheResultOver` |
| Statement table and old scenario files | The statements of a recording as the table sql.tsv (the format that tools for replaying statements read; T-SQL batches and calls as tsql-batch and tsql-rpc); scenario files of the earlier recorder are converted; a scenario can name its own logins and databases to capture | available · tested | `ExportTests.SqlTsvHasTheColumnsKindsAndEscapesOfTheReplayFormat`<br>`ExportTests.ExportSqlCommandWritesTheTableOfAnExistingRecording`<br>`ExportTests.ScenarioOfTheEarlierRecorderIsConverted`<br>`ExportTests.ScenarioCaptureObjectReplacesRolesAndDatabasesOfTheConfiguration` |
| Applications on other databases | Applications that speak SQL Server (TDS) are captured through EQLServer, a separate, optional product that stores in PostgreSQL: batches, calls and changed rows on one time line. Tested with an optional end-to-end test against a running EQLServer 1.10.0; the other protocols are not tested. EQLMaker does not need EQLServer | available · tested | `ServerTests.TdsStatementsAndChangedRowsOfAnApplicationReachTheTimeline`<br>`tests/eqlserver/run-eqlserver-e2e.ps1: changed rows: insert, update and delete of the customer table`<br>`TimelineTests.TransactionsAreTiedAcrossTheNamesOfAServerThatKeepsSeveralDatabasesInOne` |
<!-- status:end -->

"Available" means: built, and proven by the named tests of this repository. How a feature gets there is described in [CONTRIBUTING.md](CONTRIBUTING.md); the single source of this table is [docs/status.json](docs/status.json).


### Planned · project from June 2027

A clearly scoped new module. So far there is only the concept, no code. Funding is being applied for; there is no commitment yet.

- **Live questioning chatbot** – asks at the right moment during use: What are you doing? What is this field for? What must never be entered here?
- **Screen element recognition** – windows, fields and buttons get stable identifiers.
- **Rules checked against real data** – answers become readable rules ("employee number: digits only") that are tested against existing data.
- **Automatically generated new manual** – from steps, answers and screenshots.
- **Masking of personal data** – before storage and before every request to a language model.

## The honest limit

EQLMaker sees what was operated during the recording and what happened in the database at the time. **Calculation logic that the old program never displays and that appears in no recording can be missed by a rebuild.** The comparison tests show deviations – but only for what was recorded.

That is why rarely used functions and error cases belong in the recording, and for migrations a human checks screen by screen. For the archive mode the limit hardly matters, because no write or posting logic is rebuilt.

The rebuild steps are **guided, not fully automatic**: EQLMaker does not generate the code itself; it prepares and verifies the work of an AI assistant.

More limits of version 0.2: it runs on Windows only. User actions are recorded when EQLMaker operates the program (scenario file or MCP client); while a person works by hand, the recording holds the video and the database events. Personal data in recordings is not masked. The full list is in the [FAQ](docs/faq.md).

## Quick start

### From source (works today)

~~~bat
:: 1 · build: uses the C# compiler that is part of Windows, no SDK
> git clone https://github.com/robertbinsack/eqlmaker.git
> cd eqlmaker
> build.cmd
:: 2 · run the sample: a small shop application is operated, recorded and compared, end to end
> cd examples\sample-app\web
> npm ci
> cd ..\..\..
> bin\eqlmaker init demo
:: enter the connection to your PostgreSQL in demo\eqlmaker.json, then:
> set EQLMAKER_PGPASSWORD=<password of the PostgreSQL superuser>
> bin\eqlmaker sample --project demo
:: 3 · your own application
> bin\eqlmaker init my-project
> cd my-project
:: enter connection and capture.databases in eqlmaker.json, then start the recording and after it the application
> ..\bin\eqlmaker record --start --name original
> ..\bin\eqlmaker record --stop
> ..\bin\eqlmaker correlate original
> ..\bin\eqlmaker spec original
:: prepare the rebuild: migration ...
> ..\bin\eqlmaker generate
:: ... or archive clone
> ..\bin\eqlmaker clone
:: record the same steps in the rebuild as "web", then compare
> ..\bin\eqlmaker verify db original web
~~~

**Requirements from source:** Windows, a PostgreSQL 15 or later with `wal_level = logical` and a superuser connection, Node.js 18 or later and FFmpeg on the `PATH`. Nothing is installed; the build result is the folder `bin`.

### The Windows package

> [!NOTE]
> Download `eqlmaker-windows-x64.zip` from the [latest release](https://github.com/robertbinsack/eqlmaker/releases/latest). Tested before the release on Windows Server 2025 as a user without admin rights: fresh folder with a blank and an umlaut in its name, setup with all downloads, self test with speech recognition, removal. Not tested yet: a clean Windows 10 or Windows 11, a download through a proxy, and building the package with PowerShell 7.

~~~bat
:: 1 · unpack the zip, then: sets everything up in its own folder, no admin rights
> eqlmaker-setup.cmd
:: 2 · run the sample
> eqlmaker sample --project project
:: 3 · your own application: as above, with "eqlmaker" instead of "..\bin\eqlmaker"
> eqlmaker init my-project
~~~

**Requirements of the package:** Windows 10 or later (x64). No installer, no admin rights, no system changes – uninstalling means deleting the folder. The setup brings FFmpeg, Node.js, the speech recognition and, if wanted, a portable PostgreSQL; details in [docs/setup.md](docs/setup.md).

## Documentation

| | |
|---|---|
| [Quick start](docs/quick-start.md) | Both routes step by step, first own project |
| [Pipeline](docs/pipeline.md) | Every step with inputs, outputs and the fixed rules |
| [Command reference](docs/cli.md) · [MCP server](docs/mcp.md) | Every command, option and tool |
| [Guided steps](docs/guided-steps.md) · [Comparison tests](docs/verify.md) | What EQLMaker prepares, what the assistant or person does, how it is checked |
| [Sample application](docs/sample-app.md) | The sample, its hidden rules and what the numbers mean |
| [Applications on other databases](docs/other-databases.md) · [PowerBuilder libraries](docs/powerbuilder.md) | The principle and the steps for SQL Server, MySQL and others; reading the libraries of a PowerBuilder application |
| [All pages](docs/index.md) | Map of the documentation, in English and German |

## Who it is for – and what society gains

Old line-of-business software is a silent risk. Knowledge about it lives in the heads of a few people and retires with them. This hits hardest where there is no money for consulting projects.

- **People who use old programs every day** – in small municipalities, associations, medical practices and craft businesses. They are the real experts and should be able to pass on their knowledge without workshops and without programming skills.
- **Developers replacing legacy software** – instead of oral tradition they get a verifiable foundation: recording, measured database changes, specification and comparison tests.
- **Organizations that must retire a legacy system** – a read-only clone keeps the data accessible even after the old program stops running.

**Public benefit:** The method is described openly and published as free software, so it helps not only those who can afford an expensive replacement. Software does not solve the problem alone: it can observe, compare and reveal contradictions; the decision stays with people. Because screens and recordings may contain personal data, speech recognition runs offline on the user's own machine.

## Roadmap

- **Now · in development**
  - [x] Finish and test version 0.1
  - [x] End-to-end run with an own sample application on plain PostgreSQL
  - [x] Publish source code
  - [x] First release as a Windows package
  - [x] Release 0.2 as a Windows package
  - [x] Reader plugin point with the PowerBuilder reader (0.2)
  - [x] Applications on SQL Server (TDS) through a server that stores in PostgreSQL, tested with EQLServer 1.10.0 (0.2)
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
  - [ ] Readers for more kinds of application files (for example the forms inside a Delphi program)
  - [ ] Accessibility of generated web apps (WCAG 2.1 AA)
  - [ ] Template gallery for contributors

<sub>The roadmap is a plan, not a commitment. Details and the tested state: [ROADMAP.md](ROADMAP.md).</sub>

## Related work

EQLMaker does not replace any of these approaches – it connects what they keep separate.

| Approach | What it does | What is missing |
|---|---|---|
| RPA tools | Keep driving the old UI | Do not replace it |
| Application reconstruction | Analyze program files, find structure and code | Not the intent behind a user action |
| Low-code migration | Move to new platforms | Usually require source code or a specific source product |
| Click documentation | Generate illustrated steps from clicks | Do not know the database |
| Website cloning tools | Copy a page's look | Not an application's behavior |

**What EQLMaker does differently:** it brings user interaction and measured database changes together on one timeline and proves the rebuild with comparison tests.

## Other databases

EQLMaker runs entirely on a standard **PostgreSQL** and thus reaches applications that speak PostgreSQL. Applications that speak SQL Server, MySQL/MariaDB, Oracle, SQLite, Firebird/InterBase, SAP SQL Anywhere, Advantage, Informix, Access or SAP/Sybase ASE need a server that accepts them unchanged and stores in PostgreSQL, so that EQLMaker sees their transactions. [EQLServer](https://eqlserver.com) is such a server: a commercial product by the same author, optional and **not part of EQLMaker**. EQLMaker does not need it. What is tested so far is SQL Server (TDS) with EQLServer 1.10.0; the steps, a sample configuration and what is not tested are in [Applications on other databases](docs/other-databases.md). Without any database access EQLMaker still works with screen, video and manual, but the comparison of the data is not possible then.

## First tasks for contributors

Each task below is one plugin or one document and needs no change of the core. Start from the example next to it; build with `build.cmd` and run `bin\eqlmaker-tests.exe`.

| Task | Where to start |
|---|---|
| A **reader** for the files of another kind of application (for example the forms inside a Delphi program, Access forms): menus, screens, labels, SQL | `plugins/examples/reader-sql-scripts`, [plugins](docs/plugins.md) |
| An **event source** for another database or its log (for example the MySQL general log, SQL Server Extended Events) | `plugins/examples/source-jsonl-file`, [event format](docs/event-format.md) |
| A **web template** in another framework | `plugins/examples/web-template-minimal` |
| A **clone renderer** or a **video layout** | `plugins/examples/clone-renderer-outline`, `plugins/examples/video-layout-stacked` |
| Another **language** for programs and documentation | [Translating](docs/translating.md) |
| Test the Windows package on a clean Windows 10 or 11 and report what you find | [ROADMAP.md](ROADMAP.md) |

**A plugin in five steps:** copy an example folder from `plugins/examples`; edit `plugin.json` (type, name, command); write the program, in any language: it reads one JSON line from standard input and writes JSON to standard output; list the folder under `"plugins"` in `eqlmaker.json` and try it with `eqlmaker read`, `eqlmaker record` or `eqlmaker generate`; send it as a pull request with a test, see [CONTRIBUTING.md](CONTRIBUTING.md).

## Contributing and license

- **License:** [AGPL-3.0](https://www.gnu.org/licenses/agpl-3.0.html) (GNU Affero General Public License, version 3 or later), full text in [LICENSE](LICENSE). Copyright Robert Binsack.
- **Contributions:** via the [Developer Certificate of Origin (DCO)](https://developercertificate.org/). Contributors confirm with the `Signed-off-by` line in the commit that the contribution may be made under the project's license. There are no special rights to contributions. How to build, test and contribute: [CONTRIBUTING.md](CONTRIBUTING.md) · [DCO.md](DCO.md) · [Code of conduct](CODE_OF_CONDUCT.md) · [Security](SECURITY.md) · [Third-party notices](THIRD_PARTY_NOTICES.md).
- **Contact:** office@eqlserver.de · [binsack.com](https://www.binsack.com)
