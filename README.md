# Poke Sprite Creator (PSC)

A sprite editor for Pokémon fan games, and a companion app for [PokeCreator](https://github.com/Syn34/pokeCreator).

Draw a sprite or bring one in (PNG, GIF or BMP) and edit it. A trainer front sprite keeps its **in-game walking sprites**
in the same file: upload the 4 × 4 walking sheet (or start blank), edit any of the small frames on the big canvas, and copy
a change from one frame to its walking steps or to all the other directions.

## Making a sprite

*File > New sprite* (Ctrl+N): pick the style and the kind of sprite, then start from a blank canvas or a picture.

| Kind | What it is |
|------|------------|
| Trainer front sprite | the battle picture; its walking sprites are kept with it |
| Trainer back sprite | a strip of frames (the throw animation) |
| Overworld sheet | 4 × 4 frames: rows Down, Left, Right, Up; columns are the walking steps (Walk, Run, Bike, Surf, Fish…) |

- *File > Import picture as a new sprite* (Ctrl+I) opens the same dialog with a picture chosen.
- *File > Import picture as a layer* (Ctrl+Shift+I) adds a picture to the sprite you're editing.
- Pictures are fitted to the canvas: same size → unchanged; bigger with empty space (e.g. an 80×89 download for an 80×80
  Gen 5 sprite) → trimmed with the feet kept on the bottom row; pixel-doubled art → shrunk back; animated GIFs and strips →
  frames of a back sprite; a 4 × 4 sheet → an overworld sheet. A picture bigger than the style's size makes a bigger canvas.

## Editing

Pencil, eraser, fill (it can follow the outlines of every visible layer), colour picker, line, rectangle, ellipse, move,
recolour (click a colour to change it; Shift+click: on every layer) and mirror drawing. Layers (show/hide, order, rename,
duplicate, merge), undo/redo, frame strip with animated preview, onion skin, copy a frame to all, overworld Left ↔ Right
mirror, add/remove back-sprite frames. "Limit to 15 colours like Gen 5" (4 greys for Gen 1, 4 colours for Gen 2) shows and
exports the sprite the way the games would. Going over the limit never changes the colours already in the sprite: the newest colour is shown (and
exported) as the closest colour you already have, and the status bar says which. The colour picker always picks the real
colour. Light and dark mode.

## Shading helper

The shaded-ball button in the toolbar (H). Drag a box over one part of the sprite (a leg, an arm, a sleeve), or just click
it, and the right-hand panel suggests shading for it the way a pixel artist would fix it:

- **Light from one side**, worked out from how the rest of the sprite is shaded (or pick Left / Top left / Top /
  Top right / Right).
- **An outline** where the part meets the background (around it, or on its edge pixels; optionally a softer outline on
  the lit side, as Gen 5 sprites often do).
- **The shadow tone** down the side away from the light, **a thin highlight** one pixel in from the lit edge, and **a
  darker row** under anything that overlaps the part from above (like a leg under shorts).
- It uses the part's own shades from your sprite, so it adds no colours (untick *Only colours already in the sprite* to
  let it make a missing darker or lighter shade).

- **Fix the shape too (more natural)** lets it change the shape, not just the colours. A neck (a narrower part under a
  wider part of the same colour) goes into the head's shadow: a darker row under the chin, then the shadow tone. If the
  neck is nearly as wide as the head, it's narrowed to about 60% of the head's width. It also fills gaps in the edge,
  smooths one-pixel bumps and removes stray dark pixels inside the part.
- Small details drawn in a darker shade inside the part (a mouth, a nose) are always kept.
- A box with several parts in it (say hair, skin and a headband) shades **one part** and leaves the others exactly as
  they are. **Part** lists what's in the box; it starts with the biggest part that has a darker shade to shade with.
  Shades of one part must be close in hue, in strength of colour and in lightness, so brown hair, a red headband and
  skin are never treated as one thing.

The panel shows the part now and suggested, the colours used (outline, under, shadow, base, highlight) and a list of
what it changes and why (no outline, shaded on both sides, highlight too wide, too many shades…). The suggestion is also
drawn on the canvas. **Apply** (Enter) puts it on the selected layer; Ctrl+Z takes it off; Esc clears the box.

## Walking sprites (In-game sprites panel)

A trainer front sprite's walking sprites are a 4 × 4 sheet (rows Down, Left, Right, Up; the walking steps across), the
picture used in `Graphics/Characters`. They're saved in the same `.psc` file as the front sprite.

- **Upload walking sheet…** (or **Start blank**) adds them; **Replace sheet…** swaps them for another picture.
- Click a direction or a frame in the sheet to edit it on the big canvas. **Main sprite** / **Walking sprites** switch
  between the two; each keeps its own Undo.
- **Copy my change on this frame to:**
  - **Its walking steps**: the other frames facing the same way.
  - **All directions**: every other frame.

  The change is what you did to the frame since you picked it. A recolour (a colour changed everywhere it appears on the
  frame) is recoloured on the other frames too. Drawn or erased pixels are pasted where the area around them matches each
  frame best, so they follow the walking bob, and are mirrored between left and right. Undo (Ctrl+Z) if a frame needs it
  done by hand. Also in *Frames* (Ctrl+Shift+W / Ctrl+Shift+D).
- The Recolour tool changes only the frame you're on when editing walking sprites (Shift+click: every frame and layer).
- **Export sheet…** saves the sheet (`trainer_NAME.png`) at 1×–4×, to a file or into a game folder.
- An overworld sheet opened on its own works the same way.

## Tabs and side-by-side comparing

- Every sprite you open or make gets its own **tab** above the canvas, with its own undo, zoom and scroll. **+** makes a
  new sprite; **×**, middle-click or Ctrl+W closes a tab (you're asked to save changes first). Ctrl+Tab / Ctrl+Shift+Tab
  switch tabs. Opening a file that's already open goes to its tab.
- The editing window has **scroll bars** on the right and bottom once you zoom past the window. Mouse wheel scrolls up/down,
  Shift+wheel sideways, Ctrl+wheel zooms (around the mouse), middle-drag or Space+drag pans.
- **Compare side by side:** drag a tab onto the canvas, or right-click a tab, or *View > Compare side by side with*. Both
  sprites show next to each other, each with its own scroll bars; click either side to edit it (the side you're editing
  says *Editing*). Both sides zoom together unless you turn off *View > Link zoom while comparing*. Clicking another tab
  while comparing puts it in the side you're editing. **Stop comparing** (above either side, or in *View*) goes back to
  one sprite.

## Exporting

*File > Export PNG* (Ctrl+E) saves at 1×–4× to any file or straight into a PokeCreator / Essentials game folder with the
right name: `Graphics/Trainers/NAME.png`, `Graphics/Trainers/NAME_back.png`, `Graphics/Characters/trainer_NAME.png`,
`Graphics/Characters/NAME_bike.png`, …

Sprites save as `.psc` files (layers, frames, and a front sprite's walking sprites). Files from 0.1.x open too; their part
layers become paint layers.

| Style | Front | Back | Overworld frame | Colours |
|-------|-------|------|-----------------|---------|
| Gen 1 | 56×56 | 32×32 | 16×16 | 4 greys |
| Gen 2 | 56×56 | 48×48 | 16×16 | 4 |
| Gen 3 | 64×64 | 64×64 × 4 frames | 16×32 | 15 + transparent |
| Gen 4 | 80×80 | 80×80 × 5 frames | 32×32 | 15 + transparent |
| Gen 5 | 80×80 | 80×80 × 5 frames | 32×32 | 15 + transparent |

## Building

- **Windows**: run `build.cmd` (uses the C# compiler that comes with .NET Framework 4, nothing to install),
  or open `PokeSpriteCreator.sln` in Visual Studio 2022. Output: `bin\PokeSpriteCreator.exe`.
- **Linux / macOS**: `./build.sh` with Mono (`mono-devel`).

The source is C# 5 for .NET Framework 4.8 with WinForms, no NuGet packages.

`PokeSpriteCreator.exe --selftest [folder]` draws a plain test trainer and walking sheet and checks picture import, editing,
colour limits, saving (with walking sprites), exporting, copying changes between frames, tabs, comparing and scrolling, the colour limit, and the shading helper (on a leg like the one
in Mike's screenshot).

## Keyboard shortcuts

B pencil · E eraser · G fill · I picker · L line · R rectangle · O ellipse · M move · C recolour · H shading helper ·
X swap colours · S mirror · Alt+click pick colour · Shift = filled shape / straight line ·
arrows nudge the layer · wheel / Shift+wheel scroll · Ctrl+wheel zoom · middle-drag pan · Ctrl+Tab next tab · Ctrl+W close tab · Ctrl+←/→ frames · Ctrl+Z / Ctrl+Y undo/redo ·
Ctrl+I import picture · Ctrl+S save · Ctrl+E export · Ctrl+Shift+W / Ctrl+Shift+D copy my change to its walking steps / all directions
