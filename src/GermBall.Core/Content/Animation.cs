using System;
using System.Collections.Generic;
using System.Numerics;
using GermBall.Engine;
using GermBall.Sim;

namespace GermBall.Content;

/// <summary>A compiled rig: bone indices in parent-first order, and which bones are the standard humanoid joints.</summary>
public sealed class Skeleton
{
    public readonly string[] Names; public readonly int[] Parent; public readonly Vector3[] Offset;
    /// <summary>Bone index for each humanoid joint, or -1.</summary>
    public readonly int[] Std = new int[(int)J.Count];
    /// <summary>All sixteen humanoid joints exist: the procedural motion and ragdolls work.</summary>
    public readonly bool Humanoid;
    public readonly Vector3[] RestModel;   // joint positions at rest, model space
    readonly Dictionary<string, int> index = new();
    public int Count => Names.Length;

    public static readonly string[] StdNames =
    {
        "pelvis", "chest", "neck", "head", "shoulder.L", "elbow.L", "hand.L", "shoulder.R", "elbow.R", "hand.R",
        "hip.L", "knee.L", "foot.L", "hip.R", "knee.R", "foot.R", "abdomen",
    };
    /// <summary>The rig splits its torso into abdomen and chest.</summary>
    public bool HasAbdomen => Std[(int)J.Abdomen] >= 0;

    public Skeleton(List<BoneDef> bones)
    {
        // parent-first order (bones whose parent is missing hang off the origin)
        var order = new List<BoneDef>(); var placed = new HashSet<string>();
        bool progress = true;
        while (order.Count < bones.Count && progress)
        {
            progress = false;
            foreach (var b in bones)
            {
                if (placed.Contains(b.Name)) continue;
                if (string.IsNullOrEmpty(b.Parent) || placed.Contains(b.Parent) || bones.Find(x => x.Name == b.Parent) == null)
                { order.Add(b); placed.Add(b.Name); progress = true; }
            }
        }
        foreach (var b in bones) if (!placed.Contains(b.Name)) { order.Add(b); placed.Add(b.Name); }   // cycles: break them
        int n = order.Count;
        Names = new string[n]; Parent = new int[n]; Offset = new Vector3[n]; RestModel = new Vector3[n];
        for (int i = 0; i < n; i++) { Names[i] = order[i].Name; index[order[i].Name] = i; }
        for (int i = 0; i < n; i++)
        {
            Parent[i] = !string.IsNullOrEmpty(order[i].Parent) && index.TryGetValue(order[i].Parent, out int p) && p < i ? p : -1;
            Offset[i] = order[i].Offset;
            RestModel[i] = (Parent[i] >= 0 ? RestModel[Parent[i]] : Vector3.Zero) + Offset[i];
        }
        Humanoid = true;
        for (int j = 0; j < (int)J.Count; j++) { Std[j] = IndexOf(StdNames[j]); if (Std[j] < 0 && (J)j != J.Abdomen) Humanoid = false; }
    }

    public int IndexOf(string name) => name != null && index.TryGetValue(name, out int i) ? i : -1;
    public bool IsDescendant(int bone, int ancestor)
    {
        for (int b = bone; b >= 0; b = Parent[b]) if (b == ancestor) return true;
        return false;
    }
    /// <summary>The first child of a bone (its "aim" direction), or -1 for a leaf.</summary>
    public int FirstChild(int bone) { for (int i = bone + 1; i < Count; i++) if (Parent[i] == bone) return i; return -1; }
    float Len(J a, J b) => Std[(int)a] >= 0 && Std[(int)b] >= 0 ? Vector3.Distance(RestModel[Std[(int)a]], RestModel[Std[(int)b]]) : 0;

    /// <summary>Proportions for the procedural motion, measured from this rig.</summary>
    public Rig Rig()
    {
        if (!Humanoid) return new Rig(1);
        var p = RestModel; int I(J j) => Std[(int)j];
        float abd = HasAbdomen ? Len(J.Pelvis, J.Abdomen) : 0, torso = HasAbdomen ? abd + Len(J.Abdomen, J.Chest) : Len(J.Pelvis, J.Chest);
        return new Rig(
            pelvisH: p[I(J.Pelvis)].Y, torso: torso, neck: Len(J.Chest, J.Neck), headUp: Len(J.Neck, J.Head),
            shoulderW: MathF.Abs(p[I(J.ShR)].X - p[I(J.Chest)].X), shoulderDrop: p[I(J.Chest)].Y - p[I(J.ShR)].Y,
            hipW: MathF.Abs(p[I(J.HipR)].X - p[I(J.Pelvis)].X), hipDrop: p[I(J.Pelvis)].Y - p[I(J.HipR)].Y,
            thigh: Len(J.HipL, J.KnL), shin: Len(J.KnL, J.FtL), upper: Len(J.ShL, J.ElL), fore: Len(J.ElL, J.HaL), abdomen: abd);
    }
}

/// <summary>One bone's animated transform relative to its rest pose.</summary>
public struct BoneXf
{
    public Quaternion Rot; public Vector3 Pos; public Vector3 Scale;
    public static readonly BoneXf Rest = new() { Rot = Quaternion.Identity, Scale = Vector3.One };
}

/// <summary>An evaluated pose: local transforms, then world matrices after forward kinematics.</summary>
public sealed class EntityPose
{
    public BoneXf[] Local = Array.Empty<BoneXf>();
    public Matrix4x4[] World = Array.Empty<Matrix4x4>();   // joint frames (rotation + position, entity scale included)
    public Matrix4x4 Root = Matrix4x4.Identity;
    public void Ensure(int n)
    {
        if (Local.Length != n) { Local = new BoneXf[n]; World = new Matrix4x4[n]; }
    }
    public Vector3 Joint(int bone) => bone >= 0 && bone < World.Length ? World[bone].Translation : Root.Translation;
}

/// <summary>What the game (or an editor's preview) tells the animator about the entity this frame. Directions are in world space.</summary>
public sealed class AnimContext
{
    public float Time, Dt;
    public Vector3 Feet; public float Yaw;
    public float Speed; public Vector3 MoveDir = -Vector3.UnitZ;
    public bool Grounded = true, Rolling, Skidding, Hovering, Charging;
    public bool Armed, TwoHanded, Aiming; public Vector3 AimDir = -Vector3.UnitZ;
    public float PunchT, ReloadT;
    /// <summary>Aim pitch in degrees (up positive) and vertical speed, for blend layers and speed-scaled clips.</summary>
    public float AimPitch, VSpeed;
    /// <summary>Old "procedural" layers only: idle sway amount and how far the arms are raised.</summary>
    public float Sway = 0.06f, ArmsUp;
    /// <summary>The current game state (see <see cref="AnimStates"/>): an action (punch, aim, hurt...) or a movement.</summary>
    public string State = "idle";
    /// <summary>The movement underneath any action (idle, run, skid, jump, fall, hover, curl...). State layers fall back to it,
    /// so a punch while running keeps the legs running.</summary>
    public string Move = "idle";
    /// <summary>Floor height under a world point (NaN if none); used for foot placement.</summary>
    public Func<Vector3, float> GroundAt;
    /// <summary>When set, the body follows these joint positions instead of its clips (ragdolls, get-up blends).</summary>
    public Pose Joints;
    /// <summary>Extra rotation of the whole body about a point <see cref="SpinCenter"/> above the feet (curled-up spin).</summary>
    public Quaternion Spin = Quaternion.Identity; public float SpinCenter;
}

/// <summary>
/// The animation pipeline for one entity instance. Layers are evaluated in order:
/// <list type="bullet">
/// <item>state: the clip the current game state maps to (with crossfades and speed-scaled playback)</item>
/// <item>clip: one clip on the layer's own clock</item>
/// <item>blend: clips mixed by a parameter (aim angle, speed)</item>
/// <item>footik: last of all, plants the feet on the ground under them (the only procedural motion)</item>
/// </list>
/// Each layer overrides or adds to what's below, by weight, through an optional bone mask, and only in the game states it's
/// set to play in (fading in and out). Ragdolls take over the standard bones while an entity is down.
/// </summary>
public sealed class Animator
{
    public readonly EntityDef Def; public readonly Skeleton Sk; public readonly Rig Rig;
    readonly float[] layerTime = new float[64], layerWeight = new float[64];
    string stateClip, prevStateClip; float stateT, prevStateT, fade, fadeLen;
    StateClip curSC, prevSC;
    readonly BoneXf[] layer, layerB;
    readonly bool[] driven, drivenB;
    readonly Pose joints = new();

    public Animator(EntityDef def)
    {
        Def = def; Sk = new Skeleton(def.Bones); Rig = Sk.Rig();
        layer = new BoneXf[Sk.Count]; layerB = new BoneXf[Sk.Count]; driven = new bool[Sk.Count]; drivenB = new bool[Sk.Count];
        Array.Fill(layerWeight, -1);
    }

    public string StateClip => stateClip;
    public float LayerWeight(int i) => i < layerWeight.Length ? MathF.Max(0, layerWeight[i]) : 0;

    public static float Param(AnimContext c, string name) => name switch
    {
        "speed" => c.Speed, "aimPitch" => c.AimPitch, "vspeed" => c.VSpeed, _ => 0,
    };
    static float Rate(AnimContext c, string param, float reference, float min)
    {
        if (string.IsNullOrEmpty(param)) return 1;
        return MathF.Max(min, MathF.Abs(Param(c, param)) / MathF.Max(1e-3f, reference));
    }

    /// <summary>Advances clocks: layer clips, layer fades and state crossfades.</summary>
    public void Update(AnimContext c)
    {
        float dt = c.Dt;
        for (int i = 0; i < Def.Layers.Count && i < layerTime.Length; i++)
        {
            var L = Def.Layers[i];
            bool active = L.ActiveIn == null || L.ActiveIn.Count == 0 || L.ActiveIn.Contains(c.State) || L.ActiveIn.Contains(c.Move);
            float target = active ? 1 : 0;
            if (layerWeight[i] < 0) layerWeight[i] = target;   // first frame: no fade
            if (active && layerWeight[i] <= 0) layerTime[i] = 0;   // a layer that switches on starts its clip over
            layerWeight[i] = L.Fade <= 1e-3f ? target : MathX.Clamp01(layerWeight[i] + MathF.Sign(target - layerWeight[i]) * dt / L.Fade);
            layerTime[i] += dt * L.Speed * Rate(c, L.SpeedParam, L.SpeedRef, 0);
        }
        // state clips crossfade when the state's clip changes
        var sc = Def.States.Find(s => s.State == c.State && !string.IsNullOrEmpty(s.Clip)) ?? Def.States.Find(s => s.State == c.Move);
        string want = sc?.Clip;
        if (want != stateClip)
        {
            prevStateClip = stateClip; prevStateT = stateT; prevSC = curSC;
            stateClip = want; curSC = sc; stateT = 0; fadeLen = MathF.Max(0.001f, sc?.Fade ?? 0.15f); fade = prevStateClip != null ? fadeLen : 0;
        }
        else curSC = sc;
        stateT += dt * (curSC != null ? Rate(c, curSC.SpeedParam, curSC.SpeedRef, curSC.MinRate) : 1);
        prevStateT += dt * (prevSC != null ? Rate(c, prevSC.SpeedParam, prevSC.SpeedRef, prevSC.MinRate) : 1);
        fade = MathF.Max(0, fade - dt);
    }

    static Matrix4x4 RootFor(AnimContext c, float scale)
    {
        if (c.Joints != null) return Matrix4x4.CreateScale(scale);
        var root = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateRotationY(c.Yaw) * Matrix4x4.CreateTranslation(c.Feet);
        if (c.Spin != Quaternion.Identity)
            root = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(0, -c.SpinCenter, 0) * Matrix4x4.CreateFromQuaternion(c.Spin)
                 * Matrix4x4.CreateRotationY(c.Yaw) * Matrix4x4.CreateTranslation(c.Feet + new Vector3(0, c.SpinCenter, 0));
        return root;
    }

    /// <summary>Evaluates the layer stack into <paramref name="pose"/> for an entity standing at the context's feet and yaw.</summary>
    public void Evaluate(AnimContext c, EntityPose pose, float scale)
    {
        pose.Ensure(Sk.Count);
        var root = RootFor(c, scale); pose.Root = root;
        for (int i = 0; i < Sk.Count; i++) pose.Local[i] = BoneXf.Rest;
        float footIk = 0;
        for (int li = 0; li < Def.Layers.Count; li++)
        {
            var L = Def.Layers[li];
            float w = L.Weight * (li < layerWeight.Length ? MathF.Max(0, layerWeight[li] < 0 ? 1 : layerWeight[li]) : 1);
            if (!L.Enabled || w <= 0) continue;
            Array.Clear(driven);
            float t = li < layerTime.Length ? layerTime[li] : 0;
            switch (L.Source)
            {
                case "footik": footIk = MathF.Max(footIk, w); continue;
                case "procedural": Procedural(c, root); break;   // old files: an idle sway
                case "clip": SampleInto(Def.Clip(L.Clip), t); break;
                case "blend": BlendInto(L, Param(c, L.Param), t); break;
                case "state":
                    {
                        var cur = Def.Clip(stateClip); var prev = Def.Clip(prevStateClip);
                        if (fade > 0 && prev != null)
                        {
                            SampleInto(prev, prevStateT);
                            if (cur != null) BlendSampleInto(cur, stateT, 1 - fade / fadeLen);
                            else FadeTowardRest(1 - fade / fadeLen);
                        }
                        else if (cur != null) SampleInto(cur, stateT);
                        break;
                    }
            }
            Apply(L, pose, w);
        }
        // a ragdoll (or a get-up blend) takes over the standard bones
        if (c.Joints != null && Sk.Humanoid)
        {
            Array.Clear(driven);
            Matrix4x4.Invert(root, out var toModel);
            for (int j = 0; j < (int)J.Count; j++) joints.P[j] = Vector3.Transform(c.Joints.P[j], toModel);
            JointsToLocal(joints, false);
            Apply(new LayerDef { Mode = "override" }, pose, 1);
        }
        Fk(pose);
        if (footIk > 0) FootIk(c, pose, footIk, scale);
    }

    /// <summary>Editor preview: one clip at an exact time on the rest pose, with the feet planted if asked.</summary>
    public void EvaluateSolo(AnimContext c, EntityPose pose, float scale, ClipDef clip, float t, bool footIk)
    {
        pose.Ensure(Sk.Count);
        var root = RootFor(c, scale); pose.Root = root;
        for (int i = 0; i < Sk.Count; i++) pose.Local[i] = BoneXf.Rest;
        if (clip != null) { Array.Clear(driven); SampleInto(clip, t); Apply(new LayerDef { Mode = "override" }, pose, 1); }
        Fk(pose);
        if (footIk) FootIk(c, pose, 1, scale);
    }

    void Apply(LayerDef L, EntityPose pose, float w)
    {
        w = MathX.Clamp01(w);
        int[] mask = null;
        if (L.Mask != null && L.Mask.Count > 0)
        {
            mask = new int[L.Mask.Count];
            for (int m = 0; m < mask.Length; m++) mask[m] = Sk.IndexOf(L.Mask[m]);
        }
        bool additive = L.Mode == "additive";
        for (int i = 0; i < Sk.Count; i++)
        {
            if (!driven[i]) continue;
            if (mask != null)
            {
                bool inMask = false;
                foreach (int m in mask) if (m >= 0 && (m == i || (L.MaskChildren && Sk.IsDescendant(i, m)))) { inMask = true; break; }
                if (!inMask) continue;
            }
            ref var dst = ref pose.Local[i]; var src = layer[i];
            if (additive)
            {
                dst.Rot = Quaternion.Normalize(dst.Rot * Quaternion.Slerp(Quaternion.Identity, src.Rot, w));
                dst.Pos += src.Pos * w;
                dst.Scale *= Vector3.Lerp(Vector3.One, src.Scale, w);
            }
            else
            {
                dst.Rot = Quaternion.Slerp(dst.Rot, src.Rot, w);
                dst.Pos = Vector3.Lerp(dst.Pos, src.Pos, w);
                dst.Scale = Vector3.Lerp(dst.Scale, src.Scale, w);
            }
        }
    }

    void Fk(EntityPose pose)
    {
        for (int i = 0; i < Sk.Count; i++)
        {
            var l = pose.Local[i];
            var parent = Sk.Parent[i] >= 0 ? pose.World[Sk.Parent[i]] : pose.Root;
            pose.World[i] = Matrix4x4.CreateFromQuaternion(l.Rot) * Matrix4x4.CreateTranslation(Sk.Offset[i] + l.Pos) * parent;
        }
    }

    // ---------------------------------------------------------------- clips
    public static Quaternion Euler(Vector3 deg) => Quaternion.CreateFromYawPitchRoll(deg.Y * MathX.Deg, deg.X * MathX.Deg, deg.Z * MathX.Deg);

    /// <summary>Samples one track at time t (wrapping for looping clips).</summary>
    public static BoneXf SampleTrack(TrackDef tr, float t, float length, bool loop)
    {
        var keys = tr.Keys;
        if (keys.Count == 0) return BoneXf.Rest;
        if (keys.Count == 1) return ToXf(keys[0]);
        if (length > 0) t = loop ? t - MathF.Floor(t / length) * length : MathX.Clamp(t, 0, length);
        KeyDef a, b; float u;
        if (t <= keys[0].T || t >= keys[^1].T)
        {
            if (!loop) { var k = t <= keys[0].T ? keys[0] : keys[^1]; return ToXf(k); }
            a = keys[^1]; b = keys[0];
            float span = length - a.T + b.T; float into = t >= a.T ? t - a.T : t + length - a.T;
            u = span > 1e-5f ? into / span : 0;
        }
        else
        {
            int i = 0; while (i < keys.Count - 2 && keys[i + 1].T <= t) i++;
            a = keys[i]; b = keys[i + 1];
            u = b.T - a.T > 1e-5f ? (t - a.T) / (b.T - a.T) : 0;
        }
        u = a.Ease switch { "step" => 0, "linear" => u, _ => u * u * (3 - 2 * u) };
        return new BoneXf { Rot = Quaternion.Slerp(Euler(a.Rot), Euler(b.Rot), u), Pos = Vector3.Lerp(a.Pos, b.Pos, u), Scale = Vector3.Lerp(a.Scale, b.Scale, u) };
    }
    static BoneXf ToXf(KeyDef k) => new() { Rot = Euler(k.Rot), Pos = k.Pos, Scale = k.Scale };

    void SampleInto(ClipDef clip, float t)
    {
        if (clip == null) return;
        foreach (var tr in clip.Tracks)
        {
            int b = Sk.IndexOf(tr.Bone); if (b < 0) continue;
            layer[b] = SampleTrack(tr, t, clip.Length, clip.Loop); driven[b] = true;
        }
    }
    void BlendSampleInto(ClipDef clip, float t, float k)
    {
        foreach (var tr in clip.Tracks)
        {
            int b = Sk.IndexOf(tr.Bone); if (b < 0) continue;
            var x = SampleTrack(tr, t, clip.Length, clip.Loop);
            var from = driven[b] ? layer[b] : BoneXf.Rest;
            layer[b] = Mix(from, x, k); driven[b] = true;
        }
        for (int b = 0; b < Sk.Count; b++)
            if (driven[b] && clip.Track(Sk.Names[b]) == null) layer[b] = Mix(layer[b], BoneXf.Rest, k);
    }
    void FadeTowardRest(float k) { for (int b = 0; b < Sk.Count; b++) if (driven[b]) layer[b] = Mix(layer[b], BoneXf.Rest, k); }
    static BoneXf Mix(BoneXf a, BoneXf b, float k) =>
        new() { Rot = Quaternion.Slerp(a.Rot, b.Rot, k), Pos = Vector3.Lerp(a.Pos, b.Pos, k), Scale = Vector3.Lerp(a.Scale, b.Scale, k) };

    /// <summary>A blend layer: the two clips either side of the parameter's value, mixed by where it falls between them.</summary>
    void BlendInto(LayerDef L, float value, float t)
    {
        if (L.Blend == null || L.Blend.Count == 0) return;
        var pts = new List<BlendPoint>(L.Blend); pts.Sort((x, y) => x.Value.CompareTo(y.Value));
        int hi = pts.FindIndex(p => p.Value >= value);
        BlendPoint a, b;
        if (hi < 0) a = b = pts[^1]; else if (hi == 0) a = b = pts[0]; else { a = pts[hi - 1]; b = pts[hi]; }
        float k = a == b || MathF.Abs(b.Value - a.Value) < 1e-6f ? 0 : MathX.Clamp01((value - a.Value) / (b.Value - a.Value));
        SampleInto(Def.Clip(a.Clip), t);
        if (b != a) BlendSampleInto2(Def.Clip(b.Clip), t, k);
    }
    void BlendSampleInto2(ClipDef clip, float t, float k)
    {
        if (clip == null) return;
        Array.Clear(drivenB);
        foreach (var tr in clip.Tracks) { int b = Sk.IndexOf(tr.Bone); if (b < 0) continue; layerB[b] = SampleTrack(tr, t, clip.Length, clip.Loop); drivenB[b] = true; }
        for (int b = 0; b < Sk.Count; b++)
        {
            if (!driven[b] && !drivenB[b]) continue;
            layer[b] = Mix(driven[b] ? layer[b] : BoneXf.Rest, drivenB[b] ? layerB[b] : BoneXf.Rest, k); driven[b] = true;
        }
    }

    // ---------------------------------------------------------------- foot IK (the one procedural layer)
    /// <summary>Plants each foot on the ground under it: the clip's foot lift is kept, measured from the real floor; the hips
    /// drop if a foot can't reach (stepping down); hip and knee re-aim with two-bone IK, and the foot keeps its angle.</summary>
    void FootIk(AnimContext c, EntityPose pose, float w, float scale)
    {
        if (!Sk.Humanoid || c.GroundAt == null || !c.Grounded || c.Rolling || c.Joints != null) return;
        float ankle = Rig.Ankle * scale, baseY = c.Feet.Y, reach = (Rig.Thigh + Rig.Shin) * scale * 0.995f;
        var hips = new[] { Sk.Std[(int)J.HipL], Sk.Std[(int)J.HipR] }; var knees = new[] { Sk.Std[(int)J.KnL], Sk.Std[(int)J.KnR] }; var feet = new[] { Sk.Std[(int)J.FtL], Sk.Std[(int)J.FtR] };
        var target = new Vector3[2]; var ok = new bool[2]; float drop = 0;
        for (int s = 0; s < 2; s++)
        {
            var f = pose.Joint(feet[s]);
            float g = c.GroundAt(new Vector3(f.X, baseY, f.Z));
            if (float.IsNaN(g)) continue;
            float lift = MathF.Max(0, f.Y - (baseY + ankle));
            target[s] = new Vector3(f.X, g + ankle + lift, f.Z); ok[s] = true;
            var h = pose.Joint(hips[s]); var hd = new Vector2(h.X - target[s].X, h.Z - target[s].Z).LengthSquared();
            float need = h.Y - target[s].Y - MathF.Sqrt(MathF.Max(0, reach * reach - hd));
            drop = MathF.Max(drop, need);
        }
        if (!ok[0] && !ok[1]) return;
        drop = MathX.Clamp(drop, 0, 0.45f * scale) * w;
        int pelvis = Sk.Std[(int)J.Pelvis];
        if (drop > 0) { pose.Local[pelvis].Pos.Y -= drop / scale; Fk(pose); }
        for (int s = 0; s < 2; s++)
        {
            if (!ok[s]) continue;
            var h = pose.Joint(hips[s]); var k = pose.Joint(knees[s]); var f = pose.Joint(feet[s]);
            var footRot = Rotation(pose.World[feet[s]]);
            var goal = Vector3.Lerp(f, target[s], w);
            var pole = k - (h + f) * 0.5f; if (pole.LengthSquared() < 1e-6f) pole = Vector3.Transform(-Vector3.UnitZ, Rotation(pose.Root));
            var (nk, nf) = Skel.TwoBone(h, goal, Rig.Thigh * scale, Rig.Shin * scale, pole);
            AimBone(pose, hips[s], knees[s], nk);
            AimBone(pose, knees[s], feet[s], nf);
            // the foot keeps the angle the clip gave it
            var parentRot = Rotation(pose.World[knees[s]]);
            pose.Local[feet[s]].Rot = Quaternion.Normalize(Quaternion.Inverse(parentRot) * footRot);
            Fk(pose);
        }
    }

    static Quaternion Rotation(Matrix4x4 m) => Matrix4x4.Decompose(m, out _, out var q, out _) ? q : Quaternion.Identity;

    /// <summary>Turns a bone so its child joint points at a world position (no twist).</summary>
    void AimBone(EntityPose pose, int bone, int child, Vector3 target)
    {
        var jb = pose.Joint(bone);
        var cur = pose.Joint(child) - jb; var want = target - jb;
        if (cur.LengthSquared() < 1e-10f || want.LengthSquared() < 1e-10f) return;
        var qb = Rotation(pose.World[bone]);
        var qNew = Quaternion.Normalize(FromTo(Vector3.Normalize(cur), Vector3.Normalize(want)) * qb);
        var parentRot = Sk.Parent[bone] >= 0 ? Rotation(pose.World[Sk.Parent[bone]]) : Rotation(pose.Root);
        pose.Local[bone].Rot = Quaternion.Normalize(Quaternion.Inverse(parentRot) * qNew);
        Fk(pose);
    }

    // ---------------------------------------------------------------- joints to bones (ragdolls, old procedural layers)
    void Procedural(AnimContext c, Matrix4x4 root)
    {
        if (!Sk.Humanoid) return;
        Rig.Standing(joints, Vector3.Zero, 0, c.Time, c.Sway, c.ArmsUp);
        JointsToLocal(joints, true);
    }

    /// <summary>Turns model-space joint positions into local bone rotations (and the pelvis's translation) for the standard bones.</summary>
    void JointsToLocal(Pose o, bool flatFeet)
    {
        var modelRot = new Quaternion[Sk.Count];
        for (int i = 0; i < Sk.Count; i++)
        {
            var parentRot = Sk.Parent[i] >= 0 ? modelRot[Sk.Parent[i]] : Quaternion.Identity;
            int j = Array.IndexOf(Sk.Std, i);
            Quaternion world;
            if (j < 0) { modelRot[i] = parentRot; continue; }   // extra bones ride along; clip layers move them
            var J0 = (J)j;
            if (J0 == J.Pelvis) world = Basis((Sk.HasAbdomen ? o[J.Abdomen] : o[J.Chest]) - o[J.Pelvis], o[J.HipR] - o[J.HipL]);
            else if (J0 == J.Abdomen) world = Basis(o[J.Chest] - o[J.Abdomen], (o[J.HipR] - o[J.HipL]) + (o[J.ShR] - o[J.ShL]));
            else if (J0 == J.Chest) world = Basis(o[J.Neck] - o[J.Chest], o[J.ShR] - o[J.ShL]);
            else if (J0 == J.FtL || J0 == J.FtR) world = flatFeet ? Quaternion.Identity : parentRot;
            else if (J0 == J.Head || J0 == J.HaL || J0 == J.HaR) world = parentRot;
            else
            {
                J child = J0 switch { J.Neck => J.Head, J.ShL => J.ElL, J.ElL => J.HaL, J.ShR => J.ElR, J.ElR => J.HaR, J.HipL => J.KnL, J.KnL => J.FtL, J.HipR => J.KnR, _ => J.FtR };
                var rest = Sk.Offset[Sk.Std[(int)child]];
                var want = o[child] - o[J0];
                var restWorld = Vector3.Transform(rest, parentRot);
                world = rest.LengthSquared() < 1e-8f || want.LengthSquared() < 1e-8f ? parentRot : FromTo(Vector3.Normalize(restWorld), Vector3.Normalize(want)) * parentRot;
            }
            modelRot[i] = world;
            layer[i] = BoneXf.Rest;
            layer[i].Rot = Quaternion.Normalize(Quaternion.Inverse(parentRot) * world);
            if (J0 == J.Pelvis) layer[i].Pos = o[J.Pelvis] - Sk.RestModel[i];
            driven[i] = true;
        }
    }

    static Quaternion Basis(Vector3 up, Vector3 right)
    {
        up = up.LengthSquared() > 1e-8f ? Vector3.Normalize(up) : Vector3.UnitY;
        right -= up * Vector3.Dot(right, up);
        right = right.LengthSquared() > 1e-8f ? Vector3.Normalize(right) : Vector3.UnitX;
        var back = Vector3.Cross(right, up);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0, back.X, back.Y, back.Z, 0, 0, 0, 0, 1)));
    }

    /// <summary>The shortest rotation taking unit vector a onto unit vector b.</summary>
    public static Quaternion FromTo(Vector3 a, Vector3 b)
    {
        float d = Vector3.Dot(a, b);
        if (d > 0.99999f) return Quaternion.Identity;
        if (d < -0.99999f)
        {
            var axis = Vector3.Cross(Vector3.UnitX, a); if (axis.LengthSquared() < 1e-6f) axis = Vector3.Cross(Vector3.UnitY, a);
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI);
        }
        var c = Vector3.Cross(a, b);
        return Quaternion.Normalize(new Quaternion(c.X, c.Y, c.Z, 1 + d));
    }

    /// <summary>The humanoid joint positions of an evaluated pose, in world space (for ragdolls and hit tests).</summary>
    public void WorldJoints(EntityPose pose, Pose outJoints)
    {
        for (int j = 0; j < (int)J.Count; j++) outJoints.P[j] = pose.Joint(Sk.Std[j]);
        // a one-piece torso still gets a middle joint, so ragdolls bend there
        if (!Sk.HasAbdomen) outJoints[J.Abdomen] = (outJoints[J.Pelvis] + outJoints[J.Chest]) * 0.5f;
    }
}
