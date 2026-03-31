// BloodSplat.shader
// Paints a soft, organic-looking blood splat onto BloodMap (R channel).
//
// "Organic" irregularity is achieved by offsetting the distance field with
// a low-frequency hash function driven by a per-splat seed. The result is a
// circle that is slightly irregular around its edge, like a real splatter.
//
// Used additively: multiple splats accumulate. BloodMap is never cleared
// unless the designer explicitly resets it.

Shader "Influence/BloodSplat"
{
    Properties
    {
        _Center ("Center UV",           Vector) = (0.5, 0.5, 0, 0)
        _Radius ("Radius (UV space)",   Float)  = 0.05

        // 0–1: drives both opacity and irregularity strength.
        // 0 = faint / regular circle. 1 = strong / very jagged edge.
        _Amount ("Amount",              Float)  = 0.5

        // Random seed per splat. Drives the irregularity pattern so no two
        // splats look identical even at the same position.
        _Seed   ("Seed",                Float)  = 0.0
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // Additive blend: blood accumulates without clearing
        Blend One One

        Pass
        {
            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Center;
            float  _Radius;
            float  _Amount;
            float  _Seed;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            // A simple hash: maps a float2 to a pseudo-random float in [0,1].
            // Used to perturb the distance field to create irregularity.
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // Returns an organic "wobble" value in [-1, 1] at UV p.
            // Higher frequency = more jagged edges. We keep it low (×8) for
            // a coarse, splotchy look rather than high-frequency noise.
            float organicOffset(float2 p, float seed)
            {
                // Shift p by seed so each splat has unique noise
                p += seed * 0.137;
                // Sum two octaves of hash-based value noise
                float n  = hash21(floor(p * 8.0));
                      n += hash21(floor(p * 16.0)) * 0.5;
                return (n / 1.5) * 2.0 - 1.0; // remap to [-1,1]
            }

            half4 frag(v2f i) : SV_Target
            {
                float2 delta = i.uv - _Center.xy;
                float dist   = length(delta);

                // Organic irregularity: shift the effective distance by a
                // noise-derived offset. Stronger at higher _Amount values.
                float irregularity = organicOffset(i.uv, _Seed) * _Radius * 0.4 * _Amount;

                // Perturbed radius: texels on the "bumpy" side of the edge are
                // pulled in/out, creating an uneven splat silhouette.
                float effectiveDist = dist - irregularity;

                // Soft circle: full inside, fades to 0 at edge
                float softness = _Radius * 0.25; // 25% of radius = transition band
                float influence = 1.0 - smoothstep(_Radius - softness, _Radius + softness, effectiveDist);

                // Scale by amount so weaker deaths leave fainter blood
                influence *= _Amount;

                // Write to R channel only; G/B/A are 0 (additive blend handles accumulation)
                return half4(influence, 0, 0, 0);
            }
            ENDHLSL
        }
    }
}
