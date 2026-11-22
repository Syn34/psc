using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using PokeSpriteCreator.Json;
using PokeSpriteCreator.Model;
using PokeSpriteCreator.UI;

namespace PokeSpriteCreator
{
    /// <summary>
    /// "PokeSpriteCreator.exe --selftest [folder]": draws a simple test trainer (plain shapes, not game art)
    /// and checks picture import, editing, colour limits, saving, exporting and the walking-sprite generator.
    /// The test pictures are left in the folder.
    /// </summary>
    public static class SelfTest
    {
        static int _fail;

        public static int Run(string dir)
        {
            dir = dir ?? Path.Combine(Path.GetTempPath(), "psc-selftest");
            Directory.CreateDirectory(dir);
            var trainer = TestTrainer(80, 80, 0, 3);
            var trainerPng = Path.Combine(dir, "trainer.png");
            trainer.Save(trainerPng);

            // ---- picture import
            var g5 = GenStyle.Get(5);
            var pics = ImageImport.LoadFrames(trainerPng);
            Check(pics.Count == 1 && pics[0].W == 80, "loads a PNG");
            string note;
            var lay = ImageImport.Suggest(pics, SpriteKind.Front, g5, out note);
            Check(lay.FrameW == 80 && lay.FrameH == 80, "80x80 picture fits Gen 5 as it is");
            Check(ImageImport.FramesFor(pics, lay)[0].SameAs(trainer), "same-size picture unchanged");

            var tall = TestTrainer(80, 89, 0, 9);                       // like an 80x89 download with empty rows on top
            lay = ImageImport.Suggest(new List<PixelImage> { tall }, SpriteKind.Front, g5, out note);
            var fitted = ImageImport.FramesFor(new List<PixelImage> { tall }, lay)[0];
            Check(lay.FrameH == 80 && fitted.Bounds().Bottom == 80 && fitted.Bounds().Height == tall.Bounds().Height, "taller picture trimmed with the feet on the bottom row");
            Check(fitted.Bounds().X == tall.Bounds().X, "left/right position kept when the widths match");

            var doubled = trainer.Scale(2);
            lay = ImageImport.Suggest(new List<PixelImage> { doubled }, SpriteKind.Front, g5, out note);
            Check(lay.FrameW == 80 && ImageImport.FramesFor(new List<PixelImage> { doubled }, lay)[0].SameAs(trainer), "pixel-doubled picture shrunk back");

            var strip = new PixelImage(320, 80);
            for (int i = 0; i < 4; i++) strip.Blit(TestTrainer(80, 80, i, 3), i * 80, 0);
            lay = ImageImport.Suggest(new List<PixelImage> { strip }, SpriteKind.Back, g5, out note);
            Check(lay.Cols == 4 && lay.FrameW == 80, "back strip becomes 4 frames");
            var stripFrames = ImageImport.FramesFor(new List<PixelImage> { strip }, lay);
            Check(!stripFrames[0].SameAs(stripFrames[3]), "strip frames differ");

            var sheet = new PixelImage(128, 128);
            lay = ImageImport.Suggest(new List<PixelImage> { sheet }, SpriteKind.Overworld, g5, out note);
            Check(lay.Cols == 4 && lay.Rows == 4 && lay.FrameW == 32, "4x4 sheet becomes an overworld sheet");

            // ---- a document made from the picture
            var d = new SpriteDocument(g5, SpriteKind.Front, null, g5.DefaultLayout(SpriteKind.Front));
            d.Layers.Add(d.LayerFromFrames("Picture", new List<PixelImage> { trainer }));
            var ed = new Editor(new Settings());
            ed.SetDocument(d);
            Check(d.Composite(0).SameAs(trainer), "picture layer draws");
            ed.AddLayer();
            Check(d.Layers.Count == 2 && ed.LayerIndex == 1, "new layer on top");
            d.Layers[1].Frames[0].Set(0, 0, PixelImage.Rgb(1, 2, 3));
            ed.Undo();
            Check(d.Layers.Count == 1, "undo removes the layer");
            ed.Redo(); ed.Undo();

            // colour limits
            var paint = d.Layers[0];
            for (int i = 0; i < 30; i++) paint.Frames[0].Set(i, 0, PixelImage.Rgb(i * 8, 255 - i * 8, 128));
            Check(d.ColorCount() > 15, "test has too many colours");
            Check(d.FinalFrames(true)[0].Colors().Count <= 15, "Gen 5 limit gives 15 colours or fewer");
            var g1 = new SpriteDocument(GenStyle.Get(1), SpriteKind.Front, null, GenStyle.Get(1).DefaultLayout(SpriteKind.Front));
            g1.Layers.Add(g1.LayerFromFrames("p", new List<PixelImage> { trainer.Crop(12, 24, 56, 56) }));
            Check(g1.FinalFrames(true)[0].Colors().IsSubsetOf(GenStyle.Get(1).Palette), "Gen 1 becomes 4 greys");
            for (int i = 0; i < 30; i++) paint.Frames[0].Set(i, 0, 0);

            // save / load
            var file = Path.Combine(dir, "test.psc");
            d.Name = "TESTER";
            var before = d.Composite(0);
            d.Save(file);
            var back = SpriteDocument.Load(file);
            Check(back.Layers.Count == d.Layers.Count && back.Name == "TESTER" && back.Composite(0).SameAs(before), "save and open keep the sprite");

            // files from 0.1.x with part layers open as paint layers
            var legacy = new JsonObject();
            legacy["app"] = "PokeSpriteCreator"; legacy["version"] = 1; legacy["gen"] = 5; legacy["kind"] = "Front";
            legacy["frameWidth"] = 80; legacy["frameHeight"] = 80; legacy["columns"] = 1; legacy["rows"] = 1;
            var part = new JsonObject();
            part["type"] = "part"; part["name"] = "Hair"; part["category"] = "Hair"; part["part"] = "X"; part["x"] = 0; part["y"] = 0;
            part["embedded"] = Convert.ToBase64String(trainer.ToPngBytes());
            var colors = new JsonObject(); colors[PixelImage.Hex(Shirt)] = "#00FF00"; part["colors"] = colors;
            legacy["layers"] = new List<object> { part };
            var legacyFile = Path.Combine(dir, "legacy.psc");
            File.WriteAllText(legacyFile, Json.Json.Write(legacy), new UTF8Encoding(false));
            var ld = SpriteDocument.Load(legacyFile);
            Check(ld.Layers.Count == 1 && ld.Warnings.Count == 1 && ld.Composite(0).Colors().Contains(PixelImage.Rgb(0, 255, 0)), "old part layer opens as a paint layer with its colours");

            // export
            var outPng = Path.Combine(dir, "TESTER.png");
            d.BuildSheet(true, 2).Save(outPng);
            var exp = PixelImage.Load(outPng);
            Check(exp != null && exp.W == 160 && exp.H == 160, "export at 2x is 160x160");
            Check(ExportDialog.DefaultFileName(d, "TESTER") == "TESTER.png", "front file name");
            var bd = new SpriteDocument(g5, SpriteKind.Back, null, new FrameLayout(80, 80, 4, 1));
            Check(ExportDialog.DefaultFileName(bd, "TESTER") == "TESTER_back.png", "back file name");
            var owd = new SpriteDocument(g5, SpriteKind.Overworld, "Walk", g5.DefaultLayout(SpriteKind.Overworld));
            Check(ExportDialog.DefaultFileName(owd, "TESTER") == "trainer_TESTER.png", "walk file name");
            owd.Sheet = "Bike";
            Check(ExportDialog.DefaultFileName(owd, "TESTER") == "TESTER_bike.png", "bike file name");

            // overworld mirror + back frame count
            owd.Layers.Add(owd.NewLayer("Paint"));
            owd.Layers[0].Frames[4].Set(2, 10, PixelImage.Rgb(255, 0, 0));
            var eow = new Editor(new Settings()); eow.SetDocument(owd);
            eow.MirrorRows(true);
            Check(owd.Layers[0].Frames[8].Get(29, 10) == PixelImage.Rgb(255, 0, 0), "Left to Right mirror");
            bd.Layers.Add(bd.LayerFromFrames("p", stripFrames));
            var ebd = new Editor(new Settings()); ebd.SetDocument(bd);
            ebd.SetFrameCount(5);
            Check(bd.FrameCount == 5 && bd.Layers[0].Frames.Count == 5, "frames can be added");

            // ---- walking sprites kept with the front sprite
            var sheetPic = TestSheet();
            sheetPic.Save(Path.Combine(dir, "walk_sheet.png"));
            var sheetFrames = ImageImport.LoadFrames(Path.Combine(dir, "walk_sheet.png"));
            var owLay = ImageImport.Suggest(sheetFrames, SpriteKind.Overworld, g5, out note);
            Check(owLay.Cols == 4 && owLay.Rows == 4 && owLay.FrameW == 32, "walking sheet picture gives 32x32 frames");
            var set = d.MakeOverworld(owLay, ImageImport.FramesFor(sheetFrames, owLay));
            Check(set.Parent == d && set.Root == d && d.AnyDirty, "walking sprites belong to the front sprite");
            d.Save(file);
            var reopened = SpriteDocument.Load(file);
            Check(reopened.Overworld != null && reopened.Overworld.Kind == SpriteKind.Overworld && reopened.Overworld.Parent == reopened &&
                  reopened.Overworld.FrameCount == 16 && reopened.Overworld.Composite(5).SameAs(set.Composite(5)), "walking sprites are saved in the same file");
            Check(!reopened.AnyDirty, "opened file isn't marked changed");

            // switching between the parts keeps separate undo
            var es = new Editor(new Settings());
            es.SetDocument(reopened);
            es.EditPart(reopened.Overworld, 4);
            Check(es.Doc == reopened.Overworld && es.Frame == 4 && es.Root == reopened, "edit the walking sprites");
            es.BeginChange(); es.Current.Frames[4].Set(0, 0, PixelImage.Rgb(9, 9, 9)); es.Changed();
            Check(reopened.AnyDirty && reopened.Title.EndsWith("*"), "a change to the walking sprites marks the file changed");
            es.EditPart(reopened);
            Check(!es.CanUndo, "the main sprite has its own undo");
            es.EditPart(reopened.Overworld, 4);
            Check(es.CanUndo, "the walking sprites keep theirs");
            es.Undo();

            // copy a recolour from Down 1 to every frame
            var ow = reopened.Overworld;
            es.EditPart(ow, 0);
            es.BeginChange();
            var f0 = es.Current.Frames[0];
            for (int i = 0; i < f0.Px.Length; i++) if (f0.Px[i] == Shirt) f0.Px[i] = PixelImage.Rgb(30, 160, 60);
            es.Changed();
            var pending = es.PendingChange;
            Check(pending != null && pending.Recolors.Count == 1 && pending.Pixels.Count == 0, "a recolour is seen as a recolour (" + (pending == null ? "none" : pending.Describe()) + ")");
            es.CopyChange(true);
            Check(Enumerable.Range(0, 16).All(i => !ow.Layers[0].Frames[i].Px.Contains(Shirt) && ow.Layers[0].Frames[i].Px.Contains(PixelImage.Rgb(30, 160, 60))), "recolour copied to all 16 frames");

            // copy a drawn mark from Down 1 to its walking steps: the steps are one pixel higher, so the mark follows
            es.SelectFrame(0);
            es.BeginChange();
            es.Current.Frames[0].Set(15, 20, PixelImage.Rgb(255, 255, 0)); es.Current.Frames[0].Set(16, 20, PixelImage.Rgb(255, 255, 0));
            es.Changed();
            es.CopyChange(false);
            Check(ow.Layers[0].Frames[1].Get(15, 19) == PixelImage.Rgb(255, 255, 0) && ow.Layers[0].Frames[1].Get(16, 19) == PixelImage.Rgb(255, 255, 0), "drawn pixels follow the step's bob");
            Check(ow.Layers[0].Frames[2].Get(15, 20) == PixelImage.Rgb(255, 255, 0), "and land in the same place on the other standing frame");
            Check(ow.Layers[0].Frames[4].Get(15, 20) != PixelImage.Rgb(255, 255, 0), "other directions untouched by 'its walking steps'");

            // left -> right is mirrored
            es.SelectFrame(4);
            es.BeginChange();
            es.Current.Frames[4].Set(12, 22, PixelImage.Rgb(255, 0, 255));
            es.Changed();
            es.CopyChange(true);
            Check(ow.Layers[0].Frames[8].Get(31 - 12, 22) == PixelImage.Rgb(255, 0, 255), "a change on a left frame lands mirrored on the right frame");
            Check(es.PendingChange != null && es.PendingChange.IsEmpty, "after copying, the frame has no pending change");

            // ---- going one colour over the limit merges only the closest pair, nothing else
            {
                var pal16 = new[] { 0x000000, 0x292929, 0x212942, 0x2D5A84, 0x634A42, 0x5A5A6B, 0x3184A5, 0x29A59C, 0xCE5252,
                                    0xDEA57B, 0x94949C, 0xCECEDE, 0xEFEFFF, 0xFFD6BD, 0xF0C098, 0xFFD2B8 };   // last = a near copy of FFD6BD
                var over = new PixelImage(16, 4);
                for (int i = 0; i < 16; i++) for (int y = 0; y < 4; y++) over.Set(i, y, PixelImage.Rgb((pal16[i] >> 16) & 255, (pal16[i] >> 8) & 255, pal16[i] & 255));
                over.Set(15, 0, over.Get(13, 0)); over.Set(15, 1, over.Get(13, 0));   // the new colour is used less
                var red = ColorTools.ReduceTogether(new List<PixelImage> { over }, 15)[0];
                int changedCols = 0, wrong = 0;
                for (int i = 0; i < 16; i++) for (int y = 0; y < 4; y++)
                    if (red.Get(i, y) != over.Get(i, y)) { if (i == 15) changedCols++; else wrong++; }
                Check(wrong == 0 && changedCols == 2 && red.Get(15, 3) == over.Get(13, 0) && red.Colors().Count == 15,
                      "16 colours -> 15 merges only the near copy into its twin (other pixels changed: " + wrong + ")");
            }

            // ---- a sprite already at 15 colours gets a new one: only the new colour is merged, never the old ones
            {
                var nate = new SpriteDocument(g5, SpriteKind.Front, null, g5.DefaultLayout(SpriteKind.Front)); nate.Layers.Add(nate.NewLayer("Paint"));
                var cols = new[] { 0x000000, 0x292929, 0x212942, 0x2D5A84, 0x634A42, 0x5A5A6B, 0x218C8C, 0x317BA5, 0xCE5252,
                                   0xDEA57B, 0x94949C, 0xCECEDE, 0xEFEFFF, 0xFFD6BD, 0xF0C098 };
                var px = nate.Layers[0].Frames[0];
                for (int i = 0; i < 15; i++) for (int y = 0; y < 5; y++) px.Set(i, y, PixelImage.Rgb((cols[i] >> 16) & 255, (cols[i] >> 8) & 255, cols[i] & 255));
                var nBefore = nate.FinalFrames(true)[0];
                Check(nate.LastMerged.Count == 0 && nate.ColorOrder.Count == 15, "15 colours: nothing merged");
                for (int y = 10; y < 13; y++) px.Set(40, y, PixelImage.Rgb(255, 0, 0));   // a brand-new red
                var after = nate.FinalFrames(true)[0];
                int touched = 0;
                for (int y = 0; y < 5; y++) for (int i = 0; i < 15; i++) if (after.Get(i, y) != nBefore.Get(i, y)) touched++;
                Check(touched == 0, "adding a 16th colour leaves every existing colour as it was (" + touched + " pixels changed)");
                Check(nate.LastMerged.Count == 1 && nate.LastMerged.ContainsKey(PixelImage.Rgb(255, 0, 0)) && after.Get(40, 10) == PixelImage.Rgb(0xCE, 0x52, 0x52),
                      "the new red is the one shown as its closest colour (#CE5252)");
                var orderFile = Path.Combine(dir, "order.psc");
                nate.Save(orderFile);
                var reNate = SpriteDocument.Load(orderFile);
                reNate.FinalFrames(true);
                Check(reNate.ColorOrder.Take(15).SequenceEqual(nate.ColorOrder.Take(15)) && reNate.LastMerged.ContainsKey(PixelImage.Rgb(255, 0, 0)), "the colour order is saved, so reopening merges the same colour");
                px.Set(14, 0, 0); for (int y = 0; y < 5; y++) px.Set(14, y, 0);   // remove a colour: the red fits again
                nate.FinalFrames(true);
                Check(nate.LastMerged.Count == 0, "removing a colour frees its place for the new one");
            }

            // ---- shading helper, on the leg from Mike's screenshot (no outline, light stripe down the middle)
            {
                string[] legRows = {
                    "..............ABBCBACDBABAB",
                    "..............ABBCBACDBABAB",
                    ".............ACBBBACCDBABAB",
                    ".............ACCBBBCCCABABB",
                    ".............ACCCCCCCCABBBB",
                    "............ACAACCCCCCDBBBB",
                    "............ACCCAAAAAADCCBB",
                    "............ACCCCCCCCCDBBCC",
                    ".............ACCCCCCCCCABBB",
                    "..............AACCCCCCDDBBB",
                    "..............EFAAAADD..DAB",
                    "..............EFFFE.......A",
                    "..............GGHHG........",
                    "..............GHHHG........",
                    "..............GHHGG........",
                    ".............GGHHG.........",
                    ".............GHHGG.........",
                    ".............GHHG..........",
                    ".............GHHG..........",
                    ".............GHHG..........",
                    ".........AA.IIIHG..........",
                    "........ABBAJJJJJ..........",
                    ".........KAACBALKI.........",
                    "........KCCJJALLKI.........",
                    ".......KLJJLLKLKII.........",
                    ".......KJJJKLLII...........",
                    "........IIIIII............."
                };
                var legPal = new Dictionary<char, int> {
                    { 'A', 0x5A5A6B }, { 'B', 0xCECEDE }, { 'C', 0x94949C }, { 'D', 0x212942 }, { 'E', 0xAD8473 }, { 'F', 0xDEA57B },
                    { 'G', 0xF0C098 }, { 'H', 0xFFD6BD }, { 'I', 0x000000 }, { 'J', 0x292929 }, { 'K', 0x634A42 }, { 'L', 0xCE5252 } };
                var leg = new PixelImage(legRows[0].Length, legRows.Length);
                for (int y = 0; y < legRows.Length; y++)
                    for (int x = 0; x < legRows[y].Length; x++)
                        if (legRows[y][x] != '.') { int v = legPal[legRows[y][x]]; leg.Set(x, y, PixelImage.Rgb((v >> 16) & 255, (v >> 8) & 255, v & 255)); }
                Func<int, int> rgb = v => PixelImage.Rgb((v >> 16) & 255, (v >> 8) & 255, v & 255);
                var legColors = new HashSet<int>(leg.Px.Where(c => c != 0));
                var sug = ShadingHelper.Suggest(leg, leg, new Rectangle(12, 10, 7, 11), legColors, new ShadeOptions { Light = LightFrom.Right });
                leg.Scale(8).Save(Path.Combine(dir, "shade_before.png"));
                sug.After.Scale(8).Save(Path.Combine(dir, "shade_after.png"));
                File.WriteAllText(Path.Combine(dir, "shade_notes.txt"), string.Join("\r\n", sug.Notes.ToArray()));
                Check(sug.Ramp.Outline == rgb(0x634A42) && sug.Ramp.Shadow == rgb(0xDEA57B) && sug.Ramp.Base == rgb(0xF0C098) && sug.Ramp.Highlight == rgb(0xFFD6BD) && sug.Ramp.Under == rgb(0xAD8473),
                      "shading: picks the skin ramp from the sprite (outline " + PixelImage.Hex(sug.Ramp.Outline) + ", shadow " + PixelImage.Hex(sug.Ramp.Shadow) + ", base " + PixelImage.Hex(sug.Ramp.Base) + ", highlight " + PixelImage.Hex(sug.Ramp.Highlight) + ")");
                Check(sug.Ramp.New.Count == 0 && sug.After.Colors().IsSubsetOf(legColors), "shading adds no new colours");
                Check(sug.After.Get(12, 15) == rgb(0x634A42) && sug.After.Get(13, 15) == rgb(0xDEA57B), "outline added outside on the left, shadow just inside it");
                Check(sug.After.Get(19, 13) == rgb(0x634A42), "outline on the right where it meets the background");
                bool hiInside = false;
                for (int x = 13; x < 18; x++) if (sug.After.Get(x, 16) == rgb(0xFFD6BD)) hiInside = true;
                Check(hiInside && sug.After.Get(16, 16) != rgb(0xDEA57B), "a highlight toward the lit (right) side");
                Check(sug.After.Get(14, 10) == rgb(0xAD8473) || sug.After.Get(15, 10) == rgb(0xAD8473), "darker row under the shorts");
                bool untouched = true;
                for (int y = 0; y < 10; y++) for (int x = 0; x < leg.W; x++) if (sug.After.Get(x, y) != leg.Get(x, y)) untouched = false;
                for (int y = 21; y < leg.H; y++) for (int x = 0; x < leg.W; x++) if (sug.After.Get(x, y) != leg.Get(x, y)) untouched = false;
                Check(untouched, "the shorts and shoe are left alone");
                Check(sug.Notes.Any(n => n.Contains("No outline")) && sug.Notes.Any(n => n.Contains("both sides") || n.Contains("lit side")), "explains: no outline, shaded on both sides");
                var again = ShadingHelper.Suggest(sug.After, sug.After, sug.Region, legColors, new ShadeOptions { Light = LightFrom.Right });
                Check(again.Changed <= 2, "running it again on its own result changes (almost) nothing (" + again.Changed + ")");
                Check(!again.Notes.Any(n => n.Contains("both sides") || n.Contains("No outline")), "and doesn't complain about its own result: " + string.Join(" | ", again.Notes.ToArray()));
                var clickArea = ShadingHelper.AreaAround(leg, new Point(15, 16));
                Check(clickArea.Contains(14, 12) && clickArea.Contains(13, 19) && !clickArea.Contains(20, 5), "clicking the leg boxes the leg " + clickArea);
                // light from the right is worked out from a sprite lit from the right
                var ball = new PixelImage(20, 20);
                for (int y = 2; y < 18; y++) for (int x = 2; x < 18; x++) ball.Set(x, y, x < 10 ? rgb(0x806040) : rgb(0xE0C090));
                Check(ShadingHelper.GuessLight(ball) == LightFrom.Right, "guesses light from the right");
            }

            // ---- "more natural": the neck from Mike's screenshot (lightest skin under the chin, no shadow)
            {
                string[] neckRows = {
                    ".................A.......................",
                    "............BAAA.BA.AAA..................",
                    ".............BBBABBABBBCC................",
                    "..........AAAABBBBBBBACC.................",
                    "........AABBBBBBBBBBBBBBCC...............",
                    ".......ABBBBABBBBBBABBBBBBCC.............",
                    ".......AAAAABDEEEABABABBBBBBC............",
                    ".........CBAFGDADGAFBABBBACBC............",
                    "........ABAFFAEEEAFFAFBABBC..............",
                    ".......ABBAFBBBBBFFFFFAACBBA.............",
                    "......AB.ABBFFFFFBBBFFFAABBBA............",
                    ".........CFGEEEGFFFFBBFAAACBBA...........",
                    ".........CEECCCBEEFFFFAAACACC............",
                    "........CECCBGBHBBEEBABACC.A.............",
                    "........CC.CIECIIIICJHBBHC...............",
                    "...........BHJHKKKKCEHABA................",
                    "..........BBCIKKKKKHKIKBA................",
                    "..........B.CIIKKKKKKC..BA...............",
                    ".............CIIHHKKBKK..B...............",
                    "............AKCBKKHKKKAA.................",
                    "...........AAKKKKKKKAAAAAA...............",
                    "..........AAAAAAKKKAAAAAAAAAA............",
                    ".........AAAAAAAAAAAAAAAAAAAAAAC.........",
                    ".......CAAACAAAAAAAAAAAAACCCCCCAAB.......",
                    "......CAAAACAALAACGAAACCCFFFFFBCCKKH.....",
                    "......CAAAACAAAAAECAACGEFGCCGFFBGDIIB....",
                    ".....CAACAACAAAAADECGEEBBHIHBBDEEEBIB....",
                    "....HAAACACCAAAAAECFEEBKHKHIKDEEEFCB.....",
                    "...HKHAACC..AAAAAECBFEBIHKBKBEEIFCB......",
                    "...HKKKC....AAAACCGCCBHBHBIBBFFCC........",
                    "...HKKKKC...AAAAAACGECIICCLDCCC..........",
                    "....BKKKB...AAAAAAAACECCC................",
                    ".....BIKIB.AAAAAAAAAAC...................",
                    "......BIBHBAAAAAAAAAAAC.................."
                };
                var neckPal = new Dictionary<char, int> { { 'A', 0x292929 }, { 'B', 0x634A42 }, { 'C', 0x000000 }, { 'D', 0x5A5A6B }, { 'E', 0xEFEFFF }, { 'F', 0xCE5252 }, { 'G', 0x94949C }, { 'H', 0xAD8473 }, { 'I', 0xDEA57B }, { 'J', 0xCECEDE }, { 'K', 0xFFD6BD }, { 'L', 0x212942 } };
                var neck = new PixelImage(neckRows[0].Length, neckRows.Length);
                for (int y = 0; y < neckRows.Length; y++)
                    for (int x = 0; x < neckRows[y].Length; x++)
                        if (neckRows[y][x] != '.') { int v = neckPal[neckRows[y][x]]; neck.Set(x, y, PixelImage.Rgb((v >> 16) & 255, (v >> 8) & 255, v & 255)); }
                var neckColors = new HashSet<int>(neck.Px.Where(c => c != 0));
                var nopt = new ShadeOptions { Natural = true, Light = LightFrom.Left };
                var nBox = ShadingHelper.Suggest(neck, neck, new Rectangle(10, 19, 15, 3), neckColors, nopt);
                var nClick = ShadingHelper.Suggest(neck, neck, ShadingHelper.AreaAround(neck, new Point(16, 20)), neckColors, nopt);
                neck.Scale(8).Save(Path.Combine(dir, "neck_before.png"));
                nBox.After.Scale(8).Save(Path.Combine(dir, "neck_box.png"));
                nClick.After.Scale(8).Save(Path.Combine(dir, "neck_click.png"));
                Func<int, int> nrgb = v => PixelImage.Rgb((v >> 16) & 255, (v >> 8) & 255, v & 255);
                int lightest = nrgb(0xFFD6BD);
                bool neckDark = true;
                for (int x = 10; x < 25; x++) for (int y = 20; y < 22; y++) if (nBox.After.Get(x, y) == lightest) neckDark = false;
                Check(nBox.Ramp.Base == lightest && nBox.Ramp.Outline != nrgb(0x000000), "natural: picks the skin under the box, not the black shirt (" + PixelImage.Hex(nBox.Ramp.Base) + ")");
                Check(neckDark && nBox.After.Get(16, 20) == nrgb(0xAD8473), "natural: the neck is in the head's shadow (darker row under the chin, no lightest skin left on it)");
                bool outsideSame = true;
                for (int y = 0; y < neck.H; y++) for (int x = 0; x < neck.W; x++)
                    if (!new Rectangle(9, 18, 17, 5).Contains(x, y) && nBox.After.Get(x, y) != neck.Get(x, y)) outsideSame = false;
                Check(outsideSame, "natural: nothing outside the box changes");
                int w20 = 0; for (int x = 0; x < neck.W; x++) { int c = nClick.After.Get(x, 20); if (ShadingHelper.Related(c, lightest) && PixelImage.Luma(c) > 0.45) w20++; }
                Check(nClick.Notes.Any(n => n.Contains("narrowed")) && w20 <= 6, "natural: a neck nearly as wide as the head is narrowed (" + w20 + " skin pixels wide)");
                Check(nClick.After.Get(16, 18) == neck.Get(16, 18) && nClick.After.Get(17, 18) == neck.Get(17, 18), "small details drawn in a darker shade (the mouth) are kept");
                var plain = ShadingHelper.Suggest(neck, neck, new Rectangle(10, 19, 15, 3), neckColors, new ShadeOptions { Light = LightFrom.Left });
                Check(!plain.Notes.Any(n => n.Contains("narrower part")), "without \"more natural\" the shape isn't touched");
                File.WriteAllText(Path.Combine(dir, "neck_notes.txt"), "BOX " + nBox.Area + "\r\n" + string.Join("\r\n", nBox.Notes.ToArray()) + "\r\n\r\nCLICK " + nClick.Area + "\r\n" + string.Join("\r\n", nClick.Notes.ToArray()));
            }

            // ---- tabs and comparing
            var et = new Editor(new Settings());
            var docA = new SpriteDocument(g5, SpriteKind.Front, null, g5.DefaultLayout(SpriteKind.Front)); docA.Layers.Add(docA.NewLayer("Paint"));
            var docB = new SpriteDocument(GenStyle.Get(3), SpriteKind.Front, null, GenStyle.Get(3).DefaultLayout(SpriteKind.Front)); docB.Layers.Add(docB.NewLayer("Paint"));
            var tabA = et.OpenTab(docA);
            et.BeginChange(); et.Current.Frames[0].Set(1, 1, PixelImage.Rgb(1, 1, 1)); et.Changed();
            var tabB = et.OpenTab(docB);
            Check(et.Sessions.Count == 2 && et.S == tabB && et.Doc == docB && !et.CanUndo, "a new tab has its own sprite and undo");
            et.Activate(tabA);
            Check(et.Doc == docA && et.CanUndo, "switching back keeps the first tab's undo");
            et.StartCompare(tabA, tabB);
            Check(et.Comparing && et.CompareLeft == tabA && et.CompareRight == tabB && et.S == tabA, "two tabs side by side");
            et.Activate(tabB);
            Check(et.Comparing && et.Doc == docB, "both sides can be edited");
            var docC = new SpriteDocument(g5, SpriteKind.Back, null, g5.DefaultLayout(SpriteKind.Back)); docC.Layers.Add(docC.NewLayer("Paint"));
            var tabC = et.OpenTab(docC);
            Check(et.Sessions.Count == 3 && et.Comparing, "opening a tab while comparing keeps the comparison");
            et.PickTab(tabB); et.PickTab(tabC);
            Check(et.CompareRight == tabC && et.CompareLeft == tabA, "picking another tab swaps it into the side being edited");
            et.StopCompare();
            Check(!et.Comparing && et.S == tabC, "stop comparing");
            et.StartCompare(tabA, tabB);
            et.CloseTab(tabB);
            Check(!et.Comparing && et.Sessions.Count == 2 && !et.Sessions.Contains(tabB), "closing a compared tab stops comparing");

            // scrolling: a zoomed frame bigger than the view scrolls, and the scroll stays inside the picture
            var cv = new SpriteCanvas(et) { Bound = tabA, Size = new Size(200, 150) };
            tabA.NeedsFit = false;
            cv.SetZoom(16);
            var content = cv.ContentSize;
            Check(content.Width == 80 * 16 + 2 * SpriteCanvas.ViewMargin, "content size = zoomed frame + margin");
            cv.SetScroll(99999, 99999);
            Check(cv.ScrollX == content.Width - 200 && cv.ScrollY == content.Height - 150, "scroll stops at the edge (" + cv.ScrollX + "," + cv.ScrollY + ")");
            cv.SetScroll(-50, -50);
            Check(cv.ScrollX == 0 && cv.ScrollY == 0, "scroll stops at the start");
            cv.FitToWindow();
            Check(cv.ContentSize.Width - 2 * SpriteCanvas.ViewMargin <= 200 && cv.ScrollX == 0, "fit to window shows the whole frame");

            // ---- shapes
            Check(SpriteCanvas.LinePoints(new Point(0, 0), new Point(5, 2)).Count() == 6, "line");
            Check(SpriteCanvas.EllipsePoints(new Point(0, 0), new Point(9, 9), true).Count() > SpriteCanvas.EllipsePoints(new Point(0, 0), new Point(9, 9), false).Count(), "ellipse filled > outline");

            Console.WriteLine(_fail == 0 ? "SELFTEST OK" : "SELFTEST FAILED: " + _fail);
            Console.WriteLine("Test pictures: " + dir);
            return _fail == 0 ? 0 : 1;
        }

        static void Check(bool ok, string what)
        {
            if (!ok) { _fail++; Console.WriteLine("FAIL: " + what); }
        }

        // ------------------------------------------------------------------ a plain test trainer

        static readonly int K = PixelImage.Rgb(0, 0, 0), Hair = PixelImage.Rgb(140, 90, 50), HairD = PixelImage.Rgb(90, 56, 34), Skin = PixelImage.Rgb(248, 208, 168),
            Band = PixelImage.Rgb(220, 50, 50), Shirt = PixelImage.Rgb(60, 100, 220), Pants = PixelImage.Rgb(150, 150, 160), ShoeC = PixelImage.Rgb(200, 60, 60);

        static void Rect(PixelImage img, int x0, int y0, int x1, int y1, int fill)
        {
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    img.Set(x, y, (x == x0 || x == x1 || y == y0 || y == y1) ? K : fill);
        }

        /// <summary>A plain 4 x 4 walking sheet (32x32 frames): a little figure in each frame, the steps one pixel higher, left and right mirrored.</summary>
        static PixelImage TestSheet()
        {
            var sheet = new PixelImage(128, 128);
            for (int i = 0; i < 16; i++)
            {
                var f = new PixelImage(32, 32);
                int bob = (i % 4 == 1 || i % 4 == 3) ? 1 : 0, row = i / 4;
                Rect(f, 9, 3 - bob, 22, 17 - bob, row == 3 ? HairD : Hair);    // head
                if (row != 3) Rect(f, row == 1 ? 9 : row == 2 ? 14 : 11, 10 - bob, row == 1 ? 17 : row == 2 ? 22 : 20, 16 - bob, Skin);  // face
                Rect(f, 10, 17 - bob, 21, 25 - bob, Shirt);                      // body
                Rect(f, 11, 25 - bob, 15, 30, Pants); Rect(f, 16, 25 - bob, 20, 30, Pants);
                if (row == 1) { f.Set(5, 24, Band); f.Set(5, 25, Band); }        // something only on the left side
                if (row == 2) { f.Set(26, 24, Band); f.Set(26, 25, Band); }
                sheet.Paste(f, (i % 4) * 32, row * 32);
            }
            return sheet;
        }

        /// <summary>A simple standing trainer drawn with boxes (hair, headband, face, shirt, trousers, shoes), feet on row top+76.</summary>
        static PixelImage TestTrainer(int w, int h, int arm, int top)
        {
            var p = new PixelImage(w, h);
            int ox = (w - 80) / 2 + 24, oy = top;
            Rect(p, ox + 2, oy, ox + 29, oy + 12, Hair);            // hair
            for (int x = ox + 4; x < ox + 28; x++) p.Set(x, oy + 3, HairD);
            Rect(p, ox + 2, oy + 12, ox + 29, oy + 15, Band);       // headband
            Rect(p, ox + 4, oy + 15, ox + 27, oy + 26, Skin);       // face
            p.Set(ox + 10, oy + 19, K); p.Set(ox + 20, oy + 19, K);
            Rect(p, ox + 12, oy + 26, ox + 19, oy + 28, Skin);      // neck
            Rect(p, ox + 2, oy + 28, ox + 29, oy + 50, Shirt);      // shirt
            Rect(p, ox - 4, oy + 29 - arm * 4, ox + 2, oy + 46 - arm * 4, Shirt); // arms
            Rect(p, ox + 29, oy + 29, ox + 35, oy + 46, Shirt);
            Rect(p, ox + 4, oy + 50, ox + 15, oy + 72, Pants);      // legs
            Rect(p, ox + 16, oy + 50, ox + 27, oy + 72, Pants);
            Rect(p, ox + 2, oy + 72, ox + 15, oy + 76, ShoeC);      // shoes
            Rect(p, ox + 16, oy + 72, ox + 29, oy + 76, ShoeC);
            return p;
        }
    }
}
