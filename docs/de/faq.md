<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](../faq.md) · [Deutsch](faq.md)

# Häufige Fragen

Kurze Antworten auf häufige Fragen. Jede Antwort nennt die Seite mit den Einzelheiten.

## Werden meine Daten irgendwohin gesendet?

Nein. EQLMaker nutzt das Netz nur für das, was Sie einrichten: die Verbindung zu Ihrem
Datenbankserver und den Browser und die Programme, die es auf demselben Rechner bedient. Es hat
keine Telemetrie, keine Suche nach Aktualisierungen und kein Konto. Die Spracherkennung läuft
offline auf Ihrem Rechner.

Zwei Dinge liegen außerhalb von EQLMaker und in Ihrer Hand. Die Einrichtung des Windows-Pakets
lädt Komponenten von festgelegten Adressen herunter, sofern Sie sie nicht mit `-offline`
ausführen. Und wenn Sie einen Assistenten als MCP-Client anschließen, sieht dieser Assistent,
was die Werkzeuge zurückgeben: Bildschirmfotos, die Spezifikation, Anweisungen mit ihren Werten.
Wohin der Assistent das sendet, entscheidet der Assistent, den Sie gewählt haben, nicht EQLMaker.

## Was ist mit personenbezogenen Daten in Aufnahmen?

Sie werden in Version 0.1 nicht maskiert. Eine Aufnahme enthält das Bildschirmvideo,
Referenzbilder, getippten Text, SQL-Anweisungen mit ihren Parametern und die geänderten Zeilen;
ein importiertes Video bringt Ton und Transkript dazu. Behandeln Sie einen Projektordner wie die
Daten der Anwendung selbst: Zeichnen Sie nach Möglichkeit auf Testdaten auf, veröffentlichen Sie
keine Aufnahmen echter Daten, und löschen Sie, was Sie nicht mehr brauchen. Die Maskierung
personenbezogener Daten ist geplant, siehe [Roadmap](../../ROADMAP.de.md).

## Brauche ich den Quellcode der alten Anwendung?

Nein. EQLMaker beobachtet die laufende Anwendung und die Datenbank. Es liest und ändert die
Programmdateien nicht.

## Schreibt EQLMaker die neue Anwendung?

Nein. `generate`, `clone` und `enrich` sind geführte Schritte: EQLMaker bereitet Arbeitsordner
und Anleitung vor und prüft das Ergebnis mit Vergleichstests. Ein als MCP-Client angeschlossener
Assistent oder ein Mensch erledigt die Arbeit. Siehe [geführte Schritte](guided-steps.md).

## Welche Anwendungen kann es aufzeichnen?

Anwendungen, deren Datenbank PostgreSQL ab Version 15 ist, unter Windows: Windows-Programme und
Webseiten. Für Anwendungen auf anderen Datenbankservern lässt sich PostgreSQL durch EQLServer
ersetzen, ein kommerzielles Produkt desselben Autors, das kein Teil von EQLMaker ist; siehe
[Ereignisquellen](sources.md). Quellen für weitere Server sind geplant.

## Was ändert die Erfassung an meinem Datenbankserver?

Für die Rollen und Datenbanken, die Sie nennen, schaltet sie für die Dauer der Aufnahme das
Protokollieren der Anweisungen ein und setzt es danach zurück. Mit `wal_level = logical` legt
sie je erfasster Datenbank einen Replikations-Slot an und löscht ihn am Ende. Sie ändert keine
serverweite Einstellung und startet den Server nie neu. Nach einer abgebrochenen Aufnahme räumt
`eqlmaker reset` auf. Siehe [Ereignisquellen](sources.md).

## Warum hat meine Aufnahme keine geänderten Zeilen?

Der Server läuft nicht mit `wal_level = logical`. Die Aufnahme sagt das in ihren Warnungen.
Anweisungen werden trotzdem erfasst, aber `verify db` braucht geänderte Zeilen. `wal_level` zu
setzen braucht den Administrator des Servers und einen Neustart.

## Warum werden gar keine Anweisungen erfasst?

Meist lief die Anwendung schon, als die Aufnahme begann: Die Erfassung gilt für Sitzungen, die
danach beginnen. Andere Ursachen: Die Datenbank oder Rolle ist unter `capture` in
`eqlmaker.json` nicht genannt, oder das Serverprotokoll ist nicht lesbar, siehe
[Konfiguration](configuration.md).

## Findet es Logik, die ich nicht bedient habe?

Nein. Die Spezifikation enthält nur, was während einer Aufnahme bedient wurde, und die
Vergleichstests vergleichen nur, was aufgezeichnet wurde. Zeichnen Sie auch selten benutzte
Funktionen und Fehlerfälle auf. Bei einer Migration prüft ein Mensch Maske für Maske.

## Wie genau ist die Verbindung zwischen einem Klick und seinen Datenbankänderungen?

Anweisungen tragen die Zeit des Serverprotokolls, auf die Millisekunde genau. Welche Zeilen eine
Anweisung geändert hat, ist exakt, wenn der Server Transaktionskennungen protokolliert (`%x` in
`log_line_prefix`); sonst wird es aus Zeit und Tabelle abgeleitet, und die Methode steht neben
jeder Verbindung. Siehe [Ablauf](pipeline.md).

## Übernimmt es meine Maus und Tastatur?

Im Modus `real` ja, solange es ein Programm bedient. Im Modus `background` nein: Es sendet
Fensternachrichten, und das Programm darf sogar auf einem Desktop laufen, der nie angezeigt
wird. Der Modus `background` funktioniert für Programme, die auf solche Nachrichten reagieren.
Siehe [Fernbedienung](remote-control.md).

## Werden meine Bedienhandlungen aufgezeichnet, wenn ich von Hand arbeite?

Nein. Mit `record --start` hat eine Aufnahme Video und Datenbankereignisse. Bedienhandlungen mit
Fenster und Steuerelement werden aufgezeichnet, wenn EQLMaker das Programm bedient, aus einer
Szenariodatei oder für einen MCP-Client.

## Brauche ich Adminrechte?

Nicht für EQLMaker und nicht für die Einrichtung des Pakets. Die Erfassung braucht einen
Superuser des Datenbankservers. Siehe [Einrichtung](setup.md).

## Welche Sprachen gibt es?

Englisch und Deutsch, für Programme, Dokumentation und die Web-Vorlage. Wie man eine hinzufügt,
steht unter [Übersetzen](translating.md).

## Was sind die Grenzen der Version 0.1?

- Nur Windows; nur PostgreSQL (dazu der oben genannte Adapter).
- Keine Maskierung personenbezogener Daten.
- Wörter in Bildern und Schlüsselbildern werden nicht gelesen; der PDF-Leser liefert für Scans
  keinen Text.
- Das Transkript ist so gut wie das Sprachmodell.
- Die Web-Vorlage erhebt keinen Anspruch auf Barrierefreiheit.
- Ob es ein Release-Paket gibt, steht am Anfang des [README](../../README.md).
