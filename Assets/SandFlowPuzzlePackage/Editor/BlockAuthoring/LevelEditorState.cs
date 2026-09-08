using System.Collections.Generic;
using UnityEngine;

namespace SandFlowPuzzle.BlockAuthoring.EditorTools
{
    public enum LevelEditorMainMode
    {
        EditGrid,
        Block
    }

    public enum LevelEditorBlockMode
    {
        View,
        Edit,
        Add,
        SelectCells
    }

    /// <summary>
    /// Transient editor UI state. All cleanup is centralized in the mode/document methods below so
    /// the window never scatters ad-hoc field resets across branches.
    /// </summary>
    public sealed class LevelEditorState
    {
        public LevelEditorMainMode MainMode;
        public LevelEditorBlockMode BlockMode;

        public int SelectedBlockIndex = -1;
        public Vector2Int? HoveredCell;
        public int HoveredBlockIndex = -1;

        public bool IsDraggingBlock;
        public int DragBlockIndex = -1;
        public Vector2Int DragOriginalAnchor;
        public Vector2Int DragCandidateAnchor;
        public bool DragCandidateValid;

        /// <summary>True between a block MouseDown and its MouseUp/Escape (armed click).</summary>
        public bool IsBlockPointerArmed;

        /// <summary>Screen position captured at arm time; the 5 px drag threshold is measured from it.</summary>
        public Vector2 BlockPointerDownScreen;

        /// <summary>Grid cell under the pointer at arm time.</summary>
        public Vector2Int BlockPointerDownCell;

        /// <summary>Block index armed by the pointer-down, or -1 when nothing is armed.</summary>
        public int ArmedBlockIndex = -1;

        public AddBlockDraft AddDraft = new AddBlockDraft();

        // ── Cell selection (Select Cells mode) ──────────────────────────────
        /// <summary>Cells currently selected for sand painting.</summary>
        public readonly List<Vector2Int> SelectedCells = new List<Vector2Int>();

        /// <summary>True while the user is drag-selecting cells.</summary>
        public bool IsSelectingCells;

        /// <summary>Selection paint button (0 = add, 1 = remove).</summary>
        public int SelectCellsButton = -1;

        // ── Edit Grid paint stroke ──────────────────────────────────────────
        /// <summary>Mouse button that owns the active Edit Grid paint stroke, or -1 when inactive.</summary>
        public int GridPaintButton = -1;

        /// <summary>Target value for the active Edit Grid stroke: 1 enables cells, 0 disables them.</summary>
        public int GridPaintValue;

        /// <summary>Last cell visited by the active Edit Grid stroke.</summary>
        public Vector2Int GridPaintLastCell;

        public bool HasGridPaintLastCell;
        public bool GridPaintDirty;
        public bool IsGridPainting => GridPaintButton >= 0;

        public float Zoom = 1f;
        public Vector2 MiddleScroll;
        public Vector2 LeftScroll;
        public Vector2 RightScroll;

        /// <summary>
        /// Switches the main mode. Entering EditGrid clears block selection and cancels any drag
        /// and add draft, which are meaningless outside Block mode.
        /// </summary>
        public void SetMainMode(LevelEditorMainMode mode)
        {
            EndGridPaint();

            if (mode == LevelEditorMainMode.EditGrid)
            {
                SelectedBlockIndex = -1;
                CancelTransientInteraction();
                AddDraft.Clear();
                ClearCellSelection();
            }

            MainMode = mode;
        }

        /// <summary>
        /// Switches the block mode. Leaving Add cancels the draft; leaving Edit cancels any drag.
        /// </summary>
        public void SetBlockMode(LevelEditorBlockMode mode)
        {
            if (BlockMode == LevelEditorBlockMode.Add && mode != LevelEditorBlockMode.Add)
            {
                AddDraft.Clear();
            }

            if (BlockMode == LevelEditorBlockMode.Edit && mode != LevelEditorBlockMode.Edit)
            {
                CancelDrag();
            }

            if (BlockMode == LevelEditorBlockMode.SelectCells && mode != LevelEditorBlockMode.SelectCells)
            {
                EndCellSelect();
            }

            BlockMode = mode;
        }

        /// <summary>
        /// Clears every transient field when a different document is loaded or created: selection,
        /// hover, drag, and draft. Zoom and scroll positions are view state, not document state,
        /// and are intentionally preserved.
        /// </summary>
        public void ResetForDocumentChange()
        {
            SelectedBlockIndex = -1;
            HoveredCell = null;
            HoveredBlockIndex = -1;
            EndGridPaint();
            CancelDrag();
            AddDraft.Clear();
            ClearCellSelection();
        }

        /// <summary>
        /// Cancels the current transient pointer interaction (hover and drag) without touching
        /// selection or draft.
        /// </summary>
        public void CancelTransientInteraction()
        {
            HoveredCell = null;
            HoveredBlockIndex = -1;
            CancelDrag();
        }

        /// <summary>Starts a left/right Edit Grid paint stroke with a fixed target value.</summary>
        public void BeginGridPaint(int button, int targetValue)
        {
            GridPaintButton = button;
            GridPaintValue = targetValue;
            GridPaintLastCell = default;
            HasGridPaintLastCell = false;
            GridPaintDirty = false;
        }

        /// <summary>Ends the active Edit Grid stroke without changing the document.</summary>
        public void EndGridPaint()
        {
            GridPaintButton = -1;
            GridPaintValue = 0;
            GridPaintLastCell = default;
            HasGridPaintLastCell = false;
            GridPaintDirty = false;
        }

        /// <summary>
        /// Resets <see cref="SelectedBlockIndex"/> to -1 when there are no blocks or the current
        /// index is out of range; a valid selection (including the -1 no-selection sentinel) is
        /// preserved.
        /// </summary>
        public void NormalizeSelection(int blockCount)
        {
            if (blockCount <= 0 || SelectedBlockIndex < -1 || SelectedBlockIndex >= blockCount)
            {
                SelectedBlockIndex = -1;
            }
        }

        /// <summary>
        /// Clears the armed pointer and drag state. Called by mode/document cleanup and by
        /// <see cref="BlockInteraction.CancelDrag"/> / <see cref="BlockInteraction.ReleaseBlockPointer"/>.
        /// </summary>
        public void CancelDrag()
        {
            IsBlockPointerArmed = false;
            ArmedBlockIndex = -1;
            BlockPointerDownScreen = default;
            BlockPointerDownCell = default;
            IsDraggingBlock = false;
            DragBlockIndex = -1;
            DragOriginalAnchor = default;
            DragCandidateAnchor = default;
            DragCandidateValid = false;
        }

        // ── Cell Selection helpers ──────────────────────────────────────────

        public void ClearCellSelection()
        {
            SelectedCells.Clear();
            IsSelectingCells = false;
            SelectCellsButton = -1;
        }

        public void BeginCellSelect(int button)
        {
            IsSelectingCells = true;
            SelectCellsButton = button;
        }

        public void EndCellSelect()
        {
            IsSelectingCells = false;
            SelectCellsButton = -1;
        }

        public void ToggleCell(Vector2Int cell)
        {
            int idx = SelectedCells.IndexOf(cell);
            if (idx >= 0) SelectedCells.RemoveAt(idx);
            else SelectedCells.Add(cell);
        }

        public void AddCell(Vector2Int cell)
        {
            if (!SelectedCells.Contains(cell))
                SelectedCells.Add(cell);
        }

        public void RemoveCell(Vector2Int cell)
        {
            SelectedCells.RemoveAll(c => c.x == cell.x && c.y == cell.y);
        }

        /// <summary>Returns the bounding rectangle of the selected cells, or null if empty.</summary>
        public bool GetSelectionBounds(out RectInt bounds)
        {
            if (SelectedCells.Count == 0)
            {
                bounds = default;
                return false;
            }
            int minX = int.MaxValue, minY = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue;
            foreach (var c in SelectedCells)
            {
                if (c.x < minX) minX = c.x;
                if (c.y < minY) minY = c.y;
                if (c.x > maxX) maxX = c.x;
                if (c.y > maxY) maxY = c.y;
            }
            bounds = new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
            return true;
        }

        public bool IsCellSelected(Vector2Int cell)
        {
            return SelectedCells.Contains(cell);
        }
    }
}
