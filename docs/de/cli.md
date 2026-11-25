<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](../cli.md) · [Deutsch](cli.md)

# Befehlsreferenz

Diese Seite nennt jeden Befehl von `eqlmaker` mit jeder Option. Was die Schritte tun, steht
unter [Ablauf](pipeline.md); jeder Befehl des Ablaufs ist auch ein [MCP-Werkzeug](mcp.md) mit
demselben Ergebnis.

```text
eqlmaker <command> [arguments] [options]
eqlmaker help
```

## Allgemeine Regeln

| Option | Bedeutung |
|---|---|
| `--project <folder>` | der Projektordner (Vorgabe: der aktuelle Ordner) |
| `--json` | das Ergebnis als JSON ausgeben |
| `--lang en\|de` | Sprache der Texte, siehe [Konfiguration](configuration.md) |

- Eine Option mit Wert schreibt man `--name value` oder `--name=value`; eine Option ohne Wert
  ist ein Schalter. Alles nach `--` gilt als Argument.
- Eine Aufnahme wird über ihren Namen im Projektordner `recordings` benannt oder über den Pfad
  ihres Ordners.
- Ohne `--json` wird ein Ergebnis als Zeilen der Form `key value` ausgegeben.

| Rückgabewert | Bedeutung |
|---|---|
| 0 | erledigt |
| 1 | Fehler; der Grund ist die Zeile, die mit `eqlmaker:` beginnt |
| 2 | der Schritt lief und sein Ergebnis ist „nicht bestanden“: `verify`, `sample`, `selftest` |

## Projekt

| Befehl | Wirkung |
|---|---|
| `eqlmaker init [folder]` | legt den Ordner (Vorgabe: der aktuelle) mit einer ersten `eqlmaker.json` an; lehnt ab, wenn die Datei existiert |
| `eqlmaker version` | gibt die Version aus |
| `eqlmaker help` | gibt die Kurzübersicht der Befehle aus |

## Aufzeichnen

```text
eqlmaker record --scenario <file.json> [--name <n>] [--input real|background] [--no-video] [--capture-database <db>] [--capture-role <roles>] [--quiet]
eqlmaker record --start [--name <n>] [--no-video] [--no-capture] [--region <r>] [--fps <n>]
eqlmaker record --stop
eqlmaker reset
```

| Option | Bedeutung |
|---|---|
| `--scenario <file>` | die Szenariodatei ausführen und aufzeichnen; siehe [Fernbedienung](remote-control.md) |
| `--start` | eine Aufnahme im Hintergrund starten, während ein Mensch arbeitet; kehrt zurück, sobald die Erfassung läuft |
| `--stop` | die mit `--start` begonnene Aufnahme beenden und den Aufnahmeordner schreiben |
| `--name <n>` | Name der Aufnahme (Vorgabe: der Name im Szenario oder `recording-<date>-<time>`) |
| `--input real\|background` | Eingabemodus, hat Vorrang vor Szenario und Konfiguration |
| `--no-video` | kein Video aufzeichnen |
| `--no-capture` | mit `--start`: keine Datenbankereignisse erfassen |
| `--region <r>` | mit `--start`: Teil des Bildschirms für das Video: `workarea` (Vorgabe), `screen`, `x,y,w,h` oder ein Muster für den Fenstertitel |
| `--fps <n>` | mit `--start`: Bilder pro Sekunde (Vorgabe 15) |
| `--capture-database <db>` | genau diese Datenbank erfassen, was auch immer die Konfiguration nennt |
| `--capture-role <roles>` | genau die Anweisungen dieser Logins erfassen (kommagetrennt), was auch immer die Konfiguration nennt; zusammen mit `--capture-database` beides |
| `--quiet` | mit `--scenario`: die Schritte und das Ergebnis nicht ausgeben |

`eqlmaker reset` nimmt zurück, was eine abgebrochene Erfassung auf dem Datenbankserver
hinterlassen hat: die Protokolleinstellungen von Rollen und Datenbanken und Replikations-Slots.
Siehe [Ereignisquellen](sources.md).

## Zeitachse, Spezifikation, Handbuch

| Befehl | Optionen | Wirkung |
|---|---|---|
| `eqlmaker correlate <recording>` | | schreibt `timeline.json` in den Aufnahmeordner |
| `eqlmaker spec [recording ...]` | | schreibt `spec/spec.json` und `spec/spec.md` aus den genannten Aufnahmen (Vorgabe: alle); übernimmt die Ergebnisse von `read` |
| `eqlmaker read <Dateien oder Ordner>` | `--reader <name>`, `--out <Ordner>`, `--bundle <Objekte>`, `--max-depth <n>`, `--exclude <regex>`, `--no-raw` | liest die Dateien der Originalanwendung (eingebaut: PowerBuilder-Bibliotheken) nach `static/<Reader>/`; siehe [PowerBuilder-Bibliotheken](powerbuilder.md) |
| `eqlmaker export-sql <recording>` | `--out <Datei>` | schreibt die Anweisungen einer Aufnahme als Tabelle `sql.tsv` (Vorgabe: im Aufnahmeordner) |
| `eqlmaker convert-scenario <Datei>` | `--out <Datei>` | wandelt eine Szenariodatei des früheren Recorders um (`sql` wird `capture`); siehe [Fernbedienung](remote-control.md) |
| `eqlmaker manual <file>` | `--name <n>`: Name im Projekt (Vorgabe: Dateiname) | übernimmt ein Handbuch nach `manual/`; siehe [Handbuch](manual.md) |

## Geführte Schritte

Siehe [geführte Schritte](guided-steps.md).

| Befehl | Optionen | Wirkung |
|---|---|---|
| `eqlmaker generate` | `--template <name>` (Vorgabe `default`), `--force` | bereitet `web/` und `GENERATE.md` vor |
| `eqlmaker clone` | `--renderer <name>` (Vorgabe `backdrop`), `--force` | bereitet `clone/` und `CLONE.md` vor |
| `eqlmaker enrich [<A> <B>]` | `--findings <name>` | bereitet `enrich/<name>/` und `ENRICH.md` vor; mit zwei Aufnahmen auch den Datenbankvergleich vorher |
| `eqlmaker enrich --decide <finding>` | `--status adopted\|rejected\|open`, `--note "<text>"`, `--files a,b`, `--findings <name>` | hält die Entscheidung zu einem Befund fest; Status und Notiz sind erforderlich |
| `eqlmaker enrich --finish [<A> <B>]` | `--findings <name>` | schreibt den Bericht; mit zwei Aufnahmen auch den Datenbankvergleich nachher |
| `eqlmaker prompt <name>` | `--findings <name>` für `enrich` | gibt die Anleitung von `generate`, `clone` oder `enrich` aus |

## Video und Vergleichstests

```text
eqlmaker video <A> <B> [--layout <name>] [--name <n>] [--no-video-file]
eqlmaker verify screens <A> <B> [--tolerance 16] [--max-percent 0.5] [--ignore x,y,w,h;x,y,w,h] [--name <n>]
eqlmaker verify db <A> <B> [--ignore table.column,...] [--keys table.column,...] [--references table.column=table,...] [--name <n>]
eqlmaker web-shot <url> <file.png> [--size 1280x800] [--wait 300]
```

| Option | Bedeutung |
|---|---|
| `--layout <name>` | `video`: `side-by-side` (Vorgabe) oder ein Plugin vom Typ video-layout |
| `--no-video-file` | `video`: nur die Player-Seite, keine zusammengesetzte Datei |
| `--name <n>` | Name des Ergebnisordners in `video/` oder `verify/` |
| `--tolerance <n>` | `verify screens`: Unterschied je Farbkanal, der noch als gleich gilt (Vorgabe 16) |
| `--max-percent <n>` | `verify screens`: Anteil abweichender Bildpunkte, der noch besteht (Vorgabe 0.5) |
| `--ignore` | `verify screens`: Bereiche, die nicht verglichen werden; `verify db`: ausgelassene Spalten |
| `--keys` | `verify db`: Schlüsselspalten, die nach der Reihenfolge ihrer Entstehung verglichen werden (Vorgabe `*.id`) |
| `--references` | `verify db`: Spalten, die einen Schlüssel einer anderen Tabelle enthalten |
| `--size <w>x<h>` | `web-shot`: Größe des Bildes (Vorgabe `1280x800`) |
| `--wait <ms>` | `web-shot`: Pause, nachdem die Seite geladen ist (Vorgabe 300) |

Die Regeln stehen unter [Vergleichstests](verify.md).

## Kommentiertes Video

```text
eqlmaker video-import <file.mp4> [options]
eqlmaker clock
eqlmaker import-log <file> [--name <n>] [--prefix "%m [%p] "] [--utc-offset-minutes <n>] [--database <db>] [--application <name>]
```

| Option von `video-import` | Bedeutung |
|---|---|
| `--recordings a,b` | Aufnahmen, deren Ereignisse zum Video gehören (Vorgabe: alle des Projekts) |
| `--transcript <file>` | dieses Transkript verwenden (WebVTT, SRT oder JSON), statt die Sprache zu erkennen |
| `--language <code>` | Sprache des Gesprochenen (Vorgabe: erkannt) |
| `--no-transcript` | die Tonspur überspringen |
| `--clock-offset-ms <n>` | Uhr des Datenbankservers minus Uhr des Aufnahmerechners |
| `--estimate-offset` | diesen Unterschied aus den Daten schätzen, auch wenn ein gemessener Wert vorliegt |
| `--max-shift <seconds>` | größter Uhrenunterschied, der beim Schätzen betrachtet wird (Vorgabe 900) |
| `--sample-fps <n>` | Bilder pro Sekunde, die für die Zeitabbildung gelesen werden (Vorgabe 2) |
| `--max-keyframes <n>` | Obergrenze der behaltenen Schlüsselbilder (Vorgabe 120) |
| `--attach-seconds <n>` | wie weit ein Satz oder Schlüsselbild von einem Vorgang entfernt sein darf, um ihm zugeordnet zu werden (Vorgabe 8) |
| `--name <n>` | Name des Ergebnisordners (Vorgabe: Dateiname des Videos) |

| Option von `import-log` | Bedeutung |
|---|---|
| `--name <n>` | Name der Aufnahme (Vorgabe: `log-<file name>`) |
| `--prefix` | `log_line_prefix` des Servers (Vorgabe `%m [%p] `) |
| `--utc-offset-minutes <n>` | UTC-Versatz der Zeiten im Protokoll (Vorgabe: dieser Rechner) |
| `--database <db>` | nur Anweisungen dieser Datenbank übernehmen (braucht `%d` im Präfix) |
| `--application <name>` | nur Anweisungen dieser Anwendung übernehmen (braucht `%a` im Präfix) |

`eqlmaker clock` gibt die Uhr des Datenbankservers minus die Uhr dieses Rechners aus. Siehe
[Video-Import](video-import.md). Die Zeitstempel-Leiste ist ein eigenes Programm mit eigenen
Optionen, siehe [Zeitstempel-Leiste](timestamp-overlay.md).

## Fernbedienung

| Befehl | Wirkung |
|---|---|
| `eqlmaker windows` | listet die sichtbaren Hauptfenster auf |
| `eqlmaker controls <title>` | listet die Steuerelemente des Fensters auf, dessen Titel zum Muster passt |
| `eqlmaker background-run <desktop> <command...>` | führt einen Befehl auf einem eigenen Desktop aus, der nie angezeigt wird; `--timeout <seconds>` beendet ihn nach dieser Zeit |

## MCP-Server

| Befehl | Wirkung |
|---|---|
| `eqlmaker mcp` | betreibt den MCP-Server auf Standardeingabe und Standardausgabe; siehe [MCP-Server](mcp.md) |

## Beispiel und Selbsttest

| Befehl | Optionen | Wirkung |
|---|---|---|
| `eqlmaker sample` | `--flaw`: die Browser-Variante mit ihren absichtlichen Fehlern starten; `--no-video`: kein Video aufzeichnen; `--visible`: beide Varianten mit der echten Maus auf dem sichtbaren Desktop bedienen | durchläuft den ganzen Ablauf mit der Beispielanwendung; siehe [Beispielanwendung](sample-app.md) |
| `eqlmaker selftest` | `--quick`: den zweiten Beispiellauf mit den absichtlichen Fehlern überspringen; `--audio <file>` mit `--expect "word,word"`: die Spracherkennung mit dieser Audiodatei prüfen | prüft Ressourcen, FFmpeg, Node.js, die Datenbankverbindung und, mit `--audio`, die Spracherkennung, dann läuft das Beispiel treu und mit den Fehlern; wird von der Einrichtung des Pakets benutzt |

## Für Übersetzende

| Befehl | Wirkung |
|---|---|
| `eqlmaker i18n-export <file>` | schreibt die englischen Programmtexte mit ihren Schlüsseln; siehe [Übersetzen](translating.md) |
