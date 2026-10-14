Shader "RuleGame/Enemy/VfxSprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Authored effect", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Accent ("Palette", Color) = (1,0.65,0.25,1)
        _Recolor ("Palette blend", Range(0,1)) = 0.85
        [Toggle(_VFX_SPRITE)] _Sprite ("Sprite renderer", Float) = 1
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "CanUseSpriteAtlas"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        Pass
        {
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            #pragma multi_compile_instancing
            #pragma shader_feature_local _VFX_SPRITE
            struct Attributes { COMMON_2D_INPUTS half4 color:COLOR; };
            struct Varyings { COMMON_2D_OUTPUTS half4 color:COLOR; };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _Accent;
                half _Recolor;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                #if defined(_VFX_SPRITE)
                    SetUpSpriteInstanceProperties();
                    input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
                #endif
                output.positionCS=TransformObjectToHClip(input.positionOS);
                output.uv=input.uv; output.color=input.color*_Color;
                #if defined(_VFX_SPRITE)
                    output.color *= unity_SpriteColor;
                #endif
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                half4 color=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.uv);
                // 保留作者的帧动画与透明度，只统一各 Boss 的色相；高亮核心仍保留近白色。
                half light=dot(color.rgb,half3(.2126,.7152,.0722));
                half3 palette=lerp(_Accent.rgb*.6h,_Accent.rgb,saturate(light*1.8h));
                palette=lerp(palette,half3(1,1,.98),smoothstep(.85h,1.h,light));
                color.rgb=lerp(color.rgb,palette,_Recolor);
                return color*input.color;
            }
            ENDHLSL
        }
    }
}
