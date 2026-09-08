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

        private void DrawCollectorBoardSection(LevelData level)
        {
            EditorGUILayout.LabelField("Board — Blocks & Sand", EditorStyles.boldLabel);
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
            EditorGUILayout.HelpBox("One board contains both movable blocks and fixed sand regions. Sand Tiles: paint the footprint of a picture. Blocks collect matching grains along any touching edge. Save All saves the whole board.", MessageType.Info);
            int mode = GUILayout.Toolbar(blockMode, new[] { "Edit Grid", "View", "Edit Block", "Add Block", "Sand Tiles" });
            if (mode != blockMode)
            {
                blockState.ResetForDocumentChange();
                blockMode = mode;
                sandFootprintDraft.Clear();
                blockState.SetMainMode(mode == 0 ? LevelEditorMainMode.EditGrid : LevelEditorMainMode.Block);
                blockState.SetBlockMode(mode == 3 ? LevelEditorBlockMode.Add : mode == 2 ? LevelEditorBlockMode.Edit : LevelEditorBlockMode.View);
                if (mode == 3 && blockState.AddDraft.ColorId <= 0) blockState.AddDraft.ColorId = 1;
            }
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
            blockState.MiddleScroll = EditorGUILayout.BeginScrollView(blockState.MiddleScroll, GUILayout.Height(360));
            var viewport = blockRenderer.DrawGrid(blockDocument.Data, blockState.Zoom, mode != 0, blockState, mode == 3);
            DrawSandRegions(level, viewport);
            if (mode == 4) HandleSandRegionInput(level, viewport);
            else HandleCollectorInput(viewport);
            EditorGUILayout.EndScrollView();
            if (mode == 4) DrawSandRegionInspector(level);
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
                EditorGUILayout.LabelField($"Block #{index + 1} — {selected.occupiedCells.Count} cells");
                using (new EditorGUI.DisabledScope(mode != 2))
                {
                    int color = DrawBoardColorPicker(level, selected.colorId);
                    if (color != selected.colorId) blockInteraction.TrySetBlockColor(blockDocument, index, color, out blockError);
                    if (GUILayout.Button("Delete Selected Block"))
                    {
                        if (blockInteraction.TryDeleteBlock(blockDocument, index, out blockError)) blockState.SelectedBlockIndex = -1;
                    }
                }
            }
            if (GUILayout.Button("Import BlockX Layout JSON")) ImportCollectorLayout(level);
            if (!string.IsNullOrEmpty(blockError)) EditorGUILayout.HelpBox(blockError, MessageType.Warning);
            if (!CollectorBoardUtility.TryResolve(level, out _, out string validationError))
                EditorGUILayout.HelpBox(validationError, MessageType.Warning);
            else EditorGUILayout.LabelField($"{level.collectorBoard.blocks.Count} blocks • Quotas match all picture grains", EditorStyles.miniLabel);
        }

        private static bool IsUninitializedBoard(BlockXLevelFile board)
        {
            // Unity can deserialize an absent nested class as an empty default instance.
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
