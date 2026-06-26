namespace Assets.EchoProtocol.Scripts.Core
{
    using UnityEngine;
    
    public class ScanWaveComputeController : MonoBehaviour
    {
        [Header("Compute Shader")]
        // Compute shader that fills the scan texture.
        [SerializeField] private ComputeShader scanComputeShader;

        [Header("Timing")]
        [SerializeField] private float scanDuration = 2.5f;
        [SerializeField] private float maximumDistance = 35f;

        [Header("Wave")]
        [SerializeField] private float waveWidth = 1.25f;
        [SerializeField] private float trailLength = 8f;
        [SerializeField] private float noiseScale = 9f;
        [SerializeField] private float noiseAmount = 0.6f;

        [Header("Apperance")]
        [SerializeField, ColorUsage(true, true)]
        private Color edgeColor = Color.cyan;
        
        [SerializeField, ColorUsage(true, true)]
        private Color trailColor = Color.blue;

        [SerializeField] private float edgeIntensity = 2.5f;
        [SerializeField] private float trailIntensity = 0.6f;

        [Header("Render Texture")]
        // The compute shader works in thread groups of 8x8, so CreateResources rounds these to multiples of 8.
        [SerializeField] private int textureWidth = 512;
        [SerializeField] private int textureHeight = 128;

        private RenderTexture scanTexture;
        private int kernel;

        private bool scanPlaying;
        private float elapsedTime;

        private Vector3 scanOrigin;

        private static readonly int ScanResultID = Shader.PropertyToID("_ScanWaveResult");
        private static readonly int TextureSizeID = Shader.PropertyToID("_TextureSize");
        private static readonly int WaveRadiusID = Shader.PropertyToID("_WaveRadius");
        private static readonly int WaveWidthID = Shader.PropertyToID("_WaveWidth");
        private static readonly int TrailLengthID = Shader.PropertyToID("_TrailLength");
        private static readonly int NoiseScaleID = Shader.PropertyToID("_NoiseScale");
        private static readonly int NoiseAmountID = Shader.PropertyToID("_NoiseAmount");
        private static readonly int ComputeTimeID = Shader.PropertyToID("_ScanTime");
        
        private static readonly int TextureId = Shader.PropertyToID("_ScanWaveTexture");
        private static readonly int OriginId = Shader.PropertyToID("_ScanWaveOrigin");
        private static readonly int DistanceId = Shader.PropertyToID("_ScanWaveMaxDistance");
        private static readonly int ActiveId = Shader.PropertyToID("_ScanWaveActive");
        private static readonly int EdgeColorId = Shader.PropertyToID("_ScanWaveEdgeColor");
        private static readonly int TrailColorId = Shader.PropertyToID("_ScanWaveTrailColor");
        private static readonly int EdgeIntensityId = Shader.PropertyToID("_ScanWaveEdgeIntensity");
        private static readonly int TrailIntensityId = Shader.PropertyToID("_ScanWaveTrailIntensity");
        private static readonly int TimeId = Shader.PropertyToID("_ScanWaveTime");

        private void OnEnable()
        {
            CreateResources();
        }

        private void Update()
        {
            if(!scanPlaying)
                return;
            
            elapsedTime += Time.deltaTime;

            float progress = Mathf.Clamp01(elapsedTime / scanDuration);
            float easedProgress = 1f -Mathf.Pow(1f - progress, 1.35f);
            
            Dispatch(easedProgress);

            if(progress >= 1f)
            {
                scanPlaying = false;
                Shader.SetGlobalFloat(ActiveId, 0f);
            }
        }

        public void StartScan(Vector3 worldPosition)
        {
            if(scanTexture == null) 
                CreateResources();
            

            scanOrigin = worldPosition;
            elapsedTime = 0f;
            scanPlaying = true;

            Shader.SetGlobalVector(OriginId, scanOrigin);
            Shader.SetGlobalFloat(ActiveId, 1f);

            Dispatch(0f);
        }

        private void CreateResources()
        {
            if(scanTexture != null)
                return;
            
            kernel = scanComputeShader.FindKernel("ScanWave");

            textureWidth = Mathf.CeilToInt(textureWidth / 8f) * 8;
            textureHeight = Mathf.CeilToInt(textureHeight / 8f) * 8;

            scanTexture = new RenderTexture
            (
                textureWidth,
                textureHeight,
                0,
                RenderTextureFormat.ARGBHalf
            )
            {
                name = "Scan Wave Profile",
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapModeU = TextureWrapMode.Clamp,
                wrapModeV = TextureWrapMode.Repeat,
            };

            scanTexture.Create();

            scanComputeShader.SetTexture
            (
                kernel,
                ScanResultID,
                scanTexture
            );

            scanComputeShader.SetInts
            (
                TextureSizeID,
                textureWidth,
                textureHeight
            );

            Shader.SetGlobalTexture
            (
                TextureId,
                scanTexture
            );
        }


        private void Dispatch(float progress)
        {
            float inverseDistance = 1f / Mathf.Max(maximumDistance, 0.001f);

            scanComputeShader.SetFloat(WaveRadiusID, progress);
            scanComputeShader.SetFloat(WaveWidthID, waveWidth * inverseDistance);
            scanComputeShader.SetFloat(TrailLengthID, trailLength * inverseDistance);
            scanComputeShader.SetFloat(NoiseScaleID, noiseScale);
            scanComputeShader.SetFloat(NoiseAmountID, noiseAmount);
            scanComputeShader.SetFloat(ComputeTimeID, Time.time);

            scanComputeShader.Dispatch
            (
                kernel,
                textureWidth / 8,
                textureHeight / 8,
                1
            );

            Shader.SetGlobalTexture(TextureId, scanTexture);
            Shader.SetGlobalVector(OriginId, scanOrigin);
            Shader.SetGlobalFloat(DistanceId, maximumDistance);
            Shader.SetGlobalFloat(ActiveId, 1f);
            Shader.SetGlobalColor(EdgeColorId, edgeColor);
            Shader.SetGlobalColor(TrailColorId, trailColor);
            Shader.SetGlobalFloat(EdgeIntensityId, edgeIntensity);
            Shader.SetGlobalFloat(TrailIntensityId, trailIntensity);
            Shader.SetGlobalFloat(TimeId, Time.time);
        }

        private void OnDestroy()
        {
            if (scanTexture == null)
                return;

            scanTexture.Release();
            Destroy(scanTexture);
        }
    }
}
