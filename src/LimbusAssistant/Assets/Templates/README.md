# Template images

Reference crops used for template matching. Drop PNG files here; they are
matched greyscale against the icon regions of the capture.

Naming convention:

- `skill.<skill-id>.png` — a skill icon; `<skill-id>` must match an `id` in
  `Data/identities.json` or `Data/enemies.json` (e.g. `skill.lcb-gregor-s2.png`).
  When matched, the assistant uses that skill's base/coin values for the full
  coin-by-coin clash calculation.
- `sin.<name>.png` — sin affinity glyphs (`sin.wrath.png`, `sin.gloom.png`, …).

Capture crops from a screenshot at the game's native resolution for best
matching. Matching threshold is 0.8 (normalized cross-correlation).
