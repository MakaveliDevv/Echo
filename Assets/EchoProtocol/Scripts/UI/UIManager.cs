
namespace Assets.EchoProtocol.Scripts.UI
{
    using System.Collections;
    using TMPro;
    using UnityEngine;

    /// <summary>
    /// Small UI helper used by the other gameplay scripts.
    /// Keeping all UI text changes here prevents every gameplay script from needing direct access to
    /// individual TextMeshPro objects.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        // Text fields assigned in the Canvas.
        [SerializeField] private TextMeshProUGUI cellCounterText;
        [SerializeField] private TextMeshProUGUI scannerStatusText;
        [SerializeField] private TextMeshProUGUI interactionPromptText;
        [SerializeField] private TextMeshProUGUI messageText;

        // End-game panels shown by GameManager.
        [SerializeField] private GameObject winPanel;
        [SerializeField] private GameObject losePanel;

        // Stores the currently running message coroutine so a new message can replace the old one.
        private Coroutine messageCoroutine;

        // Called by GameManager whenever the objective count changes.
        public void UpdateCellCounter(int current, int required)
        {
            cellCounterText.text =
                $"EnergyCells {current}/{required}";
        }

        // Called every frame by ScannerController so the UI reflects activation and cooldown.
        public void UpdateScannerStatus(
            bool activated,
            float cooldown)
        {
            if (!activated)
            {
                scannerStatusText.text = "SCANNER OFFLINE";
                return;
            }

            scannerStatusText.text = cooldown > 0f
                ? $"SCANNER RECHARGE: {cooldown:0.0}s"
                : "SCANNER Ready [Q / LMB]";
        }

        // Called by PlayerInteraction when an interactable object is under the crosshair.
        public void ShowInteractionPrompt(string prompt)
        {
            interactionPromptText.text = prompt;
            interactionPromptText.gameObject.SetActive(true);
        }

        // Called by PlayerInteraction when nothing interactable is being looked at.
        public void HideInteractionPrompt()
        {
            interactionPromptText.gameObject.SetActive(false);
        }

        /// <summary>
        /// Shows a message for a limited time.
        /// Starting a new message stops the previous one so texts do not fight each other.
        /// </summary>
        public void ShowTemporaryMessage(
            string message,
            float duration)
        {
            if (messageCoroutine != null)
                StopCoroutine(messageCoroutine);

            messageCoroutine = StartCoroutine(
                MessageRoutine(message, duration)
            );
        }

        // Used by GameManager when the exit is successfully used.
        public void ShowWinScreen()
        {
            winPanel.SetActive(true);
        }

        // Used by GameManager when the enemies catch the player.
        public void ShowLoseScreen()
        {
            losePanel.SetActive(true);
        }

        // Called at scene start so leftover editor state does not show panels immediately.
        public void HideEndScreens()
        {
            winPanel.SetActive(false);
            losePanel.SetActive(false);
        }

        // Coroutine means "wait over time" without blocking the whole game.
        private IEnumerator MessageRoutine(
            string message,
            float duration)
        {
            messageText.text = message;
            messageText.gameObject.SetActive(true);

            yield return new WaitForSeconds(duration);

            messageText.gameObject.SetActive(false);
            messageCoroutine = null;
        }
    }
}
