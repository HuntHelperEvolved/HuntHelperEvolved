# Third-party notices

Hunt Helper Evolved is itself MIT licensed; see [LICENSE](LICENSE).

It includes and derives from the following, each MIT licensed.
Their copyright and permission notices are reproduced in full below, as the MIT
licence requires.

---

## Hunt Helper — img02 (imaginary-png)

<https://github.com/img02/HuntHelper>

Used here:

- `Data/SpawnPointData.json` is a verbatim copy of Hunt Helper's file of the
  same name. `SpawnPointData.cs` is that data converted to source.
- Territory ids in `SRankZoneReminder.cs` and elsewhere are taken from Hunt
  Helper's `Enums.cs`.
- Mark names and ids in `SRankData.cs` and `OtherRankData.cs` are taken from
  Hunt Helper's bundled `Data/*-A.json`, `*-B.json` and `*-S.json`.
- The map overlay's design — the detection circle at two map coordinates, the
  projected path drawn at the circle's diameter, the heading line and the
  position dot, and the order they are drawn in — follows Hunt Helper's
  `Gui/MapUI.cs`. The implementation here is its own, but the behaviour is
  deliberately theirs.

Hunt Helper Evolved also reads Hunt Helper's train over its IPC at runtime, and
Hunt Helper remains a separate plugin worth having.

```
MIT License

Copyright (c) 2022 imaginary-png

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## KamiToolKit — MidoriKami

<https://github.com/MidoriKami/KamiToolKit>

Referenced as a NuGet package and **redistributed** — `KamiToolKit.dll` ships
inside the release archive. Used by `HuntMapOverlay.cs` and
`WorldSizedMarker.cs` to draw markers over the game's own map.

Its licence is MIT. The package's own copy of that licence travels with it; see
the project above for the authoritative text.

---

## Faloop

<https://faloop.app/>

The SS event coordinates in `SsMinionSpawns.cs` — four minion spots and one
mark spawn for each of the eighteen ShB, EW and DT hunt zones — were taken from
Faloop's published hunt data. Facts about the game rather than code, recorded
here so their source is not lost.
