
namespace Assets.EchoProtocol.Scripts.Gameplay
{
    using UnityEngine;
    
    public class ExitTerminal : MonoBehaviour, Interface.IInteractable
    {
        private static readonly int OpenHash = Animator.StringToHash("Open");

        [SerializeField] private Core.GameManager gameManager;

        [SerializeField] private Renderer statusRenderer;
        [SerializeField] private Material lockedMaterial;
        [SerializeField] private Material unlockedMaterial;

        [SerializeField] private Animator doorAnimator;

        private bool unlocked;

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

        public void SetUnlocked(bool newState)
        {
            unlocked = newState;

            if (statusRenderer != null)
            {
                statusRenderer.material =
                    unlocked ? unlockedMaterial : lockedMaterial;
            }
        }

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
