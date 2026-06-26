
namespace Assets.EchoProtocol.Scripts.Gameplay
{
    using UnityEngine;
    
    /// <summary>
    /// Final interaction point of the level.
    /// The terminal stays locked until GameManager says enough EnergyCells have been collected.
    /// </summary>
    public class ExitTerminal : MonoBehaviour, Interface.IInteractable
    {
        // Animator.StringToHash avoids converting the string "Open" every time the terminal is used.
        private static readonly int OpenHash = Animator.StringToHash("Open");

        // GameManager owns the objective state, so the terminal asks it whether the game can finish.
        [SerializeField] private Core.GameManager gameManager;

        // Visual feedback for locked/unlocked state.
        [SerializeField] private Renderer statusRenderer;
        [SerializeField] private Material lockedMaterial;
        [SerializeField] private Material unlockedMaterial;

        // Optional door animator. If assigned, it receives the "Open" trigger when the exit is used.
        [SerializeField] private Animator doorAnimator;

        private bool unlocked;

        // The prompt is dynamic: it changes when the terminal becomes unlocked.
        public string InteractionPrompt
        {
            get
            {
                if (unlocked)
                    return "E - Open Emergency Exit";

                return $"Exit Locked " +
                    $"[{gameManager.CollectedCells}/" +
                    $"{gameManager.RequiredCells}]";
            }
        }

        /// <summary>
        /// Called by GameManager when the required number of EnergyCells has been reached.
        /// </summary>
        public void SetUnlocked(bool newState)
        {
            unlocked = newState;

            if (statusRenderer != null)
            {
                statusRenderer.material =
                    unlocked ? unlockedMaterial : lockedMaterial;
            }
        }

        /// <summary>
        /// Called by PlayerInteraction. If locked, the player gets a missing-cells message.
        /// If unlocked, the terminal opens the door and asks GameManager to win the game.
        /// </summary>
        public void Interact()
        {
            if (!unlocked)
            {
                gameManager.TryCompleteGame();
                return;
            }

            if (doorAnimator != null)
                doorAnimator.SetTrigger(OpenHash);

            gameManager.TryCompleteGame();
        }
    }
}
