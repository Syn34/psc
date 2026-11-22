using System;
using System.Collections.Generic;
using PokeSpriteCreator.Model;

namespace PokeSpriteCreator.UI
{
    /// <summary>
    /// One tab: an open sprite file with its own selected part (the sprite or its walking sprites), layer, frame,
    /// Undo / Redo for each part, and view (zoom and scroll). The drawing colours and tool are shared by all tabs.
    /// </summary>
    public class Session
    {
        public SpriteDocument Doc;
        public int LayerIndex = -1, Frame;
        public List<DocState> Undo = new List<DocState>(), Redo = new List<DocState>();
        public readonly Dictionary<SpriteDocument, List<DocState>[]> History = new Dictionary<SpriteDocument, List<DocState>[]>();
        public string LastKey;
        public DateTime LastTime;
        public List<PixelImage> PreviewCache;
        public PixelImage Baseline;
        public Layer BaseLayer;
        public int BaseFrame = -1;

        // view
        public int Zoom = 8, ScrollX, ScrollY;
        public bool NeedsFit = true;

        public Session(SpriteDocument d)
        {
            Doc = d;
            LayerIndex = d.Layers.Count - 1;
            History[d] = new[] { Undo, Redo };
        }

        public SpriteDocument Root { get { return Doc.Root; } }
        public string Title { get { return Doc.Title; } }

        /// <summary>Name shown on the tab: the file name, or the sprite's name while it's unsaved.</summary>
        public string TabName
        {
            get
            {
                var r = Root;
                if (r.FilePath != null || string.IsNullOrEmpty(r.Name)) return r.Title;
                return r.Name + (r.AnyDirty ? " *" : "");
            }
        }

        /// <summary>Every frame of the part being edited as it will be exported (cached).</summary>
        public List<PixelImage> Preview
        {
            get
            {
                if (PreviewCache == null) PreviewCache = Doc.FinalFrames(Doc.LimitPalette);
                return PreviewCache;
            }
        }
    }
}
