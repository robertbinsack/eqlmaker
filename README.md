# EQLMaker

**Altanwendungen ohne Quellcode als moderne Web-Anwendungen neu bauen – Open Source.**

## Das Problem

Viele kleine Betriebe, Vereine, Praxen und Verwaltungen arbeiten täglich mit Windows-Fachanwendungen, die seit Jahren nicht mehr weiterentwickelt werden: Der Hersteller existiert nicht mehr, der Quellcode ist nicht verfügbar, und die darunterliegende Datenbank (z. B. Advantage, SQL Anywhere, Paradox, Access) läuft aus. Die Daten – und das fachliche Wissen, das in der Anwendung steckt – sind damit an eine Technik gebunden, die niemand mehr pflegen kann.

Eine Neuentwicklung ist für diese Nutzer meist unbezahlbar. Die Folge: Abhängigkeit, Sicherheitsrisiken und drohender Datenverlust.

## Der Ansatz

EQLMaker baut eine bestehende Anwendung als Web-Anwendung nach – **ohne Zugriff auf den Quellcode**:

1. **Beobachten auf Datenbankebene** – Während die Anwendung bedient wird, protokolliert der Datenbankserver jede Abfrage. So wird sichtbar, welche Maske welche Daten liest und schreibt.
2. **Fernsteuerung der Oberfläche** – Die Originalanwendung wird automatisiert durchgeklickt, unter Windows ebenso wie im Browser.
3. **Vorhandenes Wissen nutzen** – Handbuch und Online-Hilfe fließen als Wissensquelle ein.
4. **Erzeugen** – Daraus entsteht eine Web-Anwendung auf denselben Daten. Programmiersprache und Framework sind frei wählbar, die Oberflächen-Vorlage ist anpassbar.

## Zwei Betriebsarten

- **Migration** – Die neue Web-Anwendung ersetzt die alte Maske für Maske; beide arbeiten während der Umstellung auf denselben Daten.
- **Archivierung** – Ein lesender Klon mit der gewohnten Oberfläche im Browser. Schreibende Funktionen bleiben sichtbar, sind aber deaktiviert. Ideal, wenn Daten aufbewahrt, aber nicht mehr bearbeitet werden müssen.

Ein Nachbau trifft verborgene Rechenlogik nicht immer zu 100 %. Für die Archivierung spielt das kaum eine Rolle; bei der Migration wird jede Maske gegen das Original geprüft.

## Technik

- Läuft mit **PostgreSQL**.
- Für Anwendungen auf anderen Datenbanksystemen (z. B. SQL Server, Advantage, SQL Anywhere, Informix, Access) kann PostgreSQL durch einen kompatiblen Server mit Dialekt-Unterstützung ersetzt werden, etwa EQLServer.

## Status

Proof of Concept: Eine Hausverwaltungs-Software (PowerBuilder, SQL Anywhere) wurde vollautomatisch in rund drei Stunden als Web-Anwendung nachgebaut. Der Quellcode von EQLMaker wird schrittweise in diesem Repository veröffentlicht.

## Autor

Robert Binsack – IT Solution Architect, 30 Jahre Delphi- und Datenbankentwicklung. [binsack.com](https://www.binsack.com)
