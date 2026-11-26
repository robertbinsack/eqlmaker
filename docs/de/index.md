<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](../index.md) · [Deutsch](index.md)

# Dokumentation

Dies ist die Übersicht der Dokumentation von EQLMaker 0.2. Jede Seite gibt es auf Englisch und
Deutsch unter demselben Dateinamen. Was verfügbar und getestet ist, steht im
[README](../../README.md) und in der [Roadmap](../../ROADMAP.de.md).

## Einstieg

| Seite | Inhalt |
|---|---|
| [Schnellstart](quick-start.md) | Aus dem Quellcode bauen oder das Paket einrichten, das Beispiel ausführen, das eigene Projekt beginnen |
| [Einrichtung des Windows-Pakets](setup.md) | `eqlmaker-setup.cmd`: Optionen, Rückgabewerte, Aufbau des Ordners, portables PostgreSQL, Betrieb ohne Internet |
| [Beispielanwendung](sample-app.md) | Das Beispiel, seine verborgenen Regeln, `eqlmaker sample` und was die Zahlen bedeuten |
| [Häufige Fragen](faq.md) | Kurze Antworten, darunter: wohin Daten gehen, personenbezogene Daten, Grenzen |
| [Anwendungen auf anderen Datenbanken](other-databases.md) | Das Prinzip, die Datenbank durch einen Server ersetzen, der in PostgreSQL speichert, was getestet ist |

## Der Ablauf

| Seite | Inhalt |
|---|---|
| [Ablauf](pipeline.md) | record → correlate → spec → generate / clone → video → verify: Eingaben, Ausgaben, Dateien, feste Regeln |
| [Fernbedienung](remote-control.md) | Windows-Programme und Webseiten bedienen: Modus real und background, Steuerelemente, Szenariodateien |
| [Handbuch](manual.md) | Das Handbuch des Originals übernehmen: Formate, was herausgelesen wird, Grenzen des PDF-Lesers |
| [PowerBuilder-Bibliotheken](powerbuilder.md) | Die Bibliotheken einer PowerBuilder-Anwendung lesen: Menüs, Fenster, Beschriftungen, SQL |
| [Geführte Schritte](guided-steps.md) | `generate`, `clone`, `enrich`: was EQLMaker vorbereitet, wer die Arbeit macht, wie geprüft wird |
| [Vergleichstests](verify.md) | `verify screens` und `verify db`: Regeln, Optionen, den Bericht lesen |
| [Zeitstempel-Leiste](timestamp-overlay.md) | Die Leiste, die die Uhrzeit in jedes Videobild bringt |
| [Video-Import](video-import.md) | Ein kommentiertes Video mit den Datenbankereignissen abgleichen; gemessene Genauigkeit |
| [Vertonung](narration.md) | Einen gesprochenen Kommentar mit optionaler Hintergrundmusik über ein fertiges Video legen (lokales Fish Speech) |

## Referenz

| Seite | Inhalt |
|---|---|
| [Befehlsreferenz](cli.md) | Jeder Befehl und jede Option von `eqlmaker` |
| [MCP-Server](mcp.md) | Einen MCP-Client anschließen; Werkzeuge und Prompts |
| [Konfiguration](configuration.md) | `eqlmaker.json`, Passwörter, wo Werkzeuge gesucht werden, Sprachen |
| [Ereignisquellen](sources.md) | Was die Erfassung auf dem Datenbankserver tut und was der Server bieten muss |
| [Ereignisformat](event-format.md) | Das JSON-Lines-Format der Datenbankereignisse, Version 0.1 |
| [Plugins](plugins.md) | Eigene Ereignisquellen, Reader, Web-Vorlagen, Klon-Renderer und Videolayouts |

## Mitmachen

| Seite | Inhalt |
|---|---|
| [Übersetzen](translating.md) | Eine Sprache für Dokumentation und Programme hinzufügen; die Prüfung |
| [Mitmachen](../../CONTRIBUTING.de.md) | Bauen, Tests, Stil, Sign-off, wie eine Funktion „verfügbar“ wird |
