
namespace Assets.EchoProtocol.Scripts.Player
{
    using UnityEngine;
    using UnityEngine.InputSystem;
    
    /// <summary>
    /// Handles looking at interactable objects and pressing E to use them.
    /// This script is intentionally generic: it only depends on the IInteractable interface.
    /// </summary>
    public class PlayerInteraction : MonoBehaviour
    {
        // The ray starts from the camera, because interaction is based on what the player looks at.
        [SerializeField] private Camera playerCamera;

        // UIManager shows or hides the prompt text.
        [SerializeField] private UI.UIManager uIManager;

        // Maximum distance from camera to interactable object.
        [SerializeField] private float interactionDistance = 2.5f;

        // Only colliders on these layers can be detected by the interaction raycast.
        [SerializeField] private LayerMask interactionMask = ~0;

        // Cached interactable for the current frame. Null means nothing usable is being looked at.
        private Interface.IInteractable currentInteractable;

        private void Update()
        {
            FindInteractable();

            // If something interactable is in front of the camera, E activates it.
            if(currentInteractable != null && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
                currentInteractable.Interact();
        }

        private void FindInteractable()
        {
            currentInteractable = null;

            Ray ray = new(playerCamera.transform.position, playerCamera.transform.forward);

            // QueryTriggerInteraction.Collide allows trigger colliders to be used as interaction zones.
            if(Physics.Raycast(ray, out RaycastHit hit, interactionDistance, interactionMask, QueryTriggerInteraction.Collide))
            {
                // GetComponentInParent lets the collider live on a child while the script is on the root object.
                currentInteractable = hit.collider.GetComponentInParent<Interface.IInteractable>();
            }

            if(currentInteractable != null)
                uIManager.ShowInteractionPrompt(currentInteractable.InteractionPrompt);
            else
                uIManager.HideInteractionPrompt();
        }
    }
}
