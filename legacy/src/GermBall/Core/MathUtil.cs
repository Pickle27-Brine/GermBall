using System;
using System.Runtime.CompilerServices;

namespace GermBall.Core;

/// <summary>Mutable 3-vector with reference semantics (mirrors the original {x, y, z} objects, so hot paths reuse temporaries).</summary>
public sealed class V3
{
    public float x, y, z;
    public V3() { }
    public V3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    public V3 Set(float X, float Y, float Z) { x = X; y = Y; z = Z; return this; }
    public V3 Copy(V3 o) { x = o.x; y = o.y; z = o.z; return this; }
    public V3 Clone() => new V3(x, y, z);
    public float Len => MathF.Sqrt(x * x + y * y + z * z);
    public override string ToString() => $"({x:0.###}, {y:0.###}, {z:0.###})";
}

/// <summary>Mutable quaternion (x, y, z, w).</summary>
public sealed class Q4
{
    public float x, y, z, w = 1;
    public Q4() { }
    public Q4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
    public Q4 Set(float X, float Y, float Z, float W) { x = X; y = Y; z = Z; w = W; return this; }
    public Q4 Copy(Q4 o) { x = o.x; y = o.y; z = o.z; w = o.w; return this; }
    public Q4 Clone() => new Q4(x, y, z, w);
    public Q4 Identity() { x = y = z = 0; w = 1; return this; }
}

/// <summary>Seeded generator: the same mulberry32 stream as the original so seeds produce the same worlds.</summary>
public sealed class Mulberry32
{
    uint a;
    public Mulberry32(int seed) { a = (uint)seed; }
    public double Next()
    {
        a += 0x6D2B79F5;
        uint t = a;
        t = (uint)((int)(t ^ (t >> 15)) * (int)(1 | t));
        t = (t + (uint)((int)(t ^ (t >> 7)) * (int)(61 | t))) ^ t;
        return (t ^ (t >> 14)) / 4294967296.0;
    }
    public float F() => (float)Next();
}

public static class M
{
    public const float PI = MathF.PI, TAU = MathF.PI * 2;
    /// <summary>Unseeded randomness (the original's Math.random()).</summary>
    public static readonly Random Rnd = new();
    public static float R() => (float)Rnd.NextDouble();

    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float Lerp(float a, float b, float t) => a + (b - a) * t;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float Smooth(float a, float b, float x) { float t = Clamp((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t); }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float SmoothU(float x) => x * x * (3 - 2 * x);
    public static float Gauss(float dx, float dz, float s) => MathF.Exp(-(dx * dx + dz * dz) / (2 * s * s));
    public static float Hypot(float a, float b) => MathF.Sqrt(a * a + b * b);
    public static float Hypot(float a, float b, float c) => MathF.Sqrt(a * a + b * b + c * c);
    public static float Hypot(float a, float b, float c, float d) => MathF.Sqrt(a * a + b * b + c * c + d * d);
    public static float Sign(float v) => v > 0 ? 1 : v < 0 ? -1 : 0;
    /// <summary>JS-style modulo for floats (sign of dividend).</summary>
    public static float Mod(float a, float b) => a % b;

    public static float CR(float p0, float p1, float p2, float p3, float t) => 0.5f * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t);
    public static float CRD(float p0, float p1, float p2, float p3, float t) => 0.5f * ((-p0 + p2) + 2 * (2 * p0 - 5 * p1 + 4 * p2 - p3) * t + 3 * (-p0 + 3 * p1 - 3 * p2 + p3) * t * t);
    public static float WrapAngle(float d) { while (d > PI) d -= TAU; while (d < -PI) d += TAU; return d; }
    public static float TurnAngle(float a, float b, float k) => a + WrapAngle(b - a) * MathF.Min(1, k);

    // ---- noise (bit-exact with the original integer hash)
    public static float Hash2(int i, int j)
    {
        int h = unchecked(i * 374761393 + j * 668265263);
        h = unchecked((h ^ (int)((uint)h >> 13)) * 1274126177);
        h ^= (int)((uint)h >> 16);
        return (uint)h / 4294967296f;
    }
    static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);
    public static float VNoise(float x, float z)
    {
        int i = (int)MathF.Floor(x), j = (int)MathF.Floor(z);
        float u = Fade(x - i), v = Fade(z - j);
        float a = Hash2(i, j), b = Hash2(i + 1, j), c = Hash2(i, j + 1), d = Hash2(i + 1, j + 1);
        return (a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v) * 2 - 1;
    }
    public static float Fbm(float x, float z, int oct = 3)
    {
        float s = 0, a = 0.5f, f = 1;
        for (int k = 0; k < oct; k++) { s += VNoise(x * f + k * 17.3f, z * f - k * 9.1f) * a; f *= 2.03f; a *= 0.5f; }
        return s;
    }

    /// <summary>1D smooth curve through keys (x ascending), Hermite with finite-difference tangents.</summary>
    public static float Curve1(float[][] keys, float x)
    {
        if (x <= keys[0][0]) return keys[0][1];
        int n = keys.Length; if (x >= keys[n - 1][0]) return keys[n - 1][1];
        int i = 0; while (keys[i + 1][0] < x) i++;
        float x0 = keys[i][0], y0 = keys[i][1], x1 = keys[i + 1][0], y1 = keys[i + 1][1], h = x1 - x0, t = (x - x0) / h;
        float m0 = i > 0 ? (y1 - keys[i - 1][1]) / (x1 - keys[i - 1][0]) * h : (y1 - y0);
        float m1 = i < n - 2 ? (keys[i + 2][1] - y0) / (keys[i + 2][0] - x0) * h : (y1 - y0);
        float t2 = t * t, t3 = t2 * t;
        return (2 * t3 - 3 * t2 + 1) * y0 + (t3 - 2 * t2 + t) * m0 + (-2 * t3 + 3 * t2) * y1 + (t3 - t2) * m1;
    }

    // ---- vectors and quaternions (allocation-free, write into o)
    public static float Dot(V3 a, V3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
    public static V3 Cross(V3 a, V3 b, V3 o) { float x = a.y * b.z - a.z * b.y, y = a.z * b.x - a.x * b.z, z = a.x * b.y - a.y * b.x; o.x = x; o.y = y; o.z = z; return o; }
    public static V3 QRot(Q4 q, V3 v, V3 o, bool inv = false) => QRot(q, v.x, v.y, v.z, o, inv);
    public static V3 QRot(Q4 q, float x, float y, float z, V3 o, bool inv = false)
    {
        float qx = inv ? -q.x : q.x, qy = inv ? -q.y : q.y, qz = inv ? -q.z : q.z, qw = q.w;
        float ix = qw * x + qy * z - qz * y, iy = qw * y + qz * x - qx * z, iz = qw * z + qx * y - qy * x, iw = -qx * x - qy * y - qz * z;
        o.x = ix * qw + iw * -qx + iy * -qz - iz * -qy; o.y = iy * qw + iw * -qy + iz * -qx - ix * -qz; o.z = iz * qw + iw * -qz + ix * -qy - iy * -qx; return o;
    }
    public static Q4 QMul(Q4 a, Q4 b, Q4 o)
    {
        float x = a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y, y = a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x, z = a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w, w = a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z;
        o.x = x; o.y = y; o.z = z; o.w = w; return o;
    }
    public static void QNorm(Q4 q) { float l = Hypot(q.x, q.y, q.z, q.w); if (l == 0) l = 1; q.x /= l; q.y /= l; q.z /= l; q.w /= l; }
    public static void QIntegrate(Q4 q, float wx, float wy, float wz, float dt)
    {
        float hx = wx * dt * 0.5f, hy = wy * dt * 0.5f, hz = wz * dt * 0.5f, x = q.x, y = q.y, z = q.z, qw = q.w;
        q.x += hx * qw + hy * z - hz * y; q.y += hy * qw + hz * x - hx * z; q.z += hz * qw + hx * y - hy * x; q.w += -hx * x - hy * y - hz * z; QNorm(q);
    }
    public static Q4 QNlerp(Q4 a, Q4 b, float t, Q4 o)
    {
        float s = (a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w) < 0 ? -1 : 1;
        o.x = Lerp(a.x, b.x * s, t); o.y = Lerp(a.y, b.y * s, t); o.z = Lerp(a.z, b.z * s, t); o.w = Lerp(a.w, b.w * s, t); QNorm(o); return o;
    }
    /// <summary>Shortest rotation taking +Y onto (x, y, z).</summary>
    public static Q4 QFromY(float x, float y, float z, Q4 o)
    {
        float l = Hypot(x, y, z); if (l == 0) l = 1; x /= l; y /= l; z /= l;
        if (y < -0.9999f) { o.x = 1; o.y = 0; o.z = 0; o.w = 0; return o; }
        o.x = z; o.y = 0; o.z = -x; o.w = 1 + y; float n = Hypot(o.x, o.z, o.w); o.x /= n; o.z /= n; o.w /= n; return o;
    }
    public static Q4 QAxis(float ax, float ay, float az, float a, Q4 o) { float s = MathF.Sin(a / 2); o.x = ax * s; o.y = ay * s; o.z = az * s; o.w = MathF.Cos(a / 2); return o; }
    /// <summary>Quaternion from Euler angles in three.js "XYZ" order.</summary>
    public static Q4 QEuler(float ax, float ay, float az, Q4 o)
    {
        float c1 = MathF.Cos(ax / 2), c2 = MathF.Cos(ay / 2), c3 = MathF.Cos(az / 2), s1 = MathF.Sin(ax / 2), s2 = MathF.Sin(ay / 2), s3 = MathF.Sin(az / 2);
        o.x = s1 * c2 * c3 + c1 * s2 * s3; o.y = c1 * s2 * c3 - s1 * c2 * s3; o.z = c1 * c2 * s3 + s1 * s2 * c3; o.w = c1 * c2 * c3 - s1 * s2 * s3; return o;
    }
    /// <summary>Quaternion from an orthonormal basis given as columns (x axis, y axis, z axis).</summary>
    public static Q4 QFromBasis(float xx, float xy, float xz, float yx, float yy, float yz, float zx, float zy, float zz, Q4 o)
    {
        float tr = xx + yy + zz;
        if (tr > 0) { float S = 0.5f / MathF.Sqrt(tr + 1); o.w = 0.25f / S; o.x = (yz - zy) * S; o.y = (zx - xz) * S; o.z = (xy - yx) * S; }
        else if (xx > yy && xx > zz) { float S = 2 * MathF.Sqrt(1 + xx - yy - zz); o.w = (yz - zy) / S; o.x = 0.25f * S; o.y = (yx + xy) / S; o.z = (zx + xz) / S; }
        else if (yy > zz) { float S = 2 * MathF.Sqrt(1 + yy - xx - zz); o.w = (zx - xz) / S; o.x = (yx + xy) / S; o.y = 0.25f * S; o.z = (zy + yz) / S; }
        else { float S = 2 * MathF.Sqrt(1 + zz - xx - yy); o.w = (xy - yx) / S; o.x = (zx + xz) / S; o.y = (zy + yz) / S; o.z = 0.25f * S; }
        return o;
    }

    /// <summary>Hex colour 0xRRGGBB to linear-ish float components (0..1, sRGB as authored).</summary>
    public static (float r, float g, float b) Rgb(uint c) => (((c >> 16) & 255) / 255f, ((c >> 8) & 255) / 255f, (c & 255) / 255f);
    public static uint LerpColor(uint a, uint b, float t)
    {
        var (ar, ag, ab) = Rgb(a); var (br, bg, bb) = Rgb(b);
        return Pack(Lerp(ar, br, t), Lerp(ag, bg, t), Lerp(ab, bb, t));
    }
    public static uint Pack(float r, float g, float b) => ((uint)(Clamp(r, 0, 1) * 255 + 0.5f) << 16) | ((uint)(Clamp(g, 0, 1) * 255 + 0.5f) << 8) | (uint)(Clamp(b, 0, 1) * 255 + 0.5f);
    /// <summary>three.js Color.setHSL.</summary>
    public static uint Hsl(float h, float s, float l)
    {
        h = ((h % 1) + 1) % 1; s = Clamp(s, 0, 1); l = Clamp(l, 0, 1);
        if (s == 0) return Pack(l, l, l);
        float p = l <= 0.5f ? l * (1 + s) : l + s - l * s, q = 2 * l - p;
        static float H(float p, float q, float t) { if (t < 0) t += 1; if (t > 1) t -= 1; if (t < 1f / 6) return p + (q - p) * 6 * t; if (t < 0.5f) return q; if (t < 2f / 3) return p + (q - p) * 6 * (2f / 3 - t); return p; }
        return Pack(H(q, p, h + 1f / 3), H(q, p, h), H(q, p, h - 1f / 3));
    }
}
