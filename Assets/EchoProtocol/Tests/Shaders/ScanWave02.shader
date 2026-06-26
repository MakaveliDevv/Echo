Shader "EchoProtocol/Test/ScanWave02"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.012, 0.015, 0.018, 1)
        _EdgeColor("Edge Color", Color) = (1, 0.42, 0.04, 1)
        _TrailColor("Trail Color", Color) = (0, 0.9, 1, 1)
        _ScanOrigin("Scan Origin", Vector) = (0, 0, 0, 0)
        _ScanRadius("Scan Radius", Float) = 0
        _ScanWidth("Scan Width", Float) = 0.55
        _TrailLength("Trail Length", Float) = 4.5
        _NoiseScale("Noise Scale", Float) = 0.75
        _NoiseStrength("Noise Strength", Float) = 0.45
        _ScanIntensity("Scan Intensity", Float) = 3.5
        _ScanActive("Scan Active", Float) = 1
        _ScanTime("Scan Time", Float) = 0
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
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 viewDir : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _EdgeColor;
                half4 _TrailColor;
                float4 _ScanOrigin;
                float _ScanRadius;
                float _ScanWidth;
                float _TrailLength;
                float _NoiseScale;
                float _NoiseStrength;
                float _ScanIntensity;
                float _ScanActive;
                float _ScanTime;
            CBUFFER_END

            float hash21(float2 value)
            {
                return frac(sin(dot(value, float2(127.1, 311.7))) * 43758.5453);
            }

            float valueNoise(float2 uv)
            {
                float2 cell = floor(uv);
                float2 local = frac(uv);
                local = local * local * (3.0 - 2.0 * local);

                float a = hash21(cell);
                float b = hash21(cell + float2(1.0, 0.0));
                float c = hash21(cell + float2(0.0, 1.0));
                float d = hash21(cell + float2(1.0, 1.0));

                return lerp(lerp(a, b, local.x), lerp(c, d, local.x), local.y);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.worldNormal = normalize(TransformObjectToWorldNormal(IN.normalOS));
                OUT.viewDir = normalize(GetWorldSpaceViewDir(OUT.worldPos));
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 fromOrigin = IN.worldPos.xz - _ScanOrigin.xz;
                float rawDistance = length(fromOrigin);

                // This makes the scan edge feel unstable/digital instead of perfectly mathematical.
                float noise = valueNoise(IN.worldPos.xz * _NoiseScale + _ScanTime * 0.35);
                float noisyDistance = rawDistance + (noise - 0.5) * _NoiseStrength;

                float distanceToEdge = abs(noisyDistance - _ScanRadius);
                float edgeMask = 1.0 - smoothstep(0.0, max(_ScanWidth, 0.001), distanceToEdge);

                // Trail exists behind the leading edge. It fades as it gets farther from that edge.
                float distanceBehindEdge = _ScanRadius - noisyDistance;
                float trailMask = saturate(distanceBehindEdge / max(_TrailLength, 0.001));
                trailMask *= step(0.0, distanceBehindEdge);
                trailMask *= 1.0 - smoothstep(_TrailLength * 0.75, _TrailLength, distanceBehindEdge);

                // Vertical scanlines help sell the holographic scanner style on walls and pillars.
                float scanlines = sin((IN.worldPos.y * 18.0) - (_ScanTime * 10.0));
                scanlines = pow(saturate(scanlines * 0.5 + 0.5), 3.0);

                // Angular bursts create directional glitches around the expanding circle.
                float angle = atan2(fromOrigin.y, fromOrigin.x);
                float angularBurst = sin(angle * 18.0 + _ScanTime * 6.0);
                angularBurst = pow(saturate(angularBurst * 0.5 + 0.5), 5.0);

                // Rim glow makes vertical objects catch the scan better when viewed from an angle.
                float rim = 1.0 - saturate(dot(normalize(IN.viewDir), normalize(IN.worldNormal)));
                rim = pow(rim, 3.0);

                float active = _ScanActive;
                float edge = edgeMask * active;
                float trail = trailMask * active;

                half3 color = _BaseColor.rgb;
                color += _EdgeColor.rgb * edge * _ScanIntensity;
                color += _TrailColor.rgb * trail * (0.45 + scanlines * 0.75);
                color += _EdgeColor.rgb * edge * angularBurst * 1.4;
                color += _TrailColor.rgb * rim * trail * 0.65;

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
