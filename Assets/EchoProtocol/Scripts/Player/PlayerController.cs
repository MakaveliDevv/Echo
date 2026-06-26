
namespace Assets.EchoProtocol.Scripts.Player
{
    using UnityEngine;
    using UnityEngine.InputSystem;

    /// <summary>
    /// First-person movement and camera look controller.
    /// It uses Unity's CharacterController for collision-based movement instead of Rigidbody physics.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("References")]
        // Camera holder is pitched up/down. The player root rotates left/right.
        [SerializeField] private Transform cameraHolder;

        [Header("Movement")]
        // Basic movement speeds in units per second.
        [SerializeField] private float walkSpeed = 4f;
        [SerializeField] private float sprintSpeed = 6f;

        // Negative value pulls the player downward when not grounded.
        [SerializeField] private float gravity = -9.81f;

        [Header("Looking")]
        // Mouse sensitivity is intentionally small because Mouse.delta can be large.
        [SerializeField] private float mouseSensitibity = 0.12f;

        // Pitch limits stop the camera from flipping over vertically.
        [SerializeField] private float minimumPitch = -80f;
        [SerializeField] private float maximumPitch = 80f;

        private CharacterController characterController;

        // Stored separately so gravity can build up over time.
        private float verticalVelocity;

        // Current up/down camera angle.
        private float pitch;

        // GameManager disables this when the player wins or loses.
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

            // Escape toggles the cursor so the player can leave play mode or click UI while testing.
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

            // Horizontal mouse movement rotates the whole player body.
            transform.Rotate(Vector3.up, mouseDelta.x * mouseSensitibity);

            // Vertical mouse movement only rotates the camera holder.
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

            // Movement is relative to where the player is looking.
            Vector3 direction = transform.right * horizontal + transform.forward * vertical;

            // Prevent diagonal movement from being faster than straight movement.
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

        /// <summary>
        /// Used by GameManager to freeze the player after win/lose.
        /// </summary>
        public void SetControlsEnabled(bool enabled)
        {
            controlsEnabled = enabled;

            if(!enabled)
                SetCursorLocked(false);
        }

        // Cursor lock is private because only this movement script should decide how looking works.
        private void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    } 
}
