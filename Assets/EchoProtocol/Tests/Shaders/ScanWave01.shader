Shader "EchoProtocol/Test/ScanWave01"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.02, 0.025, 0.03, 1)
        _WaveColor("Wave Color", Color) = (0, 0.85, 1, 1)
        _ScanOrigin("Scan Origin", Vector) = (0, 0, 0, 0)
        _ScanRadius("Scan Radius", Float) = 0
        _ScanWidth("Scan Width", Float) = 0.65
        _ScanIntensity("Scan Intensity", Float) = 3
        _ScanActive("Scan Active", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _WaveColor;
                float4 _ScanOrigin;
                float _ScanRadius;
                float _ScanWidth;
                float _ScanIntensity;
                float _ScanActive;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // compare the world-space distance from the scan origin with the current scan radius.
                float distanceFromOrigin = distance(IN.worldPos.xz, _ScanOrigin.xz);
                float distanceToRing = abs(distanceFromOrigin - _ScanRadius);

                // The ring is brightest exactly on the scan radius and fades out over _ScanWidth.
                float ringMask = 1.0 - smoothstep(0.0, max(_ScanWidth, 0.001), distanceToRing);
                ringMask *= _ScanActive;

                half3 color = _BaseColor.rgb;
                color += _WaveColor.rgb * ringMask * _ScanIntensity;

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
