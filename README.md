# Limbus Assistant

![Windows](https://img.shields.io/badge/platform-Windows%2010%2B-blue)
![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)
![Read-only](https://img.shields.io/badge/game%20access-read--only-brightgreen)
![License](https://img.shields.io/badge/license-MIT-lightgrey)

A combat advisor overlay for **Limbus Company**. It watches your screen, reads
the current clash, and shows you the *real* win rate (the full coin-by-coin
calculation, not the simplified number the game displays) plus expected damage,
so you can pick the right skill with confidence.

> 📸 *Screenshot placeholder: overlay showing a live clash win rate over the game.*

## Safe by design

Limbus Assistant is a **read-only overlay**. It never touches the game:

- ❌ No injecting, hooking, or attaching to the game process
- ❌ No reading or writing game memory
- ❌ No clicks, keystrokes, macros, or any automation: it can't play for you
- ✅ It only looks at pixels on your screen and draws its own separate window

To the game, it is indistinguishable from streaming software or a screen
recorder. There is nothing for anti-cheat to detect, because nothing ever
touches the game.

## Features

- 🚦 **Traffic light (the noob mode)**: drag any skill onto any enemy attack
  and a giant badge appears: ✅ TAKE IT, ⚠️ COIN FLIP, or ❌ FIND BETTER. If
  another target is clearly better for that same card, one line tells you
  where: "Better: put it on 'Claw Swipe' ✅". Zero setup, works from the first
  fight. Hover an enemy attack and it names your best answer instead.
- 🎓 **Backseat coach**: with your team set (once), an "Ideal turn" panel
  suggests a full plan, one short line at a time, ticking itself off as you
  assign moves. It warns when an enemy hit cannot be blocked and whose
  portrait to press for Defend. Also speaks German (`"Language": "de"`).
- 🎯 **True clash win rate**: full coin-by-coin probability, modelling continued
  clashes after a lost coin (ported from vetted community calculators)
- ⚔️ **Expected damage**: weighted by sin/physical resistances, stagger state,
  offense/defense levels, and clash-count bonuses
- 🧠 **Sanity-aware**: heads chance shifts with your sinner's SP, just like in game
- 🧮 **Clash calculator**: type in any two skills and get the exact win, loss,
  and stalemate odds plus expected coins and damage
- 🧭 **Turn advisor**: build your team, pick any enemy, and an expected-value
  solver assigns every sinner the best clash (or a free hit), accounting for
  the damage that unblocked enemy skills deal to you
- 📊 **Full enemy roster**: search **any enemy in the game** (900+ enemies and
  bosses imported from the community wiki, with their real resistances, stagger
  thresholds, and skills)
- 👻 **Click-through overlay**: the game stays fully playable underneath
- ☁️ **Cloud gaming ready**: works with GeForce NOW and other streamed windows,
  including automatic black-bar (letterbox) detection
- 🪟 **Any window**: auto-detects the game, or pick any window from a list
- 🔧 **Vision view**: see exactly what the assistant reads and where

## Installation

1. Download the latest release `.zip` from the releases page.
2. Unzip it anywhere (e.g. your Desktop).
3. Start **Limbus Company**: installed locally *or* streamed through GeForce
   NOW, both work.
4. Double-click `LimbusAssistant.exe`. The assistant window opens and finds the
   game by itself; when the header says **Connected ✓** you're ready.
   (Playing on GeForce NOW and it doesn't connect? Pick the GeForce NOW window
   from the dropdown.)
5. Press `Ctrl+F8` in a fight and the advisor appears over the game. Green =
   favored, yellow = even, red = risky. That's it.

**New to Limbus?** You need zero setup: press `Ctrl+F8` in a fight, then just
drag your skill cards over enemy attacks like you normally would. A big badge
tells you instantly whether each pairing is ✅ good, ⚠️ a coin flip, or ❌ bad,
and points you to a better target when there is one. Trust the green, avoid
the red, that is the whole game.

Want a full plan too? Press `Ctrl+F9`, open **Turn Advisor**, add your sinners
once (Enter adds them; the team is remembered). The overlay then also shows an
"Ideal turn" list that ticks itself off as you assign moves. If a suggested
skill was not dealt this turn, just follow the badge instead, the plan is a
guide, not a test.

If the boxes in the *Vision (advanced)* tab don't line up with the clash UI,
adjust `%AppData%\LimbusAssistant\calibration.json` (values are fractions of
the window size) until they do.

Building from source instead:

```
dotnet build
dotnet run --project src/LimbusAssistant
```

## Hotkeys & settings

| Hotkey | Action |
| --- | --- |
| `Ctrl+F8` | Show / hide the advisor overlay |
| `Ctrl+F9` | Show / hide the assistant window |
| `Ctrl+F11` | Coach: mark the current move done and show the next one |

Settings live in `%AppData%\LimbusAssistant\settings.json`:

| Setting | Default | Meaning |
| --- | --- | --- |
| `WindowTitle` | *(empty = auto)* | Window to capture; empty auto-detects LimbusCompany or GeForce NOW |
| `ToggleOverlayHotkey` | `Ctrl+F8` | Overlay toggle hotkey |
| `ToggleDebugHotkey` | `Ctrl+F9` | Debug panel toggle hotkey |
| `CoachAdvanceHotkey` | `Ctrl+F11` | Coach skip-to-next-move hotkey |
| `CaptureIntervalMilliseconds` | `250` | How often the screen is re-read |
| `MinimumConfidence` | `0.5` | Below this, a reading shows `?` instead of a guess |
| `Team` | *(empty)* | Your team, saved automatically from the Turn Advisor tab |
| `PlainLanguage` | `true` | Verdicts in words ("easy win"); set `false` for raw percentages |
| `BigVerdict` | `true` | The giant ✅/⚠️/❌ badge while you drag a clash |
| `ShowDetails` | `false` | Show the why/fallback/number details under the NOW line |
| `ShowChecklist` | `true` | Show the small move checklist under the NOW line |
| `Language` | `en` | Coach language: `en` or `de` (German) |
| `SoundCues` | `true` | Chime on a new plan, tick on the coach's pick, done sound |
| `CoachFontScale` | `1.0` | Size of the coach panel (e.g. `1.3` for larger text) |
| `CoachPanelPosition` | `left` | Coach panel spot: `left`, `right`, or `top` |

## How it assists you while you play

Think of it as a co-pilot sitting next to you. A typical fight looks like this:

1. **You start a battle.** The assistant is already watching the game window in
   the background (about 4 times per second). You don't need to touch it.
2. **You hover a clash in the game.** The game shows you its simplified win
   percentage. At the same moment, the assistant reads the two skills off the
   screen and runs the *full* coin-by-coin math: every coin flip, every lost
   coin, every re-clash, adjusted for your current sanity.
3. **The overlay gives a verdict at a glance.** A green "FAVORED", yellow
   "EVEN", or red "RISKY" appears in the corner with the true win chance and
   the damage you can expect if you take the clash. Green means commit, red
   means look for a better matchup. You never have to do mental math mid-fight.
4. **Planning a hard turn?** Open the **Turn Advisor** (Ctrl+F9), add the
   identities you brought, search the enemy you're fighting, and press *Plan
   the turn*. The solver assigns every sinner the best clash, sends leftover
   sinners in for free hits, and warns you what the unblocked enemy skills will
   cost you. Copy the plan into the game and go.
5. **Theorycrafting between fights?** The **Clash Calculator** answers "would
   my 4+7 beat their 6+2x3 at minus 20 sanity?" exactly, with win, loss, and
   stalemate odds.

The whole time, the game stays fully playable: the overlay ignores your mouse,
never takes focus, and never touches the game itself. You play, it advises.

## Under the hood

1. **Capture**: the game window is captured with Windows Graphics Capture
   (the same API OBS uses), with a GDI fallback.
2. **Vision**: fixed UI regions are cropped out; icons are recognised by
   template matching and numbers by on-device Windows OCR. Every reading gets a
   confidence score; anything uncertain becomes "unknown", never a wrong number.
3. **Clash engine**: skill base power, coin power, coin count and sanity feed a
   coin-by-coin probability tree (ported from open-source community
   calculators), which yields the true win rate and the expected damage after
   resistances.
4. **Turn solver**: every sinner-versus-enemy-skill pairing gets an expected
   value (damage dealt minus damage taken, probability-weighted), and an exact
   assignment search picks the plan with the highest total.
5. **Overlay**: results are drawn in a transparent, click-through window
   floating above the game.

Identity and enemy data ship as plain JSON in the `Data` folder: you can add
your own identities and enemies without touching code. The full enemy roster is
generated from the community wiki with the bundled importer; refresh it any
time with `dotnet run --project tools/WikiImporter`.

## FAQ

**Can I get banned for this?**
The tool never interacts with the game process in any way: it only reads the
screen, like a capture card. There is nothing to detect. That said, use any
third-party tool at your own discretion.

**Does it play the game for me?**
No, and it never will. It gives advice; you make the plays.

**Why is the calculator's number different from the percentage the game shows?**
The game's on-screen percentage only looks at the first coin toss exchange and
ignores everything after a lost coin, so it is often far too optimistic or
pessimistic. The calculator plays out the whole clash, coin by coin, which is
your real chance to win. The sub-line under the result shows the first-exchange
figure so you can match it against the game.

**The overlay shows "screen numbers only"?**
The vision system read the clash values but couldn't identify the exact skills,
so coin power is unknown and the estimate is rougher. Add template crops of your
skills under `Assets/Templates` (see the README there) for full-fidelity math.

**Does it work with GeForce NOW / cloud gaming?**
Yes. The assistant captures the streaming window like any other, detects the
black bars around the stream automatically, and reads the game inside them.
If it isn't found automatically, pick the GeForce NOW window from the dropdown
on the Assistant tab.

**Does it work in fullscreen?**
Borderless windowed works best. Exclusive fullscreen can block window capture on
some setups.

**Which resolutions are supported?**
Any: regions are stored as fractions of the window size. If your aspect ratio
is unusual, verify alignment in the debug panel and tweak
`calibration.json` once.

## Not affiliated with Project Moon

This is an unofficial fan-made tool. Limbus Company and all related assets are
the property of Project Moon. This project is not endorsed by or associated
with Project Moon in any way.
