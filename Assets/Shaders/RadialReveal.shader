Shader "Custom/RadialReveal"
{
    Properties
    {
        _BaseMap ("Base Texture", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _RevealCenter ("Reveal Center (World)", Vector) = (0, 0, 0, 0)
        _RevealRadius ("Reveal Radius", Float) = 0
        _RevealSoftness ("Reveal Softness", Float) = 0.5
        _EdgeColor ("Edge Glow Color", Color) = (1, 1, 1, 1)
        _EdgeWidth ("Edge Width", Float) = 0.3
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "RadialReveal"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float4 _RevealCenter;
                float _RevealRadius;
                float _RevealSoftness;
                half4 _EdgeColor;
                float _EdgeWidth;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 delta = input.positionWS.xz - _RevealCenter.xz;
                float dist = length(delta);

                // Hard cutoff with soft edge via smoothstep.
                float softness = max(_RevealSoftness, 0.001);
                float revealAlpha = 1.0 - smoothstep(_RevealRadius - softness, _RevealRadius, dist);

                // Clip fully outside pixels.
                clip(revealAlpha - 0.001);

                // Base color.
                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half4 baseOutput = texColor * _BaseColor;

                // Edge glow: blend toward _EdgeColor near the boundary.
                float edgeStart = _RevealRadius - _EdgeWidth;
                float edgeFactor = smoothstep(edgeStart - softness * 0.5, _RevealRadius - softness * 0.25, dist);
                edgeFactor = saturate(edgeFactor);

                // Mix edge glow with base (HDR-friendly — EdgeColor can exceed 1 for bloom).
                half3 finalColor = lerp(baseOutput.rgb, _EdgeColor.rgb * _EdgeColor.a, edgeFactor);
                half finalAlpha = baseOutput.a * revealAlpha;

                return half4(finalColor, finalAlpha);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
