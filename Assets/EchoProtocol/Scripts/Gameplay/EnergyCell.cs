
namespace Assets.EchoProtocol.Scripts.Gameplay
{
    using UnityEngine;
    
    public class EnergyCell : MonoBehaviour, Interface.IInteractable
    {
        [SerializeField] private Core.GameManager gameManager;

        [SerializeField] private float rotationSpeed = 50f;
        [SerializeField] private float hoverHeight = 0.15f;
        [SerializeField] private float hoverSpeed = 2.5f;

        [SerializeField] private AudioSource collectionAudio;

        private Vector3 startPosition;

        private bool collected;

        public string InteractionPrompt => "E - Pickup Energycell";

        private void Start()
        {
            startPosition = transform.position;
        }

        private void Update()
        {
            if(collected)
                return;
            
            transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);

            float hoverOffset = Mathf.Sin(Time.time * hoverSpeed) * hoverHeight;

            transform.position = startPosition + Vector3.up * hoverOffset;
        }

        public void Interact()
        {
            if(collected)
                return;
            
            collected = true;
            gameManager.CollectEnergyCells();

            if(collectionAudio != null)
            {
                transform.DetachChildren();
                collectionAudio.Play();
            }

            gameObject.SetActive(false);
        }
    }
}
