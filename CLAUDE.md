# CLAUDE.md

Guidance for working in this repository.

## What this is

A read-only, out-of-process combat advisor for Limbus Company. It captures the
screen, reads the current clash with computer vision, and shows win-rate /
expected-damage suggestions in a transparent overlay. See `PLAN.md` for the full
architecture and build phases.

## The one inviolable rule

This is an **assistant only**. It must stay completely outside the game:

- Never inject into, attach to, or hook the Limbus Company process.
- Never read or write the game's memory.
- Never send input to the game - no clicking, keystrokes, macros, or any
  automation of play.

The tool only captures screen pixels and draws its own separate window. This is
what keeps it invisible to the game's anti-cheat and keeps the user unbanned. If a
task would require breaking any of the above, do not implement it - stop and raise
it with the user instead.

## Tech stack

.NET 8 � C# � Windows-only � WPF (overlay) � Windows.Graphics.Capture �
OpenCvSharp (template matching) � Windows.Media.Ocr (numeric OCR) �
System.Text.Json.

## Build & run

```
dotnet build
dotnet run --project src/LimbusAssistant
```

The game must be running for capture to work. The overlay defaults to hidden;
toggle it with the configured hotkey.

## Releasing

Releases are cut automatically by `.github/workflows/release.yml` on every push
to `master`. To publish a new version:

1. Bump `<Version>` in `Directory.Build.props` (semver, e.g. 0.3.0 to 0.4.0).
2. Merge to `master`. The workflow builds, runs the tests, publishes a
   self-contained win-x64 build, and creates a GitHub release tagged
   `v{version}` with that zip.

The version bump is what triggers the release: the workflow skips publishing when
a `v{version}` tag already exists, so nothing ships until the number changes.
Pull requests are validated separately by `.github/workflows/ci.yml`, which
builds and tests the whole solution.

## Writing style

- **No em dashes or en dashes.** Never use the characters — or – in UI strings,
  README text, commit messages, comments, or any other prose in this repo. Use
  a comma, a period, a colon, or parentheses instead.

## Code conventions

- **No comments.** Write code that reads clearly on its own - good names, small
  methods, early returns. Do not add explanatory comments, docstrings, XML doc
  comments, or region markers. The only exception is a genuinely non-obvious
  workaround, and even then prefer a well-named method over a comment.
- **C# / .NET style:**
  - File-scoped namespaces. One top-level type per file, file named after the type.
  - Root namespace `Tsundosika.LimbusAssistant`.
  - `PascalCase` for types, methods, properties, constants; `camelCase` for locals
    and parameters; `_camelCase` for private fields.
  - Enable nullable reference types and treat warnings seriously. Enable implicit
    usings.
  - Prefer `var` when the type is obvious from the right-hand side.
  - Use `async`/`await` end to end for I/O and capture loops; never block on
    `.Result` or `.Wait()`. Suffix async methods with `Async`.
  - Prefer records and immutable types for the game-state snapshot and DTOs.
  - Keep vision, clash engine, and overlay in separate projects/folders with no
    circular dependencies. The engine must not reference WPF or capture.
  - Fail loudly in debug, degrade gracefully in release (a bad OCR read becomes
    "unknown", never a silent wrong number).

## Commit conventions

- **No co-author trailers and no tool attribution.** Do not add
  `Co-Authored-By:` lines, "Generated with Claude Code" footers, or any similar
  attribution to commit messages.
- Imperative mood subject line, ~72 chars max ("Add clash win-rate engine", not
  "Added" or "Adds").
- Optional `type: subject` prefix (`feat:`, `fix:`, `refactor:`, `chore:`,
  `docs:`) when it adds clarity.
- Body (optional) explains *why*, wrapped at ~72 chars.
- One logical change per commit.

## README requirement

Maintain a polished `README.md` aimed at players, not just developers. It must:

- Open with a short, clear description of what the tool does and a screenshot (or
  a placeholder for one) of the overlay in action.
- State up front that it is a read-only overlay that never touches the game, so
  it's safe to use.
- Give **simple, numbered install steps** for a non-technical user: download the
  release, unzip, run the `.exe`. Cover launching it alongside the game and the
  first-run calibration.
- Document the hotkeys and settings in a small table.
- Look good: a title, badges, headings, a features list, and a short FAQ. Keep the
  language friendly and the steps skimmable.
- Include a brief "How it works" and a "Not affiliated with Project Moon"
  disclaimer.

## Reference resources - use these, don't invent mechanics

**Do not hand-write the clash math or damage formulas from memory.** Port the
algorithms from the vetted open-source calculators below, and confirm mechanics
against the wiki. When a formula or a resistance/coin value is needed, consult
these first.

Clash / damage algorithms (open source - port from these):

- LimbusCompute (most accurate; models continued clashes after a lost coin and
  negative coin power): https://limbuscompute.pages.dev/
- Philip-Moon/LCB-Calculator (clash win % + expected damage):
  https://github.com/Philip-Moon/LCB-Calculator
- dhnam/limbus_clash_calc (fast clash probability):
  https://github.com/dhnam/limbus_clash_calc
- SyxP/LimComClashCalc: https://github.com/SyxP/LimComClashCalc

Game mechanics & data (source of truth for values):

- Battles (clash, resistances, stagger, sanity):
  https://limbuscompany.wiki.gg/wiki/Battles
- Damage Formula (full calc, resistance tiers, clash-count and level modifiers):
  https://limbuscompany.wiki.gg/wiki/Damage_Formula
- Sin & Sin Resonance:
  https://limbuscompany.wiki.gg/wiki/Sin �
  https://limbuscompany.wiki.gg/wiki/Sin_Resonance
- Prydwen combat mechanics guide:
  https://www.prydwen.gg/limbus-company/guides/combat-mechanics

Confirmed mechanics to build against:

- Sin resistance tiers: Fatal 2x, Weak ~1.2-1.5x, Normal 1x, Endured 0.75x,
  Ineffective 0.5x. Physical (Slash/Pierce/Blunt) uses the same tier system.
- Sanity ranges roughly -45 to +45 and shifts coin heads probability; pull the
  exact coin/heads relationship from the reference calculators rather than
  assuming constants.
- When a unit is staggered, its **physical** resistances become Fatal; **sin**
  resistances are usually unchanged.
- The in-game win rate is naive - replicate the full coin-by-coin calculation from
  the reference calculators, which is more accurate.

For static data (identity/skill coin values, enemy resistances, stagger
thresholds), prefer datamined community dumps or the wiki's structured pages;
store everything as bundled JSON, never hardcoded in logic.
