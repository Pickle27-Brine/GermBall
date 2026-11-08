using System;
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
    public void Draw(DrawList dl, bool flash = false, float dim = 1, int highlight = -1, float alpha = 1)
    {
        for (int i = 0; i < Def.Shapes.Count; i++)
        {
            var s = Def.Shapes[i];
            if (s.Hidden) continue;
            var c = flash ? Vector4.One : ColorHex.Parse(s.Color);
            c = new Vector4(c.X * dim, c.Y * dim, c.Z * dim, alpha);
            if (i == highlight) c = new Vector4(Vector3.Lerp(new Vector3(c.X, c.Y, c.Z), new Vector3(1, 0.85f, 0.2f), 0.45f), alpha);
            var flags = s.Unlit ? DrawFlags.Unlit : DrawFlags.None;
            DrawShape(dl, this, s, c, flags);
        }
    }

    public static void DrawShape(DrawList dl, EntityView v, ShapeDef s, Vector4 c, DrawFlags flags)
    {
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
