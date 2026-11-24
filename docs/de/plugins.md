<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](../plugins.md) · [Deutsch](plugins.md)

# Plugins

EQLMaker lässt sich an vier Stellen erweitern, ohne seinen Code zu ändern:

| Typ | Erweitert | Eingebaut | Beispiel im Repository |
|---|---|---|---|
| `source` | woher Datenbankereignisse kommen | `postgres` und die weitere unter [Ereignisquellen](sources.md) genannte | `plugins/examples/source-jsonl-file` |
| `web-template` | den Ausgangspunkt, den `generate` kopiert | `default` | `plugins/examples/web-template-minimal` |
| `clone-renderer` | die Seite, die `clone` für jede Maske erzeugt | `backdrop` | `plugins/examples/clone-renderer-outline` |
| `video-layout` | wie `video` die beiden Aufnahmen anordnet | `side-by-side` | `plugins/examples/video-layout-stacked` |

Ein Plugin ist ein Ordner mit einer Datei `plugin.json`. EQLMaker sucht Plugins in den Ordnern,
die unter `"plugins"` in `eqlmaker.json` stehen (Vorgabe: der Ordner `plugins` des Projekts).
Jeder genannte Ordner darf ein Plugin sein oder mehrere Plugin-Ordner enthalten.

```json
{ "plugins": ["plugins", "C:\\Shared\\eqlmaker-plugins"] }
```

Plugins der Typen `source` und `clone-renderer` sind eigene Programme. EQLMaker startet sie und
spricht mit ihnen über Standardeingabe und Standardausgabe in JSON, ein Objekt je Zeile, in UTF-8
ohne Byte Order Mark, gleich welche Codepage die Konsole hat. Lies und schreibe die beiden Ströme
als Bytes in UTF-8 und nicht über die Konsolenkodierung deiner Sprache; ein Leser sollte eine
Byte Order Mark vor der ersten Zeile hinnehmen.
Sie lassen sich in jeder Sprache schreiben. Es wird kein Plugin-Code in den EQLMaker-Prozess
geladen. Was ein Plugin-Programm auf seine Fehlerausgabe schreibt, wird angezeigt, wenn es
fehlschlägt.

Jedes Plugin hat diese Schlüssel:

| Schlüssel | Bedeutung |
|---|---|
| `type` | `source`, `web-template`, `clone-renderer` oder `video-layout` |
| `name` | der Name, der in der Konfiguration und auf der Kommandozeile verwendet wird; keine Leerzeichen oder Schrägstriche |
| `description` | ein Satz für Menschen (optional) |

## Quelle

```json
{ "type": "source", "name": "jsonl-file", "command": "powershell -NoProfile -ExecutionPolicy Bypass -File source.ps1" }
```

`command` wird im Ordner des Plugins ausgeführt. Die Quelle wählt man mit
`"source": "jsonl-file"` in `eqlmaker.json`; ein Abschnitt gleichen Namens enthält ihre
Einstellungen:

```json
{ "source": "jsonl-file", "jsonl-file": { "file": "C:\\Logs\\events.jsonl" } }
```

Das Programm wird gestartet, wenn eine Erfassung beginnt:

1. Es liest eine Zeile von der Standardeingabe, die Optionen der Erfassung:
   `{"action":"start","connection":"...","roles":[...],"databases":[...],"changes":true,"logFile":"","settings":{...}}`.
   `settings` ist der Abschnitt mit dem Namen der Quelle.
2. Sobald es erfasst, schreibt es eine Zeile `{"info":{...}}` (`{"info":{}}` genügt). EQLMaker
   wartet auf diese Zeile, bevor die Aufnahme als gestartet gilt, damit nichts verloren geht, was
   die Anwendung gleich danach tut; ohne sie scheitert der Start nach 60 Sekunden
   (`"startTimeoutSeconds"` in `plugin.json` ändert das). Schlüssel, die EQLMaker versteht:
   `fields` (was die Quelle liefert, frei gestaltet), `server_minus_local_us` (Uhr des
   Datenbankservers minus Uhr dieses Rechners, in Mikrosekunden), `warnings` (Liste von Texten),
   `changes` (`"on"` oder `"off"`).
3. Es schreibt Ereignisse, eines je Zeile, im [Ereignisformat](event-format.md), zu jeder Zeit,
   solange es läuft. `source` darf fehlen; EQLMaker trägt den Namen des Plugins ein.
4. Liest es die Zeile `stop`, schreibt es die übrigen Ereignisse und endet mit Rückgabewert 0.
   Ein anderer Rückgabewert als 0 lässt die Aufnahme fehlschlagen.

Mit `"action":"reset"` nimmt das Programm zurück, was eine abgebrochene Erfassung auf dem Server
hinterlassen hat, und endet; es wird von `eqlmaker reset` aufgerufen.

## Web-Vorlage

```json
{ "type": "web-template", "name": "minimal", "files": "files", "variables": ["APP_NAME", "DB_ENV"] }
```

`files` ist der Ordner, den `eqlmaker generate --template minimal` in den Projektordner `web`
kopiert. In Textdateien wird `{{APP_NAME}}` durch den Namen des Projekts ersetzt und
`{{DB_ENV}}` durch den Namen der Umgebungsvariable, die die Datenbankverbindung der Anwendung
enthält. `generate` legt danach die Spezifikation nach `web/spec` und die Anleitung nach
`web/GENERATE.md`. Eine Vorlage bestimmt Sprache, Framework und Aussehen der Anwendung; sie muss
nicht Node verwenden.

## Klon-Renderer

```json
{ "type": "clone-renderer", "name": "outline", "command": "powershell -NoProfile -ExecutionPolicy Bypass -File render.ps1" }
```

`eqlmaker clone --renderer outline` startet das Programm einmal je Maske. Es liest eine Zeile:

```json
{"mask":{"id":"m-customer","title":"Customer #"},"reference":"../reference/m-customer.png","width":396,"height":249,"controls":[{"text":"Save customer","class":"...BUTTON...","x":162,"y":201,"w":110,"h":26,"enabled":true,"writes":true}],"queries":["q4"],"tables_read":["customer"],"out":"C:\\project\\clone\\public"}
```

- `reference` ist das Referenzbild der Maske, relativ zur Seite.
- `controls` sind die für die Maske aufgezeichneten Steuerelemente, mit Positionen innerhalb des
  Bildes; `writes` ist wahr für ein Steuerelement, das bei seiner Benutzung Daten geändert hat.
- `out` ist der Ordner für die Seite und alles Weitere, was der Renderer schreibt.

Das Programm schreibt seine Dateien und antwortet mit einer Zeile: `{"page":"m-customer.html"}`.
Ein Renderer muss das Archiv nur lesend halten: Steuerelemente mit `writes` bleiben sichtbar und
nicht bedienbar.

## Videolayout

```json
{ "type": "video-layout", "name": "stacked", "height": 540, "filter": "[a][b]vstack=inputs=2[v]", "player": "column" }
```

- `filter` ist ein FFmpeg-Filter, der aus den Eingängen `[a]` (erste Aufnahme) und `[b]` (zweite
  Aufnahme) den Ausgang `[v]` macht. Beide Eingänge sind bereits auf `height` skaliert und gleich
  lang.
- `player` ordnet die beiden Videos auf der Player-Seite an: `row` oder `column`.

Verwendung: `eqlmaker video <A> <B> --layout stacked`.

## Ein Plugin prüfen

`eqlmaker` meldet eine fehlerhafte `plugin.json` mit dem Grund, sobald das Plugin verwendet
wird. Die Beispiele sind durch die Tests in `tests/PipelineTests.cs` (`PluginTests`) abgedeckt;
sie sind ein guter Ausgangspunkt für Tests des eigenen Plugins.
