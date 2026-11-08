using System;
using System.Collections.Generic;
using System.Numerics;

namespace GermBall.Proto.Engine;

/// <summary>An oriented box: the only static collision shape. Floors, walls, steps and ramps are all boxes.</summary>
public sealed class Obb
{
    public Vector3 Center, Half;
    public Quaternion Rot = Quaternion.Identity;
    public Vector3 AxisX = Vector3.UnitX, AxisY = Vector3.UnitY, AxisZ = Vector3.UnitZ;
    public Vector3 Min, Max;   // world-space bounds, for quick rejects

    public Obb(Vector3 center, Vector3 half, Quaternion rot)
    {
        Center = center; Half = half; Rot = rot;
        AxisX = Vector3.Transform(Vector3.UnitX, rot);
        AxisY = Vector3.Transform(Vector3.UnitY, rot);
        AxisZ = Vector3.Transform(Vector3.UnitZ, rot);
        var ext = new Vector3(
            MathF.Abs(AxisX.X) * half.X + MathF.Abs(AxisY.X) * half.Y + MathF.Abs(AxisZ.X) * half.Z,
            MathF.Abs(AxisX.Y) * half.X + MathF.Abs(AxisY.Y) * half.Y + MathF.Abs(AxisZ.Y) * half.Z,
            MathF.Abs(AxisX.Z) * half.X + MathF.Abs(AxisY.Z) * half.Y + MathF.Abs(AxisZ.Z) * half.Z);
        Min = center - ext; Max = center + ext;
    }

    public Vector3 ToLocal(Vector3 p) { var d = p - Center; return new Vector3(Vector3.Dot(d, AxisX), Vector3.Dot(d, AxisY), Vector3.Dot(d, AxisZ)); }
    public Vector3 ToWorld(Vector3 l) => Center + AxisX * l.X + AxisY * l.Y + AxisZ * l.Z;
    public Vector3 DirToLocal(Vector3 d) => new(Vector3.Dot(d, AxisX), Vector3.Dot(d, AxisY), Vector3.Dot(d, AxisZ));

    public bool BoundsOverlapSphere(Vector3 c, float r) =>
        c.X + r >= Min.X && c.X - r <= Max.X && c.Y + r >= Min.Y && c.Y - r <= Max.Y && c.Z + r >= Min.Z && c.Z - r <= Max.Z;

    /// <summary>Sphere contact. Returns true when overlapping, with the push-out normal (out of the box) and depth.</summary>
    public bool SphereContact(Vector3 c, float r, out Vector3 normal, out float depth)
    {
        normal = default; depth = 0;
        if (!BoundsOverlapSphere(c, r)) return false;
        var l = ToLocal(c);
        var q = Vector3.Clamp(l, -Half, Half);
        var d = l - q; float d2 = d.LengthSquared();
        if (d2 > 1e-10f)
        {
            if (d2 >= r * r) return false;
            float dist = MathF.Sqrt(d2);
            var nl = d / dist;
            normal = AxisX * nl.X + AxisY * nl.Y + AxisZ * nl.Z;
            depth = r - dist;
            return true;
        }
        // centre inside the box: leave by the nearest face
        float px = Half.X - MathF.Abs(l.X), py = Half.Y - MathF.Abs(l.Y), pz = Half.Z - MathF.Abs(l.Z);
        if (px <= py && px <= pz) { normal = AxisX * MathF.Sign(l.X == 0 ? 1 : l.X); depth = px + r; }
        else if (py <= pz) { normal = AxisY * MathF.Sign(l.Y == 0 ? 1 : l.Y); depth = py + r; }
        else { normal = AxisZ * MathF.Sign(l.Z == 0 ? 1 : l.Z); depth = pz + r; }
        return true;
    }

    /// <summary>Ray (unit direction) against the box: entry distance and surface normal.</summary>
    public bool Raycast(Vector3 origin, Vector3 dir, float maxDist, out float t, out Vector3 normal)
    {
        t = 0; normal = default;
        var o = ToLocal(origin); var d = DirToLocal(dir);
        float tmin = 0, tmax = maxDist; int axis = -1; float sign = 0;
        for (int i = 0; i < 3; i++)
        {
            float oi = i == 0 ? o.X : i == 1 ? o.Y : o.Z, di = i == 0 ? d.X : i == 1 ? d.Y : d.Z, hi = i == 0 ? Half.X : i == 1 ? Half.Y : Half.Z;
            if (MathF.Abs(di) < 1e-8f) { if (oi < -hi || oi > hi) return false; continue; }
            float inv = 1 / di, t1 = (-hi - oi) * inv, t2 = (hi - oi) * inv; float s = -1;
            if (t1 > t2) { (t1, t2) = (t2, t1); s = 1; }
            if (t1 > tmin) { tmin = t1; axis = i; sign = s; }
            if (t2 < tmax) tmax = t2;
            if (tmin > tmax) return false;
        }
        if (axis < 0) return false;   // origin inside the box: ignore (we only want surfaces in front)
        t = tmin;
        normal = (axis == 0 ? AxisX : axis == 1 ? AxisY : AxisZ) * sign;
        return true;
    }
}

public struct Contact { public Vector3 Normal; public float Depth; public object Other; }

public struct RayHit
{
    public bool Hit; public float T; public Vector3 Point, Normal; public object Other;
    public static RayHit None => new() { T = float.MaxValue };
}

/// <summary>The static collision world: a flat list of boxes (a debug level has a few hundred at most, so no broadphase).</summary>
public sealed class StaticWorld
{
    public readonly List<Obb> Boxes = new();
    public void Add(Obb b) => Boxes.Add(b);

    /// <summary>Pushes a sphere out of every box it overlaps. Returns the contacts found (normals point away from the boxes).</summary>
    public int ResolveSphere(ref Vector3 c, float r, List<Contact> contacts, int iterations = 4)
    {
        int found = 0;
        for (int it = 0; it < iterations; it++)
        {
            bool any = false;
            foreach (var b in Boxes)
            {
                if (!b.SphereContact(c, r, out var n, out var depth)) continue;
                c += n * (depth + 1e-4f);
                contacts?.Add(new Contact { Normal = n, Depth = depth, Other = b });
                any = true; found++;
            }
            if (!any) break;
        }
        return found;
    }

    public bool SphereOverlaps(Vector3 c, float r)
    {
        foreach (var b in Boxes) if (b.SphereContact(c, r, out _, out _)) return true;
        return false;
    }

    public RayHit Raycast(Vector3 origin, Vector3 dir, float maxDist)
    {
        var best = RayHit.None; best.T = maxDist;
        foreach (var b in Boxes)
        {
            if (b.Raycast(origin, dir, best.T, out float t, out var n) && t < best.T)
            { best.Hit = true; best.T = t; best.Normal = n; best.Other = b; }
        }
        if (best.Hit) best.Point = origin + dir * best.T;
        return best;
    }
}

public static class Shapes
{
    /// <summary>Ray against a sphere; entry distance only.</summary>
    public static bool RaySphere(Vector3 o, Vector3 d, Vector3 c, float r, float maxDist, out float t)
    {
        t = 0; var m = o - c; float b = Vector3.Dot(m, d), cc = m.LengthSquared() - r * r;
        if (cc > 0 && b > 0) return false;
        float disc = b * b - cc; if (disc < 0) return false;
        t = -b - MathF.Sqrt(disc); if (t < 0) t = 0;
        return t <= maxDist;
    }

    /// <summary>Ray against an upright cylinder from <paramref name="baseY"/> to <paramref name="baseY"/>+<paramref name="height"/>, including its caps.</summary>
    public static bool RayCylinder(Vector3 o, Vector3 d, Vector3 basePos, float r, float height, float maxDist, out float t, out Vector3 normal)
    {
        t = float.MaxValue; normal = default; bool hit = false;
        float ox = o.X - basePos.X, oz = o.Z - basePos.Z;
        float a = d.X * d.X + d.Z * d.Z;
        if (a > 1e-8f)
        {
            float b = ox * d.X + oz * d.Z, c = ox * ox + oz * oz - r * r, disc = b * b - a * c;
            if (disc >= 0)
            {
                float tt = (-b - MathF.Sqrt(disc)) / a;
                if (tt >= 0 && tt <= maxDist)
                {
                    float y = o.Y + d.Y * tt - basePos.Y;
                    if (y >= 0 && y <= height) { t = tt; hit = true; normal = Vector3.Normalize(new Vector3(ox + d.X * tt, 0, oz + d.Z * tt)); }
                }
            }
        }
        // caps
        if (MathF.Abs(d.Y) > 1e-8f)
        {
            foreach (float capY in new[] { basePos.Y + height, basePos.Y })
            {
                float tt = (capY - o.Y) / d.Y;
                if (tt < 0 || tt > maxDist || tt >= t) continue;
                float x = ox + d.X * tt, z = oz + d.Z * tt;
                if (x * x + z * z <= r * r) { t = tt; hit = true; normal = new Vector3(0, capY > basePos.Y ? 1 : -1, 0); }
            }
        }
        return hit;
    }
}
