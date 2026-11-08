using System;
using System.Collections.Generic;
using System.Numerics;
using GermBall.Engine;

namespace GermBall.Content;

/// <summary>One live entity: its definition, its animator and its current pose, and how to draw it.</summary>
public sealed class EntityView
{
    public readonly EntityDef Def;
    public readonly Animator Anim;
    public readonly EntityPose Pose = new();
    public float Scale => Def.Stats.Scale;
    public Skeleton Sk => Anim.Sk;

    public EntityView(EntityDef def) { Def = def; Anim = new Animator(def); }

    /// <summary>Bones whose shapes (and everything below them) aren't drawn: severed limbs.</summary>
    public readonly HashSet<int> HiddenBones = new();
    public bool IsHidden(int bone)
    {
        if (HiddenBones.Count == 0) return false;
        for (int b = bone; b >= 0; b = Sk.Parent[b]) if (HiddenBones.Contains(b)) return true;
        return false;
    }
    /// <summary>Is this bone at or below <paramref name="limb"/>?</summary>
    public bool InLimb(int bone, int limb) => bone >= 0 && limb >= 0 && Sk.IsDescendant(bone, limb);

    public void Update(AnimContext c) => Anim.Update(c);
    public void Evaluate(AnimContext c) => Anim.Evaluate(c, Pose, Scale);

    /// <summary>A shape's world matrix for a unit mesh (before the capsule's special handling).</summary>
    public Matrix4x4 ShapeMatrix(ShapeDef s, Vector3 sizeOverride)
    {
        int b = Sk.IndexOf(s.Bone);
        var boneM = b >= 0 ? Pose.World[b] : Pose.Root;
        var boneScale = b >= 0 ? Pose.Local[b].Scale : Vector3.One;
        return Matrix4x4.CreateScale(sizeOverride) * Matrix4x4.CreateFromQuaternion(Animator.Euler(s.Rot)) * Matrix4x4.CreateTranslation(s.Pos)
             * Matrix4x4.CreateScale(boneScale) * boneM;
    }
    public Matrix4x4 ShapeMatrix(ShapeDef s) => ShapeMatrix(s, s.Size);

    /// <summary>Draws every shape. <paramref name="flash"/> turns it white (hit), <paramref name="dim"/> darkens it (dead).</summary>
    public void Draw(DrawList dl, bool flash = false, float dim = 1, int highlight = -1, float alpha = 1, int hover = -1)
    {
        for (int i = 0; i < Def.Shapes.Count; i++)
        {
            var s = Def.Shapes[i];
            if (s.Hidden || IsHidden(Sk.IndexOf(s.Bone))) continue;
            var c = flash ? Vector4.One : ColorHex.Parse(s.Color);
            c = new Vector4(c.X * dim, c.Y * dim, c.Z * dim, alpha);
            if (i == highlight) c = new Vector4(Vector3.Lerp(new Vector3(c.X, c.Y, c.Z), new Vector3(1, 0.85f, 0.2f), 0.45f), alpha);
            else if (i == hover) c = new Vector4(Vector3.Lerp(new Vector3(c.X, c.Y, c.Z), Vector3.One, 0.3f), alpha);   // under the mouse
            var flags = s.Unlit ? DrawFlags.Unlit : DrawFlags.None;
            DrawShape(dl, this, s, c, flags);
        }
    }

    public static void DrawShape(DrawList dl, EntityView v, ShapeDef s, Vector4 c, DrawFlags flags)
    {
        if (MathF.Abs(s.Taper) > 0.004f || MathF.Abs(s.Curve) > 0.004f)
        {
            // tapered or curved: its own mesh, built at its size
            dl.AddCustom(DeformedMeshes.Get(s.Type, Vector3.Abs(s.Size), s.Taper, s.Curve), v.ShapeMatrix(s, Vector3.One), c, flags);
            return;
        }
        switch (s.Type)
        {
            case "Sphere": dl.Add(MeshId.Sphere, v.ShapeMatrix(s), c, flags); break;
            case "Ball": dl.Add(MeshId.BallStriped, v.ShapeMatrix(s), c, flags); break;
            case "Cylinder": dl.Add(MeshId.Cylinder, v.ShapeMatrix(s), c, flags); break;
            case "Cone": dl.Add(MeshId.Cone, v.ShapeMatrix(s), c, flags); break;
            case "Capsule":
                {
                    // a cylinder with a sphere on each end; the size is the whole capsule
                    float r = s.Size.X * 0.5f, seg = MathF.Max(0, s.Size.Y - s.Size.X);
                    var unit = v.ShapeMatrix(s, Vector3.One);
                    var cap = new Vector3(s.Size.X, s.Size.X, s.Size.Z);
                    if (seg > 1e-4f) dl.Add(MeshId.Cylinder, Matrix4x4.CreateScale(new Vector3(s.Size.X, seg, s.Size.Z)) * unit, c, flags);
                    dl.Add(MeshId.Sphere, Matrix4x4.CreateScale(cap) * Matrix4x4.CreateTranslation(0, seg / 2, 0) * unit, c, flags);
                    dl.Add(MeshId.Sphere, Matrix4x4.CreateScale(cap) * Matrix4x4.CreateTranslation(0, -seg / 2, 0) * unit, c, flags);
                    break;
                }
            default: dl.Add(MeshId.Cube, v.ShapeMatrix(s), c, flags); break;
        }
    }
}

/// <summary>A severed limb: its shapes, frozen as they were drawn, tumbling as one piece until it fades.</summary>
public sealed class LimbDebris
{
    readonly List<DrawItem> items = new();
    public Vector3 Pos, Vel, Spin; public Quaternion Rot = Quaternion.Identity;
    public float Life, MaxLife, Radius;
    readonly List<Contact> contacts = new();

    /// <summary>Cuts the limb at <paramref name="limbBone"/> off an evaluated entity.</summary>
    public LimbDebris(EntityView v, int limbBone, Vector3 vel, float life)
    {
        var dl = new DrawList();
        for (int i = 0; i < v.Def.Shapes.Count; i++)
        {
            var s = v.Def.Shapes[i]; int b = v.Sk.IndexOf(s.Bone);
            if (s.Hidden || !v.InLimb(b, limbBone)) continue;
            EntityView.DrawShape(dl, v, s, ColorHex.Parse(s.Color), s.Unlit ? DrawFlags.Unlit : DrawFlags.None);
        }
        var c = Vector3.Zero; foreach (var it in dl.Items) c += it.World.Translation;
        Pos = dl.Items.Count > 0 ? c / dl.Items.Count : v.Pose.Joint(limbBone);
        float r = 0.08f;
        foreach (var it in dl.Items)
        {
            var d = it; d.World = it.World * Matrix4x4.CreateTranslation(-Pos); items.Add(d);
            r = MathF.Max(r, Vector3.Distance(it.World.Translation, Pos) * 0.5f);
        }
        Radius = MathF.Min(r, 0.5f * v.Scale);
        Vel = vel; Spin = new Vector3(vel.Z, 4, -vel.X) * 0.6f; Life = MaxLife = life;
    }

    public bool Dead => Life <= 0;

    public void Update(StaticWorld world, float dt, float gravity)
    {
        Life -= dt;
        Vel.Y -= gravity * dt;
        Pos += Vel * dt;
        contacts.Clear();
        world.ResolveSphere(ref Pos, Radius, contacts, 3);
        foreach (var ct in contacts)
        {
            float vn = Vector3.Dot(Vel, ct.Normal);
            if (vn < 0) Vel -= ct.Normal * vn * 1.3f;
            if (ct.Normal.Y > 0.6f) { Vel *= MathF.Exp(-4 * dt); Spin *= MathF.Exp(-5 * dt); }
        }
        float w = Spin.Length();
        if (w > 1e-4f) Rot = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Spin / w, w * dt) * Rot);
    }

    public void Draw(DrawList dl)
    {
        float fade = MathX.Clamp01(Life / 0.6f);
        var m = Matrix4x4.CreateFromQuaternion(Rot) * Matrix4x4.CreateTranslation(Pos);
        // shrink away at the end
        var shrink = Matrix4x4.CreateScale(0.2f + 0.8f * fade);
        foreach (var it in items) { var d = it; d.World = it.World * shrink * m; dl.Items.Add(d); }
    }
}
