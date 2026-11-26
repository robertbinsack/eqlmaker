<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](../mcp.md) · [Deutsch](mcp.md)

# MCP-Server

EQLMaker ist auch ein Server des Model Context Protocol (MCP). Ein MCP-Client, üblicherweise ein
Assistent, verbindet sich damit und erhält die Fernbedienung und jeden Schritt des Ablaufs als
Werkzeuge. So erledigt ein Assistent die [geführten Schritte](guided-steps.md).

## Einen Client anschließen

Der Server spricht über Standardeingabe und Standardausgabe. Lassen Sie Ihren MCP-Client diesen
Befehl starten:

```text
eqlmaker mcp --project <folder>
```

Die meisten Clients nehmen eine Konfiguration dieser Form; der Ort der Datei hängt vom Client ab:

```json
{
  "mcpServers": {
    "eqlmaker": {
      "command": "C:\\eqlmaker\\bin\\eqlmaker.exe",
      "args": ["mcp", "--project", "C:\\Projects\\my-project"],
      "env": { "EQLMAKER_PGPASSWORD": "..." }
    }
  }
}
```

- Geben Sie den vollständigen Pfad von `eqlmaker.exe` an (im Paket: `bin\eqlmaker.exe`).
- `--project` nennt den Projektordner mit `eqlmaker.json`. Ein Server arbeitet an einem Projekt.
- Das Passwort der Datenbankverbindung kommt aus der Umgebung oder einer Passwortdatei, siehe
  [Konfiguration](configuration.md). Speichert Ihr Client seine Konfiguration in einer Datei,
  die andere lesen können, verwenden Sie `passfile` in `eqlmaker.json` statt `env`.
- `--lang de` liefert die Beschreibungen der Werkzeuge auf Deutsch. Werkzeugnamen und
  Parameternamen bleiben gleich.

Der Server setzt `initialize`, `tools/list`, `tools/call`, `prompts/list`, `prompts/get` und
`ping` um. Er bietet Werkzeuge und Prompts, keine Ressourcen. Ein Bildschirmfoto kommt als Bild
zurück (`image/png`).

Der Server bedient den Rechner im Auftrag des Clients: Er kann Programme starten, klicken und
tippen. Schließen Sie nur Clients an, denen Sie vertrauen.

## Werkzeuge

### Fernbedienung

| Werkzeug | Wirkung |
|---|---|
| `list_windows` | listet die sichtbaren Hauptfenster auf |
| `focus_window` | macht ein Fenster zum aktuellen Fenster; spätere Koordinaten gelten relativ dazu |
| `use_screen` | hebt das aktuelle Fenster auf; Koordinaten sind wieder Bildschirmkoordinaten |
| `set_input_mode` | wählt `real` oder `background` |
| `list_controls` | listet die Steuerelemente des aktuellen Fensters mit Text, Klasse und Position auf |
| `launch` | startet ein Programm |
| `screenshot` | Bild des aktuellen Fensters, des Bildschirms oder der Webseite |
| `click` | klickt auf eine Position |
| `click_control` | klickt ein Steuerelement an, gefunden über Text und/oder Fensterklasse |
| `fill_control` | ersetzt den Inhalt eines Eingabefelds |
| `move` | bewegt die Maus (Modus `real`) |
| `type_text` | tippt Text in das Steuerelement mit dem Eingabefokus; `secret` hält ihn aus der Aufnahme heraus |
| `press_keys` | sendet Tasten in SendKeys-Schreibweise |
| `scroll` | dreht das Mausrad |
| `wait` | wartet |
| `web_open` | öffnet eine URL in Microsoft Edge oder Chrome unter Fernsteuerung |
| `web_click` | sucht ein Element der Webseite und klickt es an |
| `web_fill` | klickt in ein Feld der Webseite und tippt einen Wert ein |
| `web_eval` | wertet JavaScript in der Webseite aus |
| `web_reload` | lädt die Webseite neu |

Modi, Ansprache und Grenzen: [Fernbedienung](remote-control.md).

### Aufnahme

| Werkzeug | Wirkung |
|---|---|
| `record_start` | startet eine Aufnahme: Bedienhandlungen, Datenbankereignisse, wahlweise Video und Referenzbilder |
| `record_caption` | setzt eine Kapitelmarke; ein Kapitel je Maske oder fachlichem Schritt |
| `record_stop` | beendet die Aufnahme und schreibt den Aufnahmeordner |
| `run_scenario` | führt eine Szenariodatei aus und zeichnet sie auf |

Rufen Sie `record_start` vor `launch` auf: Die Erfassung gilt für Datenbanksitzungen, die danach
beginnen.

### Ablauf

| Werkzeug | Befehl | Wirkung |
|---|---|---|
| `correlate` | `correlate` | bildet die Zeitachse einer Aufnahme |
| `spec` | `spec` | leitet die Spezifikation ab |
| `manual_import` | `manual` | übernimmt ein Handbuch |
| `read_application` | `read` | liest die Dateien der Originalanwendung nach `static/<Reader>/`; `spec` übernimmt das Ergebnis |
| `export_sql` | `export-sql` | schreibt die Anweisungen einer Aufnahme als Tabelle `sql.tsv` |
| `generate` | `generate` | geführter Schritt: bereitet `web/` vor, gibt die Anleitung zurück |
| `clone` | `clone` | geführter Schritt: bereitet `clone/` vor, gibt die Anleitung zurück |
| `video` | `video` | zwei Aufnahmen auf einer Zeitachse |
| `verify_db` | `verify db` | vergleicht die gemessenen Datenbankänderungen; gibt den Bericht zurück |
| `verify_screens` | `verify screens` | vergleicht Bilder |
| `web_shot` | `web-shot` | Bild einer Webseite in vorgegebener Größe |
| `video_import` | `video-import` | importiert ein kommentiertes Video und schreibt Befunde |
| `narrate_draft` | `narrate-draft` | bereitet aus der Untertitel-Zeitachse eines Videos ein Vertonungsskript vor |
| `narrate` | `narrate` | legt eine gesprochene Vertonung (lokales Fish Speech) und optionale Musik über ein Video |
| `enrich` | `enrich` | geführter Schritt: beginnt die Übernahme der Befunde, gibt die Anleitung zurück |
| `enrich_decide` | `enrich --decide` | hält die Entscheidung zu einem Befund fest |
| `enrich_finish` | `enrich --finish` | schreibt den Bericht |
| `clock_check` | `clock` | Uhr des Datenbankservers minus Uhr dieses Rechners |
| `import_log` | `import-log` | macht aus einem früher geschriebenen Serverprotokoll eine Aufnahme |

Jedes Werkzeug gibt dasselbe Ergebnisobjekt zurück wie sein Befehl. Die Parameter sind in der
Werkzeugliste beschrieben, die der Server sendet; sie entsprechen den Optionen in der
[Befehlsreferenz](cli.md), mit `_` an Stelle von `-`.

`generate`, `clone` und `enrich` bauen nichts. Sie bereiten einen Arbeitsordner vor und geben
die Anleitung zurück; der Client erledigt die Arbeit, und `verify_db` oder `verify_screens`
prüft sie.

## Prompts

| Prompt | Argument | Inhalt |
|---|---|---|
| `generate` | | Anleitung zum Bau der Webanwendung aus der Spezifikation |
| `clone` | | Anleitung zum Bau des nur lesenden 1:1-Archivs |
| `enrich` | `findings` (optional) | Anleitung zur Übernahme der Befunde eines kommentierten Videos |

Ein Prompt ist derselbe Text wie die Datei `GENERATE.md`, `CLONE.md` oder `ENRICH.md` im
Arbeitsordner, gefüllt mit den Schritten, Masken und Befunden des Projekts.

## Eine typische Sitzung

1. `record_start`, `launch`, `focus_window`, dann das Original bedienen mit `screenshot`,
   `list_controls`, `click_control`, `fill_control`; `record_caption` vor jedem fachlichen
   Schritt; `record_stop`.
2. `correlate`, `spec`.
3. `generate`, dann die Anwendung in `web/` bauen, wie die Anleitung es sagt.
4. Dieselben Schritte in der neuen Anwendung aufzeichnen (`web_open`, `web_click`, `web_fill`,
   mit denselben Überschriften).
5. `verify_db` mit beiden Aufnahmen; beheben, was abweicht; ab Schritt 4 wiederholen.

Wie es getestet ist: `McpTests.ProtocolToolsAndPrompts` startet den Server und prüft die
Antworten des Protokolls, die Werkzeugliste und die Prompts.
