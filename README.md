# Poke Sprite Creator (PSC)

A sprite editor for Pokémon fan games, and a companion app for [PokeCreator](https://github.com/Syn34/pokeCreator).

Draw a sprite or bring one in (PNG, GIF or BMP), edit it, and when you save a trainer front sprite the side panel
makes its **overworld walking sprite**: facing down, left, right and up, with walking steps, in the right size for
the style you picked (Gen 1 to Gen 5).

## Making a sprite

*File > New sprite* (Ctrl+N): pick the style and the kind of sprite, then start from a blank canvas or a picture.

| Kind | What it is |
|------|------------|
| Trainer front sprite | the battle picture; the walking sprite is made from it |
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
exports the sprite the way the games would. Light and dark mode.

## The walking sprite (In-game directions panel)

Made from a trainer front sprite every time you save it, or when you click **Update now**.

- **Size**: the style's overworld frame size: Gen 1–2 16×16, Gen 3 16×32, Gen 4–5 32×32.
- **How**: the head keeps the trainer's hair shape (shrunk from the sprite); the body is a clean overworld-style body coloured
  with the trainer's own colours, picked out of the sprite: hair (light and dark), skin, jacket/shirt (with the inner shirt
  down the middle), trousers, shoes, and a headband or visor if there is one. So it works whatever pose the battle sprite has.
  Left and right are mirrors; walking frames lift one foot (down/up) or spread the legs (sideways).
- **Symmetric front and back**: makes both sides of the down and up views match (battle sprites are often drawn at an angle).
- **Up view hair from a back sprite**: uses a back sprite's hair shape (PNG, GIF or `.psc`) for the view from behind.
- **Export sheet…**: saves the 4 × 4 sheet at 1×–4× (`trainer_NAME.png`, for `Graphics/Characters`).
- **Edit as overworld sprite**: opens the sheet as its own overworld sprite to touch up.
- For an overworld sheet the panel plays the sheet itself; back sprites don't make a walking sprite.

It's a starting point: small details (logos, bags, things held in the hand) aren't carried over.

## Exporting

*File > Export PNG* (Ctrl+E) saves at 1×–4× to any file or straight into a PokeCreator / Essentials game folder with the
right name: `Graphics/Trainers/NAME.png`, `Graphics/Trainers/NAME_back.png`, `Graphics/Characters/trainer_NAME.png`,
`Graphics/Characters/NAME_bike.png`, …

Sprites save as `.psc` files (layers, frames and walking-sprite settings). Files from 0.1.x open too; their part layers
become paint layers.

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

`PokeSpriteCreator.exe --selftest [folder]` draws a plain test trainer and checks picture import, editing, colour limits,
saving, exporting and the walking sprite for every style.

## Keyboard shortcuts

B pencil · E eraser · G fill · I picker · L line · R rectangle · O ellipse · M move · C recolour ·
X swap colours · S mirror · Alt+click pick colour · Shift = filled shape / straight line ·
arrows nudge the layer · Ctrl+wheel zoom · middle-drag pan · Ctrl+←/→ frames · Ctrl+Z / Ctrl+Y undo/redo ·
Ctrl+I import picture · Ctrl+S save (makes the walking sprite) · Ctrl+E export
