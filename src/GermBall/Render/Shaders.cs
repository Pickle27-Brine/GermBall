namespace GermBall.Render;

/// <summary>HLSL source, compiled at startup with D3DCompiler (shader model 5.0). Variants are selected with #defines.</summary>
internal static class Shaders
{
    public const string Common = @"
cbuffer Frame : register(b0)
{
    row_major float4x4 ViewProj;
    row_major float4x4 View;
    row_major float4x4 ShadowVP;
    float4 CamPos;        // xyz, w = time
    float4 FogColor;      // rgb, w = fog near
    float4 FogParams;     // x far, y shadow bias, z shadow normal bias, w shadow texel
    float4 HemiSky;       // rgb * intensity
    float4 HemiGround;
    float4 SunDir;        // xyz toward the sun, w = shadows on
    float4 SunColor;      // rgb * intensity
    float4 Screen;        // w, h, 1/w, 1/h
    float4 Proj;          // x tan(fov/2), y aspect
};
cbuffer Object : register(b1)
{
    row_major float4x4 World;
    float4 Color;         // rgb, a opacity
    float4 Emissive;      // rgb, w alpha test
    float4 Specular;      // rgb, w shininess
    float4 Flags;         // x lit (0 basic, 1 lambert, 2 phong), y vertex colours, z textured, w receive shadow
    float4 Flags2;        // x fog, y toon steps (0 off), z point size, w size attenuation
    float4 UvXform;       // repeat xy, offset zw
    float4 U0, U1, U2, U3;
    float4 UArr[8];
};
Texture2D Tex : register(t0);
Texture2D<float> ShadowMap : register(t1);
SamplerState Samp : register(s0);
SamplerComparisonState ShadowSamp : register(s1);

struct VSIn
{
    float3 pos : POSITION; float3 nrm : NORMAL; float2 uv : TEXCOORD0; float3 col : COLOR0;
    float4 i0 : INST0; float4 i1 : INST1; float4 i2 : INST2; float4 i3 : INST3; float4 icol : INST4;
};
struct PSIn
{
    float4 pos : SV_Position; float3 wpos : TEXCOORD0; float3 nrm : TEXCOORD1; float2 uv : TEXCOORD2; float3 col : COLOR0; float viewZ : TEXCOORD3; float3 lpos : TEXCOORD4;
};

float hash12(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }
";

    public const string Main = @"
PSIn VS(VSIn v)
{
    PSIn o;
    float4x4 inst = float4x4(v.i0, v.i1, v.i2, v.i3);
    float4x4 M = mul(inst, World);
    float3 p = v.pos;
#ifdef VILLI
    // villi: sway in the current and part round whoever walks through them (U0.x time, UArr[0..3] pushers xyz + radius)
    float3 base = v.i3.xyz;
    float ph = base.x * 0.21 + base.z * 0.17, yy = p.y * p.y;
    p.x += sin(U0.x * 1.6 + ph) * yy * 0.35; p.z += cos(U0.x * 1.2 + ph * 1.3) * yy * 0.3;
    float3 pw = 0; float pf = 0;
    [unroll] for (int k = 0; k < 4; k++)
    {
        float2 dv = base.xz - UArr[k].xz; float dd = length(dv), rr = UArr[k].w;
        float f = (1.0 - smoothstep(rr * 0.35, rr, dd)) * step(abs(base.y - UArr[k].y), 3.0) * step(0.001, rr);
        pw.xz += dv / max(dd, 0.05) * f; pf = max(pf, f);
    }
    float sxx = dot(v.i0.xyz, v.i0.xyz);
    float3 ld = float3(dot(v.i0.xyz, pw), dot(v.i1.xyz, pw), dot(v.i2.xyz, pw)) / sxx;
    p.xz += ld.xz * yy * 0.9; p.y -= pf * yy * 0.28;
#endif
#ifdef POINTS
    // a camera-facing square per point: v.pos is the quad corner (-0.5..0.5), the instance row 3 holds the point
    float4 wc = mul(float4(v.i3.xyz, 1), World);
    float4 vc = mul(wc, View);
    float px = Flags2.z;
    float half_ = Flags2.w > 0.5 ? px * Proj.x * 0.5 : px * (-vc.z) * Proj.x / Screen.y;
    vc.xy += v.pos.xy * 2.0 * half_;
    o.wpos = wc.xyz;
    o.nrm = float3(0, 1, 0);
    o.uv = v.pos.xy + 0.5; o.uv.y = 1 - o.uv.y;
    o.col = v.icol.rgb;
    o.viewZ = -vc.z;
    o.lpos = 0;
    // the projection matrix rides in UArr[4..7] for points
    float4x4 P = float4x4(UArr[4], UArr[5], UArr[6], UArr[7]);
    o.pos = mul(vc, P);
    return o;
#else
    float4 wp = mul(float4(p, 1), M);
    o.wpos = wp.xyz;
    o.pos = mul(wp, ViewProj);
    o.nrm = normalize(mul(float4(v.nrm, 0), M).xyz);
    o.uv = v.uv * UvXform.xy + UvXform.zw;
    o.col = (Flags.y > 0.5 ? v.col : float3(1, 1, 1)) * v.icol.rgb;
    o.viewZ = -mul(wp, View).z;
    float3 sp = wp.xyz + o.nrm * FogParams.z;
    float4 lp = mul(float4(sp, 1), ShadowVP);
    o.lpos = lp.xyz / lp.w;
    return o;
#endif
}

float ShadowAt(float3 lpos)
{
    if (SunDir.w < 0.5 || Flags.w < 0.5) return 1;
    float2 uv = float2(lpos.x * 0.5 + 0.5, 0.5 - lpos.y * 0.5);
    if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1 || lpos.z > 1) return 1;
    float z = lpos.z + FogParams.y;
    float t = FogParams.w, s = 0;
    [unroll] for (int y = -1; y <= 1; y++) [unroll] for (int x = -1; x <= 1; x++) s += ShadowMap.SampleCmpLevelZero(ShadowSamp, uv + float2(x, y) * t, z);
    return s / 9.0;
}

float4 PS(PSIn i, bool front : SV_IsFrontFace) : SV_Target
{
    float4 base = Color;
    base.rgb *= i.col; // vertex colour (white when unused) times instance colour
    if (Flags.z > 0.5)
    {
        float4 t = Tex.Sample(Samp, i.uv);
        base *= t;
    }
#ifdef POINTS
    if (Flags.z < 0.5) { float2 d = i.uv - 0.5; if (dot(d, d) > 0.25) discard; }
#endif
    if (base.a < Emissive.w) discard;
    float3 col = base.rgb;
    if (Flags.x > 0.5)
    {
        float3 n = normalize(i.nrm); if (!front) n = -n;
        float hemi = 0.5 * n.y + 0.5;
        float3 light = lerp(HemiGround.rgb, HemiSky.rgb, hemi);
        float nl = max(dot(n, SunDir.xyz), 0);
        float sh = ShadowAt(i.lpos);
        light += SunColor.rgb * nl * sh;
        col = base.rgb * light;
        if (Flags.x > 1.5)
        {
            float3 vdir = normalize(CamPos.xyz - i.wpos);
            float3 h = normalize(SunDir.xyz + vdir);
            float spec = pow(max(dot(n, h), 0), max(Specular.w, 1)) * nl * sh;
            col += Specular.rgb * SunColor.rgb * spec * (Specular.w + 2) / 8;
        }
        col += Emissive.rgb;
    }
    if (Flags2.y > 0.5)
    {
        // coarse lighting: brightness snapped to a few steps through a screen-space ordered dither
        float2 bp = floor(i.pos.xy / 2.0);
        float2 b1 = floor(bp * 0.5); float d1 = frac(dot(b1, float2(0.5, b1.y * 0.75)));
        float d0 = frac(dot(bp, float2(0.5, bp.y * 0.75)));
        float dth = d1 * 0.25 + d0;
        float lum = max(max(col.r, col.g), col.b) + 1e-4;
        float q = floor(lum * Flags2.y + dth * 0.9) / Flags2.y;
        col = col / lum * max(q, 0.14);
    }
    if (Flags2.x > 0.5)
    {
        float f = smoothstep(FogColor.w, FogParams.x, i.viewZ);
        col = lerp(col, FogColor.rgb, f);
    }
    return float4(col, base.a);
}

// ---- shadow pass: depth only (alpha-tested surfaces still cut out)
struct SIn { float4 pos : SV_Position; float2 uv : TEXCOORD0; };
SIn VSShadow(VSIn v)
{
    SIn o;
    float4x4 inst = float4x4(v.i0, v.i1, v.i2, v.i3);
    float4 wp = mul(float4(v.pos, 1), mul(inst, World));
    o.pos = mul(wp, ShadowVP);
    o.uv = v.uv * UvXform.xy + UvXform.zw;
    return o;
}
void PSShadow(SIn i)
{
    if (Flags.z > 0.5 && Emissive.w > 0) { if (Tex.Sample(Samp, i.uv).a * Color.a < Emissive.w) discard; }
}
";

    /// <summary>The nebula sky: domain-warped colour clouds, aurora curtains, zenith ripples, twinkling stars, then a few colour steps.
    /// U0 = (time, warp), U1 = horizon, U2 = zenith, U3 = palette phases.</summary>
    public const string Sky = @"
float hash3(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
float noise3(float3 x)
{
    float3 i = floor(x); float3 f = frac(x); f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(lerp(hash3(i), hash3(i + float3(1, 0, 0)), f.x), lerp(hash3(i + float3(0, 1, 0)), hash3(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(hash3(i + float3(0, 0, 1)), hash3(i + float3(1, 0, 1)), f.x), lerp(hash3(i + float3(0, 1, 1)), hash3(i + float3(1, 1, 1)), f.x), f.y), f.z);
}
float fbm3(float3 p) { float s = 0.0; float a = 0.5; [unroll] for (int i = 0; i < 5; i++) { s += a * noise3(p); p = p * 2.03 + float3(1.7, 9.2, 3.1); a *= 0.5; } return s; }
float3 pal(float t) { return 0.5 + 0.5 * cos(6.28318 * (float3(t, t, t) + U3.xyz)); }
struct SkyV { float4 pos : SV_Position; float3 dir : TEXCOORD0; };
SkyV VSSky(VSIn v)
{
    SkyV o; float4 wp = mul(float4(v.pos, 1), World); o.pos = mul(wp, ViewProj); o.pos.z = o.pos.w * 0.99999; o.dir = v.pos; return o;
}
float4 PSSky(SkyV i) : SV_Target
{
    float uTime = U0.x, uWarp = U0.y;
    float3 d = normalize(i.dir);
    float el = d.y;
    float3 col = lerp(U1.rgb, U2.rgb, smoothstep(-0.02, 0.75, el));
    float3 q = d * 2.4 + float3(0.0, uTime * 0.01, 0.0);
    float3 w = float3(fbm3(q + uTime * 0.015), fbm3(q + float3(5.2, 1.3, 2.8) - uTime * 0.012), fbm3(q + float3(2.1, 7.7, 4.4)));
    float n = fbm3(q * 1.3 + w * uWarp);
    float3 neb = pal(n * 1.6 + uTime * 0.025 + el * 0.6);
    float m = smoothstep(0.3, 0.7, n) * smoothstep(0.02, 0.35, el);
    col = lerp(col, neb * 1.2 + 0.08, m * 0.9);
    float az = atan2(d.z, d.x);
    float cur = 0.5 + 0.5 * sin(az * 7.0 + 2.0 * sin(az * 3.0 + uTime * 0.3) + uTime * 0.2);
    float bd = (el - 0.2 - 0.06 * sin(az * 4.0 + uTime * 0.25)) * 8.0;
    col += pal(az * 0.3 + uTime * 0.05 + 0.3) * exp(-bd * bd) * cur * 0.5;
    float rr = acos(clamp(el, -1.0, 1.0));
    col += pal(rr * 2.0 - uTime * 0.08) * smoothstep(0.9, 1.0, sin(rr * 30.0 - uTime * 0.7)) * smoothstep(0.25, 0.7, el) * 0.22;
    float3 sp = floor(d * 190.0); float st = hash3(sp);
    col += step(0.9965, st) * (0.55 + 0.45 * sin(uTime * 3.0 + st * 60.0)) * smoothstep(0.08, 0.45, el) * (1.0 - m * 0.8);
    float dth = hash12(floor(i.pos.xy));
    col = floor(col * 22.0 + dth) / 22.0;
    return float4(col, 1.0);
}
";

    /// <summary>The leviathan's roof: veined, pulsing flesh fading into the haze. U0 = (time), U1 = fog colour.</summary>
    public const string Roof = @"
float h2(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
float n2(float2 p) { float2 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f); return lerp(lerp(h2(i), h2(i + float2(1, 0)), f.x), lerp(h2(i + float2(0, 1)), h2(i + float2(1, 1)), f.x), f.y); }
float fb2(float2 p) { float s = 0.0, a = 0.5; [unroll] for (int i = 0; i < 4; i++) { s += a * n2(p); p *= 2.07; a *= 0.5; } return s; }
struct RoofV { float4 pos : SV_Position; float3 w : TEXCOORD0; float d : TEXCOORD1; };
RoofV VSRoof(VSIn v)
{
    RoofV o; float4 wp = mul(float4(v.pos, 1), World); o.w = wp.xyz; o.d = length(wp.xyz - CamPos.xyz); o.pos = mul(wp, ViewProj); return o;
}
float4 PSRoof(RoofV i) : SV_Target
{
    float uT = U0.x; float3 vW = i.w;
    float2 p = vW.xz * 0.012 + vW.y * 0.01;
    float f = fb2(p + 0.4 * fb2(p * 1.7 + uT * 0.02));
    float3 col = lerp(float3(0.42, 0.06, 0.12), float3(0.85, 0.32, 0.38), f);
    float v = abs(fb2(p * 2.3 + 7.0) - 0.5); col = lerp(col, float3(0.2, 0.05, 0.32), smoothstep(0.035, 0.0, v) * 0.8);
    float pulse = 0.5 + 0.5 * sin(uT * 2.2 - length(vW.xz) * 0.03);
    col += float3(0.25, 0.02, 0.06) * smoothstep(0.02, 0.0, v) * pulse;
    float hh = h2(floor(vW.xz * 0.25));
    float dots = step(0.985, hh) * (0.5 + 0.5 * sin(uT * 3.0 + hh * 40.0));
    col += float3(1.0, 0.85, 0.4) * dots * 0.6;
    col = lerp(col, U1.rgb, smoothstep(60.0, 420.0, i.d) * 0.85);
    float dth = hash12(floor(i.pos.xy));
    col = floor(col * 18.0 + dth) / 18.0;
    return float4(col, 1.0);
}
";

    /// <summary>2D: textured, coloured quads in pixel space.</summary>
    public const string UI = @"
cbuffer UIFrame : register(b0) { float4 ScreenUI; };
Texture2D UITex : register(t0);
SamplerState UISamp : register(s0);
struct UIV { float2 pos : POSITION; float2 uv : TEXCOORD0; float4 col : COLOR0; };
struct UIP { float4 pos : SV_Position; float2 uv : TEXCOORD0; float4 col : COLOR0; };
UIP VSUI(UIV v) { UIP o; o.pos = float4(v.pos.x * ScreenUI.z * 2 - 1, 1 - v.pos.y * ScreenUI.w * 2, 0, 1); o.uv = v.uv; o.col = v.col; return o; }
float4 PSUI(UIP i) : SV_Target { return UITex.Sample(UISamp, i.uv) * i.col; }
";
}
