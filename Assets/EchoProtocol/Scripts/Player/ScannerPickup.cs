
namespace Assets.EchoProtocol.Scripts.Player
{
    using UnityEngine;
   
    /// <summary>
    /// World object that turns on the player's scanner when interacted with.
    /// The actual scanner logic lives in ScannerController; this script is only the pickup trigger.
    /// </summary>
    public class ScannerPickup : MonoBehaviour, Interface.IInteractable
    {
        // Usually assigned to the ScannerController on the player.
        [SerializeField] private ScannerController scannerController;

        // Text shown when the player looks at the pickup.
        public string InteractionPrompt => "E - Activate Scanner";

        public void Interact()
        {
            // Enable the player-held scanner and remove the pickup from the level.
            scannerController.ActivateScanner();
            gameObject.SetActive(false);
        }
    }
}
