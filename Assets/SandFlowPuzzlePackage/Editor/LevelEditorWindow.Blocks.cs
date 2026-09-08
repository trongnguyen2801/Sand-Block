using System;
using System.Collections.Generic;
using SandFlowPuzzle.BlockAuthoring;
using SandFlowPuzzle.BlockAuthoring.EditorTools;
using UnityEditor;
using UnityEngine;

namespace SandFlowPuzzle
{
    public partial class LevelEditorWindow
    {
        private LevelData blockLevel;
        private LevelEditorDocument blockDocument;
        private readonly LevelEditorState blockState = new LevelEditorState();
        private readonly BlockInteraction blockInteraction = new BlockInteraction();
        private readonly GridEditInteraction gridInteraction = new GridEditInteraction();
        private readonly LevelEditorGridRenderer blockRenderer = new LevelEditorGridRenderer();
        private string blockError;
        private int blockMode = 2;

        private sealed class EmbeddedBoardRepository : ILevelDataRepository
        {
            private readonly LevelData level;
            private readonly Action changed;
            public EmbeddedBoardRepository(LevelData level, Action changed) { this.level = level; this.changed = changed; }
            public bool TryLoad(string path, out BlockXLevelFile data, out string error)
            {
                error = null;
                data = LevelDataCloneUtility.DeepClone(level.collectorBoard);
                return true;
            }
            public bool TrySaveAtomic(string path, BlockXLevelFile data, out string error)
            {
                if (!BlockXLevelValidator.TryValidate(data, out error)) return false;
                level.collectorBoard = LevelDataCloneUtility.DeepClone(data);
                changed();
                return true;
            }
        }

        // ───────────────────────────────────────────────────────────────────
        // Board section (drawn inside the right scroll view)
        // ───────────────────────────────────────────────────────────────────

        private void DrawCollectorBoardSection(LevelData level)
        {
            EditorGUILayout.LabelField("Board \u2014 Blocks & Sand", EditorStyles.boldLabel);
            if (!level.useAuthoredCollectorBoard || IsUninitializedBoard(level.collectorBoard) || !level.collectorBoard.sandInBoard)
            {
                try { SandBoardUtility.EnsureUnified(level); dirty = true; blockLevel = null; }
                catch (InvalidOperationException error) { EditorGUILayout.HelpBox(error.Message, MessageType.Error); return; }
            }
            if (!ReferenceEquals(blockLevel, level))
            {
                blockLevel = level;
                blockState.ResetForDocumentChange();
                blockDocument = new LevelEditorDocument(new EmbeddedBoardRepository(level, () => dirty = true));
                blockDocument.Load("embedded", out blockError);
            }
            // Keep stable IDs when pictures are reordered; append newly authored colors.
            var pictures = level.PrepareRuntimePictures();
            if (level.collectorPalette == null) level.collectorPalette = new List<SerializableColor>();
            foreach (var color in pictures[0].palette)
                if (!level.collectorPalette.Exists(c => c != null && CollectorBoardUtility.ColorKey(c) == CollectorBoardUtility.ColorKey(color)))
                { level.collectorPalette.Add(new SerializableColor(color.r, color.g, color.b)); dirty = true; }
            blockRenderer.ColorResolver = id => BoardColor(level, id);

            EditorGUILayout.HelpBox(
                "Edit Grid: toggle cells on/off.  Select Cells: click cells to select, then paint sand on the right.  Edit Block: drag to move blocks.  Add Block: click cells to create a new block.",
                MessageType.Info);

            // ── Toolbar ────────────────────────────────────────────────────
            int mode = GUILayout.Toolbar(blockMode, new[] { "Edit Grid", "Select Cells", "Edit Block", "Add Block" });
            if (mode != blockMode)
            {
                blockState.ResetForDocumentChange();
                blockMode = mode;
                blockState.SetMainMode(mode == 0 ? LevelEditorMainMode.EditGrid : LevelEditorMainMode.Block);
                blockState.SetBlockMode(
                    mode == 1 ? LevelEditorBlockMode.SelectCells :
                    mode == 3 ? LevelEditorBlockMode.Add :
                    mode == 2 ? LevelEditorBlockMode.Edit :
                    LevelEditorBlockMode.View);
                if (mode == 3 && blockState.AddDraft.ColorId <= 0) blockState.AddDraft.ColorId = 1;
            }

            // ── Grid resize (Edit Grid only) ───────────────────────────────
            if (mode == 0)
            {
                EditorGUILayout.BeginHorizontal();
                int rows = EditorGUILayout.DelayedIntField("Rows", blockDocument.Data.grid.rows);
                int cols = EditorGUILayout.DelayedIntField("Columns", blockDocument.Data.grid.columns);
                EditorGUILayout.EndHorizontal();
                if (rows != blockDocument.Data.grid.rows || cols != blockDocument.Data.grid.columns)
                    gridInteraction.TryResizeGrid(blockDocument, rows, cols, out blockError);
            }

            blockState.Zoom = EditorGUILayout.Slider("Block Grid Zoom", blockState.Zoom, 0.5f, 2f);
            EditorGUILayout.LabelField("Sand regions are fixed obstacles; every exposed edge can collect.", EditorStyles.centeredGreyMiniLabel);

            // ── Grid area (scrollable) ─────────────────────────────────────
            blockState.MiddleScroll = EditorGUILayout.BeginScrollView(blockState.MiddleScroll, GUILayout.Height(360));
            var viewport = blockRenderer.DrawGrid(blockDocument.Data, blockState.Zoom, mode != 0, blockState, mode == 3);

            // Sand region overlay (always visible)
            DrawSandRegions(level, viewport);

            // Cell selection overlay (Select Cells mode)
            if (mode == 1)
                blockRenderer.DrawCellSelection(blockDocument.Data, viewport, blockState);

            // Input handling per mode
            if (mode == 1) HandleCellSelectionInput(viewport);
            else HandleCollectorInput(viewport);

            EditorGUILayout.EndScrollView();

            // ── Inspector below grid ───────────────────────────────────────
            if (mode == 1)
            {
                DrawCellSelectionInspector(level);
            }
            else if (mode == 3)
            {
                blockState.AddDraft.ColorId = DrawBoardColorPicker(level, blockState.AddDraft.ColorId);
                EditorGUILayout.LabelField("Draft cells", blockState.AddDraft.Cells.Count.ToString());
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Create Block"))
                {
                    if (blockInteraction.CommitDraft(blockDocument, blockState, out blockError)) blockMode = 2;
                }
                if (GUILayout.Button("Clear Draft")) blockInteraction.CancelDraft(blockState);
                EditorGUILayout.EndHorizontal();
            }
            else if (blockState.SelectedBlockIndex >= 0 && blockState.SelectedBlockIndex < blockDocument.Data.blocks.Count)
            {
                int index = blockState.SelectedBlockIndex;
                var selected = blockDocument.Data.blocks[index];
                EditorGUILayout.LabelField($"Block #{index + 1} \u2014 {selected.occupiedCells.Count} cells");
                using (new EditorGUI.DisabledScope(mode != 2))
                {
                    int color = DrawBoardColorPicker(level, selected.colorId);
                    if (color != selected.colorId) blockInteraction.TrySetBlockColor(blockDocument, index, color, out blockError);
                    if (GUILayout.Button("Delete Selected Block"))
                    {
                        if (blockInteraction.TryDeleteBlock(blockDocument, index, out blockError)) blockState.SelectedBlockIndex = -1;
                    }
                }
                DrawBlockQuotaEditor(level, index, selected);
            }

            if (GUILayout.Button("Import BlockX Layout JSON")) ImportCollectorLayout(level);
            if (!string.IsNullOrEmpty(blockError)) EditorGUILayout.HelpBox(blockError, MessageType.Warning);
            if (!CollectorBoardUtility.TryResolve(level, out _, out string validationError))
                EditorGUILayout.HelpBox(validationError, MessageType.Warning);
            else EditorGUILayout.LabelField($"{level.collectorBoard.blocks.Count} blocks \u2022 Quotas match all picture grains", EditorStyles.miniLabel);
        }

        // ───────────────────────────────────────────────────────────────────
        // Cell Selection mode
        // ───────────────────────────────────────────────────────────────────

        private void HandleCellSelectionInput(LevelGridViewport viewport)
        {
            Event e = Event.current;
            if (e.type == EventType.Used) return;

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                blockState.ClearCellSelection();
                e.Use(); Repaint(); return;
            }

            bool inside = viewport.TryScreenToCell(e.mousePosition, out Vector2Int cell);

            // Mouse hover
            if (e.type == EventType.MouseMove && inside)
            {
                blockState.HoveredCell = cell;
                blockState.HoveredBlockIndex = blockInteraction.FindBlockAtCell(blockDocument.Data, cell);
                Repaint(); return;
            }

            // Start selection stroke
            if (e.type == EventType.MouseDown && inside && e.button <= 1)
            {
                bool cellEnabled = blockDocument.Data.grid.cells[
                    LevelGridCoordinateUtility.ToIndex(cell, blockDocument.Data.grid.rows, blockDocument.Data.grid.columns)] == 1;
                if (!cellEnabled) { e.Use(); return; }

                blockState.BeginCellSelect(e.button);
                if (e.button == 0) blockState.AddCell(cell);
                else blockState.RemoveCell(cell);
                e.Use(); Repaint(); return;
            }

            // Continue selection stroke
            if (e.type == EventType.MouseDrag && blockState.IsSelectingCells && inside)
            {
                bool cellEnabled = blockDocument.Data.grid.cells[
                    LevelGridCoordinateUtility.ToIndex(cell, blockDocument.Data.grid.rows, blockDocument.Data.grid.columns)] == 1;
                if (!cellEnabled) { e.Use(); return; }

                if (blockState.SelectCellsButton == 0) blockState.AddCell(cell);
                else blockState.RemoveCell(cell);
                e.Use(); Repaint(); return;
            }

            // End selection stroke
            if (e.type == EventType.MouseUp && blockState.IsSelectingCells)
            {
                blockState.EndCellSelect();
                e.Use(); Repaint(); return;
            }
        }

        private void DrawCellSelectionInspector(LevelData level)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField($"Selected: {blockState.SelectedCells.Count} cell(s)", EditorStyles.boldLabel);

            if (blockState.SelectedCells.Count > 0 && blockState.GetSelectionBounds(out RectInt bounds))
            {
                EditorGUILayout.LabelField($"Region: {bounds.width} x {bounds.height} cells", EditorStyles.miniLabel);

                // Show the selected cells painter
                DrawSelectedCellsPainter(level, bounds);
            }
            else
            {
                EditorGUILayout.HelpBox("Click cells on the grid to select them. Left-click adds, right-click removes. Selected cells become a paint tile area.", MessageType.Info);
            }
        }

        // ───────────────────────────────────────────────────────────────────
        // Collector / Block input (Edit Grid, Edit Block, Add Block)
        // ───────────────────────────────────────────────────────────────────

        private void HandleCollectorInput(LevelGridViewport viewport)
        {
            Event e = Event.current;
            if (e.type == EventType.Used) return;
            bool inside = viewport.TryScreenToCell(e.mousePosition, out Vector2Int cell);
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                blockState.CancelDrag(); blockState.AddDraft.Clear(); e.Use(); Repaint(); return;
            }
            if (blockMode == 2 && blockState.IsBlockPointerArmed)
            {
                if (e.type == EventType.MouseDrag)
                {
                    if (inside) blockInteraction.UpdateBlockPointer(blockDocument.Data, cell, e.mousePosition, blockState);
                    else blockState.DragCandidateValid = false;
                    e.Use(); Repaint(); return;
                }
                if (e.type == EventType.MouseUp)
                {
                    if (inside) blockInteraction.ReleaseBlockPointer(blockDocument, blockState, out blockError);
                    else blockState.CancelDrag();
                    e.Use(); Repaint(); return;
                }
            }
            if (!inside) return;
            if (e.type == EventType.MouseMove)
            {
                blockState.HoveredCell = cell;
                blockState.HoveredBlockIndex = blockInteraction.FindBlockAtCell(blockDocument.Data, cell);
                Repaint(); return;
            }
            if ((e.type != EventType.MouseDown && e.type != EventType.MouseDrag) || e.button > 1) return;
            if (blockMode == 0)
            {
                if (gridInteraction.TryPaintCell(blockDocument, cell, e.button == 0 ? 1 : 0, out bool changed, out blockError) && changed)
                    blockDocument.Save(out blockError);
            }
            else if (blockMode == 3)
            {
                if (e.button == 0)
                {
                    if (!blockState.AddDraft.Cells.Contains(cell)) blockInteraction.TryAddDraftCell(blockDocument.Data, cell, blockState, out blockError);
                }
                else blockInteraction.RemoveDraftCell(cell, blockState);
            }
            else if (e.type == EventType.MouseDown && e.button == 0)
            {
                blockInteraction.SelectAtCell(blockDocument.Data, cell, blockState);
                if (blockMode == 2 && blockState.SelectedBlockIndex >= 0)
                    blockInteraction.ArmBlockPointer(blockDocument.Data, blockState.SelectedBlockIndex, cell, e.mousePosition, blockState);
            }
            e.Use(); Repaint();
        }

        // ───────────────────────────────────────────────────────────────────
        // Helpers
        // ───────────────────────────────────────────────────────────────────

        /// <summary>
        /// Shows per-color quota editor for a block. The user can set how many grains of each
        /// sand color this block should collect. Auto-split (legacy) is the default when left empty.
        /// </summary>
        private void DrawBlockQuotaEditor(LevelData level, int blockIndex, LevelBlockFile selected)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Collect Quotas", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Set how many grains of each color this block collects. Leave empty to auto-split evenly with other blocks of the same color.",
                MessageType.Info);

            // Compute the color's total grains available in pictures.
            var configuredColors = new Dictionary<int, bool>();
            if (selected.colorQuotas != null)
                foreach (var q in selected.colorQuotas)
                    if (q != null) configuredColors[q.colorId] = true;

            bool autoSplit = selected.colorQuotas == null || selected.colorQuotas.Count == 0;

            // Toggle auto vs manual
            bool useManual = !autoSplit;
            bool newManual = EditorGUILayout.Toggle("Manual quotas", useManual);
            if (newManual != useManual)
            {
                blockDocument.TryCommit("Toggle quotas", board =>
                {
                    var b = board.blocks[blockIndex];
                    if (newManual)
                    {
                        if (b.colorQuotas == null) b.colorQuotas = new List<BlockColorQuota>();
                        if (b.colorQuotas.Count == 0)
                            b.colorQuotas.Add(new BlockColorQuota { colorId = b.colorId, quota = 1 });
                    }
                    else b.colorQuotas = new List<BlockColorQuota>();
                }, out blockError);
                return;
            }

            // Show total grain counts per color for reference
            var pictures = level.PrepareRuntimePictures();
            var grainCounts = CollectorBoardUtility.CountSand(pictures, level.collectorBoard);

            // List every color used in the board as rows to edit quota
            var allColors = new List<int>();
            foreach (var block in blockDocument.Data.blocks)
                if (block != null && !allColors.Contains(block.colorId))
                    allColors.Add(block.colorId);

            var originalQuota = new Dictionary<int, int>();
            if (selected.colorQuotas != null)
                foreach (var q in selected.colorQuotas)
                    if (q != null) originalQuota[q.colorId] = q.quota;

            bool changed = false;
            foreach (int colorId in allColors)
            {
                int totalGrains = grainCounts.TryGetValue(colorId, out int c) ? c : 0;
                Color swatch = BoardColor(level, colorId);

                int currentQuota;
                if (!autoSplit)
                {
                    currentQuota = originalQuota.TryGetValue(colorId, out int v) ? v : 0;
                }
                else
                {
                    // Show the auto-split preview
                    if (colorId == selected.colorId)
                        currentQuota = CollectorBoardUtility.ResolveBlockQuotas(
                            blockDocument.Data, blockIndex, grainCounts).TryGetValue(colorId, out int a) ? a : 0;
                    else currentQuota = 0;
                }

                EditorGUILayout.BeginHorizontal();
                GUI.backgroundColor = swatch;
                GUILayout.Box("", GUILayout.Width(18f), GUILayout.Height(18f));
                GUI.backgroundColor = Color.white;

                string label = $"{colorId} \u2014 {totalGrains} grains";
                EditorGUILayout.LabelField(label, GUILayout.MinWidth(120));

                if (useManual)
                {
                    int next = EditorGUILayout.IntField(currentQuota, GUILayout.Width(60));
                    if (next != currentQuota)
                    {
                        blockDocument.TryCommit("Set quota", board =>
                        {
                            var b = board.blocks[blockIndex];
                            if (b.colorQuotas == null) b.colorQuotas = new List<BlockColorQuota>();
                            b.colorQuotas.RemoveAll(q => q.colorId == colorId);
                            if (next > 0) b.colorQuotas.Add(new BlockColorQuota { colorId = colorId, quota = next });
                            else if (b.colorQuotas.Count == 0) b.colorQuotas.Add(new BlockColorQuota { colorId = b.colorId, quota = 0 });
                        }, out blockError);
                        changed = true;
                    }
                }
                else
                {
                    EditorGUILayout.LabelField(currentQuota.ToString(), GUILayout.Width(60));
                }
                EditorGUILayout.EndHorizontal();
            }

            if (autoSplit && selected.colorQuotas != null && selected.colorQuotas.Count > 0)
            {
                // fallback cleanup is not needed; the toggle handles state.
            }

            // Summary of button counts
            DrawBlockQuotaSummary(level);
        }

        private void DrawBlockQuotaSummary(LevelData level)
        {
            var pictures = level.PrepareRuntimePictures();
            var grainCounts = CollectorBoardUtility.CountSand(pictures, level.collectorBoard);

            // Sum manual quotas per color across all blocks
            var manualTotal = new Dictionary<int, int>();
            var autoColors = new HashSet<int>();
            var manualCountByColor = new Dictionary<int, int>();
            foreach (var block in blockDocument.Data.blocks)
            {
                if (block == null) continue;
                if (block.colorQuotas != null && block.colorQuotas.Count > 0)
                {
                    foreach (var q in block.colorQuotas)
                        if (q != null && q.quota > 0)
                        {
                            manualTotal[q.colorId] = manualTotal.TryGetValue(q.colorId, out int t) ? t + q.quota : q.quota;
                            manualCountByColor[q.colorId] = manualCountByColor.TryGetValue(q.colorId, out int n) ? n + 1 : 1;
                        }
                }
                else autoColors.Add(block.colorId);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Quota Summary", EditorStyles.boldLabel);
            var allColors = new List<int>();
            foreach (var block in blockDocument.Data.blocks)
                if (block != null && !allColors.Contains(block.colorId))
                    allColors.Add(block.colorId);

            foreach (int colorId in allColors)
            {
                int total = grainCounts.TryGetValue(colorId, out int c) ? c : 0;
                int manual = manualTotal.TryGetValue(colorId, out int m) ? m : 0;
                int autoBlocks = 0;
                foreach (var block in blockDocument.Data.blocks)
                    if (block != null && block.colorId == colorId
                        && (block.colorQuotas == null || block.colorQuotas.Count == 0))
                        autoBlocks++;
                string autoInfo = autoBlocks > 0
                    ? $" + auto-split across {autoBlocks} block(s)"
                    : "";
                EditorGUILayout.LabelField($"Color {colorId}: {manual}/{total} assigned{autoInfo}", EditorStyles.miniLabel);
            }
        }

        // ───────────────────────────────────────────────────────────────────
        // Helpers
        // ───────────────────────────────────────────────────────────────────

        private static bool IsUninitializedBoard(BlockXLevelFile board)
        {
            return board == null || (string.IsNullOrEmpty(board.levelId)
                && (board.blocks == null || board.blocks.Count == 0));
        }

        private static Color BoardColor(LevelData level, int id)
        {
            if (id <= 0 || id > level.collectorPalette.Count) return Color.magenta;
            var color = level.collectorPalette[id - 1];
            return color == null ? Color.magenta : new Color(color.r, color.g, color.b);
        }

        private int DrawBoardColorPicker(LevelData level, int id)
        {
            EditorGUILayout.LabelField("Sand color");
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < level.collectorPalette.Count; i++)
            {
                if (i > 0 && i % 12 == 0) { EditorGUILayout.EndHorizontal(); EditorGUILayout.BeginHorizontal(); }
                Color before = GUI.backgroundColor;
                GUI.backgroundColor = BoardColor(level, i + 1);
                if (GUILayout.Button(id == i + 1 ? $"[{i + 1}]" : (i + 1).ToString(), GUILayout.Width(38))) id = i + 1;
                GUI.backgroundColor = before;
            }
            EditorGUILayout.EndHorizontal();
            return id;
        }

        // ───────────────────────────────────────────────────────────────────
        // Import
        // ───────────────────────────────────────────────────────────────────

        private void ImportCollectorLayout(LevelData level)
        {
            string path = EditorUtility.OpenFilePanel("Import BlockX Layout", "", "json");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var imported = JsonUtility.FromJson<BlockXLevelFile>(System.IO.File.ReadAllText(path));
                if (!BlockXLevelValidator.TryValidate(imported, out blockError)) return;
                level.collectorBoard = SandBoardUtility.EmbedPictures(imported, level.PrepareRuntimePictures().Count);
                level.collectorPalette = new List<SerializableColor>(level.PrepareRuntimePictures()[0].palette);
                blockLevel = null;
                dirty = true;
            }
            catch (Exception exception) { blockError = exception.Message; }
        }
    }
}
