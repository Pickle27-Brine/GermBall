using System;
using System.Collections.Generic;
using System.Numerics;

namespace GermBall.Engine;

/// <summary>The joints of every humanoid body (the hero and the germs).</summary>
/// <summary>The joints of every humanoid body. Abdomen is optional in a rig: without it the torso is one segment.</summary>
public enum J { Pelvis, Chest, Neck, Head, ShL, ElL, HaL, ShR, ElR, HaR, HipL, KnL, FtL, HipR, KnR, FtR, Abdomen, Count }

/// <summary>World-space joint positions for one body.</summary>
public sealed class Pose
{
    public readonly Vector3[] P = new Vector3[(int)J.Count];
    public Vector3 this[J j] { get => P[(int)j]; set => P[(int)j] = value; }
    public void CopyFrom(Pose o) => Array.Copy(o.P, P, P.Length);
    public void Lerp(Pose a, Pose b, float t) { for (int i = 0; i < P.Length; i++) P[i] = Vector3.Lerp(a.P[i], b.P[i], t); }
    public bool Finite() { foreach (var p in P) if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z)) return false; return true; }
}

public static class Skel
{
    /// <summary>The visible bones.</summary>
    public static readonly (J a, J b)[] Bones =
    {
        (J.Pelvis, J.Abdomen), (J.Abdomen, J.Chest), (J.Chest, J.Neck), (J.Neck, J.Head),
        (J.Chest, J.ShL), (J.ShL, J.ElL), (J.ElL, J.HaL),
        (J.Chest, J.ShR), (J.ShR, J.ElR), (J.ElR, J.HaR),
        (J.Pelvis, J.HipL), (J.HipL, J.KnL), (J.KnL, J.FtL),
        (J.Pelvis, J.HipR), (J.HipR, J.KnR), (J.KnR, J.FtR),
    };
    /// <summary>Extra ragdoll links that keep the torso and head from folding up.</summary>
    public static readonly (J a, J b)[] Braces =
    {
        (J.ShL, J.ShR), (J.HipL, J.HipR), (J.ShL, J.HipL), (J.ShR, J.HipR), (J.ShL, J.HipR), (J.ShR, J.HipL),
        (J.Head, J.ShL), (J.Head, J.ShR), (J.Neck, J.Pelvis), (J.Chest, J.HipL), (J.Chest, J.HipR), (J.Chest, J.Pelvis),
        (J.Abdomen, J.HipL), (J.Abdomen, J.HipR), (J.Abdomen, J.ShL), (J.Abdomen, J.ShR),
    };
    /// <summary>Collision radius of each joint for a body of scale 1 (1.8 m tall).</summary>
    public static readonly float[] Radius =
    {
        0.14f, 0.16f, 0.08f, 0.14f,   // pelvis, chest, neck, head
        0.09f, 0.06f, 0.06f,          // left arm
        0.09f, 0.06f, 0.06f,          // right arm
        0.09f, 0.07f, 0.07f,          // left leg
        0.09f, 0.07f, 0.07f,          // right leg
        0.14f,                        // abdomen
    };

    /// <summary>Two-bone IK: places the middle joint (elbow or knee) so the chain reaches toward <paramref name="target"/>,
    /// bending toward <paramref name="pole"/>. The end is pulled in if the target is out of reach.</summary>
    public static (Vector3 mid, Vector3 end) TwoBone(Vector3 root, Vector3 target, float l1, float l2, Vector3 pole)
    {
        var d = target - root; float len = d.Length();
        var dir = len > 1e-5f ? d / len : -Vector3.UnitY;
        len = Math.Clamp(len, MathF.Abs(l1 - l2) + 1e-3f, l1 + l2 - 1e-3f);
        float a = (l1 * l1 - l2 * l2 + len * len) / (2 * len), h = MathF.Sqrt(MathF.Max(0, l1 * l1 - a * a));
        var p = pole - dir * Vector3.Dot(pole, dir);
        if (p.LengthSquared() < 1e-8f) p = Vector3.Cross(dir, Vector3.UnitX);
        if (p.LengthSquared() < 1e-8f) p = Vector3.Cross(dir, Vector3.UnitZ);
        p = Vector3.Normalize(p);
        return (root + dir * a + p * h, root + dir * len);
    }
}

/// <summary>
/// A Verlet ragdoll: one particle per joint, held together by the bones and the torso braces, colliding with the level as
/// spheres. Cheap and unconditionally stable, which is all a knocked-over germ needs.
/// </summary>
public sealed class Ragdoll
{
    public readonly Pose Pos = new(), Prev = new();
    readonly float[] radius = new float[(int)J.Count];
    readonly List<(int a, int b, float len)> links = new();
    readonly List<Contact> contacts = new();
    public float Age;
    public const float MaxSpeed = 40;

    public Ragdoll(Pose from, Vector3 velocity, float scale, Vector3 spin = default)
    {
        Pos.CopyFrom(from);
        var c = from[J.Pelvis];
        for (int i = 0; i < (int)J.Count; i++)
        {
            radius[i] = Skel.Radius[i] * scale;
            // a tumble: points far from the pelvis get extra sideways speed
            var v = velocity + Vector3.Cross(spin, from.P[i] - c);
            Prev.P[i] = from.P[i] - v * (1f / 120f);
        }
        foreach (var (a, b) in Skel.Bones) links.Add(((int)a, (int)b, Vector3.Distance(from[a], from[b])));
        foreach (var (a, b) in Skel.Braces) links.Add(((int)a, (int)b, Vector3.Distance(from[a], from[b])));
    }

    public Vector3 Center => Pos[J.Pelvis];
    public Vector3 Velocity(float dt) => (Pos[J.Pelvis] - Prev[J.Pelvis]) / dt;

    public void Push(Vector3 dv, float dt) { for (int i = 0; i < (int)J.Count; i++) Prev.P[i] -= dv * dt; }
    public void PushJoint(int j, Vector3 dv, float dt) => Prev.P[j] -= dv * dt;

    /// <summary>The joint nearest a point (for shots and blasts).</summary>
    public int Nearest(Vector3 p)
    {
        int best = 0; float bd = float.MaxValue;
        for (int i = 0; i < (int)J.Count; i++) { float d = Vector3.DistanceSquared(Pos.P[i], p); if (d < bd) { bd = d; best = i; } }
        return best;
    }

    public bool Raycast(Vector3 o, Vector3 d, float max, out float t, out int joint)
    {
        t = max; joint = -1;
        for (int i = 0; i < (int)J.Count; i++)
            if (Shapes.RaySphere(o, d, Pos.P[i], radius[i] * 1.6f, t, out float ti) && ti < t) { t = ti; joint = i; }
        return joint >= 0;
    }

    public void Step(StaticWorld world, float dt, float gravity)
    {
        Age += dt;
        int n = (int)J.Count;
        var g = new Vector3(0, -gravity * dt * dt, 0);
        for (int i = 0; i < n; i++)
        {
            var v = (Pos.P[i] - Prev.P[i]) * 0.998f;
            float max = MaxSpeed * dt; if (v.LengthSquared() > max * max) v = Vector3.Normalize(v) * max;
            Prev.P[i] = Pos.P[i];
            Pos.P[i] += v + g;
        }
        for (int it = 0; it < 8; it++)
        {
            foreach (var (a, b, len) in links)
            {
                var d = Pos.P[b] - Pos.P[a]; float l = d.Length(); if (l < 1e-6f) continue;
                var corr = d * (0.5f * (l - len) / l);
                Pos.P[a] += corr; Pos.P[b] -= corr;
            }
            if (it % 2 == 1) Collide(world, it == 7);
        }
    }

    void Collide(StaticWorld world, bool friction)
    {
        for (int i = 0; i < (int)J.Count; i++)
        {
            contacts.Clear();
            var p = Pos.P[i];
            world.ResolveSphere(ref p, radius[i], contacts, 2);
            if (contacts.Count == 0) continue;
            Pos.P[i] = p;
            if (!friction) continue;
            // friction: no speed into the surface, and sliding along it bleeds off
            var v = Pos.P[i] - Prev.P[i];
            foreach (var c in contacts)
            {
                float vn = Vector3.Dot(v, c.Normal);
                var vt = v - c.Normal * vn;
                v = vt * 0.85f + c.Normal * MathF.Max(0, vn);
            }
            Prev.P[i] = Pos.P[i] - v;
        }
    }

    public bool Finite() => Pos.Finite();
}
