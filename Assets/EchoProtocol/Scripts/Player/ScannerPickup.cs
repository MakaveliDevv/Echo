
namespace Assets.EchoProtocol.Scripts.Player
{
    using UnityEngine;
   
    public class ScannerPickup : MonoBehaviour, Interface.IInteractable
    {
        [SerializeField] private ScannerController scannerController;

        public string InteractionPrompt => "E - Activate Scanner";

        public void Interact()
        {
            scannerController.ActivateScanner();
            gameObject.SetActive(false);
        }
    }
}
