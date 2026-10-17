Shader "RuleGame/Enemy/HitFeedback"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Mode ("Flash / Impact", Float) = 0
        _Accent ("Accent", Color) = (1,.58,.18,1)
        _Progress ("Progress", Range(0,1)) = 0
        _Strength ("Strength", Range(0,1)) = 1
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
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            struct Attributes { COMMON_2D_INPUTS half4 color : COLOR; };
            struct Varyings { COMMON_2D_OUTPUTS half4 color : COLOR; };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;
            CBUFFER_START(UnityPerMaterial)
                half4 _Accent;
                float _Mode, _Progress, _Strength;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                output.color = input.color * unity_SpriteColor;
                return output;
            }

            float Ray(float2 p, float angle, float length, float width, float travel)
            {
                float2 axis = float2(cos(angle), sin(angle));
                float along = dot(p, axis) - travel;
                float across = abs(dot(p, float2(-axis.y, axis.x)));
                float taper = saturate(1 - abs(along) / length);
                return (1 - smoothstep(width * taper, width * taper + .009, across))
                    * smoothstep(0, .025, taper);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float t = saturate(_Progress);
                if (_Mode < .5)
                {
                    half alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                    float2 d = _MainTex_TexelSize.xy * 2;
                    half adjacent = min(
                        min(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + float2(d.x, 0)).a,
                            SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv - float2(d.x, 0)).a),
                        min(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + float2(0, d.y)).a,
                            SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv - float2(0, d.y)).a));
                    float edge = saturate((alpha - adjacent) * 3);
                    // 先明确保持白色，再淡出；旧曲线的纯白阶段只有约一帧，实战难以辨认。
                    float white = 1 - smoothstep(.55, .9, t);
                    float opacity = max(white * .94, edge * (1 - t) * .85);
                    return half4(lerp(_Accent.rgb, half3(1, .98, .88), white), alpha * opacity * input.color.a);
                }

                float2 p = input.uv * 2 - 1;
                float radius = length(p);
                float expansion = sqrt(t);
                float fade = pow(1 - t, 1.8);
                float ringRadius = lerp(.14, .82, expansion);
                float arc = smoothstep(-.75, .15, p.x / max(radius, .001));
                float ring = (1 - smoothstep(.012, .035, abs(radius - ringRadius)))
                    * arc * fade * .65;
                float streaks = 0;
                // 固定采样的方向扇，既可识别受击方向，又不会由随机噪声产生整屏闪烁。
                [unroll] for (int i = 0; i < 7; i++)
                {
                    float angle = -1.85 + i * .62;
                    float speed = .58 + frac(i * .618) * .25;
                    float variation = .65 + frac(i * .381) * .6;
                    streaks += Ray(p, angle, lerp(.16, .055, t) * variation,
                        lerp(.019, .005, t), .12 + expansion * speed);
                }
                float cross = max(Ray(p, .65, .43, .022, 0), Ray(p, 2.22, .29, .016, 0))
                    * (1 - smoothstep(.05, .3, t));
                float core = (1 - smoothstep(.02, .15, radius)) * (1 - smoothstep(.02, .22, t));
                float halo = exp2(-radius * radius * 55) * fade * .24 * (1 - smoothstep(.05, .5, t));
                float light = saturate(streaks * fade + cross + core);
                float alpha = saturate(light + ring + halo) * (1 - smoothstep(.94, 1, radius));
                half3 color = lerp(_Accent.rgb, half3(1, .98, .86), saturate(light * 1.2));
                return half4(color, alpha * input.color.a * lerp(.75, 1, _Strength));
            }
            ENDHLSL
        }
    }
}
