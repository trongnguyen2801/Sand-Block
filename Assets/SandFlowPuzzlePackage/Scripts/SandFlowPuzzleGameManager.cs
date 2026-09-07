using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

using HypercasualGameEngine;

namespace SandFlowPuzzle
{
    public class SandFlowPuzzleGameManager : MonoBehaviour
    {
        [Header("External References")]
        public LoseWinPanelManager loseWinPanel;

        [Header("Booster Counts")]
        public int slotBoosterCount = 3;
        public int shuffleBoosterCount = 3;
        public int magicBoosterCount = 3;

        [Header("Booster Unlock Levels (0-based)")]
        public int slotBoosterUnlockLevel = 0;
        public int shuffleBoosterUnlockLevel = 0;
        public int magicBoosterUnlockLevel = 0;

        // Runtime references
        private SandSimulator sandSimulator;
        private SandCollectorManager collectorManager;
        private Transform level1;
        private Vector3 sandWorldMin;
        private Vector3 sandWorldMax;

        // 2D UI references on canvas
        private TextMeshProUGUI beltCounterTMP;
        private TextMeshProUGUI slotBoosterCountText;
        private TextMeshProUGUI shuffleBoosterCountText;
        private TextMeshProUGUI magicBoosterCountText;
        private Button slotBoosterButton;
        private Button shuffleBoosterButton;
        private Button magicBoosterButton;
        private Image magicBoosterImage;
        private Image slotBoosterImage;
        private Image shuffleBoosterImage;
        private GameObject slotBoosterBg;
        private GameObject shuffleBoosterBg;
        private GameObject magicBoosterBg;
        private GameObject slotBoosterCountGo;
        private GameObject shuffleBoosterCountGo;
        private GameObject magicBoosterCountGo;

        private bool isGameOver;
        private RectTransform worldCanvas;
        private Color magicBoosterOriginalColor;
        private int frameCount;

        // Booster tutorial popups (shown once per booster via PlayerPrefs)
        private GameObject popupSlot;
        private GameObject popupShuffle;
        private GameObject popupHand;
        private const string PrefKeySlotPopup = "BoosterPopupShown_Slot";
        private const string PrefKeyShufflePopup = "BoosterPopupShown_Shuffle";
        private const string PrefKeyHandPopup = "BoosterPopupShown_Hand";

        private Image counterTextBgImage;
        private static readonly Color counterBgNormal = new Color(0.16f, 0.18f, 0.23f, 1f);

        // Slot booster popup animation
        private TextMeshProUGUI popupTMP;
        private RectTransform popupRt;
        private float popupTimer;
        private const float popupDuration = 0.7f;

        private void Start()
        {
            // Load level system
            // Always reload the JSON when entering play mode so a disabled domain
            // reload cannot keep an older 80x80 level cached in static fields.
            LevelManager.ForceReload();

            // SandFlowPuzzleGameManager lives under -Level_1-
            level1 = transform.parent; // -Level_1-
            Transform gameRoot = level1 != null ? level1.parent : null; // #16_SandFlowPuzzle
            if (gameRoot != null)
            {
                Canvas c = gameRoot.GetComponentInChildren<Canvas>();
                if (c != null) worldCanvas = c.GetComponent<RectTransform>();
            }
            // Fallback: search ancestors
            if (worldCanvas == null)
            {
                Canvas c = GetComponentInParent<Canvas>();
                if (c != null) worldCanvas = c.GetComponent<RectTransform>();
            }

            if (worldCanvas == null)
            {
                Transform canvasParent = gameRoot != null ? gameRoot : transform.root;
                worldCanvas = CreateStandaloneWorldCanvas(canvasParent);
            }

            // Ensure the World Space Canvas has its Event Camera set — required for
            // UI button clicks to work reliably on WebGL where Camera.main fallback
            // may not resolve correctly for GraphicRaycaster hit testing.
            Canvas worldCanvasComponent = worldCanvas.GetComponent<Canvas>();
            if (worldCanvasComponent != null && worldCanvasComponent.renderMode == RenderMode.WorldSpace
                && worldCanvasComponent.worldCamera == null)
            {
                worldCanvasComponent.worldCamera = Camera.main;
            }

            // Ensure EventSystem exists — required for all UI interactions.
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
                esGo.AddComponent<InputSystemUIInputModule>();
#else
                esGo.AddComponent<StandaloneInputModule>();
#endif
            }

            if (loseWinPanel == null)
                loseWinPanel = worldCanvas.GetComponentInChildren<LoseWinPanelManager>(true);

            // Apply level data to booster counts
            LevelData currentLevel = LevelManager.GetCurrentLevel();
            if (currentLevel != null)
            {
                slotBoosterCount = currentLevel.slotBoosterCount;
                shuffleBoosterCount = currentLevel.shuffleBoosterCount;
                magicBoosterCount = currentLevel.magicBoosterCount;
            }

            BuildUI2D();
            Build3D();
            InitializeGame();
            FindBoosterPopups();
            ApplyBoosterUnlocks();
            SetCurrentLevelTexts();
        }

        private static RectTransform CreateStandaloneWorldCanvas(Transform parent)
        {
            GameObject canvasObject = new GameObject(
                "WorldCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(parent, false);

            RectTransform rect = canvasObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(3.24f, 6.18f);
            rect.localPosition = Vector3.zero;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            canvas.sortingOrder = 10;
            return rect;
        }

        // =====================================================================
        // BuildUI2D — 2D elements on WorldCanvas (counter, boosters)
        // Sand display is on a dedicated canvas built in Build3D.
        // =====================================================================
        private void BuildUI2D()
        {
            float canvasW = worldCanvas.rect.width;   // ~3.24
            float canvasH = worldCanvas.rect.height;   // ~6.18
            float displayW = canvasW * 0.84f;          // ~2.72

            // Root container (stretch to fill canvas) — NO layout group, manual positioning
            GameObject rootGo = new GameObject("SandFlowPuzzleUI");
            rootGo.transform.SetParent(worldCanvas, false);
            rootGo.transform.SetAsFirstSibling();
            RectTransform rootRt = rootGo.AddComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            // Sand display size (used for counter positioning — display itself is on a separate canvas)
            float sandSize = displayW;

            // ------ BELT COUNTER (below belt area in canvas coords) ------
            float counterH = 0.22f;
            float counterY = -(sandSize + 0.02f + 0.55f + 0.03f);

            GameObject counterGo = new GameObject("BeltCounter");
            counterGo.transform.SetParent(rootGo.transform, false);
            RectTransform counterRt = counterGo.AddComponent<RectTransform>();
            counterRt.anchorMin = new Vector2(0.5f, 1f);
            counterRt.anchorMax = new Vector2(0.5f, 1f);
            counterRt.pivot = new Vector2(0.5f, 0.5f);
            counterRt.sizeDelta = new Vector2(displayW * 0.45f, counterH);
            counterRt.anchoredPosition = new Vector2(0f, counterY);
            counterGo.transform.localPosition = new Vector3(0f, -0.7153002f, 1.215f);
            counterGo.transform.localScale = new Vector3(0.726f, 0.726f, 0.726f);
            counterGo.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);

            Image counterBg = counterGo.AddComponent<Image>();
            counterBg.color = new Color(0.94f, 0.96f, 0.98f, 1f);
            counterBg.raycastTarget = false;

            HorizontalLayoutGroup hlg = counterGo.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = false;
            hlg.childControlHeight = false;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.spacing = 0.04f;

            // Counter text
            GameObject counterTextGo = new GameObject("CounterText");
            counterTextGo.transform.SetParent(counterGo.transform, false);
            RectTransform ctRt = counterTextGo.AddComponent<RectTransform>();
            ctRt.sizeDelta = new Vector2(displayW * 0.28f, counterH * 0.85f);

            counterTextBgImage = counterTextGo.AddComponent<Image>();
            counterTextBgImage.color = counterBgNormal;
            counterTextBgImage.raycastTarget = false;

            GameObject counterTextLabel = new GameObject("Label");
            counterTextLabel.transform.SetParent(counterTextGo.transform, false);
            RectTransform ctlRt = counterTextLabel.AddComponent<RectTransform>();
            ctlRt.anchorMin = Vector2.zero;
            ctlRt.anchorMax = Vector2.one;
            ctlRt.offsetMin = Vector2.zero;
            ctlRt.offsetMax = Vector2.zero;
            beltCounterTMP = counterTextLabel.AddComponent<TextMeshProUGUI>();
            beltCounterTMP.text = "0/5";
            beltCounterTMP.enableAutoSizing = true;
            beltCounterTMP.fontSizeMin = 0.2f;
            beltCounterTMP.fontSizeMax = 6f;
            beltCounterTMP.fontStyle = FontStyles.Bold;
            beltCounterTMP.color = Color.white;
            beltCounterTMP.alignment = TextAlignmentOptions.Center;
            beltCounterTMP.raycastTarget = false;

            // Popup text (hidden by default, animated on slot booster use)
            GameObject popupGo = new GameObject("PopupText");
            popupGo.transform.SetParent(counterTextGo.transform, false);
            popupRt = popupGo.AddComponent<RectTransform>();
            popupRt.anchorMin = new Vector2(0.5f, 0.5f);
            popupRt.anchorMax = new Vector2(0.5f, 0.5f);
            popupRt.pivot = new Vector2(0.5f, 0.5f);
            popupRt.sizeDelta = new Vector2(displayW * 0.56f, counterH * 1.4f);
            popupRt.anchoredPosition = new Vector2(0f, 0.21f);
            popupTMP = popupGo.AddComponent<TextMeshProUGUI>();
            popupTMP.text = "";
            popupTMP.enableAutoSizing = true;
            popupTMP.fontSizeMin = 0.1f;
            popupTMP.fontSizeMax = 4f;
            popupTMP.fontStyle = FontStyles.Bold;
            popupTMP.color = new Color(0.29f, 0.87f, 0.5f, 0f);
            popupTMP.alignment = TextAlignmentOptions.Center;
            popupTMP.raycastTarget = false;

            // Plus button
            GameObject plusBtnGo = new GameObject("PlusButton");
            plusBtnGo.transform.SetParent(counterGo.transform, false);
            RectTransform plusRt = plusBtnGo.AddComponent<RectTransform>();
            plusRt.sizeDelta = new Vector2(counterH * 0.85f, counterH * 0.85f);

            Image plusBg = plusBtnGo.AddComponent<Image>();
            plusBg.color = new Color(0.29f, 0.87f, 0.5f, 1f);
            plusBg.raycastTarget = true;

            GameObject plusTextGo = new GameObject("PlusText");
            plusTextGo.transform.SetParent(plusBtnGo.transform, false);
            RectTransform ptRt = plusTextGo.AddComponent<RectTransform>();
            ptRt.anchorMin = Vector2.zero;
            ptRt.anchorMax = Vector2.one;
            ptRt.offsetMin = Vector2.zero;
            ptRt.offsetMax = Vector2.zero;
            TextMeshProUGUI plusTmp = plusTextGo.AddComponent<TextMeshProUGUI>();
            plusTmp.text = "+";
            plusTmp.enableAutoSizing = true;
            plusTmp.fontSizeMin = 0.2f;
            plusTmp.fontSizeMax = 6f;
            plusTmp.fontStyle = FontStyles.Bold;
            plusTmp.color = Color.white;
            plusTmp.alignment = TextAlignmentOptions.Center;
            plusTmp.raycastTarget = false;

            Button plusBtn = plusBtnGo.AddComponent<Button>();
            plusBtn.targetGraphic = plusBg;
            plusBtn.onClick.AddListener(OnSlotBooster);

            // The belt and its slot counter no longer belong to the block-drag mode.
            counterGo.SetActive(false);

            // ------ HOOK UP EXISTING BOOSTER BUTTONS from GameHUD_Panel/BottomHolder ------
            HookUpExistingBoosters();
        }

        // =====================================================================
        // Build3D — sand picture and draggable DogJam-style collector blocks.
        // =====================================================================
        private void Build3D()
        {
            if (level1 == null) level1 = transform.parent;
            Transform parent3D = level1 != null ? level1 : transform;

            float canvasW = worldCanvas != null ? worldCanvas.rect.width : 3.24f;
            float displayW = canvasW * 0.84f; // ~2.72

            // ---- Sand Display (dedicated canvas facing camera) ----
            float sandFrameSize = displayW;
            float sandInnerSize = sandFrameSize * 0.92f;
            float sandCenterZ = 1.71f;
            float sandY = 0.07f;

            // Create a dedicated World Space canvas rotated to face the camera
            GameObject sandCanvasGo = new GameObject("SandDisplayCanvas");
            sandCanvasGo.transform.SetParent(parent3D, false);
            sandCanvasGo.transform.localPosition = new Vector3(0f, 0.45f, 1.71f);
            sandCanvasGo.transform.localRotation = Quaternion.Euler(-121.084f, 0f, 0f);

            Canvas sandCanvas = sandCanvasGo.AddComponent<Canvas>();
            sandCanvas.renderMode = RenderMode.WorldSpace;
            sandCanvas.worldCamera = Camera.main;
            sandCanvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            RectTransform sandCanvasRt = sandCanvasGo.GetComponent<RectTransform>();
            sandCanvasRt.sizeDelta = new Vector2(sandFrameSize, sandFrameSize);
            sandCanvasRt.localScale = Vector3.one;

            // Background frame
            Image sandBg = sandCanvasGo.AddComponent<Image>();
            sandBg.color = new Color(0.91f, 0.95f, 0.98f, 1f);
            sandBg.raycastTarget = false;

            // Inner sand display (92% of frame)
            GameObject innerSandGo = new GameObject("SandInner");
            innerSandGo.transform.SetParent(sandCanvasGo.transform, false);
            RectTransform innerRt = innerSandGo.AddComponent<RectTransform>();
            innerRt.anchorMin = new Vector2(0.04f, 0.04f);
            innerRt.anchorMax = new Vector2(0.96f, 0.96f);
            innerRt.offsetMin = Vector2.zero;
            innerRt.offsetMax = Vector2.zero;

            RawImage rawImg = innerSandGo.AddComponent<RawImage>();
            rawImg.color = Color.white;
            rawImg.raycastTarget = false;
            // Flip V to compensate for Euler(-90,0,0) inverting the vertical axis
            rawImg.uvRect = new Rect(0, 1, 1, -1);

            sandSimulator = innerSandGo.AddComponent<SandSimulator>();

            // Sand display world bounds for flying blocks (convert local to world)
            float sandHalf = sandInnerSize * 0.5f;
            Vector3 parentWorldPos = parent3D.position;
            sandWorldMin = new Vector3(parentWorldPos.x - sandHalf, parentWorldPos.y + sandY, parentWorldPos.z + sandCenterZ - sandHalf);
            sandWorldMax = new Vector3(parentWorldPos.x + sandHalf, parentWorldPos.y + sandY, parentWorldPos.z + sandCenterZ + sandHalf);

            // Remove the old conveyor presentation from the runtime hierarchy.
            SetLegacyObjectActive(parent3D, "ConveyorBelt", false);
            SetLegacyObjectActive(parent3D, "pipe-base-left", false);
            SetLegacyObjectActive(parent3D, "pipe-base-right", false);
            SetLegacyObjectActive(parent3D, "BucketGrid", false);
            SetLegacyObjectActive(parent3D, "BeltBucketsContainer", false);

            GameObject collectorManagerGo = new GameObject("SandCollectorManagerGO");
            collectorManagerGo.transform.SetParent(parent3D, false);
            collectorManager = collectorManagerGo.AddComponent<SandCollectorManager>();
        }

        private static void SetLegacyObjectActive(Transform root, string objectName, bool active)
        {
            GameObject found = FindChildByName(root, objectName);
            if (found != null) found.SetActive(active);
        }

        private void HookUpExistingBoosters()
        {
            // Find GameHUD_Panel > BottomHolder — try multiple strategies since
            // GameObject.Find cannot find inactive objects.
            Transform bottomHolder = null;
            GameObject hudPanel = GameObject.Find("GameHUD_Panel");

            // Fallback: search under the world canvas hierarchy (works for inactive objects)
            if (hudPanel == null && worldCanvas != null)
            {
                GameObject found = FindChildByName(worldCanvas, "GameHUD_Panel");
                if (found != null)
                {
                    hudPanel = found;
                    hudPanel.SetActive(true);
                }
            }

            if (hudPanel != null)
                bottomHolder = hudPanel.transform.Find("BottomHolder");

            if (bottomHolder == null)
            {
                Debug.LogWarning("[SandFlowPuzzle] Could not find GameHUD_Panel/BottomHolder — boosters will use manual UI raycast only.");
                return;
            }

            // ExtraBucket_PowerUp → Slot booster
            HookUpBoosterButton(bottomHolder, "ExtraBucket_PowerUp", OnSlotBooster,
                slotBoosterCount, out slotBoosterButton, out slotBoosterImage, out slotBoosterCountText,
                out slotBoosterBg, out slotBoosterCountGo);

            // Shuffle_PowerUp → Shuffle booster
            HookUpBoosterButton(bottomHolder, "Shuffle_PowerUp", OnShuffleBooster,
                shuffleBoosterCount, out shuffleBoosterButton, out shuffleBoosterImage, out shuffleBoosterCountText,
                out shuffleBoosterBg, out shuffleBoosterCountGo);

            // Pick_PowerUp → Magic wand booster
            HookUpBoosterButton(bottomHolder, "Pick_PowerUp", OnMagicBooster,
                magicBoosterCount, out magicBoosterButton, out magicBoosterImage, out magicBoosterCountText,
                out magicBoosterBg, out magicBoosterCountGo);

            if (magicBoosterImage != null)
                magicBoosterOriginalColor = magicBoosterImage.color;
        }

        private void HookUpBoosterButton(Transform parent, string name,
            UnityEngine.Events.UnityAction onClick, int count,
            out Button button, out Image bgImage, out TextMeshProUGUI countText,
            out GameObject bgGo, out GameObject countTextGo)
        {
            button = null;
            bgImage = null;
            countText = null;
            bgGo = null;
            countTextGo = null;

            Transform t = parent.Find(name);
            if (t == null)
            {
                Debug.LogWarning($"[SandFlowPuzzle] Booster button '{name}' not found under BottomHolder");
                return;
            }

            button = t.GetComponent<Button>();
            if (button == null)
                button = t.GetComponentInChildren<Button>();

            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(onClick);
            }

            bgImage = t.GetComponent<Image>();

            // Find Bg child
            Transform bgT = t.Find("Bg");
            if (bgT != null)
                bgGo = bgT.gameObject;

            // Find PowerUpCountText child and set initial count
            Transform countTextT = t.Find("PowerUpCountText");
            if (countTextT != null)
            {
                countText = countTextT.GetComponent<TextMeshProUGUI>();
                countTextGo = countTextT.gameObject;
            }

            if (countText != null)
                countText.text = count.ToString();
        }

        private void InitializeGame()
        {
            isGameOver = false;

            LevelData currentLevel = LevelManager.GetCurrentLevel();
            RawImage rawImg = sandSimulator.GetComponent<RawImage>();

            if (currentLevel != null && currentLevel.sandGrid != null && currentLevel.sandGrid.Count > 0)
            {
                sandSimulator.Initialize(rawImg, currentLevel.sandGrid, currentLevel.palette);
            }
            else
            {
                sandSimulator.Initialize(rawImg);
            }

            collectorManager.Initialize(sandSimulator, currentLevel, sandWorldMin, sandWorldMax);
        }

        private void Update()
        {
            if (sandSimulator == null || isGameOver) return;

            // Ensure World Space Canvas camera stays assigned (Camera.main may resolve late on WebGL)
            if (worldCanvas != null)
            {
                Canvas wc = worldCanvas.GetComponent<Canvas>();
                if (wc != null && wc.renderMode == RenderMode.WorldSpace && wc.worldCamera == null)
                    wc.worldCamera = Camera.main;
            }

            frameCount++;
            bool inputReady = frameCount > 5; // skip first 5 frames to avoid stale key states

            // Arrow key level navigation (debug/dev)
            if (inputReady && IsKeyDown(KeyCode.RightArrow))
            {
                LevelManager.AdvanceLevel();
                UnityEngine.SceneManagement.SceneManager.LoadScene(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
                return;
            }
            if (inputReady && IsKeyDown(KeyCode.LeftArrow))
            {
                LevelManager.GoToPreviousLevel();
                UnityEngine.SceneManagement.SceneManager.LoadScene(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
                return;
            }
            if (inputReady && IsKeyDown(KeyCode.U))
            {
                slotBoosterUnlockLevel = 0;
                shuffleBoosterUnlockLevel = 0;
                magicBoosterUnlockLevel = 0;
                UpdateBoosterUI();
            }

            // Slot booster popup animation (float up + fade out)
            if (popupTimer > 0f)
            {
                popupTimer -= Time.deltaTime;
                float t = 1f - Mathf.Clamp01(popupTimer / popupDuration); // 0→1
                float ease = 1f - (1f - t) * (1f - t); // ease-out quad
                popupRt.anchoredPosition = new Vector2(0f, 0.21f + ease * 0.08f);
                float alpha = t < 0.3f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.3f) / 0.7f);
                popupTMP.color = new Color(popupTMP.color.r, popupTMP.color.g, popupTMP.color.b, alpha);
                if (popupTimer <= 0f)
                    popupTMP.color = new Color(popupTMP.color.r, popupTMP.color.g, popupTMP.color.b, 0f);
            }

            // Sand gravity, flying-particle physics and rendering remain unchanged.
            sandSimulator.SimulateGravity();
            sandSimulator.UpdateParticles();
            sandSimulator.RenderToTexture();
            CheckGameState();
        }

        public void TriggerWin()
        {
            if (isGameOver) return;
            isGameOver = true;
            if (collectorManager != null) collectorManager.SetGameplayEnabled(false);
            if (HypercasualGameEngine.SoundManager.Instance != null) HypercasualGameEngine.SoundManager.Instance.PlayLevelClear();
            LevelManager.AdvanceLevel();
            if (loseWinPanel != null) loseWinPanel.ShowWin();
        }

        public void TriggerLose()
        {
            if (isGameOver) return;
            isGameOver = true;
            if (collectorManager != null) collectorManager.SetGameplayEnabled(false);
            if (HypercasualGameEngine.SoundManager.Instance != null) HypercasualGameEngine.SoundManager.Instance.PlayLevelFail();
            if (loseWinPanel != null) loseWinPanel.ShowLose();
        }

        private void CheckGameState()
        {
            if (isGameOver) return;

            // Check win: all sand empty and no particles
            if (sandSimulator.IsAllEmpty() && sandSimulator.particles.Count == 0)
            {
                isGameOver = true;
                if (collectorManager != null) collectorManager.SetGameplayEnabled(false);
                if (HypercasualGameEngine.SoundManager.Instance != null) HypercasualGameEngine.SoundManager.Instance.PlayLevelClear();
                LevelManager.AdvanceLevel();
                if (loseWinPanel != null) loseWinPanel.ShowWin();
            }
        }

        // === CURRENT LEVEL TEXT ===

        private void SetCurrentLevelTexts()
        {
            string levelText = $"Level {LevelManager.CurrentLevelIndex + 1}";
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                SetAllChildTexts(root.transform, "CurrentLevelText", levelText);
            }
        }

        private static void SetAllChildTexts(Transform parent, string name, string text)
        {
            if (parent.name == name)
            {
                var tmp = parent.GetComponent<TextMeshProUGUI>();
                if (tmp != null) tmp.text = text;
            }
            for (int i = 0; i < parent.childCount; i++)
                SetAllChildTexts(parent.GetChild(i), name, text);
        }

        // === BOOSTER POPUPS ===

        private void FindBoosterPopups()
        {
            // Popups are inactive by default in the scene, so GameObject.Find won't work.
            // Search all root objects and their children recursively.
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (popupSlot == null) popupSlot = FindChildByName(root.transform, "Popup_Powerup_ExtraBucket");
                if (popupShuffle == null) popupShuffle = FindChildByName(root.transform, "Popup_Powerup_Shuffle");
                if (popupHand == null) popupHand = FindChildByName(root.transform, "Popup_Powerup_Pick");
                if (popupSlot != null && popupShuffle != null && popupHand != null) break;
            }

        }

        private static void EnsurePopupSorting(GameObject popup)
        {
            if (popup == null) return;
            Canvas c = popup.GetComponent<Canvas>();
            if (c == null) c = popup.AddComponent<Canvas>();
            c.overrideSorting = true;
            c.sortingOrder = 100;
            if (c.renderMode == RenderMode.WorldSpace && c.worldCamera == null)
                c.worldCamera = Camera.main;
            if (popup.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
                popup.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        }

        private static GameObject FindChildByName(Transform parent, string name)
        {
            if (parent.name == name) return parent.gameObject;
            for (int i = 0; i < parent.childCount; i++)
            {
                GameObject found = FindChildByName(parent.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        private void TryShowBoosterPopup(GameObject popup, string prefKey)
        {
            if (popup == null) return;
            if (PlayerPrefs.GetInt(prefKey, 0) == 1) return;
            PlayerPrefs.SetInt(prefKey, 1);
            PlayerPrefs.Save();
            popup.SetActive(true);
            EnsurePopupSorting(popup);
        }

        // === BOOSTERS ===

        private void OnSlotBooster()
        {
            // Extra belt slots do not apply after the conveyor has been removed.
        }

        private void ShowCounterPopup(string text)
        {
            if (popupTMP == null) return;
            popupTMP.text = text;
            popupTMP.color = new Color(0.29f, 0.87f, 0.5f, 1f);
            popupRt.anchoredPosition = new Vector2(0f, 0.21f);
            popupTimer = popupDuration;
        }

        private void OnShuffleBooster()
        {
            TryShowBoosterPopup(popupShuffle, PrefKeyShufflePopup);
            if (shuffleBoosterCount <= 0 || collectorManager == null || collectorManager.IsDragging) return;
            shuffleBoosterCount--;
            collectorManager.ShuffleBlocks();
            UpdateBoosterUI();
            if (HypercasualGameEngine.SoundManager.Instance != null) HypercasualGameEngine.SoundManager.Instance.PlaySandFlowBoosterShuffle();
        }

        private void OnMagicBooster()
        {
            // Every block is directly draggable, so the old bucket-pick booster is obsolete.
        }

        private void UpdateBoosterUI()
        {
            int currentIdx = LevelManager.CurrentLevelIndex;

            bool shuffleLocked = currentIdx < shuffleBoosterUnlockLevel;

            // Slot and magic belonged to bucket/conveyor gameplay. Hide their controls.
            if (slotBoosterButton != null) slotBoosterButton.gameObject.SetActive(false);
            if (slotBoosterBg != null) slotBoosterBg.SetActive(false);
            if (slotBoosterCountGo != null) slotBoosterCountGo.SetActive(false);

            // Shuffle booster
            bool shuffleUsable = !shuffleLocked && shuffleBoosterCount > 0;
            if (shuffleBoosterCountText != null)
                shuffleBoosterCountText.text = shuffleLocked
                    ? $"lvl {shuffleBoosterUnlockLevel + 1}"
                    : shuffleBoosterCount.ToString();
            if (shuffleBoosterButton != null)
                shuffleBoosterButton.interactable = shuffleUsable;
            if (shuffleBoosterBg != null)
                shuffleBoosterBg.SetActive(shuffleUsable);
            if (shuffleBoosterCountGo != null)
                shuffleBoosterCountGo.SetActive(shuffleLocked || shuffleUsable);

            if (magicBoosterButton != null) magicBoosterButton.gameObject.SetActive(false);
            if (magicBoosterBg != null) magicBoosterBg.SetActive(false);
            if (magicBoosterCountGo != null) magicBoosterCountGo.SetActive(false);
            if (magicBoosterImage != null) magicBoosterImage.color = magicBoosterOriginalColor;
        }

        private void ApplyBoosterUnlocks()
        {
            UpdateBoosterUI();
        }

        private static bool IsKeyDown(KeyCode legacyKey)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(legacyKey)) return true;
#endif
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                switch (legacyKey)
                {
                    case KeyCode.U: return kb[Key.U].wasPressedThisFrame;
                    case KeyCode.RightArrow: return kb[Key.RightArrow].wasPressedThisFrame;
                    case KeyCode.LeftArrow: return kb[Key.LeftArrow].wasPressedThisFrame;
                }
            }
#endif
            return false;
        }

    }
}
