using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using PokeSpriteCreator.Model;
using PokeSpriteCreator.UI;

namespace PokeSpriteCreator
{
    /// <summary>
    /// "PokeSpriteCreator.exe --selftest [folder]": makes a small set of simple test parts (plain shapes,
    /// not game art) in folder\Parts, then checks building, recolouring, colour limits, saving and exporting.
    /// </summary>
    public static class SelfTest
    {
        static int _fail;

        public static int Run(string dir)
        {
            dir = dir ?? Path.Combine(Path.GetTempPath(), "psc-selftest");
            Directory.CreateDirectory(dir);
            var root = Path.Combine(dir, "Parts");
            MakeSampleParts(root);
            var lib = new PartLibrary(root);

            // ---- library
            Check(lib.Categories(3, SpriteKind.Front, null).First() == "Poses", "Poses is the bottom category");
            var cats = lib.Categories(3, SpriteKind.Front, null);
            Check(cats.IndexOf("Hair") < cats.IndexOf("Hats") && cats.IndexOf("Tops") < cats.IndexOf("Hair"), "category order");
            var pose = lib.Find(3, SpriteKind.Front, null, "Poses", "Standing");
            Check(pose != null && pose.Sheet != null && pose.Sheet.W == 64, "pose loads");
            var pack = lib.Find(3, SpriteKind.Front, null, "Backpacks", "Pack");
            Check(pack != null && pack.BehindPath != null, "_behind companion found");
            Check(!lib.Parts(3, SpriteKind.Front, null, "Backpacks").Any(p => p.Name.EndsWith("_behind")), "_behind not listed as its own part");

            // ---- building a front sprite
            var st = GenStyle.Get(3);
            var d = new SpriteDocument(st, SpriteKind.Front, null, st.DefaultLayout(SpriteKind.Front));
            d.Layers.Add(d.MakePartLayer(pose));
            d.Layers.Add(d.NewPaintLayer("Paint"));
            var ed = new Editor(new Settings { PartsFolder = root }) { Lib = lib };
            ed.SetDocument(d);
            foreach (var n in new[] { "Tops/Shirt", "Bottoms/Pants", "Hair/Short", "Hats/Cap", "Backpacks/Pack", "Sunglasses/Shades" })
            {
                var parts = n.Split('/');
                ed.SetPart(lib.Find(3, SpriteKind.Front, null, parts[0], parts[1]), false);
            }
            Check(d.Layers.Count == 8, "8 layers (pose, paint, 6 parts), got " + d.Layers.Count);
            var order = d.Layers.OfType<PartLayer>().Select(l => l.Category).ToList();
            Check(order.IndexOf("Hats") > order.IndexOf("Hair") && order.IndexOf("Tops") > order.IndexOf("Poses"), "parts inserted in layer order: " + string.Join(",", order));
            var shades = d.Layers.OfType<PartLayer>().First(l => l.Category == "Sunglasses");
            Check(shades.IsLoose(d.Layout) && shades.OffsetX > 0, "small piece is loose and centred");
            var img = d.Composite(0);
            Check(img.Get(32, 40) != 0, "body is drawn");
            // backpack's behind picture sits under the body: the strap area right of the body shows the pack colour
            Check(img.Colors().Contains(PixelImage.Rgb(120, 80, 40)), "behind picture drawn");

            // replacing within a category keeps one layer
            ed.SetPart(lib.Find(3, SpriteKind.Front, null, "Hair", "Long"), false);
            Check(d.Layers.Count(l => l is PartLayer && ((PartLayer)l).Category == "Hair") == 1, "hair replaced, not added");
            ed.SetPart(lib.Find(3, SpriteKind.Front, null, "Hats", "Cap"), true);
            Check(d.Layers.Count(l => l is PartLayer && ((PartLayer)l).Category == "Hats") == 2, "Ctrl adds an extra layer");
            ed.Undo();
            Check(d.Layers.Count(l => l is PartLayer && ((PartLayer)l).Category == "Hats") == 1, "undo removes it");
            ed.Redo(); ed.Undo();

            // ---- recolouring
            var shirt = ed.Doc.Layers.OfType<PartLayer>().First(l => l.Category == "Tops");
            var blue = PixelImage.Rgb(60, 100, 220);
            Check(shirt.SourcePalette().Contains(blue), "shirt palette has its blue");
            shirt.ColorMap[blue] = PixelImage.Rgb(40, 180, 60);
            Check(ed.Doc.Composite(0).Colors().Contains(PixelImage.Rgb(40, 180, 60)), "colour swap shows");
            shirt.ResetColors();
            shirt.Hue = 120;
            var c = ed.Doc.Composite(0);
            Check(!c.Colors().Contains(blue), "hue shift changes the blue");
            Check(c.Colors().Contains(PixelImage.Rgb(0, 0, 0)), "outline kept black");
            shirt.ResetColors();
            ed.Doc.ReplaceColorEverywhere(blue, PixelImage.Rgb(250, 0, 250));
            Check(ed.Doc.Composite(0).Colors().Contains(PixelImage.Rgb(250, 0, 250)), "replace everywhere reaches part layers");
            shirt.ResetColors();

            // ---- colour limits
            var paint = ed.Doc.Layers.OfType<PaintLayer>().First();
            for (int i = 0; i < 30; i++) paint.Frames[0].Set(i, 63, PixelImage.Rgb(i * 8, 255 - i * 8, 128));
            Check(ed.Doc.ColorCount() > 15, "test has too many colours");
            var lim = ed.Doc.FinalFrames(true)[0].Colors().Count;
            Check(lim <= 15, "Gen 3 limit gives <= 15 colours, got " + lim);
            var g1 = new SpriteDocument(GenStyle.Get(1), SpriteKind.Front, null, GenStyle.Get(1).DefaultLayout(SpriteKind.Front));
            var gp = g1.NewPaintLayer("p"); g1.Layers.Add(gp);
            for (int i = 0; i < 56; i++) gp.Frames[0].Set(i, 10, PixelImage.Rgb(i * 4, i * 2, 200));
            Check(g1.FinalFrames(true)[0].Colors().IsSubsetOf(GenStyle.Get(1).Palette), "Gen 1 becomes 4 greys");

            // ---- save / load round trip
            var file = Path.Combine(dir, "test.psc");
            ed.Doc.Name = "TESTER";
            shirt.ColorMap[blue] = PixelImage.Rgb(40, 180, 60);
            var before = ed.Doc.Composite(0);
            ed.Doc.Save(file);
            var back = SpriteDocument.Load(file, lib);
            Check(back.Layers.Count == ed.Doc.Layers.Count && back.Name == "TESTER", "load keeps layers and name");
            Check(back.Composite(0).SameAs(before), "load draws the same picture");
            var noLib = SpriteDocument.Load(file, new PartLibrary(Path.Combine(dir, "nowhere")));
            Check(noLib.Composite(0).SameAs(before) && noLib.Warnings.Count > 0, "opens without the parts (embedded copies)");

            // ---- export
            var outPng = Path.Combine(dir, "TESTER.png");
            ed.Doc.BuildSheet(true, 2).Save(outPng);
            var exp = PixelImage.Load(outPng);
            Check(exp != null && exp.W == 128 && exp.H == 128, "export at 2x is 128x128");
            Check(ExportDialog.DefaultFileName(ed.Doc, "TESTER") == "TESTER.png", "front file name");

            // ---- back sprite (strip of frames)
            var bpose = lib.Find(3, SpriteKind.Back, null, "Poses", "Throw");
            var bd = new SpriteDocument(st, SpriteKind.Back, null, new FrameLayout(64, 64, 4, 1));
            bd.Layers.Add(bd.MakePartLayer(bpose));
            Check(!bd.Composite(0).SameAs(bd.Composite(3)), "back frames differ");
            Check(bd.BuildSheet(false, 1).W == 256, "back sheet is a strip");
            Check(ExportDialog.DefaultFileName(bd, "TESTER") == "TESTER_back.png", "back file name");
            var ebd = new Editor(new Settings()) { Lib = lib }; ebd.SetDocument(bd);
            ebd.SetFrameCount(5);
            Check(bd.FrameCount == 5 && bd.Composite(4) != null, "frames can be added");

            // ---- overworld sheet
            var owPose = lib.Find(3, SpriteKind.Overworld, "Walk", "Poses", "Body");
            var ow = new SpriteDocument(st, SpriteKind.Overworld, "Walk", st.DefaultLayout(SpriteKind.Overworld));
            ow.Layers.Add(ow.MakePartLayer(owPose));
            var owp = ow.NewPaintLayer("Paint"); ow.Layers.Add(owp);
            var hair = lib.Find(3, SpriteKind.Overworld, "Walk", "Hair", "Spiky");
            Check(hair.FramesFor(ow.Layout).Scale == 2, "pixel-doubled part is shrunk (scale " + hair.FramesFor(ow.Layout).Scale + ")");
            ow.Layers.Add(ow.MakePartLayer(hair));
            Check(ow.Composite(0).Get(8, 6) != 0, "overworld hair drawn");
            Check(ow.BuildSheet(false, 1).W == 64 && ow.BuildSheet(false, 1).H == 128, "overworld sheet 64x128");
            owp.Frames[4].Set(2, 10, PixelImage.Rgb(255, 0, 0));
            var eow = new Editor(new Settings()) { Lib = lib }; eow.SetDocument(ow);
            eow.MirrorRows(true);
            Check(owp.Frames[8].Get(13, 10) == PixelImage.Rgb(255, 0, 0), "Left → Right mirror");
            Check(ExportDialog.DefaultFileName(ow, "TESTER") == "trainer_TESTER.png", "walk file name");
            ow.Sheet = "Bike";
            Check(ExportDialog.DefaultFileName(ow, "TESTER") == "TESTER_bike.png", "bike file name");

            // ---- built-in parts (the "Built-in parts" folder next to the exe)
            var bi = Settings.BuiltInPartsFolder;
            if (!Directory.Exists(bi)) Check(false, "built-in parts folder is missing: " + bi);
            else
            {
                var both = new PartLibrary(root, bi);
                var g5 = GenStyle.Get(5);
                var nateCats = new[] { "Poses", "Shoes", "Bottoms", "Tops", "Backpacks", "Hair", "Visors", "Accessories" };
                foreach (var cat in nateCats) Check(both.Categories(5, SpriteKind.Front, null).Contains(cat), "built-in category " + cat);
                var nd = new SpriteDocument(g5, SpriteKind.Front, null, g5.DefaultLayout(SpriteKind.Front));
                Check(nd.Layout.FrameW == 80, "Gen 5 front is 80x80");
                var ne = new Editor(new Settings()) { Lib = both };
                ne.SetDocument(nd);
                foreach (var cat in nateCats)
                {
                    var p = both.Parts(5, SpriteKind.Front, null, cat).FirstOrDefault(x => x.Name.StartsWith("Nate") && x.Name != "Nate line art");
                    Check(p != null && p.BuiltIn && p.Sheet != null && p.Sheet.W == 80 && !p.FramesFor(nd.Layout).Loose, "built-in Nate part in " + cat);
                    if (p != null) ne.SetPart(p, false);
                }
                var nate = nd.Composite(0);
                Check(nate.Colors().Count == 15, "Nate rebuilt with 15 colours, got " + nate.Colors().Count);
                Check(nd.Layers.OfType<PartLayer>().Select(l => l.Category).SequenceEqual(nateCats), "Nate layers in stack order");
                // the user's own part with the same name wins
                Save(new PixelImage(80, 80), root, "Gen 5/Front/Hair/Nate.png");
                both.Refresh();
                var hairPart = both.Find(5, SpriteKind.Front, null, "Hair", "Nate");
                Check(hairPart != null && !hairPart.BuiltIn && both.Parts(5, SpriteKind.Front, null, "Hair").Count(x => x.Name == "Nate") == 1, "user part replaces built-in");
                Check(both.CountBuiltIn() == 16, "16 built-in parts, got " + both.CountBuiltIn());
                // Ace Trainer M: every piece is a full 80x80 frame and together they make the 15-colour sprite
                var ad = new SpriteDocument(g5, SpriteKind.Front, null, g5.DefaultLayout(SpriteKind.Front));
                var ae = new Editor(new Settings()) { Lib = both };
                ae.SetDocument(ad);
                foreach (var cat in new[] { "Poses", "Shoes", "Bottoms", "Tops", "Hair", "Accessories" })
                {
                    var p = both.Parts(5, SpriteKind.Front, null, cat).FirstOrDefault(x => x.Name.StartsWith("Ace Trainer M") && !x.Name.EndsWith("line art"));
                    Check(p != null && p.BuiltIn && !p.FramesFor(ad.Layout).Loose, "built-in Ace Trainer M part in " + cat);
                    if (p != null) ae.SetPart(p, false);
                }
                Check(ad.Composite(0).Colors().Count == 15, "Ace Trainer M rebuilt with 15 colours, got " + ad.Composite(0).Colors().Count);
            }

            // ---- shapes
            Check(SpriteCanvas.LinePoints(new Point(0, 0), new Point(5, 2)).Count() == 6, "line");
            Check(SpriteCanvas.EllipsePoints(new Point(0, 0), new Point(9, 9), true).Count() > SpriteCanvas.EllipsePoints(new Point(0, 0), new Point(9, 9), false).Count(), "ellipse filled > outline");

            Console.WriteLine(_fail == 0 ? "SELFTEST OK" : "SELFTEST FAILED: " + _fail);
            Console.WriteLine("Sample parts: " + root);
            return _fail == 0 ? 0 : 1;
        }

        static void Check(bool ok, string what)
        {
            if (!ok) { _fail++; Console.WriteLine("FAIL: " + what); }
        }

        // ------------------------------------------------------------------ sample parts (plain test shapes)

        static int K = PixelImage.Rgb(0, 0, 0);
        static readonly int Skin = PixelImage.Rgb(248, 208, 168), SkinD = PixelImage.Rgb(208, 152, 112);

        static void Rect(PixelImage img, int x0, int y0, int x1, int y1, int fill, int outline)
        {
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    img.Set(x, y, (x == x0 || x == x1 || y == y0 || y == y1) ? outline : fill);
        }

        static void Oval(PixelImage img, int x0, int y0, int x1, int y1, int fill, int outline, bool topOnly = false)
        {
            var pts = SpriteCanvas.EllipsePoints(new Point(x0, y0), new Point(x1, y1), true).ToList();
            var set = new HashSet<Point>(pts);
            foreach (var p in pts)
            {
                if (topOnly && p.Y > (y0 + y1) / 2) continue;
                bool edge = !set.Contains(new Point(p.X + 1, p.Y)) || !set.Contains(new Point(p.X - 1, p.Y)) || !set.Contains(new Point(p.X, p.Y + 1)) || !set.Contains(new Point(p.X, p.Y - 1));
                if (topOnly && p.Y == (y0 + y1) / 2) edge = true;
                img.Set(p.X, p.Y, edge ? outline : fill);
            }
        }

        static void Save(PixelImage img, string root, string rel)
        {
            var path = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            img.Save(path);
        }

        /// <summary>A front pose outline: head, body, arms, legs drawn as black outlines with an empty inside.</summary>
        static PixelImage FrontPose(int arms)
        {
            var p = new PixelImage(64, 64);
            Oval(p, 24, 6, 39, 21, 0, K);                 // head
            Rect(p, 23, 22, 40, 44, 0, K);                // body
            Rect(p, 17, 23 - arms, 22, 40 - arms, 0, K);  // left arm
            Rect(p, 41, 23, 46, 40, 0, K);                // right arm
            Rect(p, 24, 45, 31, 61, 0, K);                // legs
            Rect(p, 32, 45, 39, 61, 0, K);
            return p;
        }

        public static void MakeSampleParts(string root)
        {
            int blue = PixelImage.Rgb(60, 100, 220), blueD = PixelImage.Rgb(30, 60, 160), brown = PixelImage.Rgb(140, 90, 50), brownD = PixelImage.Rgb(90, 55, 30);
            int red = PixelImage.Rgb(220, 50, 50), redD = PixelImage.Rgb(150, 20, 30), grey = PixelImage.Rgb(80, 80, 96);
            Save(FrontPose(0), root, "Gen 3/Front/Poses/Standing.png");
            Save(FrontPose(4), root, "Gen 3/Front/Poses/Waving.png");

            var shirt = new PixelImage(64, 64); Rect(shirt, 23, 22, 40, 44, blue, K); for (int y = 23; y < 44; y++) shirt.Set(39, y, blueD);
            Rect(shirt, 17, 23, 22, 32, blue, K); Rect(shirt, 41, 23, 46, 32, blue, K);
            Save(shirt, root, "Gen 3/Front/Tops/Shirt.png");
            var pants = new PixelImage(64, 64); Rect(pants, 24, 45, 31, 58, grey, K); Rect(pants, 32, 45, 39, 58, grey, K);
            Save(pants, root, "Gen 3/Front/Bottoms/Pants.png");
            var shoes = new PixelImage(64, 64); Rect(shoes, 23, 58, 31, 62, red, K); Rect(shoes, 32, 58, 40, 62, red, K);
            Save(shoes, root, "Gen 3/Front/Shoes/Sneakers.png");
            var hair = new PixelImage(64, 64); Oval(hair, 23, 4, 40, 17, brown, K, true); for (int x = 26; x < 38; x++) hair.Set(x, 7, brownD);
            Save(hair, root, "Gen 3/Front/Hair/Short.png");
            var hairL = new PixelImage(64, 64); Oval(hairL, 22, 4, 41, 19, brown, K, true); Rect(hairL, 21, 11, 24, 30, brown, K); Rect(hairL, 39, 11, 42, 30, brown, K);
            Save(hairL, root, "Gen 3/Front/Hair/Long.png");
            var cap = new PixelImage(64, 64); Oval(cap, 23, 2, 40, 15, red, K, true); Rect(cap, 22, 9, 46, 11, redD, K);
            Save(cap, root, "Gen 3/Front/Hats/Cap.png");
            var beanie = new PixelImage(64, 64); Oval(beanie, 23, 1, 40, 16, PixelImage.Rgb(90, 170, 90), K, true);
            Save(beanie, root, "Gen 3/Front/Hats/Beanie.png");
            var shadesImg = new PixelImage(14, 4); Rect(shadesImg, 0, 0, 5, 3, grey, K); Rect(shadesImg, 8, 0, 13, 3, grey, K); shadesImg.Set(6, 1, K); shadesImg.Set(7, 1, K);
            Save(shadesImg, root, "Gen 3/Front/Sunglasses/Shades.png");
            var packFront = new PixelImage(64, 64); for (int y = 22; y < 44; y++) { packFront.Set(26, y, brownD); packFront.Set(37, y, brownD); }
            Save(packFront, root, "Gen 3/Front/Backpacks/Pack.png");
            var packBehind = new PixelImage(64, 64); Rect(packBehind, 18, 20, 45, 42, PixelImage.Rgb(120, 80, 40), K);
            Save(packBehind, root, "Gen 3/Front/Backpacks/Pack_behind.png");

            // back sprite: 4-frame throw (the arm rises)
            var back = new PixelImage(256, 64);
            for (int f = 0; f < 4; f++)
            {
                int ox = f * 64;
                Oval(back, ox + 22, 8, ox + 41, 27, 0, K);
                Rect(back, ox + 18, 28, ox + 45, 63, 0, K);
                Rect(back, ox + 46, 40 - f * 8, ox + 52, 56 - f * 8, 0, K);
            }
            Save(back, root, "Gen 3/Back/Poses/Throw.png");
            var backHair = new PixelImage(64, 64); Oval(backHair, 21, 6, 42, 26, brown, K);
            Save(backHair, root, "Gen 3/Back/Hair/Short.png");

            // overworld walk sheet 4x4 frames of 16x32
            var ow = new PixelImage(64, 128);
            var owHair = new PixelImage(128, 256);
            for (int i = 0; i < 16; i++)
            {
                int ox = (i % 4) * 16, oy = (i / 4) * 32, step = (i % 2 == 1) ? 1 : 0;
                Oval(ow, ox + 3, oy + 4, ox + 12, oy + 13, Skin, K);
                Rect(ow, ox + 4, oy + 14, ox + 11, oy + 24, SkinD, K);
                Rect(ow, ox + 4, oy + 25, ox + 7, oy + 30 - step, SkinD, K);
                Rect(ow, ox + 8, oy + 25, ox + 11, oy + 30 - (1 - step) * (i % 4 == 3 ? 1 : 0), SkinD, K);
                // hair drawn pixel-doubled to test auto shrinking
                for (int y = 3; y <= 8; y++) for (int x = 3; x <= 12; x++)
                    { int c = y == 3 || x == 3 || x == 12 ? K : brown; for (int a = 0; a < 2; a++) for (int b = 0; b < 2; b++) owHair.Set((ox + x) * 2 + a, (oy + y) * 2 + b, c); }
            }
            Save(ow, root, "Gen 3/Overworld/Walk/Poses/Body.png");
            Save(owHair, root, "Gen 3/Overworld/Walk/Hair/Spiky.png");
            var bike = new PixelImage(64, 128);
            for (int i = 0; i < 16; i++) { int ox = (i % 4) * 16, oy = (i / 4) * 32; Oval(bike, ox + 2, oy + 22, ox + 13, oy + 31, 0, red); }
            Save(bike, root, "Gen 3/Overworld/Bike/Bikes/Basic.png");
            Save(ow, root, "Gen 3/Overworld/Bike/Poses/Rider.png");
        }
    }
}
