
namespace Assets.EchoProtocol.Scripts.Core
{
    using UnityEngine;
    using UnityEngine.SceneManagement;

    public class GameManager : MonoBehaviour
    {
        [Header("Objective")]
        [SerializeField] private int requiredCells = 3;

        [Header("References")]
        [SerializeField] private Player.PlayerController playerController;
        [SerializeField] private UI.UIManager uiManager;
        [SerializeField] private Gameplay.ExitTerminal exitTerminal;

        public int CollectedCells { get; private set; }
        public int RequiredCells => requiredCells;
        public bool GameHasEnded { get; private set; }

        private void Start()
        {
            CollectedCells = 0;
            GameHasEnded = false;

            exitTerminal.SetUnlocked(false);
            uiManager.UpdateCellCounter(CollectedCells, requiredCells);
            uiManager.HideEndScreens();
        }

        public void CollectEnergyCells()
        {
            if(GameHasEnded) 
                return;

            CollectedCells++;
            uiManager.UpdateCellCounter(CollectedCells, requiredCells);

            if(CollectedCells >= requiredCells)
            {
                exitTerminal.SetUnlocked(true);
                uiManager.ShowTemporaryMessage("All energycells are collected. Go back to the exit", 4f);
            }
            else
            {
                uiManager.ShowTemporaryMessage($"Energycell collected [{CollectedCells}/{requiredCells}]", 2.5f);
            }
        }

        public void TryCompleteGame()
        {
            if(GameHasEnded)
                return;
            
            if(CollectedCells < requiredCells)
            {
                int missing = requiredCells - CollectedCells;
                uiManager.ShowTemporaryMessage($"Need {missing} energycells yet", 2.5f);
               
                return;
            }
            
            GameHasEnded = true;
            playerController.SetControlsEnabled(false);
            uiManager.ShowWinScreen();

            UnlockCursor();
        }

        public void PlayerCaught()
        {
            if(GameHasEnded)
                return;
            
            GameHasEnded = true;
            playerController.SetControlsEnabled(false);
            uiManager.ShowLoseScreen();

            UnlockCursor();
        }

        public void RestartGame()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private static void UnlockCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        
    }
}
