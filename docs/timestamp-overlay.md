<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](timestamp-overlay.md) · [Deutsch](de/timestamp-overlay.md)

# Time stamp overlay

`eqlmaker-timestamp.exe` shows a strip with the current date and time, to the millisecond, on top
of all windows. Start it before you start any screen recorder. Every frame of the video then
carries the time at which it was taken, and [`eqlmaker video-import`](video-import.md) can match
the video to the database events of that moment later, even when the video was cut.

```text
eqlmaker-timestamp.exe
eqlmaker-timestamp.exe --position bottom-left --cell 6
eqlmaker-timestamp.exe --display all
eqlmaker-timestamp.exe --stop
```

| Option | Meaning |
|---|---|
| `--position` | `top-right` (default), `top-left`, `bottom-right`, `bottom-left`, or `x,y` on the display |
| `--display` | number of the display (default 1), or `all` for one strip on every display |
| `--cell` | size of one cell of the pattern in pixels (default 8: the strip is 872 x 72 pixels) |
| `--opacity` | 0.1 to 1 (default 1); keep it at 1 for recordings |
| `--lang` | `en` or `de` for the small text line |
| `--seconds` | end after this many seconds |
| `--stop` | end a running overlay |
| `--render file.png` | draw one strip into a file instead of showing a window; with `--at <UTC milliseconds>`, `--offset <minutes>`, `--mono <milliseconds>` |

The strip does not take the mouse: clicks go through to the window below. It has a button in
the task bar; close it there, or with `--stop`.

## What the strip shows

```text
+----------------------------------+-----------------------------------------------+
| [#] data cells 16 x 7        [#] |  2026-11-24 18:20:31.123 +0100                |
|                                  |  UTC+01:00   monotonic 12.345 s   display 1   |
+----------------------------------+-----------------------------------------------+
   pattern, read by the program        for people; the digits are the fallback
```

- **Date and time** of this machine with milliseconds and the offset from UTC, in large digits
  made of seven segments.
- **Monotonic time**: seconds since the overlay started. It never jumps, so a wall clock that was
  set during the recording is noticed.
- **Display**: the number of the display the strip is on.
- **Pattern**: the same values as black and white cells, with a checksum.

## The pattern (version 1)

The strip is 109 cells wide and 9 cells high. The left 34 cells are a white box. Inside it, one
cell from the edge, stand two finder squares of 7 x 7 cells (black frame, white ring, black
centre of 3 x 3) with their centres 25 cells apart, and between them 16 x 7 data cells.

The data cells hold 112 bits, row by row, most significant bit first; a black cell is 1. Each bit
is flipped where row plus column is even, so that no large area has one colour.

| Bits | Content |
|---|---|
| 4 | version (1) |
| 44 | wall clock: milliseconds since 1970-01-01 UTC |
| 11 | offset of local time from UTC in minutes, plus 1024 |
| 32 | monotonic milliseconds since the overlay started |
| 3 | display number |
| 2 | reserved (0) |
| 16 | CRC-16 (polynomial 0x1021, start value 0xFFFF) over the 96 bits before it |

The digits start 35 cells right of the left edge of the left finder, one digit every 2.5 cells;
a digit is 2 cells wide and 3.5 cells high. The text is `yyyy-MM-dd HH:mm:ss.fff +HHMM`.

## How it is read

The reader looks for the two finder squares anywhere in a frame, at any scale, takes the cell
size from their distance, and reads the data cells. If the checksum does not match (the pattern
is too small, blurred or partly covered), it reads the seven-segment digits at their known
places instead. Such a reading has the time and the UTC offset, but no monotonic time. No
general text recognition is involved; the fallback reads exactly these digits.

Measured in the tests: the pattern is read from 1920 x 1080 frames scaled down to one third and
compressed as JPEG at quality 35; the digits are read when data cells are covered.

## Accuracy

The strip is repainted every 2 ms. A display shows a new picture at its refresh rate, and a
screen recorder takes its frame some time after that: the time in a frame is therefore a few
milliseconds old, up to about one refresh interval (17 ms at 60 Hz) plus 2 ms. `video-import`
does not correct for this; the numbers are in [video import](video-import.md).

Keep the strip visible for the whole recording. A frame in which it is covered has no time of
its own; the frames around it carry the mapping.
