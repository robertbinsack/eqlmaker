<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](../video-import.md) · [Deutsch](video-import.md)

# Video-Import und Zeitabgleich

Jemand, der die alte Anwendung kennt, nimmt beim Arbeiten ein Video auf und erklärt, was er tut.
Das Video kommt Tage später an. `eqlmaker video-import` findet heraus, welche Datenbankereignisse
zu welchem Moment des Videos gehören, und sammelt je Vorgang, was gesagt wurde, was zu sehen war
und was die Datenbank tat, das die Spezifikation noch nicht enthält.

```text
eqlmaker-timestamp.exe                                 (before the screen recorder starts)
eqlmaker record --start --no-video --name session      (captures the database events meanwhile)
   ... work with the application, record the screen with any recorder ...
eqlmaker record --stop
eqlmaker video-import session.mp4 --recordings session
```

Das Video muss die Leiste der [Zeitstempel-Leiste](timestamp-overlay.md) zeigen. Die
Datenbankereignisse können auch aus einem Serverprotokoll stammen, das damals geschrieben wurde,
siehe `import-log` unten.

| Option | Bedeutung |
|---|---|
| `--recordings a,b` | Aufnahmen, deren Ereignisse zum Video gehören (Vorgabe: alle des Projekts) |
| `--transcript file` | dieses Transkript verwenden (WebVTT, SRT oder das JSON des Spracherkennungsprogramms), statt die Sprache zu erkennen |
| `--language de` | Sprache des Gesprochenen (Vorgabe: erkannt) |
| `--no-transcript` | die Tonspur überspringen |
| `--clock-offset-ms n` | Uhr des Datenbankservers minus Uhr des Aufnahmerechners, wenn Sie sie kennen |
| `--estimate-offset` | diesen Unterschied aus den Daten schätzen, auch wenn ein gemessener Wert vorliegt |
| `--sample-fps 2` | wie viele Bilder pro Sekunde für die Zeitabbildung gelesen werden |
| `--max-keyframes 120` | Obergrenze der behaltenen Schlüsselbilder |
| `--attach-seconds 8` | wie weit ein Satz oder Schlüsselbild zeitlich von einem Vorgang entfernt sein darf, um ihm zugeordnet zu werden |
| `--name n` | Name des Ergebnisordners (Vorgabe: Dateiname des Videos) |

Ergebnis: `findings/<name>/findings.json` und `findings/<name>/keyframes/*.png`.

## Was geschieht

1. **Zeit der Bilder.** FFmpeg dekodiert eine Stichprobe der Bilder mit ihren genauen
   Anzeigezeiten. Aus jedem wird der Zeitstempel gelesen: zuerst das Muster, als Rückfall die
   Ziffern.
2. **Uhrenabbildung.** Die Videozeit wird je ungeschnittenem Abschnitt durch eine Gerade auf die
   Uhrzeit des Aufnahmerechners abgebildet. Die Steigung nimmt die Drift zwischen dem Bildtakt
   des Rekorders und der Uhr auf. Ein Sprung zwischen zwei benachbarten Stichproben, den der
   Bildabstand nicht erklären kann (mehr als 0.25 s), ist ein Schnitt; dann wird jedes Bild um
   den Schnitt gelesen, um ihn genau zu setzen. Eine einzelne Lesung, die beiden Nachbarn
   widerspricht, wird verworfen.
3. **Schlüsselbilder und Momente der Änderung.** Ein Schlüsselbild ist ein Bild, nachdem sich
   das Bild geändert hat und zur Ruhe gekommen ist. Die Leiste selbst bleibt beim Vergleich
   außen vor.
4. **Transkript.** Die Tonspur wird in 16 kHz mono umgewandelt und vom Spracherkennungsprogramm
   des Pakets (whisper.cpp) transkribiert, offline. Jeder Satz erhält seine Videozeit.
5. **Ereignisse und Vorgänge.** Die Ereignisse der genannten Aufnahmen werden zu Vorgängen
   gruppiert: Ein neuer Vorgang beginnt nach zwei Sekunden ohne Datenbankaktivität. Jeder
   Vorgang wird einem Schritt der Spezifikation zugeordnet, über das Kapitel seiner Aufnahme
   oder über die Anweisungen, die er mit dem Schritt gemeinsam hat (mindestens 34 %
   Überdeckung).
6. **Uhr des Servers.** Ereignisse tragen die Zeit des Datenbankservers. Der Unterschied zur Uhr
   des Aufnahmerechners ist, in dieser Reihenfolge: der mit `--clock-offset-ms` angegebene Wert;
   der Wert, der beim Erfassen der Ereignisse gemessen wurde (richtig, wenn die Erfassung auf
   dem Aufnahmerechner lief); eine Schätzung aus den Daten: die Verschiebung, bei der die
   meisten Momente der Änderung auf dem Bildschirm auf einen Beginn von Datenbankaktivität
   folgen. Die Schätzung wird mit einem Vertrauenswert gemeldet und hinkt um die Zeit nach, die
   die Anwendung zum Anzeigen eines Ergebnisses braucht.
7. **Befunde.** Jeder Satz und jedes Schlüsselbild geht zum zeitlich nächsten Vorgang oder
   bleibt ohne Zuordnung. Zu einem Satz werden die Wörter aufgeführt, die in der Spezifikation
   des zugeordneten Schritts nicht vorkommen. Anweisungen, deren Form nicht in der Spezifikation
   steht, werden ebenfalls aufgeführt.

Befunde sind Kandidaten mit dem Status `open`. EQLMaker entscheidet nicht, was sie bedeuten; das
ist der geführte Schritt [`enrich`](guided-steps.md).

## Genauigkeit

Zahlen aus den Tests des Repositorys (`tests/ClockMapTests.cs`, `tests/VideoImportTests.cs`).
Fehler sind die abgebildete Zeit minus die wahre Zeit.

| Fall | Fehler |
|---|---|
| Echte Videodateien, von FFmpeg mit 15, 30 und 60 Bildern pro Sekunde kodiert, Drift 200 ppm, ein Schnitt von 7.3 s | Abbildung: höchstens 0.6 ms; Position des Schnitts: bildgenau |
| Dasselbe, Muster beschädigt, über die Ziffern gelesen | höchstens 0.6 ms |
| Nachgestellte Aufnahme mit der Verzögerung eines 60-Hz-Bildschirms und dem Zeichenintervall der Leiste, 15/30/60 Bilder pro Sekunde, Drift 80 ppm | Mittel -9.9 ms, höchstens 10.4 ms |
| Nachgestellt, starke Drift (1500 ppm über 10 Minuten) | höchstens 11.1 ms |
| Nachgestellt, drei Schnitte (15.5 s, 0.9 s, mehr als eine Stunde) | höchstens 19.5 ms |
| Uhr des Aufnahmerechners um 42.5 s falsch, nichts gemessen, aus fünf Vorgängen geschätzt | -200 bis -333 ms (die Anwendung im Test zeigt ihr Ergebnis 200 ms nach der Anweisung; Bilder werden alle 500 ms abgetastet) |
| Schätzung aus 45 Vorgängen zwischen unzusammenhängenden Bildschirmänderungen und Anweisungen | -139 bis -191 ms bei einer mittleren Bildschirmverzögerung von 196 ms |

So sind diese Zahlen zu lesen: Die Zeit in einem Bild ist einige Millisekunden alt (siehe
[Zeitstempel-Leiste](timestamp-overlay.md)), abgebildete Zeiten liegen auf einem echten
Bildschirm also etwa 10 ms zu früh. Ein geschätzter Uhrenunterschied stimmt auf einige
Zehntelsekunden; das genügt, um Sätze Vorgängen zuzuordnen, die Sekunden auseinanderliegen, und
genügt nicht, um einzelne Anweisungen zu ordnen. Messen Sie den Unterschied, wenn Sie können:

```text
eqlmaker clock
```

auf dem Aufnahmerechner ausgeführt, gibt die Uhr des Servers minus die Uhr dieses Rechners aus.

## Ereignisse aus einem früher geschriebenen Serverprotokoll

Lief keine Erfassung, hat der Server damals aber Anweisungen protokolliert (`log_statement`),
machen Sie aus dem Protokoll eine Aufnahme, die nur Ereignisse enthält:

```text
eqlmaker import-log postgresql.log --name session --prefix "%m [%p] " --utc-offset-minutes 60
```

Eine solche Aufnahme hat Anweisungen und Angaben zur Sitzung, soweit das Präfix der
Protokollzeilen sie hat. Sie hat keine geänderten Zeilen und keine Bedienhandlungen.

## Was in findings.json steht

`source` (das Video), `readings` (Bilder, die über das Muster gelesen wurden, über die Ziffern,
nicht lesbar; wo die Leiste war), `clock` (Abschnitte mit Rate, Drift und Restfehler; Schnitte),
`clock_offset` (Wert, Methode, Schätzung mit Vertrauenswert), `transcript`, `keyframes`,
`screen_changes`, `operations` (jeder mit dem zugeordneten Schritt, den Zeiten in Video und
Uhrzeit, dem, was die Datenbank tat, den Kennungen dessen, was gesagt und gesehen wurde, und
`findings`), `unassigned`, `warnings`.

## Grenzen

- Die Wörter in einem Schlüsselbild werden nicht herausgelesen; die Bilder werden als Bilder
  übergeben.
- Das Transkript ist so gut wie das Sprachmodell; Namen und Fachbegriffe sind oft falsch.
- Ohne die Leiste im Bild gibt es keinen Zeitabgleich.
- Personenbezogene Daten in Video, Ton und Transkript werden nicht maskiert.
