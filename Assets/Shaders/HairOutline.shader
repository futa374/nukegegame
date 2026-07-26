Shader "Custom/HairOutline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (0.2, 0.8, 1, 1)
        _OutlineWidth ("Outline Width", Float) = 0.008
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "HairOutline"
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float  _OutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                // ビュー空間でノーマルを計算（UNITY_MATRIX_IT_MV の代替）
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
                float3 normalVS = TransformWorldToViewDir(normalWS, true);

                float4 posCS = TransformObjectToHClip(IN.positionOS.xyz);
                // スクリーン空間で一定幅に膨らませる
                float2 nd = normalize(normalVS.xy);
                posCS.xy += nd * _OutlineWidth * posCS.w;
                OUT.positionHCS = posCS;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return half4(_OutlineColor.rgb, 1.0);
            }
            ENDHLSL
        }
    }
}
