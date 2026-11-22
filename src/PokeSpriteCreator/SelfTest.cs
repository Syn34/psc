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
            d.OwSymmetric = false;
            d.UpSource = Path.Combine(dir, "back.png");
            var before = d.Composite(0);
            d.Save(file);
            var back = SpriteDocument.Load(file);
            Check(back.Layers.Count == d.Layers.Count && back.Name == "TESTER" && back.Composite(0).SameAs(before), "save and open keep the sprite");
            Check(!back.OwSymmetric && back.UpSource == d.UpSource, "save keeps the walking-sprite settings");

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

            // ---- walking sprite made from the front sprite
            foreach (var st in GenStyle.All)
            {
                var r = OverworldGenerator.Make(trainer, st, true, null);
                string g = st.Name + ": ";
                Check(r.Frames.Count == 16 && r.Frames.All(f => f.W == st.OwW && f.H == st.OwH), g + "16 frames of " + st.OwW + "x" + st.OwH);
                Check(r.Frames.All(f => !f.Bounds().IsEmpty && f.Bounds().Bottom == st.OwH), g + "every frame stands on the bottom row");
                var colorsUsed = new HashSet<int>(); foreach (var f in r.Frames) colorsUsed.UnionWith(f.Colors());
                Check(st.Greyscale ? colorsUsed.IsSubsetOf(st.Palette) : colorsUsed.Count <= st.MaxColors, g + "within the colour limit (" + colorsUsed.Count + ")");
                for (int s = 0; s < 4; s++) Check(r.Frame(OverworldGenerator.Right, s).SameAs(r.Frame(OverworldGenerator.Left, s).FlipH()), g + "right is the mirror of left, step " + s);
                Check(!r.Frame(OverworldGenerator.Down, 1).SameAs(r.Frame(OverworldGenerator.Down, 0)) && !r.Frame(OverworldGenerator.Down, 3).SameAs(r.Frame(OverworldGenerator.Down, 1)), g + "walking steps differ");
                Check(r.Frame(OverworldGenerator.Down, 0).SameAs(r.Frame(OverworldGenerator.Down, 2)), g + "columns 1 and 3 are the standing frame");
                if (!st.Greyscale)
                {
                    Check(r.Colors.Skin == Skin && r.Colors.Torso == Shirt && r.Colors.Legs == Pants && r.Colors.Shoes == ShoeC && r.Colors.Band == Band, g + "picks the trainer's colours");
                    // with few colours the skin may be merged into a near colour: count the frame's colour nearest to skin
                    var downF = r.Frame(OverworldGenerator.Down, 0);
                    int skinNow = ColorTools.Nearest(Skin, downF.Colors().ToList());
                    int downSkin = downF.Px.Count(c => c == skinNow), upSkin = r.Frame(OverworldGenerator.Up, 0).Px.Count(c => c == skinNow);
                    Check(downSkin > upSkin + 6, g + "face shows facing down but not facing up");
                }
                var sym = r.Frame(OverworldGenerator.Down, 0);
                Check(sym.SameAs(sym.FlipH()), g + "symmetric down view is exactly symmetric");
            }
            var blank = OverworldGenerator.Make(new PixelImage(80, 80), g5, true, null);
            Check(blank.Frames.Count == 16 && blank.Frames.All(f => f.IsEmpty), "empty sprite gives an empty sheet");
            // a back sprite's hair is used for the up view
            var backPic = TestTrainer(80, 80, 0, 3);
            for (int y = 0; y < 30; y++) for (int x = 0; x < 80; x++) if (backPic.Get(x, y) != 0) backPic.Set(x, y, Hair);
            var withBack = OverworldGenerator.Make(trainer, g5, true, backPic);
            Check(withBack.Note.Contains("back sprite"), "up view from a back sprite");
            OverworldGenerator.Make(trainer, g5, true, null);
            SpriteDocument.MakeSheet(OverworldGenerator.Make(trainer, g5, true, null).Frames, new FrameLayout(32, 32, 4, 4), 1).Save(Path.Combine(dir, "trainer_walk.png"));

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
