Shader "RuleGame/Player/SwordWave"
{
    Properties
    {
        _Tint ("Sword edge", Color) = (.8,.9,1,1)
        _Progress ("Lifetime", Range(0,1)) = 0
        _Heavy ("Heavy strike", Range(0,1)) = 0
        _GlowOnly ("Soft glow layer", Float) = 0
        _GlowScale ("Glow UV scale", Vector) = (1,1,0,0)
        _GlowStrength ("Glow strength", Range(0,2)) = .65
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            struct Attributes { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            float4 _Tint;
            float _Progress, _Heavy;
            float _GlowOnly, _GlowStrength;
            float4 _GlowScale;
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                return output;
            }
            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                if (_GlowOnly > .5) uv = (uv - .5) * _GlowScale.xy + .5;
                float y = (uv.y - .5) / .492;
                float roundness = saturate(1 - y * y);
                float taper = pow(roundness, .65);
                // 实心月牙：尖端收细、正面厚实。轮廓外完全透明，不铺矩形能量底色。
                float front = .975 * sqrt(roundness) + .022 * y * roundness;
                float thickness = lerp(.29, .43, _Heavy) * taper * (1 + .2 * y);
                thickness *= 1 - .65 * smoothstep(.32, .95, _Progress);
                float back = front - thickness;
                float aa = max(fwidth(uv.x) * 1.2, .001);
                float blade = smoothstep(back - aa, back + aa, uv.x)
                    * (1 - smoothstep(front - aa, front + aa, uv.x));

                // 两条断开的拖笔和内缘留白，让轮廓有挥斩的笔锋，避免像规则圆环。
                float cutA = 1 - smoothstep(.004, .013, abs(uv.y - (.77 - uv.x * .22)));
                float cutB = 1 - smoothstep(.003, .010, abs(uv.y - (.23 + uv.x * .15)));
                float innerCut = 1 - smoothstep(back, back + thickness * .68, uv.x);
                blade *= 1 - saturate(cutA + cutB) * innerCut;
                float echoFront = front - lerp(.37, .5, _Heavy) * taper;
                float echo = (1 - smoothstep(.009, .024, abs(uv.x - echoFront)))
                    * taper * smoothstep(.12, .5, abs(y)) * .5;
                float speedLine = 1 - smoothstep(.003, .011, abs(uv.y - (.6 - uv.x * .17)));
                speedLine *= smoothstep(.02, .12, uv.x) * (1 - smoothstep(.4, .72, uv.x)) * .6;

                // 命中帧即有完整亮白刀光，随后按挥斩方向快速擦除；连段翻面反转擦除方向。
                float sweep = 1 - smoothstep(1.15 - _Progress * 1.45,
                    1.5 - _Progress * 1.45, uv.y);
                float fade = 1 - smoothstep(.28, 1, _Progress);
                float echoFade = 1 - smoothstep(.08, .65, _Progress);
                float rim = exp(-abs(uv.x - front) * 110) * taper * .1;
                float edge = smoothstep(0, .012, uv.x) * (1 - smoothstep(.985, 1, uv.x))
                    * smoothstep(0, .009, uv.y) * (1 - smoothstep(.991, 1, uv.y));
                float alpha = saturate(blade + rim + (echo + speedLine) * echoFade)
                    * sweep * fade * edge * _Tint.a;
                if (_GlowOnly > .5)
                {
                    // 有限半径柔化轮廓，避免给整张四边形铺透明底色。
                    float distance = max(max(back - uv.x, uv.x - front), 0);
                    float halo = exp(-distance * distance * 650) * pow(roundness, .4);
                    halo *= smoothstep(-.1, .15, uv.x) * (1 - smoothstep(1.08, 1.15, uv.x));
                    return float4(_Tint.rgb * 1.35, halo * sweep * fade * _GlowStrength * .55);
                }
                float3 color = lerp(_Tint.rgb, float3(1, 1, 1), blade * .97);
                // HDR 核心可供已有 Bloom 使用；无后处理时也有独立的柔光层。
                return float4(color * lerp(1, 2.1, blade), alpha);
            }
            ENDCG
        }
    }
}
