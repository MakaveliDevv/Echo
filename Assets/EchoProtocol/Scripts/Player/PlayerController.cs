
namespace Assets.EchoProtocol.Scripts.Player
{
    using UnityEngine;
    using UnityEngine.InputSystem;

    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform cameraHolder;

        [Header("Movement")]
        [SerializeField] private float walkSpeed = 4f;
        [SerializeField] private float sprintSpeed = 6f;

        [SerializeField] private float gravity = -9.81f;

        [Header("Looking")]
        [SerializeField] private float mouseSensitibity = 0.12f;

        [SerializeField] private float minimumPitch = -80f;
        [SerializeField] private float maximumPitch = 80f;

        private CharacterController characterController;

        private float verticalVelocity;

        private float pitch;

        private bool controlsEnabled = true;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
        }

        private void Start()
        {
            SetCursorLocked(true);   
        }

        private void Update()
        {
            if(!controlsEnabled)
                return;
            
            HandleLooking();
            HandleMovement();

            if(Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                bool shouldLock = Cursor.lockState != CursorLockMode.Locked;
                SetCursorLocked(shouldLock);
            }
        }

        private void HandleLooking()
        {
            if(Mouse.current == null || Cursor.lockState != CursorLockMode.Locked)
                return;
            
            Vector2 mouseDelta = Mouse.current.delta.ReadValue();

            transform.Rotate(Vector3.up, mouseDelta.x * mouseSensitibity);

            pitch -= mouseDelta.y * mouseSensitibity;
            pitch = Mathf.Clamp(pitch, minimumPitch, maximumPitch);

            cameraHolder.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        private void HandleMovement()
        {
            if(Keyboard.current == null)
                return;
            
            float horizontal = 0f;
            float vertical = 0f;

            if(Keyboard.current.aKey.isPressed) horizontal -= 1f;
            if (Keyboard.current.dKey.isPressed) horizontal += 1f;
            if (Keyboard.current.sKey.isPressed) vertical -= 1f;
            if (Keyboard.current.wKey.isPressed) vertical += 1f;

            Vector3 direction = transform.right * horizontal + transform.forward * vertical;

            direction = Vector3.ClampMagnitude(direction, 1f);

            bool sprinting = Keyboard.current.leftShiftKey.isPressed;
            float speed = sprinting ? sprintSpeed : walkSpeed;

            if(characterController.isGrounded && verticalVelocity < 0f)
                verticalVelocity = -2f;

            verticalVelocity += gravity * Time.deltaTime;

            Vector3 velocity = direction * speed;
            velocity.y = verticalVelocity;

            characterController.Move(velocity * Time.deltaTime);
        }

        public void SetControlsEnabled(bool enabled)
        {
            controlsEnabled = enabled;

            if(!enabled)
                SetCursorLocked(false);
        }

        private void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    } 
}
