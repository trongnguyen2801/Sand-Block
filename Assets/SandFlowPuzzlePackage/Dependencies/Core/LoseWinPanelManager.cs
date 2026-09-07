using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using UnityEngine.UI;

namespace HypercasualGameEngine
{
    public class LoseWinPanelManager : MonoBehaviour
    {
        [Header("UI Panels")]
        public GameObject winPanel;
        public GameObject losePanel;
        
        [Header("Buttons")]
        public Button retryButton;
        public Button nextLevelButton; // Optional if auto-load
    
        [Header("Settings")]
        public float autoLoadDelay = 3.0f;
        public bool autoLoadNextLevel = true;
    
        private void Start()
        {
            // Auto-setup references if missing
            if (winPanel == null)
            {
                Transform t = transform.Find("WinPanel");
                if (t != null) winPanel = t.gameObject;
            }
            if (losePanel == null)
            {
                Transform t = transform.Find("LosePanel");
                if (t != null) losePanel = t.gameObject;
            }
            if (retryButton == null && losePanel != null)
            {
                retryButton = losePanel.GetComponentInChildren<Button>();
            }
    
            // Ensure panels are hidden at start
            if (winPanel != null) winPanel.SetActive(false);
            if (losePanel != null) losePanel.SetActive(false);
    
            // Setup Retry Button
            if (retryButton != null)
            {
                retryButton.onClick.RemoveAllListeners();
                retryButton.onClick.AddListener(OnRetryClicked);
            }
            
            // Setup Next Level Button (if exists and we want manual click)
            if (nextLevelButton != null)
            {
                nextLevelButton.onClick.RemoveAllListeners();
                nextLevelButton.onClick.AddListener(LoadNextLevel);
            }
        }
    
        public void ShowWin()
        {
            if (winPanel != null)
            {
                winPanel.SetActive(true);
                if (autoLoadNextLevel)
                {
                    StartCoroutine(AutoLoadNextLevelRoutine());
                }
            }
        }
    
        public void ShowLose()
        {
            if (losePanel != null)
            {
                losePanel.SetActive(true);
            }
        }
    
        private IEnumerator AutoLoadNextLevelRoutine()
        {
            yield return new WaitForSeconds(autoLoadDelay);
            LoadNextLevel();
        }
    
        private void LoadNextLevel()
        {
            // Logic to load next level for the specific game
            // Since we don't have a unified Level Manager for all games yet, 
            // we might just reload the scene for now or call a specific manager.
            // Requirement says: "auto-load next level if there is any for thag specific game"
            
            // For now, let's assume we just reload the scene to simulate "Next Level" 
            // or we can try to find a LevelManager in the parent hierarchy.
            
            // If we want to support specific game logic, we might need an event or delegate.
            // But for this task, let's just reload the scene as a placeholder for "Next Level" 
            // OR if the game has its own logic, it should handle it.
            
            // Actually, the requirement says "auto-load next level if there is any".
            // If we don't know how to load the next level, maybe we just hide the panel?
            // Or we can try to find a component that implements a "INextLevelHandler" interface?
            
            // Let's try to find a known manager.
            // ArrowsLevelManager? GameManager (Word)?
            
            // Simple approach: Reload Scene (Simulates replay/next level for prototype)
            // But ideally we should increment a level index.
            
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
    
        private void OnRetryClicked()
        {
            // Reload the whole scene
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
    }
    
}