
namespace Assets.EchoProtocol.Scripts.Player
{
    using UnityEngine;
    using UnityEngine.InputSystem;
    
    public class PlayerInteraction : MonoBehaviour
    {
        [SerializeField] private Camera playerCamera;

        [SerializeField] private UI.UIManager uIManager;

        [SerializeField] private float interactionDistance = 2.5f;

        [SerializeField] private LayerMask interactionMask = ~0;

        private Interface.IInteractable currentInteractable;

        private void Update()
        {
            FindInteractable();

            if(currentInteractable != null && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
                currentInteractable.Interact();
        }

        private void FindInteractable()
        {
            currentInteractable = null;

            Ray ray = new(playerCamera.transform.position, playerCamera.transform.forward);

            if(Physics.Raycast(ray, out RaycastHit hit, interactionDistance, interactionMask, QueryTriggerInteraction.Collide))
            {
                currentInteractable = hit.collider.GetComponentInParent<Interface.IInteractable>();
            }

            if(currentInteractable != null)
                uIManager.ShowInteractionPrompt(currentInteractable.InteractionPrompt);
            else
                uIManager.HideInteractionPrompt();
        }
    }
}
