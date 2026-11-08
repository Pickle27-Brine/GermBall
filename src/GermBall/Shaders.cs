namespace GermBall.Win;

/// <summary>HLSL for the renderer. Matrices are row-major with row vectors, the same as System.Numerics.
/// The lighting, grid and fog match SoftRenderer.Shade in the test project.</summary>
internal static class Shaders
{
    public const string Main = @"
cbuffer Frame : register(b0)
{
    row_major float4x4 ViewProj;
    float4 CamPos;
    float4 SunDir;
    float4 Sky;
    float4 Ground;
    float4 Fog;
    float4 FogParams;   // x start, y end
};

struct VSIn
{
    float3 pos : POSITION;
    float3 nrm : NORMAL;
    float3 col : COLOR0;
    float4 r0 : INST0;
    float4 r1 : INST1;
    float4 r2 : INST2;
    float4 r3 : INST3;
    float4 tint : INST4;
    float4 prm : INST5;   // x grid, y unlit
};

struct PSIn
{
    float4 pos : SV_Position;
    float3 wp : TEXCOORD0;
    float3 n : TEXCOORD1;
    float3 col : COLOR0;
    float4 tint : TEXCOORD2;
    float4 prm : TEXCOORD3;
};

PSIn VS(VSIn v)
{
    float4x4 W = float4x4(v.r0, v.r1, v.r2, v.r3);
    float4 wp = mul(float4(v.pos, 1), W);
    // normals: inverse-transpose for scale-then-rotate matrices (divide by the squared axis scales)
    float3 s2 = float3(dot(v.r0.xyz, v.r0.xyz), dot(v.r1.xyz, v.r1.xyz), dot(v.r2.xyz, v.r2.xyz));
    float3 n = mul(v.nrm / max(s2, 1e-8), (float3x3)W);
    PSIn o;
    o.pos = mul(wp, ViewProj);
    o.wp = wp.xyz;
    o.n = n;
    o.col = v.col;
    o.tint = v.tint;
    o.prm = v.prm;
    return o;
}

float GridLine(float v, float period, float width)
{
    float d = abs(frac(v / period + 0.5) - 0.5) * period;
    float fw = fwidth(v);
    return 1 - smoothstep(width, width + fw * 1.5, d);
}

float GridShade(float3 wp, float3 n)
{
    float3 an = abs(n);
    float2 uv = (an.y >= an.x && an.y >= an.z) ? wp.xz : (an.x >= an.z ? wp.zy : wp.xy);
    float major = max(GridLine(uv.x, 5, 0.04), GridLine(uv.y, 5, 0.04));
    float minor = max(GridLine(uv.x, 1, 0.02), GridLine(uv.y, 1, 0.02));
    return 1 - max(major * 0.28, minor * 0.14);
}

float4 PS(PSIn i) : SV_Target
{
    float3 n = normalize(i.n);
    float3 albedo = i.tint.rgb * i.col;
    // no branches: the grid uses screen-space derivatives, which FXC won't allow inside flow control
    float grid = lerp(1, GridShade(i.wp, n), i.prm.x);
    float3 hemi = lerp(Ground.rgb, Sky.rgb, n.y * 0.5 + 0.5);
    float sun = max(0, dot(n, -SunDir.xyz));
    float3 lit = albedo * grid * (hemi * 0.75 + float3(1.0, 0.96, 0.88) * sun * 0.75);
    float3 c = lerp(lit, albedo, step(0.5, i.prm.y));
    float dist = distance(i.wp, CamPos.xyz);
    float fog = saturate((dist - FogParams.x) / (FogParams.y - FogParams.x));
    c = lerp(c, Fog.rgb, fog);
    return float4(c, i.tint.a);
}
";

    public const string Flat2D = @"
cbuffer Screen : register(b0) { float4 ScreenSize; };

struct VSIn2 { float2 pos : POSITION; float4 col : COLOR0; };
struct PSIn2 { float4 pos : SV_Position; float4 col : COLOR0; };

PSIn2 VS2D(VSIn2 v)
{
    PSIn2 o;
    o.pos = float4(v.pos.x / ScreenSize.x * 2 - 1, 1 - v.pos.y / ScreenSize.y * 2, 0, 1);
    o.col = v.col;
    return o;
}

float4 PS2D(PSIn2 i) : SV_Target { return i.col; }
";
}
