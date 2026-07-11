# Limbus Assistant - Build Plan

An out-of-process, read-only combat advisor for Limbus Company. It watches the
screen, reads the current clash, and shows win-rate / expected-damage suggestions
in a transparent overlay. It never touches the game.

## Prime directive (non-negotiable)

This tool is a **passive advisor only**. It must never:

- Inject into, attach to, or hook the Limbus Company process.
- Read or write the game's memory.
- Send any input to the game (no clicks, keystrokes, macros, or automation).

The only two things it does are **capture pixels from the screen** and **draw its
own separate window on top**. This is what keeps it invisible to the game's
anti-cheat (ACTk) and keeps the user's account safe. Any feature request that
would break these rules is out of scope - surface it to the user, do not implement
it.

There is no PvP in Limbus, so this only ever helps the player against PvE content.
Even so, keep it strictly read-only.

## Architecture

```
 ÚÄÄÄÄÄÄÄÄÄÄÄÄÄÄ¿   frames   ÚÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄ¿   regions   ÚÄÄÄÄÄÄÄÄÄÄÄÄÄÄ¿
 ³ Screen       ³ÄÄÄÄÄÄÄÄÄÄ? ³ Vision        ³ÄÄÄÄÄÄÄÄÄÄÄ? ³ Game-state   ³
 ³ capture (WGC)³            ³ (match + OCR) ³             ³ model        ³
 ÀÄÄÄÄÄÄÄÄÄÄÄÄÄÄÙ            ÀÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÙ             ÀÄÄÄÄÄÄÂÄÄÄÄÄÄÄÙ
                                                                  ³
 ÚÄÄÄÄÄÄÄÄÄÄÄÄÄÄ¿   render   ÚÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄ¿   suggestions      ³
 ³ Overlay      ³?ÄÄÄÄÄÄÄÄÄÄ ³ Clash engine  ³?ÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÙ
 ³ (WPF, click- ³            ³ + recommender ³        
 ³  through)    ³            ÀÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÙ        ³ static data
 ÀÄÄÄÄÄÄÄÄÄÄÄÄÄÄÙ                                     ³
                                              ÚÄÄÄÄÄÄÄÁÄÄÄÄÄÄÄ¿
                                              ³ Bundled JSON  ³
                                              ³ dataset       ³
                                              ÀÄÄÄÄÄÄÄÄÄÄÄÄÄÄÄÙ
```

Everything runs in a single standalone process, separate from the game.

## Tech stack

- **.NET 8**, C#, Windows-only.
- **Windows.Graphics.Capture (WGC)** for capturing the game window. Handles
  borderless/fullscreen with low CPU. Fallback: DXGI Desktop Duplication.
- **WPF** for the overlay: a transparent, topmost, click-through window.
- **OpenCvSharp** for template matching (icons/glyphs) and region cropping.
- **Windows.Media.Ocr** (built into Windows, on-device, no external dependency)
  for numeric text, with a digit whitelist. Keep the OCR engine behind an
  interface so it can be swapped for Tesseract/PaddleOCR later if accuracy demands.
- **System.Text.Json** for the dataset.

## Key engineering decisions

- **Template-match icons, OCR only numbers.** Sin affinities, damage-type icons,
  coin icons, and skill types are fixed sprites - matching them against reference
  crops is far more robust than OCR. Reserve OCR for numbers (skill base power,
  coin power, sanity, HP) and enemy names.
- **Anchor regions are resolution-relative.** The combat UI has a stable layout.
  Store anchor rectangles normalized to the game window size, then scale to the
  detected window. Support a manual calibration fallback.
- **The clash engine is ported, not invented.** Port the win-rate algorithm from
  the vetted open-source calculators listed in `claude.md`. The in-game win rate
  is a naive number that ignores continued clashes after a lost coin; the good
  calculators model the full coin-by-coin tree. Match that behaviour.
- **Vision runs on a background loop; the overlay reads a snapshot.** Do heavy
  work (capture, match, OCR, clash math) off the UI thread and publish an
  immutable snapshot the WPF layer renders. Never block the UI thread.

## Phases

Build in this order - each phase leaves something runnable.

### Phase 1 - Overlay shell
Scaffold the solution. Get a transparent, always-on-top, **click-through** WPF
window pinned over the running game, with a global hotkey to toggle visibility and
a draggable debug panel. This de-risks the trickiest platform work first.

### Phase 2 - Screen capture
Capture the Limbus window via WGC into frames the vision layer can read. Detect
the game window by title, expose its bounds, handle resolution changes.

### Phase 3 - Calibration & regions
Map normalized anchor rectangles (clash panel, each skill slot, sanity readouts,
enemy panel) onto the live window. Persist per-resolution config. Ship a debug
view that draws the region boxes over the capture so alignment is verifiable.

### Phase 4 - Vision extraction
Template-match icons and OCR numbers inside each region into a raw reading, with a
confidence value per field. Provide a debug overlay showing what was read where.
Handle low-confidence reads gracefully (show "?" rather than a wrong number).

### Phase 5 - Static dataset
Assemble a bundled JSON dataset of identities, skills (base power, coin power, coin
count, sin affinity, damage type) and enemy sin/physical resistances and stagger
thresholds. Source from datamined dumps / the wiki (see `claude.md`). This is a
parallelizable workstream - stub it early so the engine has data to chew on.

### Phase 6 - Clash engine
Port the coin-by-coin clash win-rate and expected-damage calculation from the
reference calculators. Apply sin/physical resistance tiers (Fatal 2x, Weak
~1.2-1.5x, Normal 1x, Endured 0.75x, Ineffective 0.5x) and the staggered-state
rule (physical resistances become Fatal; sin resistances usually unchanged). Unit-
test against known cases from the reference calculators.

### Phase 7 - Recommender
For each sinner skill against each viable enemy skill this turn, compute win rate
and expected damage, weight by resistances and stagger state, and rank the
allocations. Output a small, ordered set of suggestions the overlay can show.

### Phase 8 - Overlay rendering
Render suggestions cleanly, anchored near the relevant clash. Keep it glanceable:
win %, expected damage, best-skill highlight. Respect the click-through rule so
the game stays fully playable underneath.

### Phase 9 - Polish
Settings window, configurable hotkeys, resilient error handling, a "vision
confidence" indicator, and the README (see `claude.md` for its requirements).

## Testing

- Unit-test the clash engine against fixtures derived from the reference
  calculators - no running game needed.
- Test the vision pipeline against saved screenshot fixtures at several
  resolutions. Capture a small corpus of real combat screenshots early.
- Keep vision, engine, and overlay decoupled so each is testable in isolation.

## Risks & mitigations

- **OCR fragility on stylized fonts.** Mitigate with template matching for glyphs,
  digit whitelisting, and confidence thresholds that degrade to "unknown" instead
  of guessing.
- **Resolution / UI-scale variance.** Normalized anchors + a calibration step.
- **Game patches shift UI or data values.** Keep anchors and the dataset in
  external config/JSON, not hardcoded, so updates are data edits not code changes.
- **Capture performance.** WGC + a throttled capture loop (only re-run vision when
  the clash view is on screen and has changed).
