
namespace Assets.EchoProtocol.Scripts.UI
{
    using System.Collections;
    using TMPro;
    using UnityEngine;

    public class UIManager : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI cellCounterText;
        [SerializeField] private TextMeshProUGUI scannerStatusText;
        [SerializeField] private TextMeshProUGUI interactionPromptText;
        [SerializeField] private TextMeshProUGUI messageText;

        [SerializeField] private GameObject winPanel;
        [SerializeField] private GameObject losePanel;

        private Coroutine messageCoroutine;

        public void UpdateCellCounter(int current, int required)
        {
            cellCounterText.text =
                $"EnergyCells {current}/{required}";
        }

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

        public void ShowInteractionPrompt(string prompt)
        {
            interactionPromptText.text = prompt;
            interactionPromptText.gameObject.SetActive(true);
        }

        public void HideInteractionPrompt()
        {
            interactionPromptText.gameObject.SetActive(false);
        }

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

        public void ShowWinScreen()
        {
            winPanel.SetActive(true);
        }

        public void ShowLoseScreen()
        {
            losePanel.SetActive(true);
        }

        public void HideEndScreens()
        {
            winPanel.SetActive(false);
            losePanel.SetActive(false);
        }

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
