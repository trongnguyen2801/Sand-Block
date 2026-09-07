using System.Collections.Generic;
using UnityEngine;

namespace SandFlowPuzzle
{
    /// <summary>
    /// A hand-authored sand picture that does not require a source texture.
    /// Pixel values are palette indices: 0 is empty, 1..N are colors.
    /// Keep pattern assets under a Resources folder so levels can load them at runtime.
    /// </summary>
    [CreateAssetMenu(fileName = "SandPattern", menuName = "Sand Flow Puzzle/Sand Pattern")]
    public sealed class SandPatternAsset : ScriptableObject
    {
        public int gridSize = SandSimulator.GRID_SIZE;
        public List<byte> pixels = new List<byte>();
        public List<SerializableColor> palette = new List<SerializableColor>();

        public void EnsureValidGrid()
        {
            gridSize = Mathf.Max(1, gridSize);
            int requiredCount = gridSize * gridSize;
            if (pixels == null)
                pixels = new List<byte>(requiredCount);

            while (pixels.Count < requiredCount)
                pixels.Add(0);
            while (pixels.Count > requiredCount)
                pixels.RemoveAt(pixels.Count - 1);

            if (palette == null)
                palette = new List<SerializableColor>();
        }
    }
}
