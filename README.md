<p align="right"><b>Deutsch</b> · <a href="README.en.md">English</a></p>

<h1 align="center">EQLMaker</h1>

<p align="center"><b>Alte Software verstehen und als moderne Webanwendung nachbauen. Ohne Quellcode.</b></p>

<p align="center">
  <img alt="Status: in Entwicklung" src="https://img.shields.io/badge/Status-in%20Entwicklung-orange">
  <img alt="Lizenz: AGPL-3.0" src="https://img.shields.io/badge/Lizenz-AGPL--3.0-blue">
  <img alt="Plattform: Windows" src="https://img.shields.io/badge/Plattform-Windows-0078D6">
  <img alt="PostgreSQL 15+" src="https://img.shields.io/badge/PostgreSQL-15%2B-336791">
  <img alt="MCP-Server" src="https://img.shields.io/badge/MCP-Server-6E56CF">
</p>

EQLMaker schaut bei der Bedienung zu, misst, was dabei in der Datenbank geschieht, und liest das Handbuch. Daraus entsteht die Grundlage für einen Nachbau, der sich mit Vergleichstests belegen lässt.

> [!NOTE]
> **Stand:** EQLMaker wird gerade gebaut. Version 0.1 ist noch nicht veröffentlicht; bis dahin gilt alles hier als „in Entwicklung“ und nichts als verfügbar. Der Quellcode folgt mit v0.1 in diesem Repository.

---

## Das Problem

Viele Betriebe, Vereine und Verwaltungen arbeiten mit alter Software, deren Hersteller es nicht mehr gibt und deren Quellcode fehlt. Sie bekommt keine Sicherheitsupdates mehr und wird zum Risiko. Die Ablösung scheitert meist nicht an der Technik, sondern daran, dass **niemand mehr genau weiß, was das Programm alles tut**.

## Was EQLMaker macht

EQLMaker sammelt das Wissen, das in einer laufenden Anwendung steckt, ohne ihren Quellcode zu brauchen:

- Jemand bedient das alte Programm wie gewohnt. EQLMaker **zeichnet auf**, was auf dem Bildschirm geschieht, und **misst** gleichzeitig, welche Daten die Anwendung in der Datenbank liest und ändert.
- Dazu kommen das **Handbuch** und auf Wunsch ein **Video mit gesprochener Erklärung**.
- Alles landet auf einer gemeinsamen **Zeitachse**: dieser Klick, diese Maske, diese Änderung in der Datenbank, dieser Satz im Handbuch.
- Aus der Zeitachse entsteht eine **Spezifikation**. Mit ihr baut ein angeschlossener KI-Assistent die Anwendung als moderne Webanwendung nach. EQLMaker liefert Vorlage und Anleitung und prüft danach mit **Vergleichstests**, ob der Nachbau dasselbe zeigt und dieselben Daten ändert wie das Original.

## So funktioniert es

~~~mermaid
flowchart LR
    subgraph IN["1 · Was hineingeht"]
        A["Bedienung<br/>aufzeichnen"]
        B["Datenbank-<br/>änderungen messen"]
        C["Handbuch<br/>einlesen"]
        D["Video mit<br/>Zeitstempel & Ton"]
    end
    subgraph MAKE["2 · Was EQLMaker daraus macht"]
        E["Zeitachse"]
        F["Spezifikation"]
        G["Nachbau<br/>(Migration / Archiv)"]
    end
    subgraph PROOF["3 · Der Beleg"]
        H["Vergleichstests"]
        I["Vergleichsvideo"]
    end
    A --> E
    B --> E
    C --> E
    D --> E
    E --> F --> G --> H --> I
~~~

| Stufe | Schritt | Was passiert | Befehl |
|---|---|---|---|
| 1 | Bedienung beobachten | Bildschirmvideo und jede Bedienhandlung mit Zeit, Fenster und Steuerelement – für Windows-Programme und Browser-Anwendungen | `record` |
| 1 | Datenänderungen messen | Anweisungen, Parameter, Transaktionen und geänderte Zeilen mit Bordmitteln von PostgreSQL. Gemessen, nicht geraten. | `record` |
| 1 | Handbuch einlesen | PDF, HTML, DOCX, Text oder Markdown; Kapitel werden mit den Masken verknüpft | `manual` |
| 1 | Video mit Zeitstempel und Ton | Eine Leiste zeigt Datum und Uhrzeit mit Millisekunden über allen Fenstern; Aufnahme mit beliebigem Bildschirmrekorder; Transkript offline per Whisper | `video-import` |
| 2 | Zeitachse | Zeitstempel im Bild wird mit den Zeiten der Datenbankänderungen abgeglichen | `correlate` |
| 2 | Spezifikation | Masken, Abfragen, gelesene und geschriebene Tabellen, Verweise ins Handbuch, Hinweise aus dem Video | `spec` |
| 2 | Nachbau | Moderne Webanwendung (Migration) oder nur lesender Klon (Archiv) – geführte Schritte für den KI-Assistenten | `generate` · `clone` · `enrich` |
| 3 | Vergleichstests | Bildvergleich der Masken und Vergleich der gemessenen Datenbankänderungen; der Bericht zeigt Abweichungen | `verify` |
| 3 | Vergleichsvideo | Original und Nachbau auf einer Zeitachse nebeneinander, mit Player | `video` |

Alle Schritte gibt es auch als Werkzeuge eines **MCP-Servers**.

## Zwei Modi

| | Modus 1 · Migration | Modus 2 · Archiv |
|---|---|---|
| **Ergebnis** | Neue Oberfläche, die liest und schreibt | 1:1-Klon im Browser, nur lesend |
| **Basis** | Moderne Webanwendung nach Vorlage, auf denselben Tabellen wie das Original | Die Masken des Originals; Speichern, Buchen und Neuanlage sichtbar, aber ausgegraut |
| **Altanwendung** | Kann währenddessen weiterlaufen | Kann abgeschaltet werden |
| **Für wen** | Wer seine Software weiter nutzen und weiterentwickeln will | Wer ein Altsystem abschalten und seine Daten weiter einsehen muss |

## Beispiele: Altanwendung und Nachbau nebeneinander

> [!IMPORTANT]
> Diese Proofs of Concept entstanden mit demselben Verfahren auf EQLServer, je Projekt von einem KI-Agenten, **bevor EQLMaker ein eigenes Programm war**. EQLMaker v0.1 hat sie nicht erzeugt. Sie zeigen, was das Verfahren leistet.

| Anwendung | Technik | KI-Erstellungszeit neue Oberfläche | Abgedeckt |
|---|---|---|---|
| Hausverwaltung | PowerBuilder 6.5 · SQL Anywhere | 24,5 Min. | 70 / 70 Menüfunktionen (100 %) |
| Warenwirtschaft | Delphi · Advantage | 16,9 Min. + 5 Std. 17 Min. Ausbau | 398 / 494 Menüfunktionen (81 %) |
| Informix-Beispielanwendung | ASP.NET · Informix | 7,7 Min. | 8 / 8 Seiten und Aktionen (100 %) |
| InvoicePlane (Open Source) | PHP · MySQL/MariaDB | 41 Min. | 81 / 82 GUI-Funktionen (99 %) |
| TurboCASH (Open Source) | Delphi · Firebird | 42 Min. | 228 / 266 Menüfunktionen (86 %) |
| Northwind (Access-Vorlage) | Microsoft Access · ODBC | 45 Min. | 58 / 58 GUI-Funktionen (100 %) |
| Contoso University (Microsoft-Beispiel) | ASP.NET Core · SQL Server | 14 Min. | 32 / 32 GUI-Funktionen (100 %) |
| Dolibarr ERP/CRM (Open Source) | PHP · PostgreSQL | 3 Std. 09 Min. | 81 / 93 GUI-Funktionen (87 %) |

▶️ **Videos (Original und Nachbau nebeneinander):** [eqlserver.de/eqlmaker](https://eqlserver.de/eqlmaker/)

<sub>Die Anwendungen stammen von Drittherstellern oder sind quelloffen. Produkt- und Herstellernamen fremder Programme sind anonymisiert.</sub>

## Funktionen

### In Entwicklung · Version 0.1

| Funktion | Beschreibung |
|---|---|
| Bedienung aufzeichnen | Windows-Programme und Browser-Anwendungen: Bildschirmvideo und jede Bedienhandlung mit Zeit, Fenster und Steuerelement |
| Gemessene Datenbankänderungen | Anweisungen, Parameter, Transaktionen und geänderte Zeilen mit Bordmitteln von PostgreSQL – nichts wird serverweit umgestellt, kein Neustart |
| Offenes Ereignisformat | Datenbankzugriffe als JSON Lines, dokumentiert, Version 0.1 |
| Handbuch einlesen | PDF, HTML, DOCX, Text oder Markdown; Kapitel werden mit Masken verknüpft |
| Zeitstempel-Leiste | Kleines Programm, das Datum und Uhrzeit mit Millisekunden über allen Fenstern zeigt |
| Video mit Ton nachreichen | Zeitstempel wird aus dem Bild gelesen und mit den Datenbankänderungen abgeglichen |
| Transkript mit Whisper | Spracherkennung über whisper.cpp, im Paket enthalten, vollständig offline |
| Zeitachse und Spezifikation | Klick, Maske, Anweisung und Transaktion auf einer Zeitachse; daraus Masken, Abfragen und Tabellen |
| Nachbau als Webanwendung | Geführter Schritt mit Web-Vorlage und Anleitung für einen angeschlossenen KI-Assistenten |
| Archiv-Klon, nur lesend | Geführter Schritt mit Referenzbildern je Maske |
| Vergleichstests | Bildvergleich und Vergleich der gemessenen Datenbankänderungen zweier Aufnahmen |
| Vergleichsvideo mit Player | Original und Nachbau auf einer Zeitachse |
| MCP-Server und Kommandozeile | Dieselben Schritte als Werkzeuge für jeden MCP-Client und als Befehle |
| Plugin-Schnittstellen | Für Ereignisquellen, Web-Vorlagen, Klon-Renderer und Videolayouts, dokumentiert |
| Einrichtung ohne Adminrechte | Eine Batchdatei richtet alles im eigenen Ordner ein: kein Installer, keine Registry, kein Dienst |
| Deutsch und Englisch | Programme, Dokumentation und Web-Vorlage in beiden Sprachen |

### Geplant · Vorhaben ab Juni 2027

Ein klar abgegrenztes neues Modul. Bisher gibt es nur das Konzept, keinen Code. Für dieses Vorhaben wird eine Förderung beantragt; eine Zusage gibt es nicht.

- **Live fragender Chatbot** – fragt während der Bedienung im richtigen Moment nach: Was tun Sie gerade? Wofür ist dieses Feld? Was darf hier nicht stehen?
- **Erkennung der Bildschirmelemente** – Fenster, Felder und Schaltflächen erhalten stabile Kennungen.
- **Regeln, an echten Daten geprüft** – aus Antworten werden lesbare Regeln („Personalnummer: nur Ziffern“), die gegen vorhandene Daten getestet werden.
- **Automatisch erzeugte neue Anleitung** – aus Schritten, Antworten und Bildschirmfotos.
- **Maskierung personenbezogener Daten** – vor dem Speichern und vor jeder Anfrage an ein Sprachmodell.

## Die ehrliche Grenze

EQLMaker sieht, was während der Aufnahme bedient wurde und was dabei in der Datenbank geschah. **Rechenlogik, die das alte Programm nie anzeigt und die in keiner Aufnahme vorkommt, kann ein Nachbau übersehen.** Die Vergleichstests zeigen Abweichungen – aber nur für das, was aufgenommen wurde.

Deshalb gehören selten benutzte Funktionen und Fehlerfälle in die Aufnahme, und bei der Migration prüft ein Mensch Maske für Maske. Beim Archiv fällt die Grenze kaum ins Gewicht, weil keine Schreib- und Buchungslogik nachgebaut wird.

Die Nachbau-Schritte sind **geführt, nicht vollautomatisch**: EQLMaker erzeugt den Code nicht selbst, sondern bereitet die Arbeit eines KI-Assistenten vor und prüft sie.

## Schnellstart (geplant)

> [!WARNING]
> Das Paket gibt es noch nicht. Namen und Optionen der Befehle können sich bis zur ersten Veröffentlichung ändern.

~~~bat
:: richtet alles im eigenen Ordner ein, ohne Adminrechte
> eqlmaker-setup.cmd
:: Bedienung und Datenbankänderungen aufzeichnen
> eqlmaker record
:: Zeitachse bilden
> eqlmaker correlate
:: Spezifikation schreiben
> eqlmaker spec
:: Nachbau vorbereiten: Migration ...
> eqlmaker generate
:: ... oder Archiv-Klon
> eqlmaker clone
:: Original und Nachbau vergleichen
> eqlmaker verify
~~~

**Voraussetzungen (geplant):** Windows, PostgreSQL ab Version 15. Kein Installationsprogramm, keine Adminrechte, keine Änderungen am System – Entfernen heißt Ordner löschen.

## Für wen – und was die Allgemeinheit davon hat

Alte Fachprogramme sind ein stilles Risiko. Das Wissen über sie steckt in den Köpfen weniger Menschen und geht mit ihnen in Rente. Besonders trifft das Stellen ohne Geld für Beratungsprojekte.

- **Menschen, die alte Programme täglich bedienen** – in kleinen Gemeinden, Vereinen, Praxen und Handwerksbetrieben. Sie sind die eigentlichen Fachleute und sollen ihr Wissen weitergeben können, ohne Workshops und ohne Programmierkenntnisse.
- **Entwicklerinnen und Entwickler, die Altsoftware ablösen** – sie bekommen statt mündlicher Überlieferung eine nachprüfbare Grundlage: Aufnahme, gemessene Datenbankänderungen, Spezifikation und Vergleichstests.
- **Stellen, die ein Altsystem abschalten müssen** – ein nur lesender Klon hält die Daten einsehbar, auch wenn das alte Programm nicht mehr läuft.

**Nutzen für die Allgemeinheit:** Das Verfahren wird offen beschrieben und als freie Software veröffentlicht, damit es nicht nur denen hilft, die sich eine teure Ablösung leisten können. Software löst das Problem nicht allein: Sie kann beobachten, vergleichen und Widersprüche zeigen; die Entscheidung bleibt bei den Menschen. Weil Bildschirme und Aufnahmen persönliche Daten enthalten können, läuft die Spracherkennung offline auf dem eigenen Rechner.

## Roadmap

- **Jetzt · in Entwicklung**
  - [ ] Version 0.1 fertig bauen und testen
  - [ ] Ende-zu-Ende-Lauf mit einer eigenen Beispielanwendung auf reinem PostgreSQL
  - [ ] Quellcode veröffentlichen, erste Veröffentlichung als Windows-Paket
- **Ab Juni 2027 · geplant**
  - [ ] Bildschirmelemente erkennen und mit Datenänderungen verknüpfen
  - [ ] Live-Dialog per Text, später per Sprache
  - [ ] Personenbezogene Daten erkennen und ersetzen
  - [ ] Regeln aus Antworten ableiten und an vorhandenen Daten prüfen
  - [ ] Anleitung mit Bildern, Deutsch und Englisch
  - [ ] Tests mit Anwenderinnen und Anwendern, Fallstudie an einer freien Anwendung
- **Später · ohne Termin**
  - [ ] Ereignisformat als eigenständige Spezifikation 1.0 für andere Werkzeuge
  - [ ] Weitere offene Quellen: MySQL/MariaDB und SQL Server
  - [ ] Barrierefreiheit der erzeugten Webanwendungen (WCAG 2.1 AA)
  - [ ] Vorlagen-Galerie für Beitragende

<sub>Die Roadmap ist ein Plan, keine Zusage.</sub>

## Verwandte Arbeiten

EQLMaker ersetzt keinen dieser Ansätze – es verbindet etwas, das sie getrennt lassen.

| Ansatz | Was er leistet | Was fehlt |
|---|---|---|
| RPA-Werkzeuge | Steuern die alte Oberfläche weiter | Lösen sie nicht ab |
| Rekonstruktion von Anwendungen | Untersuchen Programmdateien, finden Aufbau und Code | Nicht die Absicht hinter einer Bedienung |
| Low-Code-Migration | Überführen in neue Plattformen | Setzen meist Quellcode oder ein bestimmtes Ausgangsprodukt voraus |
| Klick-Dokumentation | Erzeugt bebilderte Schritte aus Klicks | Kennt die Datenbank nicht |
| Website-Klonwerkzeuge | Kopieren das Aussehen einer Seite | Nicht das Verhalten einer Anwendung |

**Was EQLMaker anders macht:** Es bringt die Bedienung und die gemessenen Datenbankänderungen auf einer Zeitachse zusammen und belegt den Nachbau mit Vergleichstests.

## Weitere Datenbanken

EQLMaker läuft vollständig mit einem normalen **PostgreSQL** und erreicht damit Anwendungen, die PostgreSQL sprechen. Für Anwendungen auf anderen Datenbankservern lässt sich PostgreSQL durch [EQLServer](https://eqlserver.de) ersetzen; die Konfiguration bleibt gleich. EQLServer ist ein kommerzielles Produkt desselben Autors und **kein Teil von EQLMaker**. Kein Teil von EQLMaker setzt EQLServer voraus.

## Mitmachen und Lizenz

- **Lizenz:** [AGPL-3.0](https://www.gnu.org/licenses/agpl-3.0.html) (GNU Affero General Public License, Version 3 oder später). Urheber ist Robert Binsack.
- **Beiträge:** über das [Developer Certificate of Origin (DCO)](https://developercertificate.org/). Wer beiträgt, bestätigt mit der Zeile `Signed-off-by` im Commit, dass der Beitrag unter der Lizenz des Projekts eingebracht werden darf. Es gibt keine Sonderrechte an Beiträgen.
- **Kontakt:** office@eqlserver.de · [binsack.com](https://www.binsack.com)
