<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](../manual.md) · [Deutsch](manual.md)

# Handbuch

`eqlmaker manual` übernimmt das Handbuch der Originalanwendung als Kontext des Projekts: seinen
Text und seine Kapitel. Die Spezifikation nennt dann zu jedem Schritt und jeder Maske die
Kapitel, die dazugehören.

```text
eqlmaker manual <file> [--name <name>]
eqlmaker spec
```

Führen Sie `spec` danach erneut aus; es bildet die Verknüpfungen. Es lassen sich mehrere
Handbücher übernehmen, jedes unter eigenem Namen (Vorgabe: der Dateiname).

## Formate

| Format | Wie Kapitel gefunden werden |
|---|---|
| Reiner Text (`.txt`) | nummerierte Überschriften (`2.1 Invoices`), unterstrichene Überschriften, Überschriften in Großbuchstaben |
| Markdown (`.md`) | Überschriften mit `#` |
| HTML (`.html`, `.htm`) | `h1` bis `h6` |
| Word (`.docx`) | Überschriftenformate und Gliederungsebenen der Absätze |
| PDF (`.pdf`) | Überschriften nach Schriftgröße, siehe unten |

Es wird kein weiteres Programm gebraucht: Die Leser für DOCX und PDF sind Teil von EQLMaker.

## Was herausgelesen wird

`manual/<name>.json`:

| Schlüssel | Inhalt |
|---|---|
| `format`, `version` | `eqlmaker-manual` und die Formatversion |
| `name`, `title` | Name im Projekt; Titel des Dokuments |
| `source_file`, `source_type` | Dateiname und Format des Originals |
| `characters` | Länge des Textes |
| `chapters` | Liste der Kapitel: `id` (`c1`, `c2`, ...), `level`, `title`, `page` (nur PDF), `text` |
| `warnings` | was nicht gelesen werden konnte |

`manual/<name>.txt` ist der ganze Text, zum Lesen und für die geführten Schritte (`generate`
kopiert ihn nach `web/spec/`).

Text vor der ersten Überschrift wird ein Kapitel mit dem Titel `(start)`.

Bilder, Tabellen als Tabellen und das Layout des Handbuchs werden nicht herausgelesen. Nur Text
und Überschriften.

## Wie Kapitel verknüpft werden

`spec` vergleicht die Wörter eines Kapitels mit den Wörtern eines Schritts und seiner Masken.
Ein gemeinsames Wort in der Kapitelüberschrift zählt 3 Punkte, im Kapiteltext 1; eine
Verknüpfung braucht mindestens 3 Punkte. Die Regel ist fest und ergibt für dieselbe Eingabe
dieselben Verknüpfungen. Ein Handbuch in einer anderen Sprache als die Oberfläche der Anwendung
erhält deshalb wenige Verknüpfungen.

## Der PDF-Leser

Der eingebaute Leser verarbeitet, was Text-PDFs üblicherweise enthalten:

- komprimierte Inhaltsströme (Flate) und Objektströme;
- Zeichentabellen (`ToUnicode`) und die Standardkodierungen;
- Zeichenbreiten, um zu erkennen, wo ein Wort endet;
- Überschriften nach Schriftgröße: Text, der größer gesetzt ist als der Fließtext, wird eine
  Überschrift, je größer, desto höher die Ebene.

Seine Grenzen:

- **Verschlüsselte PDFs werden abgelehnt.** Entfernen Sie zuerst den Schutz, wenn Sie das dürfen.
- **Gescannte PDFs ergeben keinen Text.** Ein Scan enthält Bilder von Seiten. Das Ergebnis trägt
  eine Warnung; EQLMaker hat keine Texterkennung für Bilder.
- **Schriften ohne Zeichentabelle** lassen sich nicht lesen; das Ergebnis trägt für solchen Text
  eine Warnung.
- Text in Spalten, in Tabellen oder entlang eines Pfads kann in einer anderen Reihenfolge
  herauskommen, als ein Mensch erwartet.
- Überschriften, die nicht größer sind als der Fließtext, werden nicht gefunden. Ergibt ein PDF
  wenige Kapitel, exportieren Sie das Handbuch als DOCX oder HTML, wenn Sie die Quelle haben.

Wie es getestet ist: mit PDFs, die für die Tests gebaut werden (komprimiert, mit Zeichentabelle
und Objektstrom; mit Standardschrift und Überschriften nach Größe), und mit einem PDF, das ein
Browser gedruckt hat. Öffnen Sie nach einem Import `manual/<name>.txt` und prüfen Sie, ob der
Text dem entspricht, was Sie erwarten.
