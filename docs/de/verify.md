<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](../verify.md) · [Deutsch](verify.md)

# Vergleichstests

`verify` vergleicht das Original mit der nachgebauten Anwendung. Es gibt zwei Tests: `screens`
vergleicht Bilder, `db` vergleicht die gemessenen Datenbankänderungen zweier Aufnahmen. Beide
schreiben einen Bericht und enden mit Rückgabewert 0, wenn der Test bestanden ist, sonst mit 2.

## verify screens

```text
eqlmaker verify screens <A> <B> [--tolerance 16] [--max-percent 0.5] [--ignore x,y,w,h;x,y,w,h] [--name <name>]
```

`<A>` ist die Referenz, `<B>` das Geprüfte. Beide sind Bilddateien, oder beide sind Ordner;
Bilder in Ordnern werden über den Dateinamen gepaart (`*.png`).

**Regeln**

- Zwei Bilder müssen gleich groß sein. Bilder unterschiedlicher Größe fallen durch.
- Ein Bildpunkt weicht ab, wenn einer seiner Farbkanäle um mehr als die Toleranz abweicht
  (Vorgabe 16 von 255).
- Ein Paar besteht, wenn höchstens `--max-percent` der verglichenen Bildpunkte abweichen
  (Vorgabe 0.5).
- Bereiche, die mit `--ignore` angegeben sind, werden nicht verglichen. Jeder Bereich ist
  `x,y,w,h` in Bildpunkten; mehrere werden durch `;` getrennt.
- Der Test besteht, wenn jedes Paar besteht, kein Bild ohne Partner ist und es mindestens ein
  Paar gibt.

**Ergebnis** in `verify/<name>/` (Vorgabename `screens`):

| Datei | Inhalt |
|---|---|
| `report.json` | `tolerance`, `max_percent`, `ignored`, `pairs`, `failed`, `unpaired`, `passed` und je Paar: `same_size`, `compared_pixels`, `different_pixels`, `percent`, `bounds_of_differences`, `passed` |
| `<picture>.diff.png` | für ein Paar mit Abweichungen: das Bild aufgehellt, jeder abweichende Bildpunkt in Rot |

Bilder einer Webseite in der Größe der Referenz entstehen mit:

```text
eqlmaker web-shot <url> <file.png> --size 1280x800
```

## verify db

```text
eqlmaker verify db <A> <B> [--ignore table.column,...] [--keys table.column,...] [--references table.column=table,...] [--name <name>]
```

`<A>` ist die Aufnahme des Originals, `<B>` die Aufnahme der nachgebauten Anwendung. Beide
müssen mit geänderten Zeilen (`wal_level = logical`) und mit denselben Kapitelüberschriften
aufgezeichnet sein, und beide Läufe sollten vom selben Datenbankstand ausgehen.

**Regeln**

1. Die geänderten Zeilen werden je Schritt verglichen. Ein Schritt ist ein Kapitel; `correlate`
   ordnet die Zeilen den Kapiteln zu.
2. Innerhalb eines Schritts werden die Zeilen beider Aufnahmen als Mengen verglichen: Die
   Reihenfolge zählt nicht, die Anzahl schon. Eine Zeile ist ihre Tabelle, ihre Operation und
   ihre Spaltenwerte.
3. **Schlüssel, die während einer Aufnahme entstanden sind**, werden nach der Reihenfolge ihrer
   Entstehung verglichen, nicht nach ihrem Wert. Die Schlüsselspalten sind in der Vorgabe
   `*.id`: Die erste in `invoice` eingefügte Zeile ist auf beiden Seiten
   `<new public.invoice #1>`, welche Nummer die Datenbank ihr auch gegeben hat. Eine Spalte
   namens `<table>_id`, die einen solchen Schlüssel enthält, wird genauso behandelt. Schlüssel,
   die es vor der Aufnahme gab, werden wörtlich verglichen.
4. Werte, die wie ein Zeitstempel aussehen (`2026-11-24 18:20:31...`), werden als „Zeit des
   Laufs“ verglichen. Der Bericht nennt jede Spalte, bei der das geschah.
5. Spalten, die mit `--ignore` genannt sind, werden ausgelassen.
6. Alles andere wird wörtlich verglichen, als Text.
7. Das Ergebnis ist nur dann `equal`, wenn auf keiner Seite eine Zeile übrig bleibt, jeder
   Schritt in beiden Aufnahmen vorkommt und mindestens eine Zeile verglichen wurde. Zwei
   Aufnahmen ohne geänderte Zeilen sind nie gleich.

**Optionen**

| Option | Bedeutung | Auch in `eqlmaker.json` |
|---|---|---|
| `--ignore` | auszulassende Spalten: `invoice.updated_at`, `*.changed` | `verify.ignoreColumns` |
| `--keys` | Schlüsselspalten, deren neue Werte nach der Reihenfolge ihrer Entstehung verglichen werden (Vorgabe `*.id`) | `verify.keyColumns` |
| `--references` | Spalten, die einen Schlüssel einer anderen Tabelle enthalten, wenn der Name das nicht sagt: `invoice_line.inv=invoice` | `verify.references` |
| `--name` | Name des Ergebnisordners (Vorgabe `db-<A>-<B>`) | |

Ein Spaltenmuster ist `table.column`, `*.column` oder `schema.table.column`.

**Ergebnis** in `verify/<name>/`: `report.json` und `report.txt`. Der Befehl gibt den lesbaren
Bericht aus.

### Den Bericht lesen

```text
Database changes: desktop (A) against web-flawed (B)
RESULT: DIFFERENT (9 rows agree, 5 only in A, 2 only in B)

[ok] New customer: A 1 rows, B 1 rows

[ok] Change article price: A 1 rows, B 1 rows

[DIFF] New invoice: A 11 rows, B 8 rows
      only A: insert public.invoice customer_id=<new public.customer #1> discount=31.45 discount_percent=5.00 gross=705.79 id=<new public.invoice #1> invoice_date=2026-11-24 net=628.90 number=INV-2026-00004 status=open tax=108.34
      only A: insert public.stock_movement article_id=3 id=<new public.stock_movement #3> invoice_id=<new public.invoice #1> quantity=-3 reason=invoice
      only A: insert public.stock_movement article_id=6 id=<new public.stock_movement #1> invoice_id=<new public.invoice #1> quantity=-14 reason=invoice
      only A: insert public.stock_movement article_id=7 id=<new public.stock_movement #2> invoice_id=<new public.invoice #1> quantity=-7 reason=invoice
      only B: insert public.invoice customer_id=<new public.customer #1> discount=31.45 discount_percent=5.00 gross=705.78 id=<new public.invoice #1> invoice_date=2026-11-24 net=628.90 number=INV-2026-00004 status=open tax=108.33

[DIFF] Mark invoice paid: A 1 rows, B 1 rows
      only A: update public.invoice ... gross=705.79 ... status=paid tax=108.34
      only B: update public.invoice ... gross=705.78 ... status=paid tax=108.33
```

- `[ok]` und `[DIFF]` kennzeichnen jeden Schritt.
- `only A` ist eine Änderung, die das Original gemacht hat und die nachgebaute Anwendung nicht;
  `only B` das Umgekehrte. Ein abweichender Wert erscheint als je eine Zeile auf beiden Seiten.
- `step missing in A` oder `B` heißt, dass die Kapitelüberschrift nur auf einer Seite vorkommt.
- `<new public.invoice #1>` ist ein Schlüssel, der während der Aufnahme entstanden ist
  (Regel 3); `<time of the run>` ist ein Zeitstempel (Regel 4).
- Zeilen mit `note:` am Ende nennen die Spalten, die als Zeit des Laufs verglichen wurden, und
  sagen, wenn keine der beiden Aufnahmen geänderte Zeilen hat.

Der Bericht oben ist der des Beispiels mit seinen absichtlichen Fehlern, in den letzten beiden
Zeilen gekürzt.

## Die absichtlichen Fehler des Beispiels

Die Browser-Variante der Beispielanwendung lässt sich mit zwei eingebauten Fehlern starten
(`SAMPLE_FLAW=1`): Sie rundet die Steuer je Zeile statt einmal je Steuerklasse, und sie schreibt
keine Lagerbewegungen. Keiner von beiden ist auf einem Bildschirm zu sehen.

```text
eqlmaker sample --flaw
```

`verify db` findet beide: 5 Zeilen nur im Original (drei Lagerbewegungen und die Rechnung mit
Steuer 108.34 beim Schreiben und beim Markieren als bezahlt), 2 nur in der nachgebauten
Anwendung (dieselbe Rechnung mit Steuer 108.33). Der Lauf gilt als bestanden, wenn die
Abweichungen gefunden werden. Ohne `--flaw` lautet das Ergebnis „equal (14 changed rows
agree)“. Siehe [Beispielanwendung](sample-app.md).

## Grenzen

- `verify db` vergleicht, was aufgezeichnet wurde. Ein Schritt, den niemand aufgezeichnet hat,
  wird nicht verglichen.
- Die Zuordnung der Zeilen zu Schritten folgt `correlate`; ohne Transaktionskennungen im
  Serverprotokoll ist sie eine Näherung über Zeit und Tabelle. Siehe [Ablauf](pipeline.md).
- Regel 3 setzt voraus, dass beide Anwendungen Zeilen innerhalb einer Tabelle in derselben
  Reihenfolge anlegen.
- `verify screens` vergleicht Bildpunkte. Andere Schriften, ein anderer Fensterrahmen oder
  andere Daten ergeben Abweichungen; nehmen Sie Bereiche, die berechtigt abweichen, mit
  `--ignore` aus und sagen Sie das in Ihrem Bericht.
