<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
<!-- Copyright (c) 2026 Robert Binsack -->
[English](../setup.md) · [Deutsch](setup.md)

# Das Windows-Paket einrichten

Das Windows-Paket ist eine Zip-Datei. Entpacken Sie sie an beliebiger Stelle, dann führen Sie
`eqlmaker-setup.cmd` im entpackten Ordner aus (Doppelklick oder aus einer Eingabeaufforderung).
Die Batchdatei lädt herunter, was noch fehlt, prüft es, entpackt es in denselben Ordner und
sorgt dafür, dass dort alles funktioniert.

- Es läuft kein Installationsprogramm, es sind keine Adminrechte nötig, es erscheint keine
  Rückfrage von Windows.
- Nichts wird außerhalb des Ordners geschrieben: keine Registry-Einträge, keine Änderung von
  `PATH`, kein Windows-Dienst, keine Firewall-Regel, keine geplante Aufgabe.
- Wieder entfernen: `eqlmaker-db-stop.cmd` ausführen, dann den Ordner löschen.

## Voraussetzungen

- Windows 10 (ab Version 1803), Windows 11 oder Windows Server ab 2019, 64 Bit (x64).
- Windows PowerShell 5.1, `curl.exe` und `tar.exe`, die zu diesen Windows-Versionen gehören.
- Ein Prozessor mit AVX2 für die Spracherkennung (Intel ab 2013, AMD ab 2015).
- Etwa 1.1 GB freier Plattenplatz mit dem portablen PostgreSQL (etwa 0.6 GB ohne) und eine
  Internetverbindung für den ersten Lauf (etwa 455 MB Downloads mit PostgreSQL, etwa 118 MB
  ohne), sofern Sie nicht `-offline` verwenden. Die heruntergeladenen Archive bleiben für
  spätere Läufe in `downloads\`; sie dürfen gelöscht werden, wenn die Einrichtung abgeschlossen
  ist.
- Für das portable PostgreSQL: die Laufzeitbibliotheken von Microsoft Visual C++, siehe
  [Portables PostgreSQL](#portables-postgresql).

## Die Einrichtung ausführen

```
eqlmaker-setup.cmd [options]
```

| Option | Bedeutung |
|---|---|
| `-unattended` | Keine Fragen und keine Pause am Ende; Vorgaben für alles. Für Skripte. |
| `-offline` | Gar kein Netzzugriff. Verwendet die Archive in `downloads\`. |
| `-lang en` / `-lang de` | Sprache der Meldungen. Vorgabe: die Sprache von Windows, sonst Englisch. |
| `-model <name>` | Lädt ein größeres Spracherkennungsmodell herunter und macht es zur Vorgabe. |
| `-no-postgres` | Installiert das portable PostgreSQL nicht; Sie verwenden Ihren eigenen Server. |
| `-no-selftest` | Führt den Selbsttest am Ende nicht aus. |
| `-help` | Zeigt die Optionen. |

Alle Optionen lassen sich kombinieren, auch mit `-unattended`.

Ohne `-unattended` stellt die Einrichtung eine Frage, und nur beim ersten Lauf: ob das portable
PostgreSQL installiert werden soll (Vorgabe: ja). Nach einem Start per Doppelklick bleibt das
Fenster am Ende offen, bis eine Taste gedrückt wird.

### Rückgabewerte

| Wert | Bedeutung |
|---|---|
| 0 | Bereit. Auch dann, wenn alles schon eingerichtet war und der Lauf es nur geprüft hat. |
| 1 | Fehlgeschlagen. Der Grund ist die Zeile, die mit `SETUP FAILED:` beginnt. |
| 2 | Alles wurde eingerichtet, aber der Selbsttest ist fehlgeschlagen. |
| 3 | `-offline` war angegeben, und Archive fehlen oder sind beschädigt. Nichts wurde geändert. Die Ausgabe nennt jede fehlende Datei, woher man sie bekommt und wohin sie gehört. |

Die vollständige Ausgabe jedes Laufs wird außerdem an `downloads\setup.log` angehängt.

### Was die Einrichtung tut

1. Prüft die im Paket mitgelieferte Spracherkennung gegen `MANIFEST.json`.
2. Stellt sicher, dass die Archive von FFmpeg, Node.js und (wahlweise) PostgreSQL in
   `downloads\` liegen: lädt sie über HTTPS herunter, setzt einen unterbrochenen Download fort
   und vergleicht den SHA-256 jedes Archivs mit `setup\components.json`. Ein Archiv mit falscher
   Prüfsumme wird ein weiteres Mal heruntergeladen und dann abgelehnt. Nichts wird entpackt,
   bevor alle Archive geprüft sind.
3. Entpackt die Archive. Jeder Eintrag wird geprüft, bevor die erste Datei geschrieben wird; ein
   Archiv mit einem Eintrag, der außerhalb seines Zielordners landen würde, wird abgelehnt.
4. Installiert die Pakete der Browser-Variante der Beispielanwendung mit dem npm des Pakets, aus
   den Paketarchiven, die in `downloads\npm` mitgeliefert werden (kein Netzzugriff).
5. Legt den portablen Datenbankcluster an, startet den Server und lädt die Beispieldatenbank.
6. Schreibt `project\eqlmaker.json`, die fertige Projektkonfiguration für das Beispiel.
7. Führt den Selbsttest aus: Ein von einer Windows-Stimme gesprochener Satz wird von der
   Spracherkennung erkannt (Englisch und Deutsch, wenn Windows solche Stimmen hat), dann
   `bin\eqlmaker.exe selftest --project <folder>\project`.
8. Gibt eine Zusammenfassung aus: was wo liegt, Versionen, Port, wie man startet und wie man
   entfernt.

Ein zweiter Lauf prüft nur. Er braucht keinen Netzzugriff.

## Aufbau des Ordners

```
eqlmaker-setup.cmd            the setup
eqlmaker.cmd                  starts bin\eqlmaker.exe with the arguments given
eqlmaker-npm.cmd              npm of the package, with the cache inside the package
eqlmaker-db-start.cmd         starts the portable PostgreSQL
eqlmaker-db-stop.cmd          stops it
LICENSE, THIRD_PARTY_NOTICES.md, README.md, MANIFEST.json
bin\                          the programs and share\
docs\, examples\
setup\                        setup script, components.json, message texts, COMPONENTS.md
tools\whisper\                speech recognition: whisper-cli.exe, models\, model.txt   (in the zip)
tools\ffmpeg\bin\             ffmpeg.exe, ffprobe.exe and their libraries                (downloaded)
tools\node\                   node.exe, npm                                              (downloaded)
tools\pgsql\                  PostgreSQL programs                                        (downloaded, optional)
data\pg\                      database cluster of the portable PostgreSQL
data\pg-port.txt              its port
data\pgpass.conf              its passwords (password file format of PostgreSQL)
downloads\                    downloaded archives, npm\ (package archives), npm-cache\, setup.log
project\eqlmaker.json         project configuration for the sample
```

Die Drittkomponenten, ihre Versionen, Quellen und Lizenzen stehen in `setup\COMPONENTS.md`.

## Ohne Internetzugang

Führen Sie die Einrichtung einmal auf einem Rechner mit Internetzugang aus und kopieren Sie den
ganzen Ordner, oder kopieren Sie nur die Archive: Legen Sie die in `setup\components.json`
genannten Dateien nach `downloads\` und führen Sie aus

```
eqlmaker-setup.cmd -offline
```

Fehlt etwas, endet die Einrichtung mit Rückgabewert 3, bevor sie etwas ändert, und nennt jede
fehlende Datei mit ihrer Adresse, ihrem Zielpfad und ihrer Prüfsumme.

## Proxy

Downloads nutzen den Proxy der Umgebungsvariablen `HTTPS_PROXY`, `ALL_PROXY` oder `HTTP_PROXY`,
wenn eine davon gesetzt ist, sonst den in Windows eingerichteten Proxy. Beispiel:

```
set HTTPS_PROXY=http://proxy.example.org:3128
eqlmaker-setup.cmd
```

Es werden nur HTTPS-Adressen verwendet, Umleitungen auf andere Protokolle werden abgelehnt.

## Spracherkennung

Das Paket enthält whisper.cpp (`tools\whisper\whisper-cli.exe`) und ein mehrsprachiges Modell.
Es läuft auf dem Prozessor, braucht keine Grafikkarte und keinen Netzzugriff.
`tools\whisper\model.txt` enthält den Dateinamen des Vorgabemodells in `tools\whisper\models\`.

Größere Modelle erkennen genauer und sind langsamer. `-model <name>` lädt eines herunter und
macht es zur Vorgabe; `-model small-q5_1` schaltet zurück:

| Name | Größe | Bemerkung |
|---|---|---|
| `base` | 141 MB | am schnellsten, im Deutschen deutlich mehr Fehler |
| `small-q5_1` | 181 MB | mitgelieferte Vorgabe |
| `small` | 465 MB | |
| `medium-q5_0` | 514 MB | |
| `large-v3-turbo-q5_0` | 547 MB | bestes Deutsch in unserer Messung, etwa ein Viertel der Geschwindigkeit der Vorgabe |
| `large-v3-turbo-q8_0` | 834 MB | |

Wie EQLMaker es aufruft (der Ton wird zuerst mit FFmpeg in 16 kHz mono WAV umgewandelt):

```
working directory: <folder>\tools\whisper
whisper-cli.exe -m models\<model> -f <audio.wav> -l <en|de|auto> -t <threads> -oj -of <output path without extension> -np
```

Das Modell wird absichtlich relativ zum Arbeitsverzeichnis angegeben: `whisper-cli.exe` endet
mit einem Fehler, wenn der vollständige Pfad des Modells Zeichen außerhalb von ASCII hat (zum
Beispiel einen Umlaut im Ordnernamen). Die Pfade der Audiodatei und der Ausgabe dürfen solche
Zeichen haben, solange es sie in der ANSI-Codepage des Rechners gibt.

Das Ergebnis ist `<output path>.json` (UTF-8). `transcription` ist die Liste der Segmente;
`offsets.from` und `offsets.to` sind Millisekunden ab dem Beginn des Tons, `timestamps` enthält
dieselben Zeiten als Text (`hh:mm:ss,mmm`), `text` beginnt mit einem Leerzeichen.
`result.language` ist die verwendete oder erkannte Sprache. Mit `-ojf` statt `-oj` hat jedes
Segment zusätzlich `tokens` mit eigenen Zeiten.

`setup\make-test-audio.ps1` erzeugt Testton mit einer Sprachsynthese-Stimme von Windows
(`-Text`, `-Language en|de`, `-OutFile`); es endet mit Rückgabewert 3 und einer Meldung, wenn
Windows keine Stimme für die Sprache hat. Es wird kein aufgenommener Ton mitgeliefert.

## Portables PostgreSQL

Sofern nicht `-no-postgres` angegeben ist, entpackt die Einrichtung die Programme von
PostgreSQL 18 nach `tools\pgsql` und legt einen Cluster in `data\pg` an:

- Er lauscht nur auf `127.0.0.1`. Der Port ist der erste freie ab 55432 und wird nach
  `data\pg-port.txt` geschrieben.
- Anmeldungen brauchen ein Passwort (`scram-sha-256`). Das Passwort des Benutzers `postgres` und
  das des Beispielbenutzers `sample_app` werden auf Ihrem Rechner zufällig erzeugt und nach
  `data\pgpass.conf` geschrieben, das nur Ihr Windows-Benutzer lesen kann. Sie werden nicht
  angezeigt und nicht protokolliert.
- `wal_level = logical` und `logging_collector = on` sind gesetzt; das Serverprotokoll liegt in
  `data\pg\log`.
- Er ist kein Windows-Dienst. `eqlmaker-db-start.cmd` startet ihn, `eqlmaker-db-stop.cmd`
  beendet ihn. Er läuft weiter, wenn das Fenster geschlossen wird, und endet, wenn Sie sich
  abmelden. Nach der Einrichtung läuft er.
- Die Beispieldatenbank `eqlmaker_sample` wird von `examples\sample-app\db\setup.ps1` angelegt.

Um sich mit einem eigenen Werkzeug zu verbinden:

```
set PGPASSFILE=<folder>\data\pgpass.conf
tools\pgsql\bin\psql.exe -h 127.0.0.1 -p <port> -U postgres
```

**Laufzeit von Microsoft Visual C++.** Die PostgreSQL-Programme von EnterpriseDB brauchen
`vcruntime140.dll`, `vcruntime140_1.dll` und `msvcp140.dll`, und das Archiv enthält sie nicht.
Auf den meisten Rechnern sind sie vorhanden, weil viele Programme sie installieren. Fehlen sie,
sagt die Einrichtung das, bevor sie etwas herunterlädt. Dann installiert entweder Ihre
IT-Betreuung das „Microsoft Visual C++ Redistributable (x64)“, oder Sie verwenden mit
`-no-postgres` Ihr eigenes PostgreSQL, oder Sie verwenden ein Paket, das mit `-IncludeVcRuntime`
gebaut wurde: Es bringt die drei Bibliotheken in `tools\vcruntime` mit, und die Einrichtung
kopiert sie neben die PostgreSQL-Programme. Die übrigen Teile des Pakets (EQLMaker,
Spracherkennung, FFmpeg, Node.js) brauchen diese Bibliotheken nicht.

### Ihr eigenes PostgreSQL

Mit `-no-postgres` wird nichts von PostgreSQL heruntergeladen. Beim ersten Lauf schreibt die
Einrichtung `project\eqlmaker.json` als Vorlage; tragen Sie dort Ihre Verbindung ein. Die
Beispieldatenbank lässt sich mit `examples\sample-app\db\setup.ps1` laden (siehe
`examples\sample-app\README.md`). Führen Sie die Einrichtung danach erneut aus, um den
Selbsttest zu erhalten. Eine vorhandene `project\eqlmaker.json` wird nie überschrieben.

## npm des Pakets

`eqlmaker-npm.cmd <arguments>` führt das npm aus `tools\node` im aktuellen Ordner aus, mit dem
Cache in `downloads\npm-cache`. Der Cache enthält die Pakete des Beispiels; im Ordner einer
erzeugten Webanwendung, die dieselben Versionen verwendet, funktioniert das deshalb ohne
Netzzugriff:

```
<folder>\eqlmaker-npm.cmd ci --offline
```

## Grenzen

- **Länge des Pfads.** Der Pfad des Paketordners darf höchstens 140 Zeichen haben; die
  Einrichtung lehnt einen längeren ab. (Die längste Datei der heruntergeladenen Komponenten hat
  dann einen Pfad von 259 Zeichen, die Grenze von Windows PowerShell.)
- **Zeichen im Pfad.** Leerzeichen und Umlaute sind in Ordnung. PostgreSQL selbst kann unterhalb
  eines Pfads mit Zeichen außerhalb von ASCII nicht arbeiten; für einen solchen Ordner führen
  die Einrichtung und die Batchdateien zum Starten und Beenden es über den Kurznamen (8.3) aus,
  den Windows für den Ordner führt. Auf einem Laufwerk, auf dem Kurznamen abgeschaltet sind,
  sagt die Einrichtung das und endet, bevor sie etwas herunterlädt; verwenden Sie dann einen
  Pfad mit einfachen Buchstaben oder `-no-postgres`. Vermeiden Sie `%`, `!`, `^` und `&` im
  Pfad: Die Batchdateien von Windows gehen damit nicht verlässlich um.
- **Temporäre Dateien.** Die Einrichtung und die Programme, die sie startet, verwenden
  `downloads\tmp` innerhalb des Ordners und leeren es am Ende. Bekannte Ausnahmen außerhalb des
  Ordners: Windows PowerShell kann seine eigenen Start-Caches im Benutzerprofil aktualisieren,
  und die für den Selbsttest verwendete Sprachsynthese von Windows kann dasselbe tun. Nichts von
  EQLMaker wird dort gespeichert.
- **Ein Benutzer.** Der Ordner gehört dem Windows-Benutzer, der die Einrichtung ausgeführt hat;
  die Passwortdatei ist nur für diesen Benutzer lesbar.

## Das Paket bauen

`scripts\pack-release.ps1` baut `out\eqlmaker-windows-x64.zip` (mit
`eqlmaker-windows-x64.zip.sha256` und `eqlmaker-version.json`) aus dem Repository; siehe den
Kommentar am Anfang dieses Skripts. Dateien Dritter werden mit festgelegten Prüfsummen in einen
Cache-Ordner geholt und gelangen nie ins Repository. Die Tests des Pakets liegen in
`scripts\setup\tests`.
