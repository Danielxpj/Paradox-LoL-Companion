# On-card augment tier badges — design

**Date:** 2026-07-18
**Status:** approved

## Goal

When the OCR identifies the offered augments during the Mayhem pick window, pin a
colored tier-letter badge (S/A/B…, tier-colored) directly above each card's title
in-game, with the gold **BEST** chip on the recommended one. The player reads the
verdict without glancing away from the cards.

The existing OFFERED NOW list (overlay + main window) stays unchanged as fallback
and for verdict text.

## Constraints

- Badges must be click-through and never steal focus from the game
  (`WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE`, topmost).
- Works only when capture works (Borderless/Windowed) — same limitation as
  detection today. Fullscreen exclusive: no badges, list remains the fallback.
- No absolute card geometry: positions come from the OCR bounding boxes of the
  matched titles, so any resolution works.

## Data flow

1. **`WindowsOcrReader`** returns per line the text **and its bounding box**
   (union of the word rects Windows OCR already computes; today only `.Text`
   survives).
2. **`FrameOps.CenterCropUpscaled`** also returns the crop transform
   (`OffsetX`, `OffsetY`, `Scale`) so a rect in the upscaled pass-2 crop maps
   back to native frame coordinates: `native = offset + cropCoord / scale`.
3. **`OfferedAugmentDetector`** (Core) additionally reports the index of the
   source line each match came from. The App keeps the per-line rects (with each
   line's transform) parallel to the text list it hands the detector, then maps
   matched line index → rect → native frame coords → screen coords
   (`ClientToScreen` on the game hwnd, then DPI divide for WPF).
4. **`AugmentBadgeWindow`** (new): transparent topmost no-activate click-through
   WPF window with a Canvas; one badge per matched card, centered horizontally on
   the title rect, sitting just above it. Repositioned on every successful
   detection tick (~2 s); hidden when `OfferedAugments` clears (lifetime governed
   by the v2.1.6 `PickWindowTracker` 60 s post-respawn grace, same as the list).

## Components

- `Core/Augments/OcrLineBox` — plain int rect record (no Windows types in Core).
- `Core/Augments/OfferedAugmentDetector` — matches gain `LineIndex`.
- `Core/Augments/BadgeGeometry` — pure math: crop-transform inversion + badge
  anchor placement. Unit-tested.
- `App/Capture/WindowsOcrReader` — new `ReadLinesWithBoxesAsync` returning
  text + box; existing string API stays for callers that don't need geometry.
- `App/Capture/FrameOps` — `CenterCropUpscaled` returns frame + transform.
- `App/Capture/GameWindowCapture` — exposes the captured hwnd's client→screen
  origin so frame coords become screen coords.
- `App/AugmentBadgeWindow.xaml(.cs)` — the badge surface.
- `App/ViewModels/MainViewModel` — after a successful detection, computes badge
  placements and shows/updates/hides the badge window on the UI thread.

## Error handling

- Any missing piece (no rect for a match, hwnd gone, DPI lookup fails) → that
  badge simply isn't shown; never throws into the advisor loop (same fail-open
  policy as the OCR pipeline).
- Badge window creation failure is logged and disables badges for the session.

## Testing

- `BadgeGeometryTests` (Core): crop-transform inversion round-trip against the
  known calibration (crop x 15–85 %, y 20–70 %, scale 2 at 1080p); anchor
  placement above the title rect.
- `OfferedAugmentDetectorTests`: existing real-frame fixture asserts `LineIndex`
  points at the line containing each matched name.
- Manual live check: badges land on cards in a real Mayhem game (Borderless).

## Rejected alternatives

- **Fixed relative card positions** — no OCR rects needed, but breaks across
  layouts/resolutions/card counts; we get real rects for free anyway.
- **Image recognition of card borders** — heavy, unnecessary.
