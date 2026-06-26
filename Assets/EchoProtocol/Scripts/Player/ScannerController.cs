
namespace Assets.EchoProtocol.Scripts.Player
{
    using System;
    using UnityEngine;
    using UnityEngine.InputSystem;
    
    /// <summary>
    /// Controls the handheld scanner.
    /// The scanner launches a ScanProbeBall first; when that probe stops, the actual scan wave starts
    /// from the probe's position and enemies are notified through OnScanTriggered.
    /// </summary>
    public class ScannerController : MonoBehaviour
    {
        [Header("References")]
        // Visual model in the player's hand. It is hidden until ScannerPickup activates the scanner.
        [SerializeField] private GameObject heldScannerVisual;

        // Starts the scan-wave render texture effect.
        [SerializeField] private Core.ScanWaveComputeController scanWaveController;

        // Shows scanner status, cooldown, and messages.
        [SerializeField] private UI.UIManager uiManager;

        [Header("Settings")]
        // Delay before the next probe/scan can be fired.
        [SerializeField] private float cooldownDuration = 3f;

        // Useful for teaching the player what the scanner does as soon as they pick it up.
        [SerializeField] private bool automaticFirstScan = true;

        // The probe starts here, usually at the front of the scanner model.
        [SerializeField] private Transform probeLaunchPoint;

        // Prefab that physically travels forward before triggering the scan.
        [SerializeField] private ScanProbeBall scanProbePrefab;

        // How far the probe can travel if it does not hit a wall first.
        [SerializeField] private float probeDistance = 12f;
        [SerializeField] private float probeSpeed = 18f;

        // Layers the probe is allowed to collide with.
        [SerializeField] private LayerMask probeCollisionMask = Physics.DefaultRaycastLayers;

        // EnemyComputeController subscribes to this event to make enemies investigate the scan point.
        public event Action<Vector3> OnScanTriggered;

        // Public read-only state for UI or other scripts.
        public bool ScannerActivated { get; private set; }
        public float RemainingCooldown { get; private set; }

        private void Start()
        {
            // The player begins without the scanner active.
            ScannerActivated = false;
            heldScannerVisual.SetActive(false);
            uiManager.UpdateScannerStatus(false, 0f);
        }

        private void Update()
        {
            // Cooldown counts down every frame but never goes below zero.
            RemainingCooldown = Mathf.Max(0f, RemainingCooldown - Time.deltaTime);

            uiManager.UpdateScannerStatus(ScannerActivated, RemainingCooldown);

            if(ScanButtonPressed())
                TryScan();
        }

        /// <summary>
        /// Called by ScannerPickup. This switches the scanner from a world pickup into a usable player tool.
        /// </summary>
        public void ActivateScanner()
        {
            if(ScannerActivated)
                return;

            ScannerActivated = true;
            heldScannerVisual.SetActive(true);

            uiManager.ShowTemporaryMessage("Scanner online. Press 0 or the left mousebutton", 4f);

            if(automaticFirstScan)
                PerformScan();
        }

        private void TryScan()
        {
            // The player can press the scan button before picking up the scanner, so give clear feedback.
            if(!ScannerActivated)
            {
                uiManager.ShowTemporaryMessage("Scanner is offline", 1.5f);
                return;
            }

            if(RemainingCooldown > 0f)
                return;
            
            PerformScan();
        }

        private void PerformScan()
        {
            // Instantiate the probe at the launch point. The probe decides where the scan origin should be.
            ScanProbeBall probe = Instantiate(
                scanProbePrefab,
                probeLaunchPoint.position,
                probeLaunchPoint.rotation
            );

            probe.Launch(
                probeLaunchPoint.forward,
                probeDistance,
                probeSpeed,
                probeCollisionMask,
                transform.root,
                TriggerScanFromProbe
            );

            // Cooldown starts immediately so the player cannot spam multiple probes at once.
            RemainingCooldown = cooldownDuration;
        }

        private static bool ScanButtonPressed()
        {
            // Q and left mouse both trigger a scan for easier testing/playing.
            bool keyboardScan = Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame;
            bool mouseScan = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;

            return keyboardScan || mouseScan;
        }

        /// <summary>
        /// Callback passed into ScanProbeBall.Launch().
        /// This is where the projectile movement ends and the shader/enemy scan logic begins.
        /// </summary>
        private void TriggerScanFromProbe(Vector3 scanPosition)
        {
            scanWaveController.StartScan(scanPosition);
            OnScanTriggered?.Invoke(scanPosition);
        }
    }
}
