using System;
using System.Collections.Generic;

namespace SandFlowPuzzle
{
    public enum SandSourceMode
    {
        Image = 0,
        Pattern = 1,
        Tiles = 2
    }

    [Serializable]
    public class SandPictureData
    {
        public string levelName = "New Level";

        // Sand grid (gridSize x gridSize flattened, color indices 0=empty, 1..N = palette colors)
        public List<byte> sandGrid = new List<byte>();
        public int gridSize = SandSimulator.GRID_SIZE;

        // Color palette (RGB, variable length)
        public List<SerializableColor> palette = new List<SerializableColor>();
        public int desiredColorCount = 4;

        // Source image (editor-only, path relative to Assets/)
        public string sourceImagePath = "";
        public int quantizationSeed = 0;

        // Optional hand-authored pattern stored below a Resources folder.
        // Existing levels default to Image, so their JSON remains compatible.
        public SandSourceMode sandSourceMode = SandSourceMode.Image;
        public string sandPatternResourcePath = "";

    }

    [Serializable]
    public class LevelData : SandPictureData
    {
        // Empty list preserves the original single picture stored above.
        public List<SandPictureData> sandPictures = new List<SandPictureData>();
        public float pictureGap = 0.04f;

        public List<SandPictureData> PrepareRuntimePictures()
        {
            var sources = sandPictures != null && sandPictures.Count > 0
                ? sandPictures : new List<SandPictureData> { this };
            var result = new List<SandPictureData>();
            var sharedPalette = new List<SerializableColor>();
            var colorIds = new Dictionary<int, byte>();
            foreach (SandPictureData source in sources)
            {
                if (source == null) throw new InvalidOperationException("A picture tile is missing.");
                var picture = new SandPictureData { levelName = source.levelName, gridSize = source.gridSize };
                var remap = new List<byte> { 0 };
                if (source.palette != null)
                foreach (SerializableColor color in source.palette)
                {
                    if (color == null) { remap.Add(0); continue; }
                    var rgb = (UnityEngine.Color32)new UnityEngine.Color(color.r, color.g, color.b);
                    int key = (rgb.r << 16) | (rgb.g << 8) | rgb.b;
                    if (!colorIds.TryGetValue(key, out byte id))
                    {
                        if (sharedPalette.Count >= byte.MaxValue)
                            throw new InvalidOperationException("Pictures may contain at most 255 distinct colors in total.");
                        id = (byte)(sharedPalette.Count + 1);
                        colorIds.Add(key, id);
                        sharedPalette.Add(new SerializableColor(rgb.r / 255f, rgb.g / 255f, rgb.b / 255f));
                    }
                    remap.Add(id);
                }
                if (source.sandGrid != null)
                    foreach (byte pixel in source.sandGrid)
                        picture.sandGrid.Add(pixel < remap.Count ? remap[pixel] : (byte)0);
                // An ungenerated tile is empty instead of silently spawning fallback art.
                int size = UnityEngine.Mathf.RoundToInt(UnityEngine.Mathf.Sqrt(picture.sandGrid.Count));
                if (size == 0 || size * size != picture.sandGrid.Count)
                    picture.sandGrid = new List<byte>(new byte[SandSimulator.GRID_SIZE * SandSimulator.GRID_SIZE]);
                result.Add(picture);
            }
            if (sharedPalette.Count == 0) sharedPalette.Add(new SerializableColor(1f, 1f, 1f));
            foreach (SandPictureData picture in result) picture.palette = sharedPalette;
            return result;
        }

        public bool useAuthoredCollectorBoard;
        public SandFlowPuzzle.BlockAuthoring.BlockXLevelFile collectorBoard;
        // Stable authoring colors; runtime resolves these against the current picture palette.
        public List<SerializableColor> collectorPalette = new List<SerializableColor>();

        // Legacy serialized fields retained for existing level files; the editor no longer uses them.
        // Bucket grid
        public int bucketRows = 5;
        public int bucketColumns = 5;
        public float bucketSpacingX = 0f;
        public float bucketSpacingY = 0f;
        public List<bool> gridCellEnabled = new List<bool>();

        // Bucket assignments
        public List<BucketDef> buckets = new List<BucketDef>();

        // Belt
        public int maxBeltSlots = 5;

        // Boosters
        public int slotBoosterCount = 3;
        public int shuffleBoosterCount = 3;
        public int magicBoosterCount = 3;
    }

    [Serializable]
    public class SerializableColor
    {
        public float r, g, b;

        public SerializableColor() { }

        public SerializableColor(float r, float g, float b)
        {
            this.r = r;
            this.g = g;
            this.b = b;
        }
    }

    [Serializable]
    public class BucketDef
    {
        public int row, col;
        public int colorId;    // palette index (1-based)
        public bool isMystery;
    }
}
