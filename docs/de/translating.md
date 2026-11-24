<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](../translating.md) · [Deutsch](translating.md)

# Übersetzen

EQLMaker ist auf Englisch geschrieben und ins Deutsche übersetzt. Diese Seite sagt, wo die Texte
stehen, wie man eine Übersetzung ändert und wie man eine Sprache hinzufügt. Englisch ist die
Referenz: Weichen zwei Fassungen voneinander ab, gilt die englische.

## Was es in welcher Sprache gibt

| Was | Englisch | Deutsch | Weitere Sprachen |
|---|---|---|---|
| Dokumentation | `docs/*.md` | `docs/de/*.md` | `docs/<lang>/*.md` |
| README, ROADMAP, CONTRIBUTING | siehe `docs/languages.json` | siehe `docs/languages.json` | `README.<lang>.md` und so weiter |
| Programmtexte: Hilfe, Meldungen, Beschreibungen der MCP-Werkzeuge | im Code, exportiert nach `src/i18n/en.json` | `src/i18n/de.json` | `src/i18n/<lang>.json` |
| Statustabellen in README und ROADMAP | `labels.en` und die Texte `en` in `docs/status.json` | `labels.de` und die Texte `de` | dieselben Schlüssel für die Sprache |
| Web-Vorlage | `src/templates/web/public/i18n/en.json` | `de.json` im selben Ordner | eine weitere Datei dort |
| Meldungen der Paket-Einrichtung | `scripts/setup/strings.en.json` | `strings.de.json` | eine weitere Datei dort |

Nicht übersetzt, in keiner Sprache: Dateiformate und ihre Schlüssel, Bezeichner, Namen von
Befehlen, Optionen und MCP-Werkzeugen, Fehlermeldungen des Kerns und die Anleitungen der
geführten Schritte (`src/prompts`).

## Die Einstellungsdatei

`docs/languages.json` sagt, welche Sprache die Referenz ist und welche Datei das README und die
ROADMAP jeder Sprache ist:

```json
{
  "canonical": "en",
  "languages": ["en", "de"],
  "readme": { "de": "README.md", "en": "README.en.md" },
  "roadmap": { "en": "ROADMAP.md", "de": "ROADMAP.de.md" }
}
```

Die Prüfungen und die Statustabellen lesen diese Datei. Soll eine andere Sprache diejenige sein,
die die Seite des Repositorys zeigt, benennen Sie die beiden README-Dateien um und ändern Sie
`readme` hier; danach berichtigen Sie die Verweise in den Zeilen zum Sprachwechsel.

## Eine Übersetzung ändern

1. Zuerst die englische Seite ändern, dann dieselbe Stelle in jeder Übersetzung.
2. Den Aufbau beibehalten: dieselben Überschriften in derselben Reihenfolge, dieselbe Zahl von
   Verweisen, dieselben Tabellen mit derselben Zahl von Zeilen.
3. Codeblöcke identisch halten. Befehle, JSON und Dateinamen werden nicht übersetzt. Was einen
   übersetzten Kommentar braucht, kommt in den Text um den Block.
4. Die Prüfung ausführen.

## Die Prüfung

```text
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\check-docs.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\check-docs.ps1 -Language de -Table translation-status.md
```

Sie vergleicht jedes Original mit seiner Übersetzung: die Folge der Überschriftenebenen, die
Codeblöcke, die Zahl der Verweise und Bilder und die Zeilenzahl jeder Tabelle. Sie gibt je Datei
eine Zeile mit `in step`, `drift` (und was abweicht), `missing` oder `no original` aus und endet
mit Rückgabewert 1, wenn etwas nicht im Gleichschritt ist. `-Table <file>` schreibt diese Liste
zusätzlich als Markdown-Tabelle; die Datei ist ein Bericht für Sie und wird nicht eingecheckt.

Die Prüfung sieht den Aufbau, nicht den Sinn. Eine Übersetzung, die besteht, kann trotzdem
falsch sein.

Eine Übersetzung, die absichtlich eine andere Zahl von Verweisen hat, sagt das in einem
Kommentar, mit dem Unterschied zum Original:

```text
<!-- check-docs: links -1 -->
```

Programmtexte werden von Tests geprüft:

```text
bin\eqlmaker-tests.exe I18n
```

`I18nTests.TranslationsHaveTheSameKeysAndPlaceholders` schlägt fehl, wenn einer Übersetzung ein
Schlüssel fehlt, sie einen unbekannten Schlüssel hat oder einen Platzhalter (`{0}`), einen
Optionsnamen (`--name`) oder einen Bezeichner mit Unterstrich (`record_start`) verloren hat.
`I18nTests.EnglishFileIsTheExportOfTheCode` schlägt fehl, wenn `en.json` nicht der Export des
Codes ist.

## Eine Sprache hinzufügen

Verwenden Sie den zweibuchstabigen Code der Sprache, hier `xx`.

1. **Dokumentation.** Kopieren Sie `docs/*.md` nach `docs/xx/` und übersetzen Sie den Text.
   Erweitern Sie in jeder Seite die Zeile zum Sprachwechsel am Anfang, in allen Sprachen.
2. **README, ROADMAP, CONTRIBUTING.** Fügen Sie `README.xx.md`, `ROADMAP.xx.md` und
   `CONTRIBUTING.xx.md` hinzu. Tragen Sie `xx` in `languages`, `readme` und `roadmap` in
   `docs/languages.json` ein.
3. **Statustabellen.** Fügen Sie `labels.xx` in `docs/status.json` hinzu und einen Text `xx`
   bei `name` und `description` jeder Funktion. Führen Sie `scripts\update-status.ps1` aus; es
   füllt die Statusblöcke des neuen README und der neuen ROADMAP.
4. **Programmtexte.** Exportieren Sie die englischen Texte und übersetzen Sie die Werte, nicht
   die Schlüssel:

   ```text
   bin\eqlmaker i18n-export src\i18n\en.json
   copy src\i18n\en.json src\i18n\xx.json
   ```

   Im Hilfetext (Schlüssel `usage`) übersetzen Sie die Beschreibung jeder Zeile und lassen die
   Spalte mit den Befehlen, wie sie ist. Bauen Sie erneut (`build.cmd`), damit die Datei neben
   die Programme kopiert wird, dann probieren Sie es aus: `bin\eqlmaker help --lang xx`.
5. **Web-Vorlage**, wenn Sie erzeugte Anwendungen in der Sprache wollen: Fügen Sie
   `src/templates/web/public/i18n/xx.json` mit den Schlüsseln der dortigen `en.json` hinzu; die
   Node-Tests der Vorlage vergleichen die Schlüssel.
6. Führen Sie die Prüfung und die Tests oben aus.

Eine Sprache gilt als vollständig, wenn die Prüfung für sie besteht und die Programmtexte jeden
Schlüssel haben. Auch eine Teilübersetzung ist willkommen: Sagen Sie im Pull Request, was fehlt.

## Wortwahl

- Einfache, kurze Sätze. Sagen, was zu tun ist.
- Ein Wort für eine Sache, überall: Aufnahme, Zeitachse, Spezifikation, Maske, Anweisung,
  geführter Schritt, Vergleichstest. Sehen Sie nach, wie die deutschen Seiten sie nennen, bevor
  Sie Wörter wählen.
- `generate`, `clone` und `enrich` sind in jeder Sprache geführte Schritte. Übersetzen Sie sie
  nicht mit Wörtern, die „automatisch“ bedeuten.
