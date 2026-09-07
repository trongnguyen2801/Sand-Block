// Simple ColorBlock component to identify colored blocks
using UnityEngine;

namespace HypercasualGameEngine
{
    public class ColorBlock : MonoBehaviour
    {
        [SerializeField] private string blockColor = "Red";
        [SerializeField] private Color visualColor = Color.red;
        private bool isBeingAbsorbed = false;

        public string BlockColor => blockColor;
        public Color VisualColor => visualColor;
        public bool IsBeingAbsorbed
        {
            get => isBeingAbsorbed;
            set => isBeingAbsorbed = value;
        }

        void Start()
        {
            Debug.Log($"[ColorBlock] Initializing - GameObject: {gameObject.name}, Color: {blockColor}, VisualColor: {visualColor}");

            // Only update in play mode
            if (Application.isPlaying)
            {
                UpdateVisualsPlayMode();
            }
        }

        // Optional: Method to set color dynamically
        public void SetColor(string color, Color visual)
        {
            blockColor = color;
            visualColor = visual;

            Debug.Log($"[ColorBlock] SetColor called - GameObject: {gameObject.name}, Color: {color}, VisualColor: {visual}");

            if (Application.isPlaying)
            {
                UpdateVisualsPlayMode();
            }
            else
            {
#if UNITY_EDITOR
                UpdateVisualsEditorMode();
#endif
            }
        }

        private void OnValidate()
        {
#if UNITY_EDITOR
            // In editor, update visuals when color changes
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (this != null)
                    {
                        UpdateVisualsEditorMode();
                    }
                };
            }
#endif
        }

        // Public method for external scripts like HoleEditor
        public void UpdateVisuals()
        {
            if (Application.isPlaying)
            {
                UpdateVisualsPlayMode();
            }
            else
            {
#if UNITY_EDITOR
                UpdateVisualsEditorMode();
#endif
            }
        }

        private void UpdateVisualsPlayMode()
        {
            Debug.Log($"[ColorBlock] UpdateVisualsPlayMode - GameObject: {gameObject.name}");

            // Check if we have HoleDefinition - if so, let it handle visuals
            var holeDef = GetComponent<HypercasualGameEngine.HoleDefinition>();
            if (holeDef != null && holeDef.visualObject != null)
            {
                Debug.Log($"[ColorBlock] Found HoleDefinition with visualObject on {gameObject.name}");
                Renderer renderer = holeDef.visualObject.GetComponent<Renderer>();
                if (renderer != null)
                {
                    Debug.Log($"[ColorBlock] Updating HoleDefinition renderer material properties");

                    // Create a material instance if we don't have one
                    if (renderer.material == renderer.sharedMaterial)
                    {
                        renderer.material = new Material(renderer.sharedMaterial);
                        Debug.Log($"[ColorBlock] Created new material instance for {gameObject.name}");
                    }

                    SetMaterialColor(renderer.material, renderer.gameObject.name);

                    // Set additional properties if they exist
                    Material mat = renderer.material;
                    if (mat.HasProperty("_OutlineColor"))
                    {
                        Color outlineColor = Color.Lerp(visualColor, Color.white, 0.4f);
                        mat.SetColor("_OutlineColor", outlineColor);
                    }

                    if (mat.HasProperty("_BottomColor"))
                    {
                        Color bottomColor = visualColor * 0.5f;
                        bottomColor.a = 1f;
                        mat.SetColor("_BottomColor", bottomColor);
                    }
                }
                else
                {
                    Debug.LogWarning($"[ColorBlock] HoleDefinition visualObject has no Renderer on {gameObject.name}");
                }
                return;
            }

            // Fallback: apply to all child MeshRenderers
            Debug.Log($"[ColorBlock] No HoleDefinition found, applying to child MeshRenderers");
            MeshRenderer[] meshRenderers = GetComponentsInChildren<MeshRenderer>();
            Debug.Log($"[ColorBlock] Found {meshRenderers.Length} MeshRenderers");

            foreach (MeshRenderer meshRenderer in meshRenderers)
            {
                if (meshRenderer != null)
                {
                    // Skip if parent is an ArrowHolder
                    if (meshRenderer.transform.parent != null && meshRenderer.transform.parent.name.Contains("ArrowHolder"))
                    {
                        Debug.Log($"[ColorBlock] Skipping MeshRenderer on {meshRenderer.gameObject.name} because parent is ArrowHolder");
                        continue;
                    }

                    Debug.Log($"[ColorBlock] Updating MeshRenderer on {meshRenderer.gameObject.name}");

                    // Create a material instance
                    if (meshRenderer.material == meshRenderer.sharedMaterial)
                    {
                        meshRenderer.material = new Material(meshRenderer.sharedMaterial);
                        Debug.Log($"[ColorBlock] Created new material instance for {meshRenderer.gameObject.name}");
                    }

                    SetMaterialColor(meshRenderer.material, meshRenderer.gameObject.name);
                }
            }
        }

        private void SetMaterialColor(Material mat, string objectName)
        {
            if (mat == null) return;

            Debug.Log($"[ColorBlock] Material shader: {mat.shader.name}");

            bool colorSet = false;

            // URP Lit shader uses _BaseColor
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", visualColor);
                Debug.Log($"[ColorBlock] Set _BaseColor to {visualColor} on {objectName}");
                colorSet = true;
            }

            // Standard/Built-in shader uses _Color
            if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", visualColor);
                Debug.Log($"[ColorBlock] Set _Color to {visualColor} on {objectName}");
                colorSet = true;
            }

            // Some custom shaders use _MainColor
            if (mat.HasProperty("_MainColor"))
            {
                mat.SetColor("_MainColor", visualColor);
                Debug.Log($"[ColorBlock] Set _MainColor to {visualColor} on {objectName}");
                colorSet = true;
            }

            // Try setting _EmissionColor for materials with emission
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.SetColor("_EmissionColor", visualColor * 0.3f);
                mat.EnableKeyword("_EMISSION");
            }

            if (!colorSet)
            {
                Debug.LogWarning($"[ColorBlock] Material on {objectName} has no recognized color property! Shader: {mat.shader.name}");
            }
        }

#if UNITY_EDITOR
        private void UpdateVisualsEditorMode()
        {
            // Check if we have HoleDefinition - if so, let it handle visuals
            var holeDef = GetComponent<HypercasualGameEngine.HoleDefinition>();
            if (holeDef != null && holeDef.visualObject != null)
            {
                Renderer renderer = holeDef.visualObject.GetComponent<Renderer>();
                if (renderer != null && renderer.sharedMaterial != null)
                {
                    SetMaterialColorEditor(renderer.sharedMaterial);

                    if (renderer.sharedMaterial.HasProperty("_OutlineColor"))
                    {
                        Color outlineColor = Color.Lerp(visualColor, Color.white, 0.4f);
                        renderer.sharedMaterial.SetColor("_OutlineColor", outlineColor);
                    }

                    if (renderer.sharedMaterial.HasProperty("_BottomColor"))
                    {
                        Color bottomColor = visualColor * 0.5f;
                        bottomColor.a = 1f;
                        renderer.sharedMaterial.SetColor("_BottomColor", bottomColor);
                    }
                }
                return;
            }

            // Fallback: apply to all child renderers using property blocks
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
            {
                if (renderer == null) continue;
                if (renderer is SpriteRenderer) continue;

                // Skip if parent is an ArrowHolder
                if (renderer.transform.parent != null && renderer.transform.parent.name.Contains("ArrowHolder"))
                {
                    continue;
                }

                MaterialPropertyBlock props = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(props);

                // Set both _Color and _BaseColor for compatibility
                props.SetColor("_Color", visualColor);
                props.SetColor("_BaseColor", visualColor);

                renderer.SetPropertyBlock(props);
            }
        }

        private void SetMaterialColorEditor(Material mat)
        {
            if (mat == null) return;

            // URP uses _BaseColor
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", visualColor);
            }

            // Standard uses _Color
            if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", visualColor);
            }

            // Custom shaders might use _MainColor
            if (mat.HasProperty("_MainColor"))
            {
                mat.SetColor("_MainColor", visualColor);
            }
        }
#endif
    }
}