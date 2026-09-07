using UnityEngine;
using System.Collections.Generic;
using TMPro;

namespace HypercasualGameEngine
{
    public class HoleDefinition : MonoBehaviour
    {
        public enum UIPosition { TopLeft, TopRight, BottomLeft, BottomRight }

        [System.Serializable]
        public class RowData
        {
            public bool[] cells;
            public RowData(int size) { cells = new bool[size]; }
        }
        [HideInInspector]
        public int width = 4;
        [HideInInspector]
        public int height = 4;
        [HideInInspector]
        public RowData[] rows;

        [Header("Settings")]
        public float cellSize = 0.49f;
        public float blockHeight = 0.16f;
        [Range(0.5f, 1.5f)]
        public float visualScale = 1.0f;

        // --- NEW: Controls the size of the BoxCollider for generated cubes ---
        [Tooltip("The size of the BoxCollider on generated collider objects.")]
        public Vector3 colliderScale = Vector3.one;

        [Tooltip("Physics material to apply to generated ground colliders (e.g., slippery/bouncy).")]
        public PhysicsMaterial groundPhysicsMaterial;
        // --------------------------------------------------------------------

        [Header("Visual Settings")]
        [Range(0f, 1f)]
        public float bottomDarkness = 0.3f;
        [Range(0f, 0.5f)]
        public float outlineWidth = 0.08f;
        [Range(0f, 2f)]
        public float outlineBrightness = 1.3f;

        [Header("References")]
        public Material materialTemplate;
        public GameObject holePrefab;

        [Header("Generated References")]
        public GameObject visualObject;
        public List<GameObject> colliderObjects = new List<GameObject>();
        public Texture2D generatedSDF;

        [Header("Collection Settings")]
        public UIPosition uiPosition = UIPosition.TopRight;
        public int targetCollectCount = 5;
        public Transform collectCounterBg;
        public TMP_Text collectCounterText;

        public void InitializeGrid(int w, int h)
        {
            width = w;
            height = h;
            rows = new RowData[h];
            for (int i = 0; i < h; i++)
            {
                rows[i] = new RowData(w);
            }
        }
        public bool GetCell(int x, int y)
        {
            if (x < 0 || x >= width || y < 0 || y >= height) return false;
            if (rows == null || rows.Length <= y || rows[y].cells.Length <= x) return false;
            return rows[y].cells[x];
        }
        public void SetCell(int x, int y, bool value)
        {
            if (x < 0 || x >= width || y < 0 || y >= height) return;
            if (rows == null) InitializeGrid(width, height);
            rows[y].cells[x] = value;
        }

        private void OnValidate()
        {
            if (gameObject.activeInHierarchy)
            {
                // Ensure HoleGenerator exists and has this method
                HoleGenerator.UpdateUIPosition(this);
            }
        }
    }
}
