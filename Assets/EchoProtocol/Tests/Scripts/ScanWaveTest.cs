namespace Assets.EchoProtocol.Iterations
{
    using UnityEngine;
    using UnityEngine.InputSystem;

    [ExecuteAlways]
    public class ScanWaveTest : MonoBehaviour
    {
        public enum ScanWaveTestt
        {
            Test01 = 0,
            Test02 = 1
        }

        [Header("Iteration")]
        [SerializeField] private ScanWaveTestt iteration = ScanWaveTestt.Test01;

        [Header("Scan Targets")]
        [SerializeField] private Material scanMaterial;

        [SerializeField] private Renderer[] targetRenderers = System.Array.Empty<Renderer>();

        [Header("Scan Origin")]
        [SerializeField] private Transform scanOriginTransform;

        [SerializeField] private Camera scanCamera;

        [Header("Preview")]
        [SerializeField] private bool loopPreview = true;
        [SerializeField] private float scanDuration = 3.5f;
        [SerializeField] private float maximumRadius = 18f;
        [SerializeField] private float waveWidth = 0.65f;

        private Vector3 scanOrigin;
        private float scanStartTime;

        private static readonly int ScanOriginId = Shader.PropertyToID("_ScanOrigin");
        private static readonly int ScanRadiusId = Shader.PropertyToID("_ScanRadius");
        private static readonly int ScanWidthId = Shader.PropertyToID("_ScanWidth");
        private static readonly int ScanActiveId = Shader.PropertyToID("_ScanActive");
        private static readonly int ScanTimeId = Shader.PropertyToID("_ScanTime");

        private void OnEnable()
        {
            scanStartTime = GetPreviewTime();
            scanOrigin = GetDefaultScanOrigin();

        }

        private void OnValidate()
        {
            scanDuration = Mathf.Max(0.25f, scanDuration);
            maximumRadius = Mathf.Max(1f, maximumRadius);
            waveWidth = Mathf.Max(0.05f, waveWidth);

        }

        private void Update()
        {
            HandleScanInput();
            UpdateScanTargets();
        }


        private void UpdateScanTargets()
        {
            float time = GetPreviewTime();
            float progress = loopPreview
                ? Mathf.Repeat(time - scanStartTime, scanDuration) / scanDuration
                : Mathf.Clamp01((time - scanStartTime) / scanDuration);

            float easedProgress = 1f - Mathf.Pow(1f - progress, 1.35f);
            float radius = easedProgress * maximumRadius;
            Vector3 origin = scanOriginTransform != null ? scanOriginTransform.position : scanOrigin;


            if (scanMaterial != null)
            {
                ApplyScanValues(scanMaterial, origin, radius, waveWidth, time);
            }

            if (targetRenderers != null)
            {
                foreach (Renderer targetRenderer in targetRenderers)
                {
                    if (targetRenderer == null)
                    {
                        continue;
                    }

                    Material[] materials = targetRenderer.sharedMaterials;

                    foreach (Material material in materials)
                    {
                        if (!IsScanMaterial(material))
                        {
                            continue;
                        }

                        ApplyScanValues(material, origin, radius, waveWidth, time);
                    }
                }
            }
        }

        private static bool IsScanMaterial(Material material)
        {
            return material != null &&
                material.HasProperty(ScanOriginId) &&
                material.HasProperty(ScanRadiusId);
        }

        private static void ApplyScanValues(
            Material material,
            Vector3 origin,
            float radius,
            float width,
            float time)
        {
            if (!IsScanMaterial(material))
            {
                return;
            }

            material.SetVector(ScanOriginId, origin);
            material.SetFloat(ScanRadiusId, radius);

            if (material.HasProperty(ScanWidthId))
            {
                // Width is still controlled by the script so changing Wave Width affects all target renderers.
                material.SetFloat(ScanWidthId, Mathf.Max(0.001f, width));
            }

            if (material.HasProperty(ScanActiveId))
            {
                material.SetFloat(ScanActiveId, 1f);
            }

            if (material.HasProperty(ScanTimeId))
            {
                material.SetFloat(ScanTimeId, time);
            }
        }

        private void HandleScanInput()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            bool pressedSpace = Keyboard.current != null &&
                Keyboard.current.spaceKey.wasPressedThisFrame;

            bool pressedMouse = Mouse.current != null &&
                Mouse.current.leftButton.wasPressedThisFrame;

            if (!pressedSpace && !pressedMouse)
            {
                return;
            }

            scanOrigin = PickScanOriginFromCamera();
            scanStartTime = GetPreviewTime();
        }

        private Vector3 PickScanOriginFromCamera()
        {
            Camera targetCamera = scanCamera != null ? scanCamera : Camera.main;

            if (targetCamera == null || Mouse.current == null)
            {
                return GetDefaultScanOrigin();
            }

            Ray ray = targetCamera.ScreenPointToRay(Mouse.current.position.ReadValue());

            if (Physics.Raycast(ray, out RaycastHit hit, 100f))
            {
                return hit.point;
            }

            return GetDefaultScanOrigin();
        }

        private Vector3 GetDefaultScanOrigin()
        {
            if (scanOriginTransform != null)
            {
                return scanOriginTransform.position;
            }

            return iteration == ScanWaveTestt.Test01
                ? new Vector3(0f, 0.15f, -5f)
                : new Vector3(-8f, 0.15f, -6f);
        }

        private static float GetPreviewTime()
        {
            return Time.time;
        }
    }
}
