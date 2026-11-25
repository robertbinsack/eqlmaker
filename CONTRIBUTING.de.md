<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](CONTRIBUTING.md) · [Deutsch](CONTRIBUTING.de.md)

# Mitmachen

Beiträge sind willkommen: Fehlerberichte, Korrekturen, Tests, Übersetzungen, Plugins und
Dokumentation. Diese Seite sagt, wie man baut, wie man testet und was eine Änderung braucht,
bevor sie übernommen werden kann.

## Bauen

```text
build.cmd
build.cmd -Lint
```

`build.cmd` braucht nichts außer Windows: Es nutzt den C#-Compiler des .NET Framework 4, der zu
jedem Windows gehört. Es gibt kein SDK, keinen Paketmanager und keine Projektdatei. Das Ergebnis
ist der Ordner `bin` mit `eqlmaker.exe`, `eqlmaker-timestamp.exe`, `sample-desktop.exe`,
`eqlmaker-tests.exe` und den Ressourcen in `bin\share`. `-Lint` prüft zusätzlich die
JavaScript-, PowerShell- und JSON-Dateien auf Syntaxfehler (JavaScript braucht Node.js im `PATH`).

## Tests

```text
bin\eqlmaker-tests.exe
bin\eqlmaker-tests.exe Timeline
bin\eqlmaker-tests.exe --list
bin\eqlmaker-tests.exe --json test-results.json
```

Ein Wort hinter dem Programmnamen ist ein Filter auf die Testnamen. Ein Test, dem etwas fehlt,
wird als übersprungen gemeldet, mit dem Grund. Damit alles läuft, brauchen die Tests:

| Was | Wie |
|---|---|
| Ein PostgreSQL ab Version 15 für die Datenbanktests | `EQLMAKER_TEST_PG` auf die Verbindungszeichenfolge eines Superusers setzen, zum Beispiel `host=127.0.0.1 port=5432 user=postgres dbname=postgres password='...'`. Der Server braucht `wal_level = logical` und `logging_collector = on`. Einen Server für Tests verwenden: Die Tests legen Datenbanken und Rollen an und löschen sie. |
| FFmpeg für die Videotests | `ffmpeg` im `PATH`, oder sein Pfad in `EQLMAKER_TEST_FFMPEG` |
| Ein Spracherkennungsprogramm für den Sprachtest | Pfad eines `whisper-cli.exe` in `EQLMAKER_TRANSCRIBE` (das Release-Paket enthält eines) |
| Microsoft Edge oder Chrome | für die Tests, die eine Webseite bedienen; Edge gehört zu Windows |
| Das Paket eines Datenbankservers, der in PostgreSQL speichert, für den optionalen Ende-zu-Ende-Test von [Anwendungen auf anderen Datenbanken](docs/de/other-databases.md) | ein laufender Server; seine Ports und, nur in der Umgebung, seine beiden Kennwörter in den Variablen `EQLMAKER_E2E_*` aus der [Beschreibung des Tests](docs/de/other-databases.md); der Test installiert, startet und stoppt nichts |

Die Tests der Web-Vorlage und der Browser-Variante des Beispiels laufen mit Node.js ab
Version 18:

```text
cd src\templates\web
node --test
cd ..\..\..\examples\sample-app\web
npm ci
node --test
```

Die Prüfungen des Repositorys:

```text
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\check-spdx.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\check-docs.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\update-status.ps1 -Check
```

Der Workflow `.github/workflows/ci.yml` führt all das bei jedem Pull Request aus, gegen ein
reines PostgreSQL.

## Stil

- **C# 5.** Der Compiler des .NET Framework kennt keine neuere Sprachversion: keine
  Zeichenfolgeninterpolation, kein `?.`, keine Ausdruckskörper für Member, kein `out var`.
- **Warnungen sind Fehler.** Der Compiler ist der Linter (`/warn:4 /warnaserror+`).
- **Keine Abhängigkeiten im Kern.** Alles in `src/core` entsteht aus der Klassenbibliothek des
  .NET Framework. Keine Pakete und keine Binärdateien hinzufügen.
- **Lizenzkopf.** Jede Quell- und Skriptdatei beginnt mit

  ```text
  SPDX-License-Identifier: AGPL-3.0-or-later
  Copyright (c) 2026 Robert Binsack
  ```

  in der Kommentarschreibweise der Datei; Markdown-Dateien beginnen mit der ersten Zeile als
  HTML-Kommentar. `scripts\check-spdx.ps1` prüft das.
- **Texte.** Einfache, kurze Sätze. Englisch ist die Referenz für Programmtexte und
  Dokumentation. Dateiformate, Bezeichner, Werkzeugnamen und Fehlermeldungen des Kerns bleiben
  in jeder Sprache englisch.
- **Keine Geheimnisse, keine echten Daten.** Keine Passwörter, keine Aufnahmen echter
  Anwendungen, keine Namen echter Kunden in Code, Tests oder Beispielen. Testdaten sind erfunden.

## Sign-off

Jeder Commit trägt eine Zeile `Signed-off-by` (`git commit -s`). Damit erklären Sie, dass Sie
die Änderung unter der Lizenz des Projekts beitragen dürfen. Das ist das Developer Certificate
of Origin; es gibt keine Beitragsvereinbarung und niemand erhält Sonderrechte an Ihrem Beitrag.
Der Text und die Anleitung stehen in [DCO.md](DCO.md).

## Wie eine Funktion „verfügbar“ wird

README und Roadmap führen eine Funktion nur dann als verfügbar, wenn ein Test sie belegt:

1. Die Funktion schreiben und mindestens einen Test, der ohne sie fehlschlägt.
2. Ihren Eintrag in [docs/status.json](docs/status.json) anlegen oder ändern: `status`
   `available`, die Testnamen unter `tests` (`Class.Method` für das Testprogramm,
   `<file>: <test name>` für Node-Tests und die Tests des Pakets), `since` und `date`.
3. `scripts\update-status.ps1` ausführen. Es schreibt die Statustabellen in die README- und
   ROADMAP-Dateien jeder Sprache und schlägt fehl, wenn ein genannter Test nicht existiert.
4. Sie in `docs/` beschreiben, auf Englisch und in jeder vollständigen Sprache (siehe unten).

Unfertige Arbeit steht als `in development` da, Ideen als `planned`, beides ohne Tests.

## Übersetzungen

Die Dokumentation gibt es auf Englisch (`docs/`) und Deutsch (`docs/de/`), mit denselben
Dateinamen. Eine Änderung an einer englischen Seite braucht dieselbe Änderung an der deutschen
Seite; Codeblöcke bleiben identisch. `scripts\check-docs.ps1` vergleicht den Aufbau.
Programmtexte stehen in `src/i18n/<language>.json`. Wie man eine Sprache hinzufügt:
[docs/de/translating.md](docs/de/translating.md).

Wer kein Deutsch spricht, sagt das im Pull Request und markiert den deutschen Text, der fehlt;
jemand anderes ergänzt ihn vor der Übernahme.

## Plugins

Ereignisquellen, Web-Vorlagen, Klon-Renderer und Videolayouts sind Plugins: ein Ordner mit
`plugin.json`, beschrieben in [docs/de/plugins.md](docs/de/plugins.md). Beispiele, die zum
Projekt gehören, kommen nach `plugins/examples/<type>-<name>` und erhalten einen Test in
`tests/PipelineTests.cs` (`PluginTests`). Ein Plugin für ein einzelnes Produkt oder einen
einzelnen Einsatzort ist in einem eigenen Repository besser aufgehoben.

## Pull Requests

- Ein Thema je Pull Request, mit einer Beschreibung, was sich ändert und warum.
- Die Tests laufen durch, die Prüfungen oben laufen durch, die Commits sind mit Sign-off versehen.
- Fragen gehören in die Discussions des Repositorys, Fehler und Vorschläge in Issues.
- Sicherheitsprobleme: siehe [SECURITY.md](SECURITY.md). Verhalten: [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).
