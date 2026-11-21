# Poke Sprite Creator (PSC)

A sprite maker for Pokémon fan games, and a companion app for [PokeCreator](https://github.com/Syn34/pokeCreator).

Make **trainer front sprites**, **trainer back sprites** (animated throw strips) and **overworld sheets**
(walk, run, bike, surf, fish…) in the style of **Gen 1 to Gen 5**. Build a character piece by piece from
your parts folder (poses, hair, tops, bottoms, shoes, hats, visors, sunglasses, backpacks, bikes…),
recolour every piece, and draw anything else free-hand.

## Features

- **Styles Gen 1–5**: each with the games' own sprite sizes and colour limits (Gen 1: 4 greys, Gen 2: 4 colours,
  Gen 3–5: 15 colours + transparency). Tick "Limit to … colours" to see and export the sprite the way the games would.
- **Start from a pose** (a blank outline to fill in) or from a blank canvas. A pose also sets the canvas size.
- **Parts browser**: click a part to wear it; each thumbnail shows *your* sprite wearing that part.
  Ctrl+click adds a second layer of the same category (two accessories, say). "(None)" takes it off.
- **Recolouring**: per part, click any of its colours to change it, or slide Hue / Saturation / Brightness
  (dark outlines are kept). The Recolour tool changes a colour by clicking it on the sprite (Shift+click: on every layer).
  Right-click a colour under "In this sprite" to replace it everywhere (e.g. skin tone).
- **Free-hand tools**: pencil, eraser, fill, colour picker, line, rectangle, ellipse, move, mirror drawing.
  Fill can follow the outlines of every visible layer, so colouring in a pose outline is one click per area.
- **Layers**: show/hide, reorder, rename, duplicate, merge down, convert a part to an editable paint layer.
- **Frames**: frame strip with an animated preview; onion skin; copy a frame to all; mirror Left ↔ Right
  for overworld sheets; add/remove frames on back sprites.
- **Undo / Redo** for everything.
- **Export PNG** at 1×–4×, to any file or straight into a PokeCreator / Essentials game folder with the right name:
  `Graphics/Trainers/NAME.png`, `Graphics/Trainers/NAME_back.png`, `Graphics/Characters/trainer_NAME.png`,
  `Graphics/Characters/NAME_bike.png`, …
- Sprites save as `.psc` files, which keep a copy of the parts they use, so they open on another PC too.
- Light and dark mode.

## Parts folder

The app ships without any part art: you supply the parts. On first run it creates `Parts\` next to the exe
with an empty folder for every style, sprite kind and category, plus a `README.txt` guide
(also under *Help > Parts folder guide*). You can point the app at another folder in *File > Change parts folder*.

```
Parts\
  Gen 3\
    Front\     Poses\ Hair\ Hats\ Visors\ Sunglasses\ Tops\ Jackets\ Bottoms\ Shoes\ Gloves\ Backpacks\ Accessories\
    Back\      (same categories; pictures are strips of frames)
    Overworld\
      Walk\ Run\ Bike\ Surf\ Fish\   (same categories, plus Bikes\ in Bike; pictures are 4×4 sheets)
```

- The folder a picture is in decides where it appears; the file name is the part's name. Any new folder is a new category.
- Draw parts at the game's size (or exactly 2×/3×/4× pixel-doubled — they're shrunk automatically),
  on a transparent background, lined up with the poses.
- A picture the size of the whole sprite/sheet gives each frame its own picture; one frame's size is used on every frame;
  anything else is a "loose" piece you drag into place.
- `NAME_behind.png` next to `NAME.png` is drawn underneath the body (long hair behind the back, a backpack, a bike's back wheel).
- Layer order (bottom → top) when parts are added: Poses, Shoes, Bottoms, Tops, Jackets, Gloves, Backpacks, Hair, Hats,
  Visors, Sunglasses, Accessories, Bikes. Put an `order.txt` in a kind folder to change it.

| Style | Front | Back | Overworld frame |
|-------|-------|------|-----------------|
| Gen 1 | 56×56 | 32×32 | 16×16 |
| Gen 2 | 56×56 | 48×48 | 16×16 |
| Gen 3 | 64×64 | 64×64 × 4 frames | 16×32 |
| Gen 4 | 80×80 | 80×80 × 5 frames | 32×32 |
| Gen 5 | 80×80 | 80×80 × 5 frames | 32×32 |

## Building

- **Windows**: run `build.cmd` (uses the C# compiler that comes with .NET Framework 4 — nothing to install),
  or open `PokeSpriteCreator.sln` in Visual Studio 2022. Output: `bin\PokeSpriteCreator.exe`.
- **Linux / macOS**: `./build.sh` with Mono (`mono-devel`).

The source is C# 5 for .NET Framework 4.8 with WinForms, no NuGet packages.

`PokeSpriteCreator.exe --selftest [folder]` makes a few plain test shapes as parts and checks building, recolouring,
colour limits, saving and exporting.

## Keyboard shortcuts

B pencil · E eraser · G fill · I picker · L line · R rectangle · O ellipse · M move · C recolour ·
X swap colours · S mirror · Alt+click pick colour · Shift = filled shape / straight line ·
arrows nudge the layer · Ctrl+wheel zoom · middle-drag pan · Ctrl+←/→ frames · Ctrl+Z / Ctrl+Y undo/redo
