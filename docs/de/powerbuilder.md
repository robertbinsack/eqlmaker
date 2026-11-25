<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](../powerbuilder.md) · [Deutsch](powerbuilder.md)

# PowerBuilder-Bibliotheken

Der Reader `powerbuilder` ist in EQLMaker eingebaut. Er liest die kompilierten Bibliotheken (`.pbd`,
`.pbl`) einer PowerBuilder-Anwendung der Versionen 5 bis 9 und listet, was darin steckt: Menüs,
Fenster, Spaltenbeschriftungen der DataWindows, Meldungstexte, jede SQL-Anweisung und welches
Objekt welches benutzt. Das ist es, was eine Aufnahme nicht zeigen kann: die Funktionen, die
während der Aufnahme niemand benutzt hat, die Fehlermeldungen, die Anweisungen von Zweigen, die
nie liefen.

Die Bibliotheken werden nur gelesen. Es braucht weder eine PowerBuilder-Installation noch den
Quellcode; die kompilierten Dateien, die mit der Anwendung ausgeliefert werden, genügen.

## Benutzung

```text
eqlmaker read C:\App
eqlmaker read C:\App\app.pbd C:\App\app_uo.pbd --bundle w_customer,w_invoice
```

| Option | Bedeutung |
|---|---|
| `<Dateien oder Ordner>` | `.pbd`- und `.pbl`-Dateien oder Ordner (alle darin, nicht die Unterordner). Die Bibliotheken in der Reihenfolge der Bibliotheksliste der Anwendung angeben: Bei einem Namen, der in mehreren Bibliotheken vorkommt, gilt die erste. |
| `--reader powerbuilder` | den Reader nennen; ohne Angabe entscheidet die Dateiendung |
| `--out <Ordner>` | Ausgabeordner (Vorgabe `static/powerbuilder` im Projekt) |
| `--bundle <Objekte>` | Startobjekte (Fenster, Funktion, Menü): je eine Markdown-Datei mit allem, was das Objekt benutzt |
| `--max-depth <n>` | Ebenen der Verweise, denen ein Bündel folgt (Vorgabe 3) |
| `--exclude <regex>` | Objekte, denen Bündel nicht folgen |
| `--no-raw` | die Rohdaten der Objekte nicht schreiben |

Das MCP-Werkzeug heißt `read_application` (siehe [MCP-Server](mcp.md)).

## Ergebnis

| Datei | Inhalt |
|---|---|
| `reader.json` | das Ergebnis in dem Format, das jeder Reader schreibt, siehe [Plugins](plugins.md); `spec` liest es |
| `summary.md` | Bibliotheken mit ihrer PowerBuilder-Version, Objekte und SQL-Anweisungen je Objektart, die Einträge jedes Menüs |
| `sql.tsv` | jede SQL-Anweisung mit ihrem Objekt; ein `PBSELECT` eines DataWindows auch in lesbarer Form (`FROM ... COLS ... JOIN ... WHERE ... ARGS ... ORDER ...`) |
| `strings/<Objekt>.<Art>.txt` | alle lesbaren Zeichenketten des Objekts in ihrer Reihenfolge |
| `objects/<Bibliothek>/` | Rohdaten jedes Eintrags und `_index.tsv` (Name, Größe, Kompilierzeit) |
| `bundles/<Objekt>.md` | mit `--bundle`: das Objekt und die Objekte, die es benutzt, mit SQL, Beschriftungen, Texten und Verweisen |

Objektarten: `win` Fenster, `dwo` DataWindow, `fun` globale Funktion, `men` Menü, `udo`
Benutzerobjekt, `apl` Anwendung, `str` Struktur, `prj` Projekt; in einer `.pbl` auch die
Quelltext-Einträge (`sr*`).

So werden die Teile erkannt:

- **SQL:** Zeichenketten, die mit `SELECT`, `INSERT`, `UPDATE`, `DELETE`, `DECLARE`, `PBSELECT`,
  `CREATE`, `ALTER`, `DROP`, `CALL` oder `EXECUTE` beginnen: eingebettetes SQL von Skripten und
  Quellen von DataWindows. Die Tabellen werden daraus entnommen.
- **Verweise:** Bezeichner in den Zeichenketten eines Objekts, die Namen anderer Objekte sind.
- **Texte:** Zeichenketten aus mindestens zwei Wörtern: Meldungen, Fenstertitel, Hilfetexte.
- **Menüeinträge:** Zeichenketten eines Menüs mit Kurztaste (`&Kunden`); ein Tabulator beendet die
  Beschriftung (`&Hilfethemen<Tab>F1`).
- **DataWindow-Beschriftungen:** die Kopftextobjekte `<Spalte>_t` und ihr Text.
- **Bündel:** Objekte, die viele andere benutzen (2 % aller Objekte, mindestens 10: Fehlerbehandlung,
  Basisklassen), werden genannt, aber nicht aufgeklappt, damit ein Bündel bei den Objekten einer
  Funktion bleibt.

Zeichenketten werden als Windows-1252 gelesen und als UTF-8 geschrieben.

## In der Spezifikation

Gibt es `static/<Reader>/reader.json`, ergänzt `eqlmaker spec` die `spec.json` um einen Teil
`static` und die `spec.md` um den Abschnitt „Static analysis“:

- die Menüs mit ihren Einträgen,
- die Fenster mit ihren DataWindows, den Spaltenbeschriftungen und den Tabellen ihrer SQL-Anweisungen,
- die Tabellen der SQL-Anweisungen der Anwendung, jede markiert als von einer Aufnahme berührt
  oder nicht.

Tabellen, die keine Aufnahme berührt hat, werden ein offener Punkt der Spezifikation. Sie sagen,
welche Funktionen als Nächstes bedient und aufgenommen werden sollten oder welche Teile des
Nachbaus aus den Bibliotheken statt aus einer Aufnahme kommen müssen. Die Fenster werden den
Masken der Aufnahmen nicht zugeordnet: Der Titel eines Fensters in der Bibliothek ist nicht das,
was der Bildschirm zeigt.

## Grenzen

- PowerBuilder 5 bis 9 (ANSI-Bibliotheken, Kopf `HDR*PowerBuilder`, Version `0500` bis `0900`).
  PowerBuilder 10 und später speichern Unicode-Bibliotheken; sie werden erkannt und mit einer
  Meldung abgelehnt.
- Der Reader findet Zeichenketten. Er dekompiliert keine Skripte und sagt nicht, was ein Skript
  mit einer Anweisung macht. Fenstertitel und Positionen von Steuerelementen werden nicht gelesen;
  für das Aussehen einer Maske dient die Aufnahme.
- Verweise werden über Namen gefunden. Ein Name, der erst zur Laufzeit gebildet wird, wird nicht
  gefunden.
- Die Rohdaten der Objekte können Texte der Anwendung enthalten, darunter Namen und Adressen, die
  in ein Skript getippt wurden. Den Ergebnisordner so sorgfältig behandeln wie die Bibliotheken;
  `--no-raw` lässt die Rohdaten weg, aber nicht die Zeichenketten und die SQL-Anweisungen.

## Tests

Die Tests bauen selbst eine synthetische Bibliothek im Aufbau von PowerBuilder 6 (Objekte über
mehrere Datenblöcke, SQL, `PBSELECT`, Beschriftungen, ein Menü, Windows-1252-Text) und prüfen das
Ergebnis, die Ablehnung von Unicode-Bibliotheken, die Reihenfolge der Bibliotheken und den Teil
`static` der Spezifikation:
`ReaderTests.PowerBuilderReaderReadsObjectsSqlLabelsMenusAndReferences` und die Tests daneben.
Keine Bibliothek einer echten Anwendung gehört zum Repository, und keine wurde in diesen Tests
benutzt.
