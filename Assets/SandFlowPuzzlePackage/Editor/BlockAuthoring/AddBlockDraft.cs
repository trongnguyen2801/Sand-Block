using System.Collections.Generic;
using UnityEngine;

namespace SandFlowPuzzle.BlockAuthoring.EditorTools
{
    /// <summary>
    /// Transient add-block draft: the color and ordered cell list being painted before a single
    /// Create transaction commits them. Editor interaction state, never document data. The first
    /// cell added stays at index 0 (the anchor) and list order is preserved exactly.
    /// </summary>
    public sealed class AddBlockDraft
    {
        public int ColorId;

        /// <summary>Draft cells in insertion order; index 0 is the anchor.</summary>
        public List<Vector2Int> Cells = new List<Vector2Int>();

        /// <summary>
        /// Empties the draft cells. <see cref="ColorId"/> is intentionally preserved: mode and
        /// document resets that need a fresh color reset it explicitly.
        /// </summary>
        public void Clear()
        {
            Cells.Clear();
        }
    }
}