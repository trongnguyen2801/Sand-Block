using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace SandFlowPuzzle
{
    public class DeveloperSettingsController : MonoBehaviour
    {
        [Header("Level Control Buttons")]
        public Button nextLevelButton;
        public Button previousLevelButton;
        public Button reloadLevelButton;
        public Button triggerWinButton;
        public Button triggerLoseButton;

        [Header("Visual Theme Control")]
        public Button previousVisualButton;
        public Button nextVisualButton;
        public TextMeshProUGUI visualsIndexText;

        private void Start()
        {
            if (nextLevelButton != null)
                nextLevelButton.onClick.AddListener(OnNextLevelClicked);

            if (previousLevelButton != null)
                previousLevelButton.onClick.AddListener(OnPreviousLevelClicked);

            if (reloadLevelButton != null)
                reloadLevelButton.onClick.AddListener(OnReloadLevelClicked);

            if (triggerWinButton != null)
                triggerWinButton.onClick.AddListener(OnTriggerWinClicked);

            if (triggerLoseButton != null)
                triggerLoseButton.onClick.AddListener(OnTriggerLoseClicked);

            if (previousVisualButton != null)
                previousVisualButton.onClick.AddListener(OnPreviousVisualClicked);

            if (nextVisualButton != null)
                nextVisualButton.onClick.AddListener(OnNextVisualClicked);
        }

        private void OnNextLevelClicked()
        {
            Debug.Log("[DevSettings] Next Level");
            LevelManager.AdvanceLevel();
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        }

        private void OnPreviousLevelClicked()
        {
            Debug.Log("[DevSettings] Previous Level");
            LevelManager.GoToPreviousLevel();
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        }

        private void OnReloadLevelClicked()
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        }

        private void OnTriggerWinClicked()
        {
            var gm = FindObjectOfType<SandFlowPuzzleGameManager>();
            if (gm != null)
            {
                Debug.Log("[DevSettings] Trigger Win");
                gm.TriggerWin();
            }
            else
                Debug.LogWarning("[DevSettings] Trigger Win - no SandFlowPuzzleGameManager found.");
        }

        private void OnTriggerLoseClicked()
        {
            var gm = FindObjectOfType<SandFlowPuzzleGameManager>();
            if (gm != null)
            {
                Debug.Log("[DevSettings] Trigger Lose");
                gm.TriggerLose();
            }
            else
                Debug.LogWarning("[DevSettings] Trigger Lose - no SandFlowPuzzleGameManager found.");
        }

        private void OnPreviousVisualClicked()
        {
            // TODO: Wire up when VisualThemeManager is implemented
            Debug.Log("[DevSettings] Previous Visual - no VisualThemeManager found.");
        }

        private void OnNextVisualClicked()
        {
            // TODO: Wire up when VisualThemeManager is implemented
            Debug.Log("[DevSettings] Next Visual - no VisualThemeManager found.");
        }
    }
}
