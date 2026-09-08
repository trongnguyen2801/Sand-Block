using System.Collections.Generic;
using SandFlowPuzzle.BlockAuthoring;
using SandFlowPuzzle.BlockAuthoring.EditorTools;
using UnityEditor;
using UnityEngine;

namespace SandFlowPuzzle
{
    public partial class LevelEditorWindow
    {
        private readonly List<LevelCellCoord> sandFootprintDraft = new List<LevelCellCoord>();
        private readonly Dictionary<int, Texture2D> boardPictureTextures = new Dictionary<int, Texture2D>();
        private readonly Dictionary<int, int> boardPictureHashes = new Dictionary<int, int>();

        private void DrawSandRegions(LevelData level, LevelGridViewport viewport)
        {
            if (blockDocument.Data.sandRegions != null)
            foreach (var region in blockDocument.Data.sandRegions)
            {
                var pictures = level.sandPictures != null && level.sandPictures.Count > 0
                    ? level.sandPictures : new List<SandPictureData> { level };
                if (region.pictureIndex < 0 || region.pictureIndex >= pictures.Count) continue;
                RectInt bounds = SandBoardUtility.Bounds(region);
                float side = Mathf.Max(bounds.width, bounds.height);
                Texture2D texture = GetBoardPictureTexture(region.pictureIndex, pictures[region.pictureIndex]);
                foreach (var cell in region.occupiedCells)
                {
                    Rect rect = viewport.GetCellRect(new Vector2Int(cell.x, cell.y));
                    if (texture != null)
                        GUI.DrawTextureWithTexCoords(rect, texture, new Rect((cell.x - bounds.x) / side,
                            (cell.y - bounds.y) / side, 1f / side, 1f / side));
                    else EditorGUI.DrawRect(rect, Color.gray);
                    DrawRectBorder(rect, new Color(0.65f, 0.8f, 1f), 2f);
                }
                Rect label = viewport.GetCellRect(new Vector2Int(region.occupiedCells[0].x, region.occupiedCells[0].y));
                GUI.Label(label, $"S{region.pictureIndex + 1}", EditorStyles.whiteBoldLabel);
            }
            if (blockMode == 4)
                foreach (var cell in sandFootprintDraft)
                {
                    Rect rect = viewport.GetCellRect(new Vector2Int(cell.x, cell.y));
                    EditorGUI.DrawRect(rect, new Color(0.2f, 0.85f, 1f, 0.4f));
                    DrawRectBorder(rect, Color.cyan, 2f);
                }
        }

        private Texture2D GetBoardPictureTexture(int index, SandPictureData picture)
        {
            if (picture.sandGrid == null || picture.gridSize < 1 || picture.sandGrid.Count != picture.gridSize * picture.gridSize) return null;
            int hash = picture.gridSize;
            unchecked
            {
                foreach (byte cell in picture.sandGrid) hash = hash * 31 + cell;
                foreach (var color in picture.palette) hash = hash * 31 + CollectorBoardUtility.ColorKey(color);
            }
            if (boardPictureTextures.TryGetValue(index, out var texture) && texture != null
                && boardPictureHashes.TryGetValue(index, out int oldHash) && hash == oldHash) return texture;
            if (texture != null) DestroyImmediate(texture);
            int size = picture.gridSize;
            texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                int id = picture.sandGrid[y * size + x];
                var color = id > 0 && id <= picture.palette.Count ? picture.palette[id - 1] : null;
                pixels[(size - 1 - y) * size + x] = color == null ? (Color32)new Color(.25f,.25f,.25f)
                    : (Color32)new Color(color.r, color.g, color.b);
            }
            texture.SetPixels32(pixels); texture.Apply(false);
            boardPictureTextures[index] = texture; boardPictureHashes[index] = hash;
            return texture;
        }

        private void HandleSandRegionInput(LevelData level, LevelGridViewport viewport)
        {
            Event e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            { sandFootprintDraft.Clear(); e.Use(); Repaint(); return; }
            if ((e.type != EventType.MouseDown && e.type != EventType.MouseDrag) || e.button > 1
                || !viewport.TryScreenToCell(e.mousePosition, out Vector2Int cell)) return;
            var coord = new LevelCellCoord(cell.x, cell.y);
            if (e.button == 1) sandFootprintDraft.RemoveAll(c => c.x == cell.x && c.y == cell.y);
            else if (!sandFootprintDraft.Exists(c => c.x == cell.x && c.y == cell.y))
            {
                var data = blockDocument.Data;
                bool blocked = data.grid.cells[LevelGridCoordinateUtility.ToIndex(cell, data.grid.rows, data.grid.columns)] == 0
                    || blockInteraction.FindBlockAtCell(data, cell) >= 0;
                foreach (var region in data.sandRegions)
                    if (region.pictureIndex != selectedPictureIndex && region.occupiedCells.Exists(c => c.x == cell.x && c.y == cell.y)) blocked = true;
                if (blocked) blockError = "This cell is disabled or already occupied.";
                else { sandFootprintDraft.Add(coord); blockError = null; }
            }
            e.Use(); Repaint();
        }

        private void DrawSandRegionInspector(LevelData level)
        {
            int count = level.sandPictures != null && level.sandPictures.Count > 0 ? level.sandPictures.Count : 1;
            string[] options = new string[count];
            for (int i = 0; i < count; i++) options[i] = $"Sand picture {i + 1}";
            int next = EditorGUILayout.Popup("Picture to place", Mathf.Clamp(selectedPictureIndex, 0, count - 1), options);
            if (next != selectedPictureIndex)
            { selectedPictureIndex = next; sandFootprintDraft.Clear(); PrepareSelectedTiles(); RegeneratePreview(); }
            EditorGUILayout.HelpBox("Left-drag to paint the footprint; right-drag erases draft cells. Apply replaces this picture's placement. Shapes may contain cutouts. The picture fills a square around the footprint; grains outside it are excluded.", MessageType.Info);
            var region = SandBoardUtility.FindRegion(blockDocument.Data, selectedPictureIndex);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Edit Existing Footprint"))
            {
                sandFootprintDraft.Clear();
                if (region != null) sandFootprintDraft.AddRange(region.occupiedCells);
            }
            using (new EditorGUI.DisabledScope(sandFootprintDraft.Count == 0))
            if (GUILayout.Button("Apply Sand Placement"))
            {
                var cells = new List<LevelCellCoord>(sandFootprintDraft);
                if (blockDocument.TryCommit("Place sand", board =>
                {
                    board.sandRegions.RemoveAll(r => r.pictureIndex == selectedPictureIndex);
                    board.sandRegions.Add(new SandRegionFile { pictureIndex = selectedPictureIndex, occupiedCells = cells });
                }, out blockError)) sandFootprintDraft.Clear();
            }
            if (GUILayout.Button("Clear Draft")) sandFootprintDraft.Clear();
            EditorGUILayout.EndHorizontal();
            if (region != null)
            {
                RectInt bounds = SandBoardUtility.Bounds(region);
                Vector2Int position = EditorGUILayout.Vector2IntField("Region origin", new Vector2Int(bounds.x, bounds.y));
                if (position.x != bounds.x || position.y != bounds.y)
                    blockDocument.TryCommit("Move sand", board =>
                    {
                        var target = SandBoardUtility.FindRegion(board, selectedPictureIndex);
                        for (int i = 0; i < target.occupiedCells.Count; i++)
                            target.occupiedCells[i] = new LevelCellCoord(target.occupiedCells[i].x + position.x - bounds.x,
                                target.occupiedCells[i].y + position.y - bounds.y);
                    }, out blockError);
            }
        }
    }
}
