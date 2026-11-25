<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](../remote-control.md) · [Deutsch](remote-control.md)

# Fernbedienung

EQLMaker bedient Windows-Programme und Webseiten selbst, damit jede Bedienhandlung mit Zeit,
Fenster und Steuerelement aufgezeichnet wird. Das geschieht aus einer Szenariodatei oder im
Auftrag eines MCP-Clients. Diese Seite beschreibt die zwei Eingabemodi, wie Steuerelemente
angesprochen werden, und die Szenariodatei.

## Zwei Eingabemodi

| | `real` | `background` |
|---|---|---|
| Eingabe | Maus und Tastatur, wie ein Mensch sie benutzt | Fensternachrichten an das Zielfenster |
| Bilder | vom Bildschirm | vom Fenster selbst |
| Browser | ein sichtbares Browserfenster | ein Browser ohne Fenster, bedient über seine DevTools-Schnittstelle |
| Braucht | einen sichtbaren, entsperrten Desktop | keinen sichtbaren Desktop; Maus und Tastatur bleiben frei |
| Funktioniert mit | jedem Programm | Programmen, die auf zugestellte Nachrichten reagieren: klassisches Win32, Windows Forms und Ähnliches |
| Tastenkombinationen | ja (`^a`, `%{F4}`, `+{TAB}`) | Kombinationen mit Strg, Alt oder Umschalt werden abgelehnt |

Den Modus wählt `"input"` in `eqlmaker.json` oder in der Szenariodatei, `--input` bei `record`
oder das MCP-Werkzeug `set_input_mode`. Die Vorgabe ist `real`.

Im Modus `background` lässt sich ein Programm auch auf einem eigenen Desktop starten, der nie
angezeigt wird:

```text
eqlmaker background-run <desktop> <command...>
```

`eqlmaker sample` nutzt das: Das Beispielprogramm läuft, wird bedient und aufgezeichnet, und auf
dem Bildschirm bewegt sich nichts.

Wie es getestet ist: Jeder Ende-zu-Ende-Test des Repositorys nutzt den Modus `background`. Den
Modus `real` decken die automatischen Tests nur dort ab, wo keine Eingabe gesendet wird (die
Fensterliste), weil ein Test Maus und Tastatur nicht übernehmen darf.

## Fenster und Steuerelemente finden

```text
eqlmaker windows
eqlmaker controls "Sample Shop*"
```

`windows` listet die sichtbaren Hauptfenster mit Titel, Prozess und Position auf. `controls`
listet die Steuerelemente (Kindfenster) eines Fensters mit Text, Fensterklasse und Position auf.

Ein Fenster wird über ein Titelmuster mit `*` und `?` benannt, oder über `pid:<n>` oder
`process:<name>`. Koordinaten gelten relativ zur linken oberen Ecke des aktuellen Fensters.

Ein Steuerelement wird in beiden Modi über eine Abfrage angesprochen:

| Schlüssel | Bedeutung |
|---|---|
| `text` | Text des Steuerelements, mit `*` und `?` |
| `class` | Fensterklasse, zum Beispiel `*EDIT*` oder `*BUTTON*` |
| `index` | der n-te Treffer, beginnend bei 0 |
| `at` | `[x, y]` innerhalb des gefundenen Steuerelements, statt seiner Mitte |

Die Ansprache über Text und Klasse übersteht ein verschobenes Fenster oder eine andere
Bildschirmgröße; Koordinaten tun das nicht. EQLMaker liest die Kindfenster eines Fensters. Den
Baum der UI Automation liest es nicht: Programme, die ihre Steuerelemente selbst zeichnen (viele
neuere Oberflächenbibliotheken), zeigen keine Kindfenster und werden über Koordinaten bedient.
Für solche Programme lässt sich ein externer MCP-Server für UI-Automatisierung neben EQLMaker
verwenden.

Elemente einer Webseite werden über `css` (ein CSS-Selektor), `text` (genauer sichtbarer Text),
`contains` (Teil des Textes) und `index` angesprochen.

## Szenariodatei

Ein Szenario ist eine JSON-Datei mit Einstellungen und einer Liste von Schritten. EQLMaker führt
die Schritte der Reihe nach aus und zeichnet dabei auf.

```json
{
  "name": "desktop",
  "input": "background",
  "video": true,
  "fps": 10,
  "closeAtEnd": true,
  "steps": [
    { "run": "${SAMPLE_BIN}/sample-desktop.exe", "args": "--connection \"${SAMPLE_CONNECTION}\"" },
    { "window": "Sample Shop", "timeout": 30000 },
    { "caption": "New customer" },
    { "control": { "text": "New customer" } },
    { "window": "Customer" },
    { "fill": { "class": "*EDIT*", "index": 0, "value": "Harbour Books" } },
    { "control": { "text": "Save customer" } }
  ]
}
```

`${NAME}` wird durch die Umgebungsvariable dieses Namens ersetzt; eine fehlende Variable bricht
das Szenario ab. Vollständige Beispiele: `examples/sample-app/scenarios/desktop.json` und
`web.json`.

### Einstellungen

| Schlüssel | Bedeutung | Vorgabe |
|---|---|---|
| `name` | Name der Aufnahme | Dateiname des Szenarios |
| `input` | `real` oder `background` | aus der Konfiguration |
| `video` | Video aufzeichnen | `true` |
| `capture` | Datenbankereignisse erfassen: `true`, `false` oder ein Objekt `{"roles": [...], "databases": [...]}`, das die Logins und Datenbanken dieses Szenarios statt der der Konfiguration nennt | `true` |
| `fps` | Bilder pro Sekunde des Videos | 15 |
| `region` | Modus `real`: `workarea`, `screen`, `x,y,w,h` oder ein Muster für den Fenstertitel | `workarea` |
| `shots` | Referenzbilder und Steuerelementlisten zu den Bedienhandlungen | `true` |
| `moveMs` | Dauer einer Mausbewegung in Millisekunden (Modus `real`) | 450 |
| `typeDelay` | Pause zwischen getippten Zeichen in Millisekunden | 55 |
| `closeAtEnd` | die vom Szenario gestarteten Programme schließen | `false` |
| `steps` | die Schritte | erforderlich |

Eine Szenariodatei des früheren Recorders nennt sie `"sql": {"roles": [...], "databases": [...]}`;
`eqlmaker convert-scenario <Datei>` macht daraus `capture`, alles andere ist in beiden gleich.

Ohne einen Schritt `record` wird das ganze Szenario aufgezeichnet, und die Erfassung beginnt vor
dem ersten Schritt, sodass ein vom Szenario gestartetes Programm ab seiner ersten Anweisung
erfasst wird.

### Schritte

Jeder Schritt ist ein Objekt mit einem dieser Schlüssel.

| Schritt | Wert | Weitere Schlüssel | Wirkung |
|---|---|---|---|
| `run` | Programm | `args`, `cwd` | startet ein Programm; ein relativer Pfad wird neben der Szenariodatei gesucht |
| `window` | Titelmuster | `maximize`, `timeout` (ms, Vorgabe 30000) | wartet auf ein Fenster und macht es zum aktuellen Fenster |
| `screen` | `true` | | hebt das aktuelle Fenster auf; Koordinaten sind wieder Bildschirmkoordinaten |
| `caption` | Text | | setzt eine Kapitelmarke |
| `control` | Abfrage eines Steuerelements | `double` | klickt ein Steuerelement an |
| `fill` | Abfrage eines Steuerelements mit `value` | | klickt ein Eingabefeld an und ersetzt seinen Inhalt |
| `click` | `[x, y]` | `button` (`left`, `right`, `middle`), `ms` | klickt auf eine Position |
| `dclick` | `[x, y]` | `ms` | Doppelklick |
| `rclick` | `[x, y]` | `ms` | Rechtsklick |
| `move` | `[x, y]` | `ms` | bewegt die Maus (Modus `real`) |
| `type` | Text | `secret` | tippt Text in das Steuerelement mit dem Eingabefokus |
| `keys` | Tasten in SendKeys-Schreibweise | | `{ENTER}`, `{TAB}`, `{F5}`, `{DEL}`; im Modus `real` auch `^a`, `%{F4}`, `+{TAB}` |
| `scroll` | Rasterschritte, positiv ist nach oben | | dreht das Mausrad |
| `wait` | Millisekunden | | wartet |
| `webopen` | URL | `bounds` `[x, y, w, h]`, `maximize` | öffnet eine URL in Microsoft Edge oder Chrome unter Fernsteuerung |
| `webgo` | URL | | navigiert die Seite |
| `webreload` | `true` | | lädt die Seite neu |
| `webclick` | Abfrage eines Elements | | klickt ein Element der Seite an |
| `webdclick` | Abfrage eines Elements | | Doppelklick |
| `webwait` | Abfrage eines Elements | `timeout` in der Abfrage (ms, Vorgabe 15000) | wartet, bis das Element existiert |
| `webmove` | Abfrage eines Elements | `ms` | bewegt die Maus zum Element, ohne zu klicken (Modus `real`) |
| `webfill` | Abfrage eines Elements mit `value` | `clear` in der Abfrage (Vorgabe `true`) | klickt in ein Feld und tippt einen Wert ein |
| `webeval` | JavaScript-Ausdruck | | wertet ihn in der Seite aus |
| `screenshot` | Dateiname | | schreibt ein Bild des aktuellen Fensters; ein relativer Name landet im Aufnahmeordner |
| `record` | `start` oder `stop` | `region` | zeichnet nur einen Teil des Szenarios auf |

Eine Abfrage eines Steuerelements oder Elements darf statt eines Objekts ein einfacher Text
sein; er bedeutet dann `text`.

`"secret": true` an einem Schritt `type` hält den getippten Text aus `actions.jsonl` heraus. Das
Video zeigt weiterhin, was die Anwendung anzeigt.

## Über einen MCP-Client

Dieselben Operationen sind MCP-Werkzeuge: `list_windows`, `focus_window`, `list_controls`,
`launch`, `screenshot`, `click`, `click_control`, `fill_control`, `type_text`, `press_keys`,
`web_open`, `web_click`, `web_fill` und weitere. Die übliche Reihenfolge ist: `record_start`,
`launch`, `focus_window`, `screenshot` oder `list_controls`, dann die Bedienhandlungen mit
`record_caption` zwischen den fachlichen Schritten, und `record_stop`. Siehe
[MCP-Server](mcp.md).

## Grenzen

- Der Modus `background` funktioniert nur für Programme, die auf zugestellte Fensternachrichten
  reagieren. Ein Programm, das den Zustand der Tastatur direkt liest oder seine Steuerelemente
  selbst zeichnet, braucht den Modus `real`.
- Der Modus `real` braucht einen sichtbaren, entsperrten Desktop und übernimmt Maus und
  Tastatur, solange er läuft.
- Tastenkombinationen mit Strg, Alt oder Umschalt werden im Modus `background` abgelehnt.
- Arbeitet ein Mensch von Hand (`record --start`), werden keine Bedienhandlungen aufgezeichnet:
  Die Aufnahme enthält nur Video und Datenbankereignisse.
- Nur Windows.
