<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](../guided-steps.md) · [Deutsch](guided-steps.md)

# Geführte Schritte: generate, clone, enrich

Drei Schritte des Ablaufs sind geführte Schritte. EQLMaker schreibt darin nicht die neue
Anwendung. Es bereitet einen Arbeitsordner vor, schreibt die Anleitung, hält Entscheidungen fest
und prüft das Ergebnis mit Vergleichstests. Die Arbeit selbst erledigt ein Assistent, der als
MCP-Client angeschlossen ist, oder ein Mensch, der derselben Anleitung folgt.

| | EQLMaker bereitet vor | Der MCP-Client oder Mensch | Geprüft durch |
|---|---|---|---|
| `generate` | Ordner `web/` aus einer Web-Vorlage, die Spezifikation daneben, `GENERATE.md` | baut die Webanwendung | `verify db` |
| `clone` | Ordner `clone/` mit Referenzbild und Gerüstseite je Maske, ein nur lesender Server, `CLONE.md` | füllt die Seiten mit Live-Daten | `verify screens` |
| `claude-app` | Ordner `claude-app/` mit eigenem MCP-Server, Anwendungsbeschreibung, funktionierender Seite, Eintrag für Claude Desktop, `CLAUDE-APP.md`; siehe [Claude-Apps](claude-app.md) | vervollständigt Beschreibung und Masken, installiert die App | `verify db`, die Tests in `claude-app/mcp-server` |
| `enrich` | Ordner `enrich/<name>/` mit dem Stand vorher und `ENRICH.md` mit jedem Befund | entscheidet jeden Befund und ändert die Webanwendung | den Bericht von `enrich --finish`, `verify db` |

Die Anleitung ist derselbe Text an drei Stellen: die Datei im Arbeitsordner, der MCP-Prompt
gleichen Namens und `eqlmaker prompt generate` (oder `clone`, `enrich`) auf der Kommandozeile.

Alle drei brauchen eine Spezifikation: Führen Sie zuerst `spec` aus.

## generate

```text
eqlmaker generate [--template <name>] [--force]
```

**Was EQLMaker vorbereitet**

- `web/`: eine Kopie der Web-Vorlage. Eingebaut ist `default`: ein Node.js-Server ohne
  Framework, der Treiber `pg` als einzige Abhängigkeit, Listenseiten mit Bearbeitungsdialogen,
  die aus einfachen Entitätsbeschreibungen entstehen, englische und deutsche Ressourcen. Weitere
  Vorlagen sind [Plugins](plugins.md). Die Platzhalter `{{APP_NAME}}` (Name des Projektordners)
  und `{{DB_ENV}}` (`APP_DB`, die Umgebungsvariable mit der Datenbankverbindung) werden gefüllt.
- `web/spec/spec.json`, `web/spec/spec.md` und `web/spec/manual-<name>.txt` für jedes Handbuch.
- `web/GENERATE.md`: die Anleitung, mit der Liste der Schritte und Masken und den offenen
  Punkten der Spezifikation.

Ein vorhandenes `web/` wird abgelehnt; `--force` kopiert die Vorlage darüber und lässt Dateien
mit anderen Namen in Ruhe.

**Was der MCP-Client oder Mensch tut**

Er liest die Spezifikation und die Referenzbilder, beschreibt die Tabellen als Entitäten, fügt
Routen und Seiten für Schritte mit eigener Logik hinzu, startet die Anwendung und bedient sie.
Die Regeln aus `GENERATE.md` in Kürze: jeden aufgezeichneten Schritt nachbauen; nichts erfinden;
unklares Verhalten nach `web/OPEN-POINTS.md` schreiben, statt zu raten; das Datenmodell
beibehalten; nur parametrisierte Anweisungen; eine Transaktion, wo das Original eine verwendet
hat; eine stabile `id` für jedes Element, das ein Test bedienen muss.

**Wie geprüft wird**

Setzen Sie die Datenbank auf den Stand zurück, von dem die Aufnahme des Originals ausging.
Zeichnen Sie dieselben Schritte in der neuen Anwendung mit denselben Kapitelüberschriften auf.
Dann:

```text
eqlmaker verify db <original> <web>
```

Der Bericht sagt je Schritt, ob die nachgebaute Anwendung dieselben Zeilen mit denselben Werten
geändert hat. Siehe [Vergleichstests](verify.md). Was noch abweicht, wird behoben oder mit
seinem Grund in `web/OPEN-POINTS.md` aufgeführt.

## clone

```text
eqlmaker clone [--renderer <name>] [--force]
```

Der Klon ist das nur lesende 1:1-Archiv: die Masken des Originals, wie sie aussahen, gefüllt mit
Live-Daten, ohne jede Möglichkeit, Daten zu ändern.

**Was EQLMaker vorbereitet**

- `clone/reference/<mask>.png`: das Referenzbild jeder Maske, aus der Aufnahme.
- `clone/public/<mask>.html`: eine Seite je Maske, erzeugt vom Klon-Renderer. Der eingebaute
  Renderer `backdrop` legt das Referenzbild hinter die Seite und je aufgezeichnetem
  Steuerelement ein positioniertes Element darüber. Bis Daten angebunden sind, zeigt die Seite
  genau das Referenzbild. Steuerelemente, die bei ihrer Benutzung in der Aufnahme Daten geändert
  haben, sind ausgegraut und lassen sich nicht bedienen. Weitere Renderer sind
  [Plugins](plugins.md).
- `clone/masks.json`: die Liste der Masken und Seiten.
- `clone/server.js`, `clone/queries.js`: ein kleiner Server, der jede Abfrage in einer
  Transaktion `READ ONLY` ausführt und nur einzelne Anweisungen annimmt, die mit `SELECT` oder
  `WITH` beginnen.
- `clone/CLONE.md`: die Anleitung.

Eine Maske ohne Referenzbild erhält keine Seite; `masks.json` sagt das.

**Was der MCP-Client oder Mensch tut**

Maske für Maske: Er bindet Listen und Felder an die Anweisungen der Spezifikation, überdeckt den
passenden Bereich des Hintergrundbilds, gleicht Schrift und Abstände an, lässt die Navigation
zwischen den Masken funktionieren und graut weitere schreibende Steuerelemente aus, die in der
Aufnahme nicht benutzt wurden.

**Wie geprüft wird**

```text
eqlmaker web-shot <url of the page> shots\<mask>.png --size <width>x<height>
eqlmaker verify screens clone\reference shots
```

Daten, die sich seit der Aufnahme geändert haben, weichen berechtigt ab: Vergleichen Sie auf dem
Datenbankstand der Aufnahme, oder nehmen Sie den Bereich mit `--ignore` aus und sagen Sie das.
Um zu prüfen, dass nichts geschrieben werden kann, bedienen Sie jedes ausgegraute Steuerelement,
während eine Aufnahme läuft: Die Aufnahme darf keine Transaktion enthalten.

## enrich

`enrich` übernimmt die Befunde eines kommentierten Videos in die Webanwendung. Befunde kommen aus
[`video-import`](video-import.md): je Vorgang, was gesagt wurde, was zu sehen war und was die
Datenbank tat, das die Spezifikation nicht enthält. Sie sind Kandidaten. EQLMaker bewertet sie
nicht.

```text
eqlmaker enrich [<A> <B>] [--findings <name>]
eqlmaker enrich --decide <finding> --status adopted|rejected|open --note "..." [--files a,b] [--findings <name>]
eqlmaker enrich --finish [<A> <B>] [--findings <name>]
```

**Was EQLMaker vorbereitet**

`eqlmaker enrich` schreibt `enrich/<name>/`:

- `state.json`: eine Prüfsumme jeder Datei von `web/` vor der Arbeit, die Entscheidungen und,
  wenn zwei Aufnahmen `<A>` (Original) und `<B>` (Webanwendung) genannt sind, das Ergebnis von
  `verify db` vorher.
- `ENRICH.md`: die Anleitung mit jedem Befund und seiner Kennung (`f1`, `f2`, ...).

`--findings <name>` ist nur nötig, wenn das Projekt mehr als einen Satz Befunde hat.

**Was der MCP-Client oder Mensch tut**

Er entscheidet jeden Befund und hält die Entscheidung mit Begründung fest:

| Status | Bedeutung | Die Notiz sagt |
|---|---|---|
| `adopted` | die Webanwendung muss es haben und hatte es nicht: `web/` wurde geändert | was geändert wurde und wo |
| `rejected` | für die Anwendung nicht von Belang | warum |
| `open` | von Belang, aber aus dem Bekannten nicht zu bauen | was fehlt |

Ein gesprochener Satz ist ein Hinweis auf die Absicht, kein Beweis für das Verhalten. Wo er den
aufgezeichneten Datenbankänderungen widerspricht, bleibt das aufgezeichnete Verhalten, und der
Befund ist `open`.

**Der Bericht**

Nach den Änderungen zeichnen Sie die Schritte in der geänderten Anwendung unter neuem Namen
erneut auf und beenden den Schritt:

```text
eqlmaker enrich --finish <original> <new recording>
```

Das schreibt `enrich/<name>/report.json` und `report.md`:

- jeden Befund als adopted, rejected oder open mit seiner Begründung; ein Befund ohne
  Entscheidung wird als open mit „no decision was recorded“ gemeldet;
- die Dateien von `web/`, die seit dem Beginn geändert, hinzugefügt oder entfernt wurden;
- den Datenbankvergleich vorher und nachher;
- Warnungen: Befunde, die als adopted markiert sind, obwohl sich keine Datei geändert hat; ein
  Vergleich, der vorher gleich war und jetzt abweicht; ein Vergleich nachher, der dieselbe
  Aufnahme wie vorher verwendet hat.

## Was geführte Schritte nicht können

- Die Qualität der nachgebauten Anwendung ist die Qualität der Arbeit des Assistenten oder
  Menschen. EQLMaker zeigt, wo das Ergebnis beim Aufgezeichneten vom Original abweicht; es
  repariert es nicht.
- Logik, die in keiner Aufnahme benutzt wurde, steht nicht in der Spezifikation und nicht im
  Vergleich.
- Die Web-Vorlage erhebt keinen Anspruch auf Barrierefreiheit.
