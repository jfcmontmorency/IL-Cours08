// Lit + déplacement de sommets (URP, Unity 6, compatible Forward et Forward+)
// Pensé pour les matériaux PBR d'ambientCG : Color, NormalGL, Roughness, Metalness,
// AmbientOcclusion, Displacement. Pas de tessellation : le déplacement agit sur les
// sommets existants, le maillage doit donc être assez subdivisé.
Shader "Custom/Lit Deplacement"
{
    Properties
    {
        [MainTexture] _BaseMap ("Couleur (_Color)", 2D) = "white" {}
        [MainColor]   _BaseColor ("Teinte", Color) = (1,1,1,1)

        [Normal][NoScaleOffset] _BumpMap ("Normal Map (_NormalGL)", 2D) = "bump" {}
        _BumpScale ("Intensite normal", Range(0, 2)) = 1

        [NoScaleOffset] _RoughnessMap ("Roughness (_Roughness)", 2D) = "white" {}
        _Roughness ("Roughness (multiplicateur)", Range(0, 1)) = 1

        [NoScaleOffset] _MetallicMap ("Metallic (_Metalness)", 2D) = "white" {}
        _Metallic ("Metallic (multiplicateur)", Range(0, 1)) = 0

        [NoScaleOffset] _OcclusionMap ("Occlusion (_AmbientOcclusion)", 2D) = "white" {}
        _OcclusionStrength ("Intensite AO", Range(0, 1)) = 1

        [NoScaleOffset] _HeightMap ("Deplacement (_Displacement)", 2D) = "gray" {}
        _HeightAmount ("Amplitude du deplacement", Range(0, 1)) = 0.05
        _HeightMidpoint ("Niveau neutre", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap);      SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap);
        TEXTURE2D(_RoughnessMap);
        TEXTURE2D(_MetallicMap);
        TEXTURE2D(_OcclusionMap);
        TEXTURE2D(_HeightMap);    SAMPLER(sampler_HeightMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4  _BaseColor;
            half   _BumpScale;
            half   _Roughness;
            half   _Metallic;
            half   _OcclusionStrength;
            float  _HeightAmount;
            float  _HeightMidpoint;
        CBUFFER_END

        // Pousse le sommet le long de sa normale selon la texture de hauteur
        float3 Deplacer(float3 positionOS, float3 normalOS, float2 uv)
        {
            float h = SAMPLE_TEXTURE2D_LOD(_HeightMap, sampler_HeightMap, uv, 0).r;
            return positionOS + normalOS * (h - _HeightMidpoint) * _HeightAmount;
        }
        ENDHLSL

        // ---------------------------------------------------------------- Rendu
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float4 tangentWS  : TEXCOORD3;
                half   fogFactor  : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                float3 positionOS = Deplacer(v.positionOS.xyz, v.normalOS, o.uv);

                VertexPositionInputs pos = GetVertexPositionInputs(positionOS);
                VertexNormalInputs   nrm = GetVertexNormalInputs(v.normalOS, v.tangentOS);

                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS   = nrm.normalWS;
                o.tangentWS  = float4(nrm.tangentWS, v.tangentOS.w * GetOddNegativeScale());
                o.fogFactor  = ComputeFogFactor(pos.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                half4 albedo    = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                half3 normalTS  = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BaseMap, i.uv), _BumpScale);
                half  roughness = SAMPLE_TEXTURE2D(_RoughnessMap, sampler_BaseMap, i.uv).r * _Roughness;
                half  metallic  = SAMPLE_TEXTURE2D(_MetallicMap,  sampler_BaseMap, i.uv).r * _Metallic;
                half  occlusion = LerpWhiteTo(SAMPLE_TEXTURE2D(_OcclusionMap, sampler_BaseMap, i.uv).r, _OcclusionStrength);

                float3 bitangent = i.tangentWS.w * cross(i.normalWS, i.tangentWS.xyz);
                half3x3 tbn = half3x3(i.tangentWS.xyz, bitangent, i.normalWS);

                InputData d = (InputData)0;
                d.positionWS              = i.positionWS;
                d.positionCS              = i.positionCS;
                d.normalWS                = NormalizeNormalPerPixel(TransformTangentToWorld(normalTS, tbn));
                d.viewDirectionWS         = GetWorldSpaceNormalizeViewDir(i.positionWS);
                d.shadowCoord             = TransformWorldToShadowCoord(i.positionWS);
                d.fogCoord                = i.fogFactor;
                d.bakedGI                 = SampleSH(d.normalWS);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.shadowMask              = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo     = albedo.rgb;
                s.alpha      = 1;
                s.metallic   = metallic;
                s.smoothness = 1 - roughness;   // ambientCG fournit la rugosité : on l'inverse
                s.normalTS   = normalTS;
                s.occlusion  = occlusion;

                half4 c = UniversalFragmentPBR(d, s);
                c.rgb = MixFog(c.rgb, d.fogCoord);
                c.a = 1;
                return c;
            }
            ENDHLSL
        }

        // ---------------------------------------------------------------- Ombres
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vertOmbre
            #pragma fragment fragVide
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 vertOmbre(Attributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                float3 positionOS = Deplacer(v.positionOS.xyz, v.normalOS, TRANSFORM_TEX(v.uv, _BaseMap));
                float3 positionWS = TransformObjectToWorld(positionOS);
                float3 normalWS   = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir)));
            }

            half4 fragVide() : SV_Target { return 0; }
            ENDHLSL
        }

        // ---------------------------------------------------------------- Profondeur
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vertProf
            #pragma fragment fragProf
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 vertProf(Attributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                return TransformObjectToHClip(Deplacer(v.positionOS.xyz, v.normalOS, TRANSFORM_TEX(v.uv, _BaseMap)));
            }

            half fragProf(float4 positionCS : SV_POSITION) : SV_Target { return positionCS.z; }
            ENDHLSL
        }

        // ---------------------------------------------------------------- Profondeur + normales (SSAO, etc.)
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vertDN
            #pragma fragment fragDN
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
            };

            Varyings vertDN(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Varyings o;
                o.positionCS = TransformObjectToHClip(Deplacer(v.positionOS.xyz, v.normalOS, TRANSFORM_TEX(v.uv, _BaseMap)));
                o.normalWS   = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            void fragDN(Varyings i
                , out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                float3 n = normalize(i.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct = saturate(PackNormalOctQuadEncode(n) * 0.5 + 0.5);
                    outNormalWS = half4(PackFloat2To888(oct), 0);
                #else
                    outNormalWS = half4(n, 0);
                #endif
                #ifdef _WRITE_RENDERING_LAYERS
                    outRenderingLayers = EncodeMeshRenderingLayer();
                #endif
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
