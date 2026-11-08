using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GermBall.Content;
using GermBall.Engine;
using GermBall.Sim;
using GermBall.Tools.Ui;

namespace GermBall.Tools.EntityEditor;

/// <summary>
/// The Entity Editor: build players, enemies and props from shapes on a rig, then animate them.
/// Left panel: Entity (stats), Rig (bones), Shapes. Right panel: Clips (keyframes), Layers (the animation pipeline),
/// States (which clip each game state plays), Preview (drive the procedural motion, test the ragdoll).
/// Bottom: the timeline. Centre: the 3D view.
/// Everything is saved to content/entities/&lt;name&gt;.entity.json, which the game loads.
/// </summary>
public sealed partial class EntityEditor
{
    public readonly ContentStore Store;
    public readonly Gui Ui = new();
    public readonly View3D View = new();
    public EntityDef Def;
    /// <summary>Starts the game with arguments (the exe host provides this).</summary>
    public Action<string> LaunchGame;
    public string Status = "Ready";

    EntityView ev; string rigKey;
    readonly List<string> undo = new(), redo = new();
    string committed, savedJson;
    float time;

    // selection and tabs
    public string SelBone; public int SelShape = -1, SelClip = 0, SelLayer = 0;
    public int LeftTab, RightTab;
    public float Playhead; public bool Playing, SoloClip = true, ClipOverProcedural = true;
    public KeyDef SelKey;

    // preview
    public readonly AnimContext Ctx = new() { State = "idle", Grounded = true };
    public bool ShowBones = true, ShowShapes = true, ShowGrid = true, ShowCollision, Turntable;
    public float PreviewYaw;
    Ragdoll rag; readonly StaticWorld floor = new();

    public const float Fps = 30;

    public EntityEditor(ContentStore store, string open = null)
    {
        Store = store;
        floor.Add(new Obb(new Vector3(0, -1, 0), new Vector3(50, 1, 50), Quaternion.Identity));
        Load(open ?? "hero");
    }

    public bool Dirty => Json.Write(Def) != savedJson;

    // ---------------------------------------------------------------- files
    public void Load(string name)
    {
        Store.Forget();
        Def = Json.Clone(Store.Entity(name));
        Def.Name = name;
        AfterLoad();
        Status = $"Opened {name}";
    }
    public void New(string template)
    {
        Def = template switch
        {
            "hero copy" => DefaultEntities.Hero(),
            "germ copy" => DefaultEntities.Coccus(),
            "empty" => new EntityDef { Bones = { new BoneDef { Name = "root", Offset = new Vector3(0, 0.5f, 0) } },
                                       Shapes = { new ShapeDef { Name = "body", Bone = "root", Type = "Box", Size = new Vector3(0.5f) } } },
            _ => NewHumanoid(),
        };
        Def.Name = "new_entity";
        AfterLoad();
        savedJson = null;
        Status = $"New entity from template: {template}";
    }
    static EntityDef NewHumanoid()
    {
        var e = new EntityDef { Kind = "enemy", Bones = DefaultEntities.HumanoidBones() };
        foreach (var b in e.Bones)
        {
            int child = e.Bones.FindIndex(x => x.Parent == b.Name);
            if (child >= 0 && b.Name != "pelvis" && b.Name != "chest")
            {
                float len = e.Bones[child].Offset.Length();
                e.Shapes.Add(new ShapeDef { Name = b.Name, Bone = b.Name, Type = "Capsule", Pos = e.Bones[child].Offset / 2, Size = new Vector3(0.12f, len + 0.12f, 0.12f), Color = "#B0B6C0", Rot = RotFor(e.Bones[child].Offset) });
            }
        }
        e.Shapes.Add(new ShapeDef { Name = "torso", Bone = "pelvis", Type = "Capsule", Pos = new Vector3(0, 0.22f, 0), Size = new Vector3(0.34f, 0.7f, 0.24f), Color = "#8090A0" });
        e.Shapes.Add(new ShapeDef { Name = "head", Bone = "head", Type = "Sphere", Size = new Vector3(0.26f), Color = "#B0B6C0" });
        e.Shapes.Add(new ShapeDef { Name = "hand.L", Bone = "hand.L", Type = "Sphere", Size = new Vector3(0.13f), Color = "#B0B6C0" });
        e.Shapes.Add(new ShapeDef { Name = "hand.R", Bone = "hand.R", Type = "Sphere", Size = new Vector3(0.13f), Color = "#B0B6C0" });
        e.Layers.Add(new LayerDef { Name = "Base", Source = "procedural" });
        return e;
    }
    /// <summary>The rotation (degrees) that turns a capsule's Y axis along a direction.</summary>
    public static Vector3 RotFor(Vector3 dir)
    {
        if (dir.LengthSquared() < 1e-8f) return Vector3.Zero;
        var q = Animator.FromTo(Vector3.UnitY, Vector3.Normalize(dir));
        return ToEuler(q);
    }
    /// <summary>Quaternion to the (pitch X, yaw Y, roll Z) degrees that <see cref="Animator.Euler"/> reads back.</summary>
    public static Vector3 ToEuler(Quaternion q)
    {
        // matches CreateFromYawPitchRoll (yaw about Y, then pitch about X, then roll about Z)
        float sinp = 2 * (q.W * q.X - q.Y * q.Z);
        float pitch = MathF.Abs(sinp) >= 1 ? MathF.CopySign(MathF.PI / 2, sinp) : MathF.Asin(sinp);
        float yaw = MathF.Atan2(2 * (q.W * q.Y + q.X * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y));
        float roll = MathF.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.X * q.X + q.Z * q.Z));
        return new Vector3(pitch, yaw, roll) / MathX.Deg;
    }

    void AfterLoad()
    {
        undo.Clear(); redo.Clear();
        committed = Json.Write(Def); savedJson = committed;
        ev = null; rigKey = null; rag = null;
        SelBone = Def.Bones.Count > 0 ? Def.Bones[0].Name : null; SelShape = Def.Shapes.Count > 0 ? 0 : -1; SelClip = 0; SelLayer = 0; SelKey = null; Playhead = 0;
        float hh = Def.Stats.Height * Def.Stats.Scale;
        View.Frame(new Vector3(0, hh * 0.5f, 0), hh * 0.6f);
    }

    public void Save()
    {
        if (string.IsNullOrWhiteSpace(Def.Name)) { Status = "Give it a name first"; return; }
        Def.Name = Def.Name.Trim();
        Store.SaveEntity(Json.Clone(Def));
        savedJson = Json.Write(Def);
        Status = $"Saved {Store.EntityPath(Def.Name)}";
    }

    public void TestInGame()
    {
        Save();
        if (LaunchGame == null) { Status = "Can't start the game from here"; return; }
        LaunchGame(Def.Kind == "player" ? $"--hero {Def.Name}" : $"--spawn {Def.Name}");
        Status = Def.Kind == "player" ? $"Started the game as {Def.Name}" : $"Started the game with a {Def.Name} in front of you";
    }

    // ---------------------------------------------------------------- undo
    /// <summary>Called once per frame: when an edit has finished (nothing being dragged), the previous state goes on the undo stack.</summary>
    void TrackUndo()
    {
        if (Ui.Dragging || View.GizmoActive) return;
        var now = Json.Write(Def);
        if (now == committed) return;
        undo.Add(committed); if (undo.Count > 200) undo.RemoveAt(0);
        redo.Clear(); committed = now;
    }
    public void Undo()
    {
        if (undo.Count == 0) { Status = "Nothing to undo"; return; }
        redo.Add(Json.Write(Def)); Restore(undo[^1]); undo.RemoveAt(undo.Count - 1); Status = "Undone";
    }
    public void Redo()
    {
        if (redo.Count == 0) { Status = "Nothing to redo"; return; }
        undo.Add(Json.Write(Def)); Restore(redo[^1]); redo.RemoveAt(redo.Count - 1); Status = "Redone";
    }
    void Restore(string json)
    {
        string name = Def.Name;
        Def = Json.Read<EntityDef>(json); committed = json; ev = null; rigKey = null;
        if (SelShape >= Def.Shapes.Count) SelShape = Def.Shapes.Count - 1;
        if (SelBone != null && Def.Bone(SelBone) == null) SelBone = Def.Bones.Count > 0 ? Def.Bones[0].Name : null;
        SelKey = null;
    }

    // ---------------------------------------------------------------- per frame
    public ClipDef CurClip => SelClip >= 0 && SelClip < Def.Clips.Count ? Def.Clips[SelClip] : null;
    public LayerDef CurLayer => SelLayer >= 0 && SelLayer < Def.Layers.Count ? Def.Layers[SelLayer] : null;
    public ShapeDef CurShape => SelShape >= 0 && SelShape < Def.Shapes.Count ? Def.Shapes[SelShape] : null;
    public Skeleton Sk => ev?.Sk;
    bool AnimTab => RightTab == 0;

    public void Frame(UiInput input, float dt, int w, int h, DrawList dl)
    {
        time += dt;
        Ui.S = h >= 860 ? 2 : 1;
        dl.Clear();
        Ui.Begin(input, dl.Overlay, w, h);
        float top = Ui.RowH + 2 * Ui.Pad, bottom = Ui.RowH + Ui.Pad, leftW = MathF.Max(300, w * 0.25f), rightW = MathF.Max(330, w * 0.27f);
        float timelineH = MathF.Max(110, h * 0.2f);
        View.Rect = new Vector4(leftW, top, w - leftW - rightW, h - top - bottom - timelineH);

        EnsureView();
        Toolbar(new Vector4(0, 0, w, top));
        LeftPanel(new Vector4(0, top, leftW, h - top - bottom));
        RightPanel(new Vector4(w - rightW, top, rightW, h - top - bottom));
        Timeline(new Vector4(leftW, h - bottom - timelineH, w - leftW - rightW, timelineH), dt);
        Ui.Bar(new Vector4(0, h - bottom, w, bottom), (Dirty ? "* unsaved   " : "") + Status +
            "      view: right-drag orbit, middle-drag pan, wheel zoom, F frame, click to select");

        Shortcuts(input);
        Scene(input, dt, w, h, dl);
        Ui.End();
        TrackUndo();
    }

    void Shortcuts(UiInput i)
    {
        if (Ui.TextFocused) return;
        if (i.Ctrl && i.Key('Z')) { if (i.Shift) Redo(); else Undo(); }
        if (i.Ctrl && i.Key('Y')) Redo();
        if (i.Ctrl && i.Key('S')) Save();
        if (i.Key(Keys.Space) && !i.Ctrl) Playing = !Playing;
        if (i.Key('F') && !i.Ctrl) FrameSelection();
        if (i.Key(Keys.Delete))
        {
            if (AnimTab && SelKey != null) DeleteSelectedKey();
            else if (LeftTab == 2 && CurShape != null) DeleteShape();
        }
        if (i.Ctrl && i.Key('D') && LeftTab == 2 && CurShape != null) DuplicateShape();
    }

    void EnsureView()
    {
        // the rig is compiled once per change of bones; the animator keeps its clocks otherwise
        var key = string.Join(";", Def.Bones.Select(b => $"{b.Name}<{b.Parent}@{b.Offset}")) + "|" + Def.Kind;
        if (ev == null || key != rigKey || ev.Def != Def) { ev = new EntityView(Def); rigKey = key; }
    }

    void FrameSelection()
    {
        if (ev == null) return;
        var c = Vector3.Zero; float r = 1;
        if (LeftTab == 2 && CurShape != null) { c = Vector3.Transform(Vector3.Zero, ev.ShapeMatrix(CurShape)); r = MathF.Max(0.3f, CurShape.Size.Length()); }
        else if (SelBone != null && Sk.IndexOf(SelBone) >= 0) { c = ev.Pose.Joint(Sk.IndexOf(SelBone)); r = 0.5f; }
        else { c = new Vector3(0, Def.Stats.Height * Def.Stats.Scale * 0.5f, 0); r = Def.Stats.Height * Def.Stats.Scale * 0.6f; }
        View.Frame(c, r);
    }

    // ---------------------------------------------------------------- the 3D view
    void Scene(UiInput input, float dt, int w, int h, DrawList dl)
    {
        EnsureView();   // undo, redo and loading (from the shortcuts) can replace the entity mid-frame
        View.Controls(input, Ui.MouseOverUi, !Ui.TextFocused, dt);
        View.Setup(dl, w, h);
        if (Turntable) PreviewYaw += dt * 0.6f;

        // pose: the solo clip at the playhead while animating, the full layer stack otherwise
        if (Playing && CurClip != null && AnimTab) { Playhead += dt; if (Playhead > CurClip.Length) Playhead = CurClip.Loop ? Playhead % MathF.Max(1e-3f, CurClip.Length) : CurClip.Length; }
        Ctx.Dt = Playing || !AnimTab || !SoloClip ? dt : 0; Ctx.Time = time; Ctx.Feet = Vector3.Zero; Ctx.Yaw = PreviewYaw;
        Ctx.GroundAt = p => 0; Ctx.Joints = null; Ctx.Spin = Quaternion.Identity;
        Ctx.MoveDir = MathX.Forward(PreviewYaw, 0);
        Ctx.AimDir = MathX.Forward(PreviewYaw + aimYaw * MathX.Deg, aimPitch * MathX.Deg);
        PunchT = MathF.Max(0, PunchT - dt); ReloadT = MathF.Max(0, ReloadT - dt);
        Ctx.PunchT = PunchT; Ctx.ReloadT = ReloadT;
        if (rag != null) { rag.Step(floor, dt, Player.Gravity); Ctx.Joints = rag.Pos; }
        ev.Update(Ctx);
        if (AnimTab && SoloClip && CurClip != null && rag == null) ev.Anim.EvaluateSolo(Ctx, ev.Pose, ev.Scale, CurClip, Playhead, ClipOverProcedural, PreviewAdditive);
        else ev.Evaluate(Ctx);

        if (ShowGrid) View.Grid(dl, 3, 0.5f);
        dl.Add(MeshId.Cylinder, MathX.Ts(new Vector3(0, 0.002f, 0), new Vector3(Def.Stats.Radius * 2.2f * Def.Stats.Scale, 0.002f, Def.Stats.Radius * 2.2f * Def.Stats.Scale)), new Vector4(0, 0, 0, 0.25f), DrawFlags.Unlit);

        bool curled = Ctx.Rolling && Def.CurlStyle == "ball";
        if (ShowShapes && !curled) ev.Draw(dl, false, 1, LeftTab == 2 ? SelShape : -1);
        if (curled) dl.Add(MeshId.BallStriped, MathX.Ts(new Vector3(0, Player.BallRadius, 0), new Vector3(Player.BallRadius * 2)), ColorHex.Parse(Def.BallColor));
        if (ShowCollision)
        {
            float r = Def.Stats.Radius * Def.Stats.Scale, hh = Def.Stats.Height * Def.Stats.Scale;
            dl.Add(MeshId.Cylinder, MathX.Ts(new Vector3(0, hh / 2, 0), new Vector3(r * 2, hh, r * 2)), new Vector4(0.3f, 0.8f, 1, 0.18f), DrawFlags.Unlit);
        }
        if (LeftTab == 2 && CurShape != null && ShowShapes) View3D.WireBox(dl, ev.ShapeMatrix(CurShape), new Vector4(1, 0.85f, 0.2f, 1), 0.004f * View.Dist);
        if (ShowBones) DrawBones(dl);

        // picking and the gizmo
        bool uiMouse = Ui.MouseOverUi;
        var giz = Vector3.Zero;
        if (LeftTab == 1 && SelBone != null && Sk.IndexOf(SelBone) >= 0 && rag == null)
        {
            int b = Sk.IndexOf(SelBone); var parent = Sk.Parent[b] >= 0 ? ev.Pose.World[Sk.Parent[b]] : ev.Pose.Root;
            giz = View.Gizmo(dl, input, ev.Pose.Joint(b), uiMouse, parent, false);
            if (giz != Vector3.Zero) { var bd = Def.Bone(SelBone); bd.Offset += ToLocal(giz, parent); }
        }
        else if (LeftTab == 2 && CurShape != null && rag == null)
        {
            int b = Sk.IndexOf(CurShape.Bone); var bm = b >= 0 ? ev.Pose.World[b] : ev.Pose.Root;
            var center = Vector3.Transform(Vector3.Zero, ev.ShapeMatrix(CurShape));
            giz = View.Gizmo(dl, input, center, uiMouse, bm, false);
            if (giz != Vector3.Zero) CurShape.Pos += ToLocal(giz, bm) / (b >= 0 ? ev.Pose.Local[b].Scale : Vector3.One);
        }
        if (!uiMouse && View.MouseIn(input) && input.Pressed[0] && !View.GizmoActive) Pick(input);
    }

    float aimYaw, aimPitch; public float PunchT, ReloadT;

    /// <summary>A world-space movement as an offset in a frame's local axes (its scale removed).</summary>
    static Vector3 ToLocal(Vector3 worldDelta, Matrix4x4 frame)
    {
        Matrix4x4.Invert(frame, out var inv);
        return Vector3.TransformNormal(worldDelta, inv);
    }

    void DrawBones(DrawList dl)
    {
        if (ev.Pose.World.Length != Sk.Count) return;
        float w = 0.0035f * View.Dist;
        for (int i = 0; i < Sk.Count; i++)
        {
            var p = ev.Pose.Joint(i);
            bool sel = Sk.Names[i] == SelBone;
            var c = sel ? new Vector4(1, 0.85f, 0.2f, 1) : new Vector4(0.9f, 0.95f, 1, 0.85f);
            if (Sk.Parent[i] >= 0) View3D.Line(dl, ev.Pose.Joint(Sk.Parent[i]), p, sel ? c : new Vector4(0.55f, 0.8f, 1, 0.8f), w, true);
            dl.Sphere(p, w * (sel ? 3.2f : 2.2f), c, DrawFlags.Unlit | DrawFlags.OnTop);
        }
    }

    void Pick(UiInput input)
    {
        // bones first on the Rig tab (and in the Clips tab, to pick what to key), shapes on the Shapes tab
        if (ShowBones && (LeftTab == 1 || AnimTab))
        {
            var m = new Vector2(input.MouseX, input.MouseY); int best = -1; float bd = 12;
            for (int i = 0; i < Sk.Count; i++)
                if (View.Project(ev.Pose.Joint(i), out var s) && Vector2.Distance(s, m) < bd) { bd = Vector2.Distance(s, m); best = i; }
            if (best >= 0) { SelBone = Sk.Names[best]; SelKey = null; return; }
        }
        if (ShowShapes)
        {
            var (o, d) = View.Ray(input); float bt = float.MaxValue; int bs = -1;
            for (int i = 0; i < Def.Shapes.Count; i++)
            {
                if (Def.Shapes[i].Hidden) continue;
                if (View3D.RayBox(o, d, ev.ShapeMatrix(Def.Shapes[i]), out float t) && t < bt) { bt = t; bs = i; }
            }
            if (bs >= 0) { SelShape = bs; SelBone = Def.Shapes[bs].Bone; if (LeftTab == 0) LeftTab = 2; }
        }
    }

    // ---------------------------------------------------------------- ragdoll test
    public void DropRagdoll()
    {
        EnsureView();
        if (!Sk.Humanoid) { Status = "Ragdolls need the 16 humanoid bones (Rig tab: Reset to humanoid)"; return; }
        var joints = new Pose(); Ctx.Joints = null; ev.Evaluate(Ctx); ev.Anim.WorldJoints(ev.Pose, joints);
        rag = new Ragdoll(joints, MathX.Forward(PreviewYaw, 0) * -3 + new Vector3(0, 3, 0), ev.Scale, new Vector3(1.5f, 0, 0.8f));
        Status = "Ragdoll dropped (Stop ragdoll to stand back up)";
    }
    public void StopRagdoll() => rag = null;
    public bool RagdollActive => rag != null;
}
