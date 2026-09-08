using System.Collections.Generic;
using SandFlowPuzzle.BlockAuthoring;
using UnityEngine;

namespace SandFlowPuzzle.BlockAuthoring.EditorTools
{
    /// <summary>
    /// Cell-based block hit testing, View-mode selection, and Block/Edit drag/drop for the BlockX
    /// level editor (Tasks 8-9).
    ///
    /// Hit testing (<see cref="FindBlockAtCell"/>) scans blocks in list order and occupied cells in
    /// list order, returning the matching block's 0-based index or -1. Valid level data cannot
    /// overlap, so there is no z-order ambiguity. Selection (<see cref="SelectAtCell"/>) mutates only
    /// <see cref="LevelEditorState.SelectedBlockIndex"/> — it never touches the document, the DTO,
    /// the revision, or the repository, and never calls <c>TryCommit</c>.
    ///
    /// Drag/drop: <see cref="ArmBlockPointer"/> captures the pointer-down on a block,
    /// <see cref="UpdateBlockPointer"/> enters true drag only after a 5 px screen threshold and
    /// snaps the candidate anchor to the pointer cell, and <see cref="ReleaseBlockPointer"/> commits
    /// exactly once through <see cref="TryMoveBlock"/> on a valid mouse-up or cancels without
    /// mutation. <see cref="CanPlaceTranslatedBlock"/> is the fast final-placement check used for
    /// ghost coloring; it validates only the final placement (no intermediate path), allows
    /// self-overlap, and rejects OFF cells, out-of-grid cells, and cells occupied by another block.
    ///
    /// Color editing, deletion, and draft/Create operations are kept on the same interaction
    /// service so the window does not need to own block mutation rules.
    /// </summary>
    public sealed class BlockInteraction
    {
        /// <summary>Screen-pixel distance the pointer must travel before a click becomes a drag.</summary>
        public const float DragThresholdPixels = 5f;

        /// <summary>
        /// Returns the 0-based index of the first block whose occupied cells contain the cell, or -1
        /// when no block occupies it. Returns -1 for null data, out-of-bounds cells, and empty
        /// cells. Scans blocks and occupied cells in list order.
        /// </summary>
        public int FindBlockAtCell(BlockXLevelFile data, Vector2Int cell)
        {
            if (data == null || data.blocks == null)
            {
                return -1;
            }

            for (int blockIndex = 0; blockIndex < data.blocks.Count; blockIndex++)
            {
                LevelBlockFile block = data.blocks[blockIndex];
                if (block == null || block.occupiedCells == null)
                {
                    continue;
                }

                for (int cellIndex = 0; cellIndex < block.occupiedCells.Count; cellIndex++)
                {
                    LevelCellCoord occupied = block.occupiedCells[cellIndex];
                    if (occupied.x == cell.x && occupied.y == cell.y)
                    {
                        return blockIndex;
                    }
                }
            }

            return -1;
        }

        /// <summary>
        /// Sets <see cref="LevelEditorState.SelectedBlockIndex"/> to the block at the cell, or -1
        /// when the cell is empty. Mutates only the state's selection; never the document, the DTO,
        /// the revision, or the repository. Null state or data is safely ignored.
        /// </summary>
        public void SelectAtCell(BlockXLevelFile data, Vector2Int cell, LevelEditorState state)
        {
            if (state == null)
            {
                return;
            }

            state.SelectedBlockIndex = FindBlockAtCell(data, cell);
        }

        /// <summary>
        /// Fast final-placement check for a translated block: every translated cell must be inside
        /// the grid, ON/playable, and not occupied by another block. Self-overlap with the block's
        /// own original cells is allowed, and no intermediate anchor/path is tested. Returns false
        /// with an error for null data, an invalid block index, or an empty block.
        /// </summary>
        public bool CanPlaceTranslatedBlock(
            BlockXLevelFile data,
            int blockIndex,
            Vector2Int candidateAnchor,
            out string error)
        {
            error = null;

            if (data == null || data.grid == null || data.grid.cells == null)
            {
                error = "Level data is not loaded.";
                return false;
            }

            if (data.blocks == null || blockIndex < 0 || blockIndex >= data.blocks.Count)
            {
                error = $"Block index {blockIndex} is out of range.";
                return false;
            }

            LevelBlockFile block = data.blocks[blockIndex];
            if (block == null || block.occupiedCells == null || block.occupiedCells.Count == 0)
            {
                error = "Block has no occupied cells.";
                return false;
            }

            int rows = data.grid.rows;
            int columns = data.grid.columns;

            var otherCells = new HashSet<Vector2Int>();
            for (int otherIndex = 0; otherIndex < data.blocks.Count; otherIndex++)
            {
                if (otherIndex == blockIndex)
                {
                    continue;
                }

                LevelBlockFile other = data.blocks[otherIndex];
                if (other == null || other.occupiedCells == null)
                {
                    continue;
                }

                for (int cellIndex = 0; cellIndex < other.occupiedCells.Count; cellIndex++)
                {
                    LevelCellCoord cell = other.occupiedCells[cellIndex];
                    otherCells.Add(new Vector2Int(cell.x, cell.y));
                }
            }

            if (data.sandInBoard && data.sandRegions != null)
                foreach (var region in data.sandRegions)
                    foreach (var cell in region.occupiedCells) otherCells.Add(new Vector2Int(cell.x, cell.y));

            LevelCellCoord sourceAnchorCell = block.occupiedCells[0];
            Vector2Int sourceAnchor = new Vector2Int(sourceAnchorCell.x, sourceAnchorCell.y);
            Vector2Int delta = candidateAnchor - sourceAnchor;

            for (int cellIndex = 0; cellIndex < block.occupiedCells.Count; cellIndex++)
            {
                LevelCellCoord source = block.occupiedCells[cellIndex];
                var translated = new Vector2Int(source.x + delta.x, source.y + delta.y);

                if (!LevelGridCoordinateUtility.IsInside(translated, rows, columns))
                {
                    error = $"Translated cell ({translated.x}, {translated.y}) is outside the grid.";
                    return false;
                }

                int gridIndex = LevelGridCoordinateUtility.ToIndex(translated, rows, columns);
                if (data.grid.cells[gridIndex] != 1)
                {
                    error = $"Translated cell ({translated.x}, {translated.y}) is not playable.";
                    return false;
                }

                if (otherCells.Contains(translated))
                {
                    error = $"Translated cell ({translated.x}, {translated.y}) is occupied by another block.";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Transactionally moves a block so its anchor lands on <paramref name="candidateAnchor"/>.
        /// Rejects an invalid index, a null/empty block, and an invalid final placement; returns true
        /// without any mutation, revision, or save when the candidate equals the current anchor.
        /// Otherwise commits exactly once via <see cref="LevelEditorDocument.TryCommit"/>, preserving
        /// the occupied-cell order and translating every coordinate by the anchor delta.
        /// </summary>
        public bool TryMoveBlock(
            LevelEditorDocument document,
            int blockIndex,
            Vector2Int candidateAnchor,
            out string error)
        {
            error = null;

            if (document == null || document.Data == null)
            {
                error = "No level document is loaded.";
                return false;
            }

            BlockXLevelFile data = document.Data;
            if (data.blocks == null || blockIndex < 0 || blockIndex >= data.blocks.Count)
            {
                error = $"Block index {blockIndex} is out of range.";
                return false;
            }

            LevelBlockFile block = data.blocks[blockIndex];
            if (block == null || block.occupiedCells == null || block.occupiedCells.Count == 0)
            {
                error = "Block has no occupied cells.";
                return false;
            }

            LevelCellCoord sourceAnchorCell = block.occupiedCells[0];
            Vector2Int sourceAnchor = new Vector2Int(sourceAnchorCell.x, sourceAnchorCell.y);
            if (candidateAnchor == sourceAnchor)
            {
                return true;
            }

            if (!CanPlaceTranslatedBlock(data, blockIndex, candidateAnchor, out error))
            {
                return false;
            }

            return document.TryCommit("Move block", candidate =>
            {
                LevelBlockFile target = candidate.blocks[blockIndex];
                LevelCellCoord anchorCell = target.occupiedCells[0];
                Vector2Int anchor = new Vector2Int(anchorCell.x, anchorCell.y);
                Vector2Int delta = candidateAnchor - anchor;

                for (int cellIndex = 0; cellIndex < target.occupiedCells.Count; cellIndex++)
                {
                    LevelCellCoord source = target.occupiedCells[cellIndex];
                    target.occupiedCells[cellIndex] =
                        new LevelCellCoord(source.x + delta.x, source.y + delta.y);
                }
            }, out error);
        }

        /// <summary>
        /// Changes only the selected block's color id through one document transaction. Reusing the
        /// current color is a no-op and therefore does not increment the revision or autosave.
        /// Gameplay does not define a color range, so the editor preserves any integer color id.
        /// </summary>
        public bool TrySetBlockColor(
            LevelEditorDocument document,
            int blockIndex,
            int colorId,
            out string error)
        {
            error = null;

            if (!TryGetExistingBlock(document, blockIndex, out LevelBlockFile block, out error))
            {
                return false;
            }

            if (block.colorId == colorId)
            {
                return true;
            }

            return document.TryCommit(
                "Change block color",
                candidate => candidate.blocks[blockIndex].colorId = colorId,
                out error);
        }

        /// <summary>
        /// Removes exactly one block list entry through a document transaction. Remaining entries
        /// stay in their original order, so their runtime list-derived indices shift naturally.
        /// </summary>
        public bool TryDeleteBlock(
            LevelEditorDocument document,
            int blockIndex,
            out string error)
        {
            error = null;

            if (!TryGetExistingBlock(document, blockIndex, out _, out error))
            {
                return false;
            }

            return document.TryCommit(
                "Delete block",
                candidate => candidate.blocks.RemoveAt(blockIndex),
                out error);
        }

        private static bool TryGetExistingBlock(
            LevelEditorDocument document,
            int blockIndex,
            out LevelBlockFile block,
            out string error)
        {
            block = null;
            error = null;

            if (document == null || document.Data == null)
            {
                error = "No level document is loaded.";
                return false;
            }

            if (document.Data.blocks == null
                || blockIndex < 0
                || blockIndex >= document.Data.blocks.Count)
            {
                error = $"Block index {blockIndex} is out of range.";
                return false;
            }

            block = document.Data.blocks[blockIndex];
            if (block == null || block.occupiedCells == null)
            {
                error = $"Block at index {blockIndex} is incomplete.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Starts a transient add-block draft. Existing draft cells are discarded, while the color
        /// id is set for the new draft. No document mutation or save occurs.
        /// </summary>
        public void BeginAdd(int colorId, LevelEditorState state)
        {
            if (state == null)
            {
                return;
            }

            if (state.AddDraft == null)
            {
                state.AddDraft = new AddBlockDraft();
            }

            state.AddDraft.ColorId = colorId;
            state.AddDraft.Clear();
        }

        /// <summary>
        /// Adds one ordered draft cell after checking the current grid and existing block
        /// occupancy. Draft cells are editor-only, so this operation never validates or mutates the
        /// document and never autosaves. The first successful cell remains the anchor at index 0.
        /// </summary>
        public bool TryAddDraftCell(
            BlockXLevelFile data,
            Vector2Int cell,
            LevelEditorState state,
            out string error)
        {
            error = null;

            if (state == null)
            {
                error = "Editor state is not available.";
                return false;
            }

            if (state.AddDraft == null)
            {
                state.AddDraft = new AddBlockDraft();
            }

            if (data == null || data.grid == null || data.grid.cells == null)
            {
                error = "Level data is not loaded.";
                return false;
            }

            if (!LevelGridCoordinateUtility.IsInside(cell, data.grid.rows, data.grid.columns))
            {
                error = $"Cell ({cell.x},{cell.y}) is outside the grid.";
                return false;
            }

            int gridIndex = LevelGridCoordinateUtility.ToIndex(cell, data.grid.rows, data.grid.columns);
            if (data.grid.cells[gridIndex] != 1)
            {
                error = $"Cell ({cell.x},{cell.y}) is not playable.";
                return false;
            }

            if (state.AddDraft.Cells.Contains(cell))
            {
                error = $"Cell ({cell.x},{cell.y}) is already in the draft.";
                return false;
            }

            int occupyingBlock = FindOccupyingBlock(data, cell);
            if (occupyingBlock >= 0)
            {
                error = $"Cell ({cell.x},{cell.y}) is occupied by Block #{occupyingBlock + 1}.";
                return false;
            }

            if (data.sandInBoard && data.sandRegions != null)
                foreach (var region in data.sandRegions)
                    foreach (var occupied in region.occupiedCells)
                        if (occupied.x == cell.x && occupied.y == cell.y)
                        { error = "This cell belongs to a sand region."; return false; }
            state.AddDraft.Cells.Add(cell);
            return true;
        }

        /// <summary>Removes one draft cell while preserving the order of every remaining cell.</summary>
        public bool RemoveDraftCell(Vector2Int cell, LevelEditorState state)
        {
            if (state == null || state.AddDraft == null)
            {
                return false;
            }

            return state.AddDraft.Cells.Remove(cell);
        }

        /// <summary>
        /// Commits the draft as one new ordered block. Failed validation leaves the draft and mode
        /// untouched; a successful document commit selects the new block, clears the draft, and
        /// enters Block/Edit. Draft painting itself never reaches this transaction.
        /// </summary>
        public bool CommitDraft(
            LevelEditorDocument document,
            LevelEditorState state,
            out string error)
        {
            error = null;

            if (document == null || document.Data == null)
            {
                error = "No level document is loaded.";
                return false;
            }

            if (state == null || state.AddDraft == null || state.AddDraft.Cells.Count == 0)
            {
                error = "Add Block draft must contain at least one cell.";
                return false;
            }

            if (document.Data.blocks == null)
            {
                error = "Blocks list cannot be null.";
                return false;
            }

            int newBlockIndex = document.Data.blocks.Count;
            int colorId = state.AddDraft.ColorId;
            var draftCells = new List<Vector2Int>(state.AddDraft.Cells);

            bool committed = document.TryCommit(
                "Create block",
                candidate =>
                {
                    var block = new LevelBlockFile
                    {
                        colorId = colorId,
                        occupiedCells = new List<LevelCellCoord>(draftCells.Count)
                    };

                    for (int cellIndex = 0; cellIndex < draftCells.Count; cellIndex++)
                    {
                        Vector2Int cell = draftCells[cellIndex];
                        block.occupiedCells.Add(new LevelCellCoord(cell.x, cell.y));
                    }

                    candidate.blocks.Add(block);
                },
                out error);

            if (!committed)
            {
                return false;
            }

            state.SelectedBlockIndex = newBlockIndex;
            state.AddDraft.Clear();
            state.SetBlockMode(LevelEditorBlockMode.Edit);
            return true;
        }

        /// <summary>Clears the transient draft without changing the document or save state.</summary>
        public void CancelDraft(LevelEditorState state)
        {
            if (state?.AddDraft == null)
            {
                return;
            }

            state.AddDraft.Clear();
        }

        private static int FindOccupyingBlock(BlockXLevelFile data, Vector2Int cell)
        {
            if (data == null || data.blocks == null)
            {
                return -1;
            }

            for (int blockIndex = 0; blockIndex < data.blocks.Count; blockIndex++)
            {
                LevelBlockFile block = data.blocks[blockIndex];
                if (block == null || block.occupiedCells == null)
                {
                    continue;
                }

                for (int cellIndex = 0; cellIndex < block.occupiedCells.Count; cellIndex++)
                {
                    LevelCellCoord occupied = block.occupiedCells[cellIndex];
                    if (occupied.x == cell.x && occupied.y == cell.y)
                    {
                        return blockIndex;
                    }
                }
            }

            return -1;
        }

        /// <summary>
        /// Captures a pointer-down on a block: stores the armed index, the down screen position and
        /// cell, and seeds the drag fields with the block's original anchor as the initial candidate.
        /// Does not enter drag state (that requires the 5 px threshold in
        /// <see cref="UpdateBlockPointer"/>) and never mutates the document. Null state or an
        /// invalid block is safely ignored.
        /// </summary>
        public void ArmBlockPointer(
            BlockXLevelFile data,
            int blockIndex,
            Vector2Int pointerCell,
            Vector2 screenPosition,
            LevelEditorState state)
        {
            if (state == null)
            {
                return;
            }

            if (data == null || data.blocks == null || blockIndex < 0 || blockIndex >= data.blocks.Count)
            {
                return;
            }

            LevelBlockFile block = data.blocks[blockIndex];
            if (block == null || block.occupiedCells == null || block.occupiedCells.Count == 0)
            {
                return;
            }

            LevelCellCoord anchorCell = block.occupiedCells[0];
            Vector2Int originalAnchor = new Vector2Int(anchorCell.x, anchorCell.y);

            state.IsBlockPointerArmed = true;
            state.ArmedBlockIndex = blockIndex;
            state.BlockPointerDownScreen = screenPosition;
            state.BlockPointerDownCell = pointerCell;
            state.IsDraggingBlock = false;
            state.DragBlockIndex = blockIndex;
            state.DragOriginalAnchor = originalAnchor;
            state.DragCandidateAnchor = originalAnchor;
            state.DragCandidateValid = true;
        }

        /// <summary>
        /// Updates an armed pointer: enters true drag state once the pointer has moved at least
        /// <see cref="DragThresholdPixels"/> from the down position, then snaps the candidate anchor
        /// to the current pointer cell and refreshes <see cref="LevelEditorState.DragCandidateValid"/>
        /// with the fast placement check. Never mutates the document. No-op when nothing is armed.
        /// </summary>
        public void UpdateBlockPointer(
            BlockXLevelFile data,
            Vector2Int pointerCell,
            Vector2 screenPosition,
            LevelEditorState state)
        {
            if (state == null || !state.IsBlockPointerArmed)
            {
                return;
            }

            if (Vector2.Distance(screenPosition, state.BlockPointerDownScreen) >= DragThresholdPixels)
            {
                state.IsDraggingBlock = true;
            }

            state.DragCandidateAnchor = pointerCell;
            state.DragCandidateValid = CanPlaceTranslatedBlock(data, state.ArmedBlockIndex, pointerCell, out _);
        }

        /// <summary>
        /// Ends an armed pointer interaction. A click that never crossed the drag threshold is a
        /// no-op; a drag whose candidate equals the original anchor is a no-op; an invalid candidate
        /// cancels with no document mutation; a valid candidate commits exactly once via
        /// <see cref="TryMoveBlock"/>. Armed/drag state is cleared in every path. Returns false with
        /// an error when nothing was armed or the candidate was invalid.
        /// </summary>
        public bool ReleaseBlockPointer(
            LevelEditorDocument document,
            LevelEditorState state,
            out string error)
        {
            error = null;

            if (state == null || !state.IsBlockPointerArmed)
            {
                error = "No block pointer is armed.";
                return false;
            }

            int blockIndex = state.ArmedBlockIndex;
            Vector2Int candidateAnchor = state.DragCandidateAnchor;
            bool dragging = state.IsDraggingBlock;

            state.CancelDrag();

            if (blockIndex < 0)
            {
                error = "No block is armed.";
                return false;
            }

            if (!dragging)
            {
                return true;
            }

            return TryMoveBlock(document, blockIndex, candidateAnchor, out error);
        }

        /// <summary>
        /// Cancels the armed pointer and drag state without touching the document. Null state is
        /// safely ignored.
        /// </summary>
        public void CancelDrag(LevelEditorState state)
        {
            if (state == null)
            {
                return;
            }

            state.CancelDrag();
        }
    }
}
