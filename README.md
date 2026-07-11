# Limbus Assistant

![Windows](https://img.shields.io/badge/platform-Windows%2010%2B-blue)
![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)
![Read-only](https://img.shields.io/badge/game%20access-read--only-brightgreen)
![License](https://img.shields.io/badge/license-MIT-lightgrey)

A combat advisor overlay for **Limbus Company**. It watches your screen, reads
the current clash, and shows you the *real* win rate — the full coin-by-coin
calculation, not the simplified number the game displays — plus expected damage,
so you can pick the right skill with confidence.

> 📸 *Screenshot placeholder — overlay showing a live clash win rate over the game.*

## Safe by design

Limbus Assistant is a **read-only overlay**. It never touches the game:

- ❌ No injecting, hooking, or attaching to the game process
- ❌ No reading or writing game memory
- ❌ No clicks, keystrokes, macros, or any automation — it can't play for you
- ✅ It only looks at pixels on your screen and draws its own separate window

To the game, it is indistinguishable from streaming software or a screen
recorder. There is nothing for anti-cheat to detect, because nothing ever
touches the game.

## Features

- 🎯 **True clash win rate** — full coin-by-coin probability, modelling continued
  clashes after a lost coin (ported from vetted community calculators)
- ⚔️ **Expected damage** — weighted by sin/physical resistances, stagger state,
  offense/defense levels, and clash-count bonuses
- 🧠 **Sanity-aware** — heads chance shifts with your sinner's SP, just like in game
- 📊 **What-if planner** — pit any identity in the bundled dataset against any
  enemy and see every matchup ranked
- 👻 **Click-through overlay** — the game stays fully playable underneath
- 🔧 **Debug view** — see exactly what the vision system reads and where

## Installation

1. Download the latest release `.zip` from the releases page.
2. Unzip it anywhere (e.g. your Desktop).
3. Start **Limbus Company** in windowed or borderless mode.
4. Double-click `LimbusAssistant.exe`.
5. The debug panel opens first — check it says *Capturing* and that the region
   boxes line up with the clash UI. If they don't, adjust
   `%AppData%\LimbusAssistant\calibration.json` (values are fractions of the
   window size) until they do.
6. Press `Ctrl+F8` in a fight and the advisor appears. That's it.

Building from source instead:

```
dotnet build
dotnet run --project src/LimbusAssistant
```

## Hotkeys & settings

| Hotkey | Action |
| --- | --- |
| `Ctrl+F8` | Show / hide the advisor overlay |
| `Ctrl+F9` | Show / hide the debug panel |

Settings live in `%AppData%\LimbusAssistant\settings.json`:

| Setting | Default | Meaning |
| --- | --- | --- |
| `WindowTitle` | `LimbusCompany` | Title of the game window to capture |
| `ToggleOverlayHotkey` | `Ctrl+F8` | Overlay toggle hotkey |
| `ToggleDebugHotkey` | `Ctrl+F9` | Debug panel toggle hotkey |
| `CaptureIntervalMilliseconds` | `250` | How often the screen is re-read |
| `MinimumConfidence` | `0.5` | Below this, a reading shows `?` instead of a guess |

## How it works

1. **Capture** — the game window is captured with Windows Graphics Capture
   (the same API OBS uses), with a GDI fallback.
2. **Vision** — fixed UI regions are cropped out; icons are recognised by
   template matching and numbers by on-device Windows OCR. Every reading gets a
   confidence score; anything uncertain becomes "unknown", never a wrong number.
3. **Clash engine** — skill base power, coin power, coin count and sanity feed a
   coin-by-coin probability tree (ported from open-source community
   calculators), which yields the true win rate and the expected damage after
   resistances.
4. **Overlay** — results are drawn in a transparent, click-through window
   floating above the game.

Identity and enemy data ship as plain JSON in the `Data` folder — you can add
your own identities and enemies without touching code.

## FAQ

**Can I get banned for this?**
The tool never interacts with the game process in any way — it only reads the
screen, like a capture card. There is nothing to detect. That said, use any
third-party tool at your own discretion.

**Does it play the game for me?**
No, and it never will. It gives advice; you make the plays.

**The overlay shows "screen numbers only"?**
The vision system read the clash values but couldn't identify the exact skills,
so coin power is unknown and the estimate is rougher. Add template crops of your
skills under `Assets/Templates` (see the README there) for full-fidelity math.

**Does it work in fullscreen?**
Borderless windowed works best. Exclusive fullscreen can block window capture on
some setups.

**Which resolutions are supported?**
Any — regions are stored as fractions of the window size. If your aspect ratio
is unusual, verify alignment in the debug panel and tweak
`calibration.json` once.

## Not affiliated with Project Moon

This is an unofficial fan-made tool. Limbus Company and all related assets are
the property of Project Moon. This project is not endorsed by or associated
with Project Moon in any way.
