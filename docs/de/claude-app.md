<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](../claude-app.md) · [Deutsch](claude-app.md)

# Claude-Apps aus Altanwendungen bauen

Eine **Claude-App** ist eine neue Oberfläche für eine Altanwendung, die **in Claude Desktop** als installierte
App läuft. Sie arbeitet auf den laufenden Daten der Originalanwendung in EQLServer, über einen **eigenen lokalen
MCP-Server**. Die Originalanwendung läuft unverändert weiter, ihr **Quellcode wird nicht gebraucht**.

EQLMaker bereitet die Claude-App mit einem geführten Schritt vor: `eqlmaker claude-app`. Er schreibt den
MCP-Server, die Anwendungsbeschreibung, eine funktionierende allgemeine Seite, den Eintrag für Claude Desktop und
die Anleitung. Ein KI-Agent oder ein Mensch vervollständigt die App danach Maske für Maske, und die
Vergleichstests entscheiden, ob sie sich wie das Original verhält.

> **Stand: Machbarkeitsnachweis, Beta.** Sechs Claude-Apps sind mit diesem Verfahren entstanden (siehe
> [Beispiele](#beispiele)). Der Befehl ist neu in EQLMaker und bereitet die App vor; die fertigen Masken
> schreibt er nicht. „Als App installieren“ ist in Claude Desktop eine Beta-Funktion. Die App ist privat und
> braucht Claude Desktop.

**Inhalt:** [Voraussetzungen](#voraussetzungen) · [Aufbau](#aufbau) · [Schritt für Schritt](#schritt-für-schritt) ·
[Block für einen KI-Agenten](#block-für-einen-ki-agenten) · [Fehlersuche](#fehlersuche) · [Fragen](#fragen) ·
[Grenzen](#grenzen-und-stand) · [Beispiele](#beispiele)

## Voraussetzungen

- **Claude Desktop** unter Windows (der Passwortspeicher nutzt Windows DPAPI) mit einem Claude-Tarif, der
  Artefakte veröffentlichen und als App installieren kann.
- **EQLServer** mit den Daten der Altanwendung (Access, SQL Server, MySQL, Firebird, SQL Anywhere, Paradox und
  andere), die Altanwendung unverändert daran angeschlossen. Siehe [weitere Datenbanken](other-databases.md).
- **EQLMaker** mit einem Projekt, das Aufzeichnungen und eine Spezifikation der Anwendung hat (siehe
  [Schnellstart](quick-start.md)). Ohne Spezifikation läuft der Befehl auch, listet dann aber keine Tabellen.
- **Node.js 20 oder neuer** auf dem Rechner mit Claude Desktop (der MCP-Server ist ein kleines Node-Programm).
- Eine Datenbankanmeldung für die App. Das Passwort steht in keiner Datei; es wird mit Windows DPAPI für den
  Windows-Benutzer gespeichert, unter dem Claude Desktop läuft.

## Aufbau

```
 Legacy application (unchanged) ──┐
                                  ├──> EQLServer: same database, same rows
 Claude Desktop                   │
   installed Claude app (page) ───┘ via host:eqlmaker-<app>
        │  window.claude.use("mcp").callTool(...)
        ▼
   local MCP server eqlmaker-<app> (Node, stdio, no network listener)
        reads mcp-server/apps/<app>.json (entities, queries, lookups, operations)
        writes only after preview + confirmation, audit log per app
```

- **Eine Anwendung = eine Datenbank (oder ein Schema) = ein MCP-Server.** Es gibt keinen Sammelserver. Der Server
  heißt `eqlmaker-<app>` und hat eine eigene Konfiguration, ein eigenes Passwortprofil und ein eigenes Protokoll
  (`%LOCALAPPDATA%\EQLMaker\audit\<app>.jsonl`).
- Der MCP-Server ist allgemein und hat neun feste Werkzeuge: `status`, `describe`, `lookups`, `query`,
  `get_record`, `plan_operation`, `run_operation`, `run_destructive_operation`, `write_outcome`. Was die App
  kann, legt die **Anwendungsbeschreibung** fest (`mcp-server/apps/<app>.json`): Entitäten mit ihren Feldern,
  Listenabfragen mit Suche, Sortierung und Filtern, Auswahllisten und die fachlichen Vorgänge des Originals. Aller
  SQL-Text steht in der Beschreibung; jeder Wert von der Seite ist ein Bind-Parameter.
- Die **Seite** (`app/app.html`) ist eine einzelne HTML-Datei. Sie wird als privates Artefakt veröffentlicht, mit
  dem Recht, nur `host:eqlmaker-<app>` und nur die genannten Werkzeuge aufzurufen, und in Claude Desktop mit
  „Als App installieren“ installiert. Sie hält keine Daten.
- **Vorschau vor jedem Schreiben.** `plan_operation` führt die echten Anweisungen in einer Transaktion aus, die
  zurückgerollt wird, und liefert die Zeilen vorher und nachher sowie einen Einmal-Token (10 Minuten gültig). Die
  Seite zeigt diese Vorschau; erst nach der Bestätigung schreibt `run_operation`, in einer Transaktion. Löschen
  braucht `run_destructive_operation`. Eine Anfrage-ID schützt vor doppeltem Schreiben, `write_outcome`
  beantwortet nach einem Verbindungsabbruch, ob das Schreiben stattfand.
- Die Seite spricht **Deutsch und Englisch** (Umschalter unten links), auch wenn das Original nur eine Sprache
  kennt. Daten werden nicht übersetzt.

## Schritt für Schritt

### 1. Altanwendung erfassen

Die Arbeit im Original aufzeichnen, während EQLMaker misst, was in der Datenbank passiert, und daraus die
Spezifikation bauen:

```
eqlmaker init my-app
cd my-app
eqlmaker record --start --name customers     # work in the original, then:
eqlmaker record --stop
eqlmaker correlate <recording>
eqlmaker spec
```

Handbuch, ein besprochenes Video oder die Dateien der Anwendung kommen dazu; siehe [Ablauf](pipeline.md).

### 2. Datenmodell

Festlegen, welche Tabellen die App liest und welche sie schreibt (Entitäten mit Schlüssel). Der App eine eigene
Datenbank oder ein eigenes Schema auf EQLServer geben: eine Kopie der Tabellen für eine Vorführung, die
Produktivtabellen für den echten Einsatz. Eine Anmeldung mit `SELECT` auf das Schema und
`INSERT`/`UPDATE`/`DELETE` nur auf die Tabellen anlegen, die die App schreibt. Das Schema der Originalanwendung
nicht ändern.

### 3. Claude-App vorbereiten

```
eqlmaker claude-app --name shop --schema shop --entity customers:id,orders:id
```

| Option | Bedeutung |
|---|---|
| `--name <app>` | Name der App (Vorgabe: Projektordner); der Server heißt `eqlmaker-<app>` |
| `--title <text>` | Titel, den die App zeigt (Vorgabe: der Name) |
| `--schema <s>` | Datenbankschema der App (Vorgabe `public`) |
| `--entity tabelle:schlüssel,...` | Tabellen, die die App anlegen, ändern und löschen darf, mit ihrer Schlüsselspalte |
| `--force` | Vorlage über einen vorhandenen Ordner `claude-app/` kopieren (Beschreibung und Konfiguration bleiben) |

Er schreibt `claude-app/`:

| Datei | Inhalt |
|---|---|
| `mcp-server/` | der MCP-Server mit seinen Tests (`npm test`) |
| `mcp-server/apps/<app>.json` | die Anwendungsbeschreibung: je Tabelle der Spezifikation eine Listenabfrage, die Entitäten mit Feldern aus der Datenbank |
| `mcp-server/config/<app>.json` | Host, Port, Datenbank, Schema, Anmeldung, Passwortprofil; kein Passwort |
| `app/app.html`, `app/capabilities.json` | die Seite und die Rechte, mit denen sie veröffentlicht wird |
| `claude_desktop_config.json` | der Eintrag unter `mcpServers` für Claude Desktop |
| `CLAUDE-APP.md` | die Anleitung für den KI-Agenten oder für Sie |

Die Verbindung kommt aus `database.connection` in `eqlmaker.json`. Denselben Schritt gibt es für MCP-Clients als
Werkzeug `claude_app` (siehe [MCP](mcp.md)).

Danach einmal das Passwort speichern:

```
cd claude-app/mcp-server
npm ci
node bin/set-password.mjs --profile shop
```

### 4. Masken erzeugen

Die allgemeine Seite funktioniert schon: ein linkes Menü aus den Listenabfragen, Listen mit Suche und Seiten,
Datensatzformulare aus den Feldern der Entitäten und der Vorschaudialog für jede Änderung. Jetzt wird sie aus der
Spezifikation und den Referenzbildern der Aufzeichnungen Maske für Maske vervollständigt:

- die Beschreibung ergänzen: Beschriftungen, Pflichtfelder, Längen, Auswahllisten, Kopf- und Positionstabellen
  (`children`), Prüfungen und Sperren sowie die fachlichen Vorgänge des Originals (buchen, versenden, Periode
  abschließen) mit demselben SQL, das die Aufzeichnung zeigt, damit beide dieselben Zeilen schreiben;
- die Masken des Originals auf der Seite bauen: dieselben Funktionen, dieselbe Arbeitsreihenfolge, ein modernes
  Layout mit linkem Menü.

Das ist die Arbeit eines KI-Agenten; der [Block unten](#block-für-einen-ki-agenten) startet sie.

### 5. Vorschau und Bestätigung

Jedes Schreiben geht durch die Vorschau. Gezielt testen: einen Datensatz ändern, die Vorschau abbrechen (nichts
wird geschrieben), bestätigen (einmal geschrieben), denselben Token noch einmal bestätigen (abgelehnt). Genau das
prüfen die Vorlagentests in `mcp-server/test/`; mit gesetztem `EQLMAKER_TEST_PG` laufen sie gegen ein echtes
PostgreSQL.

### 6. Mehrsprachig

Jeder Text der Seite steht im Wörterbuch `DE` (englischer Schlüssel, deutscher Text). Der Umschalter unten links
wechselt die ganze Seite; der Vorlagentest schlägt fehl, wenn ein Text keinen deutschen Eintrag hat. Weitere
Sprachen: ein Wörterbuch und eine Schaltfläche dazu. Siehe [Übersetzen](translating.md).

### 7. Installieren

1. `claude-app/claude_desktop_config.json` in die `mcpServers` der `claude_desktop_config.json` von Claude Desktop
   übernehmen und Claude Desktop vollständig neu starten.
2. `app/app.html` als privates Artefakt mit den Rechten aus `app/capabilities.json` veröffentlichen (nur
   `host:eqlmaker-<app>`, nur die genannten Werkzeuge, dazu `desktop`).
3. In Claude Desktop öffnen und **Als App installieren** wählen (Beta).

### 8. In beide Richtungen prüfen

Dieselben Schritte im Original und in der Claude-App auf denselben Daten aufzeichnen und vergleichen, was beide
geschrieben haben:

```
eqlmaker verify db <recording-original> <recording-claude-app> --keys customers.id
```

Danach einen Datensatz in der Claude-App ändern und im Original nachlesen, und umgekehrt. Beide Richtungen sind
Teil jedes Beispielvideos.

## Block für einen KI-Agenten

In einen KI-Agenten einfügen, der im Projektordner läuft (mit EQLMaker als MCP-Server oder auf der
Kommandozeile):

```text
Build a Claude app for the legacy application of this EQLMaker project.
1. Run `eqlmaker claude-app --name <app> --schema <schema> --entity <table:key,...>`
   (or the MCP tool claude_app) and read claude-app/CLAUDE-APP.md completely.
2. Keep the architecture: one database, one local MCP server eqlmaker-<app>, the nine fixed tools,
   preview and confirmation before every write. Never add a write path that skips the preview.
3. Complete mcp-server/apps/<app>.json screen by screen from spec/spec.md and the reference pictures:
   entities, list queries, lookups, and the domain operations with the SQL the recordings show.
4. Build the screens of the original in app/app.html (left menu, same functions, same order of work).
   Every text goes into the DE dictionary; the DE/EN switcher must cover the whole page.
5. Run `npm test` in claude-app/mcp-server and add tests for every operation you add.
6. Do not change the original application or its table structure. No passwords in any file.
7. Prove it: same steps in the original and in the app, `eqlmaker verify db <A> <B>`, and a change in each
   direction read back on the other side. Report what is built, what is missing and the test results.
```

Der Block ist englisch, weil Anleitung und Code der App englisch sind; der Agent antwortet in Ihrer Sprache.

## Fehlersuche

| Anzeichen | Ursache und Abhilfe |
|---|---|
| Die Seite meldet „Bitte in der installierten App in Claude Desktop öffnen.“ | Die Seite läuft außerhalb von Claude Desktop oder wurde ohne das Recht `mcp` veröffentlicht. Mit `app/capabilities.json` veröffentlichen und die installierte App öffnen. |
| `server_not_found` / die App zeigt kein Menü | Claude Desktop kennt `eqlmaker-<app>` nicht. Den Eintrag in `claude_desktop_config.json` prüfen (absolute Pfade, `node` im PATH) und Claude Desktop vollständig neu starten. |
| `status` meldet `ok: false` | EQLServer nicht erreichbar, Host/Port/Datenbank in `mcp-server/config/<app>.json` falsch oder kein Passwort gespeichert. `node bin/set-password.mjs --profile <app>` als der Windows-Benutzer ausführen, unter dem Claude Desktop läuft. |
| `token_invalid` oder `token_expired` | Ein Vorschau-Token gilt einmal und 10 Minuten. Die Vorschau neu öffnen. |
| `conflict` beim Speichern | Jemand (zum Beispiel die Originalanwendung) hat den Datensatz nach dem Laden geändert. Neu laden und wiederholen. |
| Löschen wird mit `validation` abgelehnt | Löschen braucht `run_destructive_operation`; die Seite tut das, wenn die Vorschau ein Löschen ist. |
| `claude-app/ exists already` | Mit `--force` die Vorlage auffrischen; Beschreibung und Konfiguration bleiben. |
| Das Original zeigt die Änderung nicht | Viele Altanwendungen aktualisieren sich nicht von selbst. Liste neu laden oder Formular neu öffnen. |

## Fragen

**Ist das eine MCP App?** Die App nutzt MCP: einen lokalen MCP-Server je Anwendung, den die Seite über Claude
Desktop aufruft. Die Seite selbst ist ein privates Artefakt, installiert mit „Als App installieren“; der Server
liefert sie nicht als `ui://`-Ressource aus.

**Brauche ich den Quellcode der Altanwendung?** Nein. EQLMaker beobachtet die laufende Anwendung und die
Datenbank. Das Original bleibt unverändert.

**Wo liegen die Daten?** In Ihrem eigenen EQLServer. Der MCP-Server läuft lokal und verbindet sich nur damit.

**Können sich mehrere Claude-Apps einen MCP-Server teilen?** Nein, mit Absicht: eine Anwendung, eine Datenbank,
ein Server, ein Passwortprofil, ein Protokoll.

**Kann die App ohne mich Daten schreiben?** Nein. Jedes Schreiben zeigt vorher eine Vorschau und braucht Ihre
Bestätigung in der App.

**Ist das ein Generator, der die ganze App baut?** Nein. `eqlmaker claude-app` bereitet eine funktionierende
Grundlage vor; die Masken des Originals vervollständigt ein KI-Agent oder ein Mensch, geprüft mit
Vergleichstests.

## Grenzen und Stand

- Machbarkeitsnachweis, Beta. Sechs Apps gibt es als Beispiele; keine ist im Produktiveinsatz.
- Braucht Claude Desktop; „Als App installieren“ ist dort eine Beta-Funktion. Die App ist privat für Ihr Konto.
- Die Altanwendung aktualisiert sich nicht von selbst; Änderungen aus der Claude-App erscheinen nach dem Neuladen.
- Die allgemeine Seite deckt Listen, Datensatzformulare und Änderungen an einer Tabelle ab. Kopf- und
  Positionsbelege, fachliche Vorgänge und besondere Masken kommen je Anwendung dazu.
- Das Paradox-Beispiel braucht EQLServer 1.12.0, das noch nicht veröffentlicht ist.

## Beispiele

Jedes Beispiel zeigt das unveränderte Original und die Claude-App auf denselben Daten in EQLServer, mit
Änderungen in beide Richtungen und der Installation als Claude-App.

| Original | Technik | Video (Deutsch) | Video (Englisch) |
|---|---|---|---|
| Northwind (Access-Vorlage) | Microsoft Access | [ansehen](https://eqlserver.de/poc-northwind-claude-app/) | [ansehen](https://eqlserver.de/poc-northwind-claude-app/en.html) |
| Contoso University | ASP.NET Core, SQL Server | [ansehen](https://eqlserver.de/poc-contoso-claude-app/) | [ansehen](https://eqlserver.de/poc-contoso-claude-app/en.html) |
| InvoicePlane | PHP, MySQL | [ansehen](https://eqlserver.de/poc-invoiceplane-claude-app/) | [ansehen](https://eqlserver.de/poc-invoiceplane-claude-app/en.html) |
| TurboCASH | Delphi, Firebird | [ansehen](https://eqlserver.de/poc-turbocash-claude-app/) | [ansehen](https://eqlserver.de/poc-turbocash-claude-app/en.html) |
| Hausverwaltung | PowerBuilder 6.5, SQL Anywhere | [ansehen](https://eqlserver.de/poc-hausverwaltung-claude-app/) | [ansehen](https://eqlserver.de/poc-hausverwaltung-claude-app/en.html) |
| Fakturierung | Delphi, Paradox (BDE) | [ansehen](https://eqlserver.de/poc-fakturierung-claude-app/) | [ansehen](https://eqlserver.de/poc-fakturierung-claude-app/en.html) |

Mehr auf [eqlmaker.org](https://eqlmaker.org/de/#claude-app).
