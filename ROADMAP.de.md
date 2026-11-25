<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](ROADMAP.md) · [Deutsch](ROADMAP.de.md)

# Roadmap

Diese Seite sagt, was EQLMaker heute kann, woran gearbeitet wird und was geplant ist. Die drei
Listen unten schreibt `scripts/update-status.ps1` aus [docs/status.json](docs/status.json);
nichts steht als verfügbar da ohne einen Test dieses Repositorys, der es belegt. Die Roadmap ist
ein Plan, keine Zusage.

<!-- status:begin release -->
> [!NOTE]
> **Stand:** Version 0.2.0 wurde am 2026-11-25 veröffentlicht. Das Windows-Paket liegt auf der [Release-Seite](https://github.com/robertbinsack/eqlmaker/releases/latest); die Tabelle unter [Funktionen](README.md#funktionen) nennt die Tests dahinter. Siehe [Schnellstart](README.md#schnellstart).
<!-- status:end -->

## Verfügbar und getestet (v0.2)

<!-- status:begin summary -->
21 von 21 Funktionen der Version 0.2 sind verfügbar und getestet, 0 in Entwicklung. Die Spalte „Test“ nennt die Tests des Repositorys, die jede Funktion belegen.
<!-- status:end -->

<!-- status:begin available -->
| Funktion | Beschreibung | Seit | Test |
|---|---|---|---|
| Bedienung aufzeichnen | Windows-Programme und Browser-Anwendungen: Bildschirmvideo, dazu jede Bedienhandlung mit Zeit, Fenster und Steuerelement, wenn EQLMaker das Programm bedient (Szenariodatei oder MCP-Client); Video und Datenbankereignisse auch, während ein Mensch von Hand arbeitet | 0.1.0 | `PipelineTests.SampleEndToEndOnPlainPostgres`<br>`RecordCliTests.StartAndStopWhileSomeoneElseWorks`<br>`RecordCliTests.StartTakesLoginsAndDatabaseFromTheCommandLine` |
| Gemessene Datenbankänderungen | Anweisungen, Parameter, Transaktionen und geänderte Zeilen mit Bordmitteln von PostgreSQL – nichts wird serverweit umgestellt, kein Neustart; geänderte Zeilen auch von Servern ohne das Modul test_decoding (über pgoutput) | 0.1.0 | `PostgresSourceTests.CapturesStatementsParametersErrorsAndChangedRows`<br>`PostgresSourceTests.RoleCaptureAndResetOfAnAbortedCapture`<br>`LogParserTests.DefaultPrefixStatementsParametersErrors`<br>`TestDecodingTests.InsertUpdateDeleteWithQuotingAndTypes`<br>`PgOutputTests.MessagesAreReadAsTransactionsWithRows`<br>`PgOutputTests.CapturesChangedRowsThroughPgoutputAndCleansUp` |
| Offenes Ereignisformat | Datenbankzugriffe als JSON Lines, dokumentiert, Version 0.1 | 0.1.0 | `EventFormatTests.FileRoundTripWithAllFields`<br>`EventFormatTests.NewerMajorVersionIsRefused`<br>`EventFormatTests.SortIsStable` |
| Handbuch einlesen | PDF, HTML, DOCX, Text oder Markdown; Kapitel werden mit Masken verknüpft | 0.1.0 | `ManualTests.PdfCompressedWithCharacterMapAndObjectStream`<br>`ManualTests.PdfPrintedByABrowser`<br>`ManualTests.Docx`<br>`ManualTests.Html`<br>`ManualTests.MarkdownAndTheSampleHandbook`<br>`ManualTests.PlainText`<br>`ManualTests.RunWritesIntoTheProjectAndSpecLinksIt` |
| Zeitstempel-Leiste | Kleines Programm, das Datum und Uhrzeit mit Millisekunden über allen Fenstern zeigt | 0.1.0 | `TimecodeTests.OverlayProgramRendersTheSameStrip`<br>`TimecodeTests.ScaledAndCompressedFramesAreRead`<br>`TimecodeTests.DamagedPatternFallsBackToTheDigits` |
| Video mit Ton nachreichen | Zeitstempel wird aus dem Bild gelesen und mit den Datenbankänderungen abgeglichen | 0.1.0 | `VideoImportTests.ImportAt15FramesPerSecond`<br>`VideoImportTests.ImportAt30FramesPerSecond`<br>`VideoImportTests.ImportAt60FramesPerSecond`<br>`ClockMapTests.CutsAreFoundAndMappedOnBothSides`<br>`ClockMapTests.OffsetOfAWrongClockIsEstimated` |
| Transkript mit Whisper | Spracherkennung über whisper.cpp, vollständig offline. Das Erkennungsprogramm und ein Modell gehören zum Windows-Paket; aus dem Quellcode nennt man ein eigenes `whisper-cli.exe` in der Konfiguration. Der Sprachtest läuft dort, wo ein Erkennungsprogramm vorhanden ist; der Test des Pakets transkribiert eine englische und eine deutsche Probe | 0.1.0 | `SpeechTests.SpokenSentenceIsTranscribed`<br>`VideoImportTests.TranscriptFormatsAreRead` |
| Zeitachse und Spezifikation | Klick, Maske, Anweisung und Transaktion auf einer Zeitachse; daraus Masken, Abfragen und Tabellen nach festen Regeln | 0.1.0 | `TimelineTests.EventsGoToTheActionBeforeThem`<br>`TimelineTests.TransactionIdsGiveExactTies`<br>`TimelineTests.TransactionsAreTiedByTimeAndTableWithoutIds`<br>`TimelineTests.SameInputGivesSameOutput`<br>`SpecTests.StepsMasksQueriesTablesAndManualLinks` |
| Nachbau als Webanwendung | Geführter Schritt mit Web-Vorlage und Anleitung für einen angeschlossenen KI-Assistenten | 0.1.0 | `GuidedTests.GenerateIsAWorkspaceAndInstructionsNotAnApplication`<br>`src/templates/web/test/template.test.js: server: API and files through HTTP with a stand-in pool` |
| Archiv-Klon, nur lesend | Geführter Schritt mit Referenzbildern je Maske | 0.1.0 | `GuidedTests.CloneScaffoldShowsTheReferenceAndGreysOutWritingControls` |
| Befunde aus dem Video übernehmen | Geführter Schritt: Jeder Befund wird als übernommen, abgelehnt oder offen entschieden, mit Bericht | 0.1.0 | `GuidedTests.EnrichRecordsDecisionsAndReportsEveryFinding` |
| Vergleichstests | Bildvergleich und Vergleich der gemessenen Datenbankänderungen zweier Aufnahmen | 0.1.0 | `VerifyScreensTests.EqualDifferentToleranceIgnoreAndSize`<br>`VerifyScreensTests.FoldersArePairedByName`<br>`VerifyDbTests.SameChangesWithOtherGeneratedKeysAreEqual`<br>`VerifyDbTests.DeliberateDifferencesAreFound`<br>`VerifyDbTests.MissingStepAndEmptyRecordingsAreNotEqual`<br>`VerifyDbTests.AWrongReferenceIsNotHiddenByTheKeyRule` |
| Vergleichsvideo mit Player | Original und Nachbau auf einer Zeitachse | 0.1.0 | `PluginTests.VideoLayoutPluginIsUsedByVideo`<br>`PipelineTests.SampleEndToEndOnPlainPostgres` |
| MCP-Server und Kommandozeile | Dieselben Schritte als Werkzeuge für jeden MCP-Client und als Befehle | 0.1.0 | `McpTests.ProtocolToolsAndPrompts`<br>`PipelineTests.CommandLineReportsUnknownCommandsAndMissingArguments` |
| Plugin-Schnittstellen | Für Ereignisquellen, Reader, Web-Vorlagen, Klon-Renderer und Videolayouts, dokumentiert | 0.1.0 | `PluginTests.ExamplesAreValidAndOfEveryType`<br>`PluginTests.SourcePluginDeliversEventsThroughTheSameInterface`<br>`PluginTests.WebTemplatePluginIsUsedByGenerate`<br>`PluginTests.CloneRendererPluginIsUsedByClone`<br>`PluginTests.VideoLayoutPluginIsUsedByVideo`<br>`PluginTests.BrokenManifestsAreRefusedWithTheReason`<br>`ReaderTests.ReaderPluginWritesTheSameFormatAndIsFoundByFileExtension`<br>`ReaderTests.ReaderPluginNeedsACommandAndAValidResult` |
| Beispielanwendung mit Ende-zu-Ende-Lauf | Eine eigene Beispielanwendung als Windows-Programm und im Browser; `eqlmaker sample` durchläuft den ganzen Ablauf auf reinem PostgreSQL und findet die absichtlichen Fehler | 0.1.0 | `PipelineTests.SampleEndToEndOnPlainPostgres`<br>`examples/sample-app/web/test/http.test.js: saving an invoice writes invoice, lines, stock movements, stock and counter`<br>`examples/sample-app/web/test/calc.test.js: the deliberate defect changes at least one rounding vector, and only the tax` |
| Deutsch und Englisch | Programme, Dokumentation und Web-Vorlage in beiden Sprachen | 0.1.0 | `I18nTests.TranslationsHaveTheSameKeysAndPlaceholders`<br>`I18nTests.GermanIsUsedWhenAskedFor`<br>`I18nTests.EnglishFileIsTheExportOfTheCode`<br>`src/templates/web/test/template.test.js: languages: English and German resources have the same keys` |
| Einrichtung ohne Adminrechte | Eine Batchdatei richtet alles im eigenen Ordner ein: kein Installer, keine Registry, kein Dienst. Getestet mit dem veröffentlichten Paket als Benutzer ohne Adminrechte unter Windows Server 2025; nicht getestet auf einem frischen Windows 10 oder 11. Die Tests des Pakets laufen lokal vor einer Veröffentlichung, nicht in den automatischen Prüfungen des Repositorys | 0.1.0 | `scripts/setup/tests/test-setup.ps1: the setup process is not elevated (restricted token, evidence taken inside the run)`<br>`scripts/setup/tests/test-setup.ps1: eqlmaker-setup.cmd -unattended as ordinary user: downloads, default PostgreSQL without psql, self test with speech and logical decoding`<br>`scripts/setup/tests/test-setup.ps1: registry autostart and uninstall keys, PATH, environment, services, firewall rules, scheduled tasks: nothing added, removed or changed by the package`<br>`scripts/setup/tests/test-setup.ps1: remove: after eqlmaker-db-stop.cmd the folders can be deleted completely by the ordinary user` |
| PowerBuilder-Bibliotheken lesen | Eingebauter Reader für die kompilierten Bibliotheken (.pbd, .pbl) von PowerBuilder 5 bis 9, ohne Quellcode: Menüs, Fenster, Beschriftungen, SQL-Anweisungen und Verweise. Das Ergebnis geht in die Spezifikation und zeigt, welche Tabellen keine Aufnahme berührt hat | 0.2.0 | `ReaderTests.PowerBuilderReaderReadsObjectsSqlLabelsMenusAndReferences`<br>`ReaderTests.PowerBuilderReaderRefusesUnicodeLibrariesAndOtherFiles`<br>`ReaderTests.FirstLibraryWinsForAnObjectNameThatOccursTwice`<br>`ReaderTests.ReadCommandPicksTheReaderByFileExtensionAndSpecTakesTheResultOver` |
| Anweisungstabelle und alte Szenariodateien | Die Anweisungen einer Aufnahme als Tabelle sql.tsv (das Format, das Werkzeuge zum Abspielen von Anweisungen lesen; T-SQL-Stapel und -Aufrufe als tsql-batch und tsql-rpc); Szenariodateien des früheren Recorders werden umgewandelt; ein Szenario kann eigene Logins und Datenbanken für den Mitschnitt nennen | 0.2.0 | `ExportTests.SqlTsvHasTheColumnsKindsAndEscapesOfTheReplayFormat`<br>`ExportTests.ExportSqlCommandWritesTheTableOfAnExistingRecording`<br>`ExportTests.ScenarioOfTheEarlierRecorderIsConverted`<br>`ExportTests.ScenarioCaptureObjectReplacesRolesAndDatabasesOfTheConfiguration` |
| Anwendungen auf anderen Datenbanken | Anwendungen, die SQL Server (TDS) sprechen, werden über EQLServer mitgeschnitten, ein eigenes, optionales Produkt, das in PostgreSQL speichert: Stapel, Aufrufe und geänderte Zeilen auf einer Zeitachse. Getestet mit einem optionalen Ende-zu-Ende-Test gegen einen laufenden EQLServer 1.10.0; die anderen Protokolle sind nicht getestet. EQLMaker braucht EQLServer nicht | 0.2.0 | `ServerTests.TdsStatementsAndChangedRowsOfAnApplicationReachTheTimeline`<br>`tests/eqlserver/run-eqlserver-e2e.ps1: changed rows: insert, update and delete of the customer table`<br>`TimelineTests.TransactionsAreTiedAcrossTheNamesOfAServerThatKeepsSeveralDatabasesInOne` |
<!-- status:end -->

Die Tests mit Namen der Form `Class.Method` gehören zu `bin\eqlmaker-tests.exe` (Quellen in
`tests/`); die übrigen sind nach Datei und Test benannt. Das sind die Node-Tests und, für die
Einrichtung, die Tests des Windows-Pakets in `scripts/setup/tests/test-setup.ps1`, die vor einer
Veröffentlichung lokal gegen das gebaute Paket laufen. Wie man sie ausführt:
[CONTRIBUTING.de.md](CONTRIBUTING.de.md).

## In Entwicklung

<!-- status:begin in-development -->
Von Version 0.2 ist derzeit nichts in Entwicklung: Jede Funktion der Liste oben ist verfügbar.
<!-- status:end -->

Version 0.2.0 ist als Windows-Paket veröffentlicht (`eqlmaker-windows-x64.zip` mit Prüfsumme).
Das Paket dieser Veröffentlichung wurde lokal unter Windows Server 2025 gebaut und getestet, ebenso
die Aktualisierung einer installierten 0.1.0 durch Darüberkopieren. Version 0.2.0 bringt die Reader-Plugin-Stelle mit dem PowerBuilder-Reader, die Anweisungstabelle
`sql.tsv`, geänderte Zeilen über `pgoutput` und den Test mit EQLServer für Anwendungen auf anderen
Datenbanken. Nächste Schritte:

- Das Paket auf einem frischen Windows 10 und einem frischen Windows 11 testen.
- Den Download über einen Proxy testen.
- Das Paket mit dem Release-Workflow bauen (PowerShell 7); bisher wurde es nur mit Windows
  PowerShell 5.1 gebaut.
- Eine kurze Demo-Animation für das README.

## Geplant

<!-- status:begin planned -->
- **Ab Juni 2027 · geplant**
  - [ ] Bildschirmelemente erkennen und mit Datenänderungen verknüpfen
  - [ ] Live-Dialog per Text, später per Sprache
  - [ ] Personenbezogene Daten erkennen und ersetzen
  - [ ] Regeln aus Antworten ableiten und an vorhandenen Daten prüfen
  - [ ] Anleitung mit Bildern, Deutsch und Englisch
  - [ ] Tests mit Anwenderinnen und Anwendern, Fallstudie an einer freien Anwendung
- **Später · ohne Termin**
  - [ ] Ereignisformat als eigenständige Spezifikation 1.0 für andere Werkzeuge
  - [ ] Weitere offene Quellen: MySQL/MariaDB und SQL Server
  - [ ] Reader für weitere Dateiarten von Anwendungen
  - [ ] Barrierefreiheit der erzeugten Webanwendungen (WCAG 2.1 AA)
  - [ ] Vorlagen-Galerie für Beitragende
<!-- status:end -->

Die Punkte „ab Juni 2027“ sind ein klar abgegrenztes neues Modul. Bisher gibt es nur das Konzept,
keinen Code. Eine Förderung wird beantragt; eine Zusage gibt es nicht.

Die Punkte „später“ haben keinen Termin. Darunter: Quellen für MySQL/MariaDB und SQL Server
brauchen einen Weg, geänderte Zeilen auf diesen Servern zu messen; bis dahin lassen sich
Anwendungen auf anderen Datenbankservern erreichen, indem PostgreSQL durch EQLServer ersetzt
wird, ein kommerzielles Produkt desselben Autors, das kein Teil von EQLMaker ist (siehe
[Anwendungen auf anderen Datenbanken](docs/de/other-databases.md)).
