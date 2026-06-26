
namespace Assets.EchoProtocol.Scripts.Gameplay
{
    using UnityEngine;
    
    /// <summary>
    /// Collectible objective item.
    /// It implements IInteractable so PlayerInteraction can detect it and call Interact().
    /// </summary>
    public class EnergyCell : MonoBehaviour, Interface.IInteractable
    {
        // GameManager receives the collection event and updates the objective progress.
        [SerializeField] private Core.GameManager gameManager;

        // Small idle animation settings to make the pickup visible in the scene.
        [SerializeField] private float rotationSpeed = 50f;
        [SerializeField] private float hoverHeight = 0.15f;
        [SerializeField] private float hoverSpeed = 2.5f;

        // Optional audio source. If assigned, it plays when the object is collected.
        [SerializeField] private AudioSource collectionAudio;

        // Stored once so the hover animation always moves around the original position.
        private Vector3 startPosition;

        // Prevents double collection if Interact() somehow gets called twice in the same frame.
        private bool collected;

        // Text shown by UIManager through PlayerInteraction.
        public string InteractionPrompt => "E - Pickup Energycell";

        private void Start()
        {
            startPosition = transform.position;
        }

        private void Update()
        {
            if(collected)
                return;
            
            // Rotate in world space so the cell always spins around the global up axis.
            transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);

            float hoverOffset = Mathf.Sin(Time.time * hoverSpeed) * hoverHeight;

            transform.position = startPosition + Vector3.up * hoverOffset;
        }

        /// <summary>
        /// Called when the player presses E while looking at this pickup.
        /// </summary>
        public void Interact()
        {
            if(collected)
                return;
            
            collected = true;
            gameManager.CollectEnergyCells();

            if(collectionAudio != null)
            {
                // Detach children before disabling this object so a child AudioSource can keep playing.
                transform.DetachChildren();
                collectionAudio.Play();
            }

            gameObject.SetActive(false);
        }
    }
}
