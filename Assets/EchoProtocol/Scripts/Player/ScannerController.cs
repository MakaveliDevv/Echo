
namespace Assets.EchoProtocol.Scripts.Player
{
    using System;
    using UnityEngine;
    using UnityEngine.InputSystem;
    
    public class ScannerController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameObject heldScannerVisual;

        [SerializeField] private Core.ScanWaveComputeController scanWaveController;

        [SerializeField] private UI.UIManager uiManager;

        [Header("Settings")]
        [SerializeField] private float cooldownDuration = 3f;

        [SerializeField] private bool automaticFirstScan = true;

        [SerializeField] private Transform probeLaunchPoint;

        [SerializeField] private ScanProbeBall scanProbePrefab;

        [SerializeField] private float probeDistance = 12f;
        [SerializeField] private float probeSpeed = 18f;

        [SerializeField] private LayerMask probeCollisionMask = Physics.DefaultRaycastLayers;

        public event Action<Vector3> OnScanTriggered;

        public bool ScannerActivated { get; private set; }
        public float RemainingCooldown { get; private set; }

        private void Start()
        {
            ScannerActivated = false;
            heldScannerVisual.SetActive(false);
            uiManager.UpdateScannerStatus(false, 0f);
        }

        private void Update()
        {
            RemainingCooldown = Mathf.Max(0f, RemainingCooldown - Time.deltaTime);

            uiManager.UpdateScannerStatus(ScannerActivated, RemainingCooldown);

            if(ScanButtonPressed())
                TryScan();
        }

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

            RemainingCooldown = cooldownDuration;
        }

        private static bool ScanButtonPressed()
        {
            bool keyboardScan = Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame;
            bool mouseScan = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;

            return keyboardScan || mouseScan;
        }

        private void TriggerScanFromProbe(Vector3 scanPosition)
        {
            scanWaveController.StartScan(scanPosition);
            OnScanTriggered?.Invoke(scanPosition);
        }
    }
}
