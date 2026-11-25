<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](../pipeline.md) · [Deutsch](pipeline.md)

# Ablauf

EQLMaker arbeitet in Schritten. Jeder Schritt liest Dateien des Projektordners und schreibt
Dateien hinein; nichts wird woanders aufbewahrt. Jeder Schritt ist ein Befehl und, mit demselben
Ergebnis, ein MCP-Werkzeug.

```text
record -> correlate -> spec -> generate / clone -> video -> verify
            manual ----^            ^---- enrich <- video-import
```

| Schritt | Befehl | Art | Liest | Schreibt |
|---|---|---|---|---|
| Aufzeichnen | `record` | Code | die laufende Anwendung, den Datenbankserver | `recordings/<name>/` |
| Zeitachse | `correlate <recording>` | Code, feste Regeln | eine Aufnahme | `recordings/<name>/timeline.json` |
| Spezifikation | `spec [recording ...]` | Code, feste Regeln | Zeitachsen, `manual/` | `spec/spec.json`, `spec/spec.md` |
| Handbuch | `manual <file>` | Code | ein Handbuch | `manual/<name>.json`, `manual/<name>.txt` |
| Anwendungsdateien | `read <files>` | Code | die Dateien der Originalanwendung | `static/<Reader>/` |
| Webanwendung | `generate` | geführt | `spec/`, `manual/`, eine Web-Vorlage | `web/` mit `GENERATE.md` |
| Archiv | `clone` | geführt | `spec/`, Referenzbilder | `clone/` mit `CLONE.md` |
| Vergleichsvideo | `video <A> <B>` | Code | zwei Aufnahmen | `video/<name>/` |
| Vergleichstests | `verify screens`, `verify db` | Code | Bilder oder zwei Aufnahmen | `verify/<name>/` |
| Kommentiertes Video | `video-import <file.mp4>` | Code | ein Video, Aufnahmen, `spec/` | `findings/<name>/` |
| Befunde übernehmen | `enrich` | geführt | `findings/`, `web/` | `enrich/<name>/` |

„Code, feste Regeln“ heißt: Dieselbe Eingabe ergibt dieselbe Ausgabe, und kein Sprachmodell ist
beteiligt. „Geführt“ heißt: EQLMaker bereitet vor und prüft, jemand anderes baut. Siehe
[geführte Schritte](guided-steps.md).

## Aufzeichnen

Eine Aufnahme entsteht auf einem von drei Wegen:

- `eqlmaker record --scenario <file.json>`: EQLMaker bedient die Anwendung, wie die
  Szenariodatei es sagt, und zeichnet dabei auf. Siehe [Fernbedienung](remote-control.md).
- Ein MCP-Client ruft `record_start` auf, bedient die Anwendung mit den Werkzeugen der
  Fernbedienung, setzt Kapitel mit `record_caption` und endet mit `record_stop`. Siehe
  [MCP-Server](mcp.md).
- `eqlmaker record --start`, ein Mensch arbeitet, `eqlmaker record --stop`. Eine solche Aufnahme
  hat Video und Datenbankereignisse, aber keine Bedienhandlungen und keine Kapitel.

Die Erfassung der Datenbankereignisse gilt für Sitzungen, die nach dem Start der Aufnahme
beginnen: Starten Sie die Anwendung nach der Aufnahme. Was die Erfassung auf dem Server tut,
steht unter [Ereignisquellen](sources.md).

**Kapitel.** Ein Kapitel ist eine Marke mit Überschrift, eines je Maske oder fachlichem Schritt
(„New customer“, „Save invoice“). Aus Kapiteln werden die Schritte der Spezifikation, und
`verify db` und `video` ordnen zwei Aufnahmen über ihre Kapitelüberschriften einander zu.
Verwenden Sie in der Aufnahme des Originals und der nachgebauten Anwendung dieselben
Überschriften.

### Der Aufnahmeordner

| Datei | Inhalt |
|---|---|
| `recording.json` | Manifest: `format`, `version`, `name`, `t0_us` (Start in Mikrosekunden), `duration`, `input`, `video`, `fps`, `frames`, `region`, `source`, `capture` (Rollen, Datenbanken, ob geänderte Zeilen erfasst wurden, was die Quelle liefert, Uhrendifferenz zum Server, Serverversion), `chapters`, `counts`, `warnings`, `files` |
| `actions.jsonl` | Eine Bedienhandlung je Zeile: `v`, `seq`, `ts`, `time`, `offset`, `kind` (`chapter`, `launch`, `window`, `click`, `fill`, `type`, `keys`, `scroll`, `web_open`, `web_click`, `web_fill`, `note`) und, soweit zutreffend, `window`, `control`, `class`, `x`, `y`, `text`, `shot` |
| `events.jsonl` | Die Datenbankereignisse im [Ereignisformat](event-format.md) |
| `video.mp4` | Bildschirmvideo (H.264), wenn Video aufgezeichnet wurde |
| `sql.vtt` | Die Anweisungen als Untertitel des Videos |
| `sql.tsv` | Die Anweisungen als Tabelle (Versatz, Zeit, Sitzung, Benutzer, Datenbank, Anwendung, Art, Anweisung), das Format, das Werkzeuge zum Abspielen von Anweisungen lesen; siehe `eqlmaker export-sql` |
| `viewer.html` | Player-Seite: Video, Bedienhandlungen und Ereignisse nebeneinander. Im Browser öffnen. |
| `shots/NNNN.png` | Referenzbild des Fensters, aufgenommen mit einer Bedienhandlung |
| `shots/NNNN.json` | Die Steuerelemente des Fensters in diesem Bild: text, class, x, y, w, h, enabled |
| `timeline.json` | Geschrieben von `correlate` |

Lesen Sie die `warnings` in `recording.json`. Sie sagen zum Beispiel, dass keine geänderten
Zeilen erfasst wurden.

## Correlate

`eqlmaker correlate <recording>` verbindet, was bedient wurde, mit dem, was die Datenbank tat,
nach zwei festen Regeln:

1. **Ereignisse zu Bedienhandlungen.** Jedes Ereignis gehört zur letzten Bedienhandlung, die zu
   seinem Zeitpunkt oder davor begann, mit einer Toleranz von 2 ms. Ereignisse vor der ersten
   Bedienhandlung werden getrennt aufgeführt.
2. **Transaktionen zu Anweisungen.** Eine Transaktion (die geänderten Zeilen) wird mit
   Anweisungen verbunden, in dieser Reihenfolge:
   - über dieselbe Transaktionskennung, wenn der Server sie protokolliert (`%x` in
     `log_line_prefix`): exakt;
   - sonst über die Sitzung, deren letzte schreibende Anweisung vor dem Commit eine der
     geänderten Tabellen nennt: Methode `time+table`;
   - sonst über die letzte Anweisung vor dem Commit: Methode `time`.

   Die Methode steht neben jeder Verbindung. Bei einem ausdrücklichen Transaktionsblock werden
   die Anweisungen von `BEGIN` bis `COMMIT` dieser Sitzung genommen.

Der SQL-Text wird von einem Lexer gelesen, nicht von einem Parser: das Verb, die Tabellen nach
`FROM`, `JOIN`, `INTO` und `UPDATE` und die „Form“ der Anweisung, also ihr Text mit `?` an
Stelle der Literale. So gefundene Tabellen sind als „laut SQL-Text“ markiert; Tabellen
geänderter Zeilen sind gemessen.

Das Ergebnis, `timeline.json`, ist ein Baum: Kapitel → Bedienhandlungen → Anweisungen →
Transaktionen → Tabellen und Zeilen. Dieselbe Aufnahme ergibt immer dieselbe Datei.

Ohne Transaktionskennungen ist die Verbindung zwischen einer Anweisung und ihren Zeilen eine
Näherung über Zeit und Tabelle. Sie ist verlässlich, solange jeweils eine Sitzung schreibt;
schreiben mehrere Sitzungen gleichzeitig in dieselben Tabellen, kann sie falsch sein, und die
Methode zeigt, wo man nachsehen muss.

## Spec

`eqlmaker spec [recording ...]` leitet die Spezifikation aus einer oder mehreren Zeitachsen ab
(Vorgabe: alle Aufnahmen des Projekts; eine fehlende Zeitachse wird zuerst gebildet). Sie
enthält:

- **Schritte**: einer je Kapitel, mit seinen Bedienhandlungen, Anweisungsformen,
  Schreibvorgängen (Tabelle, Operation, Zahl der Zeilen, gesehene Spalten, ein Beispiel),
  gelesenen und geschriebenen Tabellen, Referenzbildern und den passenden Kapiteln des
  Handbuchs.
- **Masken**: die Fenster, die zu sehen waren, benannt nach ihrem Titel mit neutralisierten
  Ziffern („Invoice 4711“ und „Invoice 4712“ sind eine Maske).
- **Abfragen**: jede Anweisungsform mit Beispielparametern.
- **Tabellen**: welche Schritte jede Tabelle lesen und schreiben, die Operationen, die gesehenen
  Spalten.
- **Offene Punkte**: gesehene Datenbankfehler, Aufnahmen ohne geänderte Zeilen, Ereignisse vor
  der ersten Bedienhandlung.

Ein Kapitel des Handbuchs wird über gemeinsame Wörter mit einem Schritt verknüpft: Ein Wort der
Kapitelüberschrift zählt 3, ein Wort des Textes 1; eine Verknüpfung braucht mindestens 3 Punkte.

`spec/spec.json` ist für Programme, `spec/spec.md` für Menschen. Die Spezifikation enthält nur,
was während einer Aufnahme bedient wurde. Eine Funktion, die niemand benutzt hat, steht nicht
darin.

## Handbuch

Siehe [Handbuch](manual.md). Führen Sie `spec` nach `manual` erneut aus, um die Verknüpfungen zu
erhalten.

## Generate, clone, enrich

Siehe [geführte Schritte](guided-steps.md).

## Video

`eqlmaker video <A> <B>` legt zwei Aufnahmen derselben Schritte auf eine Zeitachse. Kapitel mit
derselben Überschrift beginnen gemeinsam; das kürzere wartet auf seinem letzten Bild. Der Befehl
schreibt nach `video/<name>/`:

| Datei | Inhalt |
|---|---|
| `player.html` | Player-Seite mit beiden Videos, Kapitelliste und den Ereignissen beider Seiten |
| `pair.json` | Die Abschnitte: welcher Teil von A neben welchem Teil von B läuft |
| `pair.mp4` | Ein zusammengesetztes Video, erzeugt mit FFmpeg; entfällt mit `--no-video-file` |

`--layout <name>` wählt die Anordnung: `side-by-side` (eingebaut) oder ein
[Plugin vom Typ video-layout](plugins.md).

## Verify

Siehe [Vergleichstests](verify.md).

## Video-Import

Siehe [Video-Import](video-import.md) und [Zeitstempel-Leiste](timestamp-overlay.md).

## Hilfsbefehle

| Befehl | Zweck |
|---|---|
| `eqlmaker reset` | Zurücknehmen, was eine abgebrochene Erfassung auf dem Server hinterlassen hat (Einstellungen, Replikations-Slots) |
| `eqlmaker clock` | Uhr des Datenbankservers minus Uhr dieses Rechners |
| `eqlmaker import-log <file>` | Aus einem früher geschriebenen Serverprotokoll eine Aufnahme machen, die nur Ereignisse enthält |
| `eqlmaker web-shot <url> <png>` | Bild einer Webseite in vorgegebener Größe, für `verify screens` |

Alle Befehle und Optionen: [Befehlsreferenz](cli.md).
