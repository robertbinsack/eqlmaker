<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](../timestamp-overlay.md) · [Deutsch](timestamp-overlay.md)

# Zeitstempel-Leiste

`eqlmaker-timestamp.exe` zeigt eine Leiste mit dem aktuellen Datum und der Uhrzeit, auf die
Millisekunde genau, über allen Fenstern. Starten Sie sie, bevor Sie einen Bildschirmrekorder
starten. Jedes Bild des Videos trägt dann die Zeit, zu der es entstand, und
[`eqlmaker video-import`](video-import.md) kann das Video später den Datenbankereignissen dieses
Moments zuordnen, auch wenn das Video geschnitten wurde.

```text
eqlmaker-timestamp.exe
eqlmaker-timestamp.exe --position bottom-left --cell 6
eqlmaker-timestamp.exe --display all
eqlmaker-timestamp.exe --stop
```

| Option | Bedeutung |
|---|---|
| `--position` | `top-right` (Vorgabe), `top-left`, `bottom-right`, `bottom-left` oder `x,y` auf dem Bildschirm |
| `--display` | Nummer des Bildschirms (Vorgabe 1) oder `all` für eine Leiste auf jedem Bildschirm |
| `--cell` | Größe einer Zelle des Musters in Bildpunkten (Vorgabe 8: Die Leiste ist 872 x 72 Bildpunkte groß) |
| `--opacity` | 0.1 bis 1 (Vorgabe 1); für Aufnahmen bei 1 lassen |
| `--lang` | `en` oder `de` für die kleine Textzeile |
| `--seconds` | nach so vielen Sekunden enden |
| `--stop` | eine laufende Leiste beenden |
| `--render file.png` | eine Leiste in eine Datei zeichnen, statt ein Fenster zu zeigen; mit `--at <UTC milliseconds>`, `--offset <minutes>`, `--mono <milliseconds>` |

Die Leiste nimmt die Maus nicht an: Klicks gehen an das Fenster darunter. Sie hat eine
Schaltfläche in der Taskleiste; schließen Sie sie dort oder mit `--stop`.

## Was die Leiste zeigt

```text
+----------------------------------+-----------------------------------------------+
| [#] data cells 16 x 7        [#] |  2026-11-24 18:20:31.123 +0100                |
|                                  |  UTC+01:00   monotonic 12.345 s   display 1   |
+----------------------------------+-----------------------------------------------+
   pattern, read by the program        for people; the digits are the fallback
```

- **Datum und Uhrzeit** dieses Rechners mit Millisekunden und dem Versatz zu UTC, in großen
  Ziffern aus sieben Segmenten.
- **Monotone Zeit**: Sekunden seit dem Start der Leiste. Sie springt nie, sodass eine Uhr, die
  während der Aufnahme gestellt wurde, auffällt.
- **Bildschirm**: die Nummer des Bildschirms, auf dem die Leiste liegt.
- **Muster**: dieselben Werte als schwarze und weiße Zellen, mit einer Prüfsumme.

## Das Muster (Version 1)

Die Leiste ist 109 Zellen breit und 9 Zellen hoch. Die linken 34 Zellen sind ein weißes Feld.
Darin stehen, eine Zelle vom Rand, zwei Suchquadrate von 7 x 7 Zellen (schwarzer Rahmen, weißer
Ring, schwarze Mitte von 3 x 3), deren Mitten 25 Zellen auseinanderliegen, und zwischen ihnen
16 x 7 Datenzellen.

Die Datenzellen enthalten 112 Bits, Zeile für Zeile, das höchstwertige Bit zuerst; eine schwarze
Zelle ist 1. Jedes Bit ist dort umgekehrt, wo Zeile plus Spalte gerade ist, damit keine große
Fläche eine Farbe hat.

| Bits | Inhalt |
|---|---|
| 4 | Version (1) |
| 44 | Uhrzeit: Millisekunden seit 1970-01-01 UTC |
| 11 | Versatz der Ortszeit zu UTC in Minuten, plus 1024 |
| 32 | monotone Millisekunden seit dem Start der Leiste |
| 3 | Nummer des Bildschirms |
| 2 | reserviert (0) |
| 16 | CRC-16 (Polynom 0x1021, Startwert 0xFFFF) über die 96 Bits davor |

Die Ziffern beginnen 35 Zellen rechts vom linken Rand des linken Suchquadrats, eine Ziffer alle
2.5 Zellen; eine Ziffer ist 2 Zellen breit und 3.5 Zellen hoch. Der Text ist
`yyyy-MM-dd HH:mm:ss.fff +HHMM`.

## Wie gelesen wird

Der Leser sucht die beiden Suchquadrate an beliebiger Stelle eines Bildes, in beliebigem
Maßstab, nimmt die Zellgröße aus ihrem Abstand und liest die Datenzellen. Stimmt die Prüfsumme
nicht (das Muster ist zu klein, unscharf oder teilweise verdeckt), liest er stattdessen die
Sieben-Segment-Ziffern an ihren bekannten Stellen. Eine solche Lesung hat die Uhrzeit und den
UTC-Versatz, aber keine monotone Zeit. Es ist keine allgemeine Texterkennung beteiligt; der
Rückfall liest genau diese Ziffern.

Gemessen in den Tests: Das Muster wird aus Bildern von 1920 x 1080 gelesen, die auf ein Drittel
verkleinert und als JPEG mit Qualität 35 komprimiert sind; die Ziffern werden gelesen, wenn
Datenzellen verdeckt sind.

## Genauigkeit

Die Leiste wird alle 2 ms neu gezeichnet. Ein Bildschirm zeigt ein neues Bild in seiner
Bildwiederholrate, und ein Bildschirmrekorder nimmt sein Bild einige Zeit danach: Die Zeit in
einem Bild ist deshalb einige Millisekunden alt, bis zu etwa einem Wiederholintervall (17 ms bei
60 Hz) plus 2 ms. `video-import` korrigiert das nicht; die Zahlen stehen unter
[Video-Import](video-import.md).

Halten Sie die Leiste während der ganzen Aufnahme sichtbar. Ein Bild, in dem sie verdeckt ist,
hat keine eigene Zeit; die Bilder davor und danach tragen die Abbildung.
