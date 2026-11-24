<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](../configuration.md) · [Deutsch](configuration.md)

# Konfiguration

Ein Projekt ist ein Ordner mit der Datei `eqlmaker.json` und einem Unterordner je Schritt des
Ablaufs. Diese Seite nennt jeden Schlüssel dieser Datei, woher Passwörter kommen, wo externe
Werkzeuge gesucht werden und wie die Sprache gewählt wird.

## Der Projektordner

`eqlmaker init [folder]` legt den Ordner und eine erste `eqlmaker.json` an. Jeder Befehl
arbeitet im aktuellen Ordner oder in dem Ordner, der mit `--project <folder>` genannt ist. Die
Schritte legen diese Unterordner an:

| Ordner | Geschrieben von |
|---|---|
| `recordings/<name>/` | `record`, `import-log` |
| `manual/` | `manual` |
| `spec/` | `spec` |
| `web/` | `generate` |
| `clone/` | `clone` |
| `video/<name>/` | `video` |
| `verify/<name>/` | `verify` |
| `findings/<name>/` | `video-import` |
| `enrich/<name>/` | `enrich` |
| `.eqlmaker/` | interner Zustand: eine laufende Aufnahme, das Browserprofil |

## eqlmaker.json

```json
{
  "source": "postgres",
  "connection": "host=127.0.0.1 port=5432 user=postgres dbname=postgres",
  "passfile": "",
  "capture": { "roles": [], "databases": ["appdb"], "changes": true, "logFile": "" },
  "eqlserver": { "statsPort": 0 },
  "input": "real",
  "lang": "en",
  "tools": { "ffmpeg": "", "transcribe": "", "transcribeModel": "", "browser": "", "node": "" },
  "plugins": ["plugins"],
  "recordings": "recordings",
  "verify": { "ignoreColumns": [], "keyColumns": ["*.id"], "references": [] }
}
```

| Schlüssel | Bedeutung | Vorgabe |
|---|---|---|
| `source` | Ereignisquelle: `postgres`, `eqlserver` oder der Name eines Quellen-Plugins. Siehe [Ereignisquellen](sources.md). | `postgres` |
| `connection` | Verbindung zum Datenbankserver, als Schlüsselwörter (`host=... port=... user=... dbname=...`) oder als `postgres://`-Adresse. Der Benutzer muss Superuser sein. `sslmode=require`, `verify-ca` oder `verify-full` schalten TLS ein. | – |
| `passfile` | eine Passwortdatei im pgpass-Format von PostgreSQL; ein relativer Pfad gilt relativ zum Projektordner | – |
| `capture.roles` | Anmelderollen, deren Anweisungen erfasst werden | keine |
| `capture.databases` | Datenbanken, deren Anweisungen und geänderte Zeilen erfasst werden. Rollen, Datenbanken oder beides nennen. | keine |
| `capture.changes` | geänderte Zeilen erfassen (braucht `wal_level = logical` auf dem Server) | `true` |
| `capture.logFile` | Pfad des Serverprotokolls, wenn der Server es nicht über den Logging Collector schreibt | – |
| `eqlserver.statsPort` | nur für die Quelle `eqlserver`: der Statistik-Port für die Anweisungsverfolgung; 0 = nicht benutzt. Ein Abschnitt mit dem Namen der Quelle enthält die Einstellungen dieser Quelle; ein Quellen-Plugin erhält seinen Abschnitt auf dieselbe Weise. | 0 |
| `input` | wie Programme bedient werden: `real` oder `background`. Siehe [Fernbedienung](remote-control.md). | `real` |
| `lang` | Sprache der Programmtexte: `en` oder `de` | siehe unten |
| `tools.ffmpeg` | Pfad von `ffmpeg.exe` | siehe unten |
| `tools.transcribe` | Pfad des Spracherkennungsprogramms `whisper-cli.exe` | siehe unten |
| `tools.transcribeModel` | Pfad des Sprachmodells | das Modell neben dem Erkennungsprogramm |
| `tools.browser` | Pfad von `msedge.exe` oder `chrome.exe` | die üblichen Installationsordner |
| `tools.node` | Pfad von `node.exe` | siehe unten |
| `plugins` | Ordner mit Plugins, relativ zum Projektordner oder absolut. Siehe [Plugins](plugins.md). | der Ordner `plugins` des Projekts, wenn es ihn gibt |
| `recordings` | Name des Ordners für Aufnahmen | `recordings` |
| `verify.ignoreColumns` | Spalten, die `verify db` auslässt | keine |
| `verify.keyColumns` | Schlüsselspalten, die `verify db` nach der Reihenfolge ihrer Entstehung vergleicht | `*.id` |
| `verify.references` | Spalten, die einen Schlüssel einer anderen Tabelle enthalten, als `table.column=table` | keine |

Die Optionen von `verify db` auf der Kommandozeile ersetzen die Werte von `verify`. Siehe
[Vergleichstests](verify.md).

## Passwörter

Schreiben Sie nie ein Passwort in `eqlmaker.json`. EQLMaker nimmt das Passwort der
Datenbankverbindung aus der ersten dieser Stellen, die eines hat:

1. `password=` in der Verbindungszeichenfolge (möglich, nicht empfohlen);
2. die Umgebungsvariable `EQLMAKER_PGPASSWORD`;
3. die mit `passfile` genannte Datei;
4. die Umgebungsvariable `PGPASSWORD`;
5. die Passwortdatei des Benutzers: die mit `PGPASSFILE` genannte Datei, sonst
   `%APPDATA%\postgresql\pgpass.conf`.

`EQLMAKER_PGPASSWORD` wird einmal gelesen und dann aus der Umgebung des EQLMaker-Prozesses
entfernt. Ein Programm, das EQLMaker danach startet, etwa die aufgezeichnete Anwendung, erbt sie
nicht.

Eine Passwortdatei hat eine Zeile je Server:

```text
127.0.0.1:5432:*:postgres:<password>
```

Das Windows-Paket schreibt eine solche Datei für sein portables PostgreSQL (`data\pgpass.conf`)
und nennt sie in der `eqlmaker.json` seines Beispielprojekts.

## Wo Werkzeuge gesucht werden

EQLMaker selbst braucht für `record`, `correlate`, `spec`, `manual` und `verify` kein weiteres
Programm. FFmpeg wird für `video-import` und für die zusammengesetzte Datei von `video`
gebraucht, das Spracherkennungsprogramm für das Transkript in `video-import`, Node.js für die
Webanwendungen, ein Browser für Webseiten.

Für `ffmpeg`, `transcribe` und `node` zählt der erste Treffer:

1. `tools.<name>` in `eqlmaker.json`;
2. die Umgebungsvariable `EQLMAKER_FFMPEG`, `EQLMAKER_TRANSCRIBE` oder `EQLMAKER_NODE`;
3. der Paketordner: `tools\ffmpeg\bin\ffmpeg.exe`, `tools\whisper\whisper-cli.exe`,
   `tools\node\node.exe` neben dem Ordner `bin`;
4. der `PATH` (nicht für das Spracherkennungsprogramm: Ohne eines arbeitet `video-import` mit
   `--transcript <file>` oder `--no-transcript`).

Das Sprachmodell ist `tools.transcribeModel`, sonst die Datei, die in `model.txt` neben dem
Erkennungsprogramm genannt ist, sonst die erste Datei `models\ggml-*.bin` dort.

Der Browser ist `tools.browser`, sonst Microsoft Edge, sonst Chrome in ihren üblichen Ordnern.

## Sprache

Die Sprache der Programmtexte (Hilfe, Meldungen, Beschreibungen der MCP-Werkzeuge) ist die erste
dieser Angaben: die Option `--lang`, der Schlüssel `lang` in `eqlmaker.json`, die
Umgebungsvariable `EQLMAKER_LANG`, die Sprache von Windows. Englisch wird verwendet, wenn es für
die Sprache keine Übersetzung gibt. Verfügbar: `en`, `de`.

Dateiformate, Bezeichner, Werkzeugnamen, Optionsnamen und die Fehlermeldungen des Kerns bleiben
in jeder Sprache englisch. Siehe [Übersetzen](translating.md).

## EQLServer an Stelle von PostgreSQL verwenden

Die Konfiguration bleibt gleich; nur `source`, `capture.logFile` und der Abschnitt `eqlserver`
ändern sich. Siehe [Ereignisquellen](sources.md).
