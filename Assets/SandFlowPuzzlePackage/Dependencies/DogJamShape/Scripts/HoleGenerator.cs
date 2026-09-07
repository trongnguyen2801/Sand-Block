using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace HypercasualGameEngine
{
    public static class HoleGenerator
    {
        public static void Generate(HoleDefinition def)
        {
            if (def == null) return;

            // 1. Clear previous
            if (def.visualObject != null)
            {
                if (Application.isPlaying) Object.Destroy(def.visualObject);
                else Object.DestroyImmediate(def.visualObject);
            }
            foreach (var col in def.colliderObjects)
            {
                if (col != null)
                {
                    if (Application.isPlaying) Object.Destroy(col);
                    else Object.DestroyImmediate(col);
                }
            }
            def.colliderObjects.Clear();

            // 2. Identify filled cells and bounds
            List<Vector2Int> filledCells = new List<Vector2Int>();
            int minX = def.width, maxX = -1, minY = def.height, maxY = -1;

            for (int y = 0; y < def.height; y++)
            {
                for (int x = 0; x < def.width; x++)
                {
                    if (def.GetCell(x, y))
                    {
                        filledCells.Add(new Vector2Int(x, y));
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            if (filledCells.Count == 0) return;

            // Calculate bounds in local space
            float gridWidth = def.width * def.cellSize;
            float gridHeight = def.height * def.cellSize;
            Vector3 gridOrigin = new Vector3(-gridWidth / 2f, 0, -gridHeight / 2f);

            // 3. Generate Colliders
            foreach (var cell in filledCells)
            {
                GameObject colObj = new GameObject($"Cell_{cell.x}_{cell.y}");
                colObj.transform.SetParent(def.transform, false);
                colObj.transform.localPosition = gridOrigin + new Vector3((cell.x + 0.5f) * def.cellSize, def.blockHeight / 2f, (cell.y + 0.5f) * def.cellSize);
                colObj.layer = def.gameObject.layer;

                BoxCollider box = colObj.AddComponent<BoxCollider>();
                box.size = def.colliderScale;

                // Apply physics material if assigned
                if (def.groundPhysicsMaterial != null)
                {
                    box.material = def.groundPhysicsMaterial;
                }

                colObj.name = "GroundCube";

                def.colliderObjects.Add(colObj);
            }

            // 4. Generate Visual Mesh
            GameObject visualObj = new GameObject("Visuals");
            visualObj.transform.SetParent(def.transform, false);
            visualObj.transform.localPosition = Vector3.zero;
            visualObj.layer = def.gameObject.layer;

            MeshFilter mf = visualObj.AddComponent<MeshFilter>();
            MeshRenderer mr = visualObj.AddComponent<MeshRenderer>();

            Mesh mesh = GenerateMesh(def, filledCells, gridOrigin, minX, maxX, minY, maxY);
            mf.sharedMesh = mesh;

            // 5. Generate SDF Texture
            Texture2D sdfTex = MergedSDFGenerator.GenerateMergedSDF(def, filledCells, minX, maxX, minY, maxY, 512);
            def.generatedSDF = sdfTex;

            // Debug: Save texture to file to verify
#if UNITY_EDITOR
            // Runtime users (SandFlowPuzzle) generate the same visual in memory.
            // Only persist the debug texture while authoring a hole in edit mode.
            if (!Application.isPlaying)
            {
                byte[] bytes = sdfTex.EncodeToPNG();
                System.IO.File.WriteAllBytes("Assets/Games/HoleEditor/DebugSDF.png", bytes);
                UnityEditor.AssetDatabase.Refresh();
            }
#endif

            // 6. Setup Material
            if (def.materialTemplate != null)
            {
                Material mat = new Material(def.materialTemplate);
                mat.SetTexture("_MainTex", sdfTex);
                mat.SetVector("_MainTex_ST", new Vector4(1, 1, 0, 0));
                mat.SetFloat("_Height", def.blockHeight);
                mat.SetFloat("_VisualScale", def.visualScale);

                // Apply visual settings from HoleDefinition
                mat.SetFloat("_BottomDarkness", def.bottomDarkness);
                mat.SetFloat("_OutlineWidth", def.outlineWidth);
                mat.SetFloat("_OutlineBrightness", def.outlineBrightness);

                // Get color from ColorBlock component
                ColorBlock cb = def.GetComponent<ColorBlock>();
                Color mainColor = new Color(0, 0.5f, 1f, 1f); // Default blue

                if (cb != null)
                {
                    // Use the visual color from ColorBlock
                    mainColor = cb.GetType().GetField("visualColor",
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance).GetValue(cb) as Color? ?? mainColor;
                }

                // Set colors
                mat.SetColor("_Color", mainColor);

                // Calculate outline color: lighter/brighter version of main color
                Color outlineColor = Color.Lerp(mainColor, Color.white, 0.4f);
                mat.SetColor("_OutlineColor", outlineColor);

                // Bottom color: darker version of main color
                Color bottomColor = mainColor * 0.5f;
                bottomColor.a = 1f;
                mat.SetColor("_BottomColor", bottomColor);

                mr.sharedMaterial = mat;

                // Sync UI Background Color with Outline Color
                if (def.collectCounterBg != null)
                {
                    SpriteRenderer bgSprite = def.collectCounterBg.GetComponent<SpriteRenderer>();
                    if (bgSprite != null)
                    {
                        bgSprite.color = outlineColor;
                    }
                }

                if (cb != null)
                {
                    cb.SendMessage("UpdateVisuals", SendMessageOptions.DontRequireReceiver);
                }
            }

            def.visualObject = visualObj;

            // 7. Update UI Position
            UpdateUIPosition(def);
        }

        public static void UpdateUIPosition(HoleDefinition def)
        {
            if (def == null || def.collectCounterBg == null) return;

            // Calculate grid origin based on current def properties
            float gridWidth = def.width * def.cellSize;
            float gridHeight = def.height * def.cellSize;
            Vector3 gridOrigin = new Vector3(-gridWidth / 2f, 0, -gridHeight / 2f);

            int targetX = 0;
            int targetY = 0;
            bool found = false;

            // Smart positioning: "Hug" the shape instead of using the bounding box
            switch (def.uiPosition)
            {
                case HoleDefinition.UIPosition.TopLeft:
                    // Find Max Y, then Min X
                    for (int y = def.height - 1; y >= 0; y--)
                    {
                        for (int x = 0; x < def.width; x++)
                        {
                            if (def.GetCell(x, y))
                            {
                                targetX = x; targetY = y + 1; // Top-Left corner of this cell
                                found = true;
                                break;
                            }
                        }
                        if (found) break;
                    }
                    break;

                case HoleDefinition.UIPosition.TopRight:
                    // Find Max Y, then Max X
                    for (int y = def.height - 1; y >= 0; y--)
                    {
                        for (int x = def.width - 1; x >= 0; x--)
                        {
                            if (def.GetCell(x, y))
                            {
                                targetX = x + 1; targetY = y + 1; // Top-Right corner of this cell
                                found = true;
                                break;
                            }
                        }
                        if (found) break;
                    }
                    break;

                case HoleDefinition.UIPosition.BottomLeft:
                    // Find Min Y, then Min X
                    for (int y = 0; y < def.height; y++)
                    {
                        for (int x = 0; x < def.width; x++)
                        {
                            if (def.GetCell(x, y))
                            {
                                targetX = x; targetY = y; // Bottom-Left corner of this cell
                                found = true;
                                break;
                            }
                        }
                        if (found) break;
                    }
                    break;

                case HoleDefinition.UIPosition.BottomRight:
                    // Find Min Y, then Max X
                    for (int y = 0; y < def.height; y++)
                    {
                        for (int x = def.width - 1; x >= 0; x--)
                        {
                            if (def.GetCell(x, y))
                            {
                                targetX = x + 1; targetY = y; // Bottom-Right corner of this cell
                                found = true;
                                break;
                            }
                        }
                        if (found) break;
                    }
                    break;
            }

            if (!found) return;

            // Convert grid coordinates to local position
            float xPos = gridOrigin.x + targetX * def.cellSize;
            float zPos = gridOrigin.z + targetY * def.cellSize;

            // Keep Y above the block
            float yPos = def.blockHeight + 0.1f;

            def.collectCounterBg.localPosition = new Vector3(xPos, yPos, zPos);
        }

        private static Mesh GenerateMesh(HoleDefinition def, List<Vector2Int> filledCells, Vector3 gridOrigin, int minX, int maxX, int minY, int maxY)
        {
            Mesh mesh = new Mesh();
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Vector2> uvs = new List<Vector2>();
            List<Color> colors = new List<Color>();

            // --- Top Face (Quad covering bounds + padding for outer outline) ---
            float outlinePadding = def.cellSize * 0.25f; // Extra space for outer outline

            float boundsMinX = minX * def.cellSize - outlinePadding;
            float boundsMaxX = (maxX + 1) * def.cellSize + outlinePadding;
            float boundsMinZ = minY * def.cellSize - outlinePadding;
            float boundsMaxZ = (maxY + 1) * def.cellSize + outlinePadding;

            Vector3 p0 = gridOrigin + new Vector3(boundsMinX, def.blockHeight, boundsMinZ);
            Vector3 p1 = gridOrigin + new Vector3(boundsMaxX, def.blockHeight, boundsMinZ);
            Vector3 p2 = gridOrigin + new Vector3(boundsMaxX, def.blockHeight, boundsMaxZ);
            Vector3 p3 = gridOrigin + new Vector3(boundsMinX, def.blockHeight, boundsMaxZ);

            int topStart = vertices.Count;
            vertices.Add(p0); vertices.Add(p1); vertices.Add(p2); vertices.Add(p3);

            uvs.Add(new Vector2(0, 0));
            uvs.Add(new Vector2(1, 0));
            uvs.Add(new Vector2(1, 1));
            uvs.Add(new Vector2(0, 1));

            colors.Add(new Color(1, 0, 0, 1));
            colors.Add(new Color(1, 0, 0, 1));
            colors.Add(new Color(1, 0, 0, 1));
            colors.Add(new Color(1, 0, 0, 1));

            triangles.Add(topStart + 0);
            triangles.Add(topStart + 2);
            triangles.Add(topStart + 1);
            triangles.Add(topStart + 0);
            triangles.Add(topStart + 3);
            triangles.Add(topStart + 2);

            // --- Side Faces ---
            Vector2Int[] directions = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

            foreach (var cell in filledCells)
            {
                for (int i = 0; i < 4; i++)
                {
                    Vector2Int neighbor = cell + directions[i];
                    if (!def.GetCell(neighbor.x, neighbor.y))
                    {
                        AddWall(def, gridOrigin, cell, i, vertices, triangles, uvs, colors);
                    }
                }
            }

            mesh.vertices = vertices.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.colors = colors.ToArray();
            mesh.RecalculateNormals();

            return mesh;
        }

        private static void AddWall(HoleDefinition def, Vector3 gridOrigin, Vector2Int cell, int dirIndex,
            List<Vector3> vertices, List<int> triangles, List<Vector2> uvs, List<Color> colors)
        {
            float x = cell.x * def.cellSize;
            float z = cell.y * def.cellSize;
            float s = def.cellSize;
            float h = def.blockHeight;

            Vector3 v1_bottom, v2_bottom, v1_top, v2_top;

            if (dirIndex == 0) // Up
            {
                v1_bottom = new Vector3(x, 0, z + s);
                v2_bottom = new Vector3(x + s, 0, z + s);
            }
            else if (dirIndex == 1) // Right
            {
                v1_bottom = new Vector3(x + s, 0, z + s);
                v2_bottom = new Vector3(x + s, 0, z);
            }
            else if (dirIndex == 2) // Down
            {
                v1_bottom = new Vector3(x + s, 0, z);
                v2_bottom = new Vector3(x, 0, z);
            }
            else // Left
            {
                v1_bottom = new Vector3(x, 0, z);
                v2_bottom = new Vector3(x, 0, z + s);
            }

            v1_bottom += gridOrigin;
            v2_bottom += gridOrigin;
            v1_top = v1_bottom + Vector3.up * h;
            v2_top = v2_bottom + Vector3.up * h;

            int start = vertices.Count;
            vertices.Add(v1_bottom);
            vertices.Add(v2_bottom);
            vertices.Add(v2_top);
            vertices.Add(v1_top);

            uvs.Add(new Vector2(0, 0));
            uvs.Add(new Vector2(1, 0));
            uvs.Add(new Vector2(1, 1));
            uvs.Add(new Vector2(0, 1));

            colors.Add(new Color(0, 0, 0, 1));
            colors.Add(new Color(0, 0, 0, 1));
            colors.Add(new Color(0, 0, 0, 1));
            colors.Add(new Color(0, 0, 0, 1));

            triangles.Add(start + 0);
            triangles.Add(start + 2);
            triangles.Add(start + 1);
            triangles.Add(start + 0);
            triangles.Add(start + 3);
            triangles.Add(start + 2);
        }
    }
}
