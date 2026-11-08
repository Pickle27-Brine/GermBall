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
        var e = new EntityDef { Kind = "enemy", Bones = DefaultEntities.HumanoidBones(abdomen: true) };
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
    /// <summary>The entity as last drawn (pose, shapes), for tests and tools.</summary>
    public EntityView Preview => ev;
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
        if (!View.Looking && !i.Ctrl)
        {
            if (i.Key('W')) Mode = GizmoMode.Move;
            if (i.Key('E')) Mode = GizmoMode.Rotate;
            if (i.Key('R')) Mode = GizmoMode.Scale;
        }
        if (i.Key('F') && !i.Ctrl) FrameSelection();
        if (i.Key(Keys.Delete))
        {
            if (Target == EditTarget.Pose && SelKey != null) DeleteSelectedKey();
            else if (Target == EditTarget.Shape && CurShape != null) DeleteShape();
        }
        if (i.Ctrl && i.Key('D') && Target == EditTarget.Shape && CurShape != null) DuplicateShape();
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
        if (Target == EditTarget.Shape && CurShape != null) { c = Vector3.Transform(Vector3.Zero, ev.ShapeMatrix(CurShape)); r = MathF.Max(0.3f, CurShape.Size.Length()); }
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
        // what's under the mouse (pre-highlighted, like Blender), then the model with the selection lit
        bool uiMouse = Ui.MouseOverUi;
        HoverShape = !uiMouse && View.MouseIn(input) && !View.GizmoActive && !View.Looking && ShowShapes && !curled ? ShapeUnder(input) : -1;
        if (ShowShapes && !curled) ev.Draw(dl, false, 1, Target == EditTarget.Shape ? SelShape : -1, 1, HoverShape);
        if (curled) dl.Add(MeshId.BallStriped, MathX.Ts(new Vector3(0, Player.BallRadius, 0), new Vector3(Player.BallRadius * 2)), ColorHex.Parse(Def.BallColor));
        if (ShowCollision)
        {
            float r = Def.Stats.Radius * Def.Stats.Scale, hh = Def.Stats.Height * Def.Stats.Scale;
            dl.Add(MeshId.Cylinder, MathX.Ts(new Vector3(0, hh / 2, 0), new Vector3(r * 2, hh, r * 2)), new Vector4(0.3f, 0.8f, 1, 0.18f), DrawFlags.Unlit);
        }
        if (Target == EditTarget.Shape && CurShape != null && ShowShapes) View3D.WireBox(dl, ShapeBounds(CurShape), new Vector4(1, 0.85f, 0.2f, 1), 0.004f * View.Dist);
        if (ShowBones) DrawBones(dl);

        if (rag == null) Gizmos(input, dl, uiMouse);
        if (!uiMouse && View.MouseIn(input) && input.Pressed[0] && !View.GizmoActive) Pick(input);
        // what the gizmo is working on, in the corner of the view
        var vr = View.Rect;
        dl.Overlay.TextShadow(vr.X + Ui.Pad, vr.Y + Ui.Pad, GizmoLabel(), Ui.S, new Vector4(1, 1, 1, 0.9f));
    }

    // ---------------------------------------------------------------- gizmos
    public enum GizmoMode { Move, Rotate, Scale }
    /// <summary>What the gizmo and clicks work on: the rig's rest pose (Rig tab), a shape (Shapes tab), or the clip's pose (Clips tab).</summary>
    public enum EditTarget { Rig, Shape, Pose }
    public GizmoMode Mode = GizmoMode.Move;
    public EditTarget Target = EditTarget.Shape;
    public int HoverShape = -1;

    string GizmoLabel()
    {
        string what = Target switch
        {
            EditTarget.Shape => CurShape != null ? $"shape {CurShape.Name}" : "click a shape",
            EditTarget.Rig => SelBone != null ? $"bone {SelBone} (rest pose)" : "click a bone",
            _ => CurClip == null ? "make a clip to pose bones" : SelBone != null ? $"pose {SelBone} in {CurClip.Name} at {Playhead:0.00}s" : "click a bone to pose it",
        };
        return $"{Mode.ToString().ToUpperInvariant()} (W E R)   {what}";
    }

    void Gizmos(UiInput input, DrawList dl, bool uiMouse)
    {
        switch (Target)
        {
            case EditTarget.Shape:
                {
                    var c = CurShape; if (c == null) return;
                    int b = Sk.IndexOf(c.Bone); var bm = b >= 0 ? ev.Pose.World[b] : ev.Pose.Root;
                    var frame = ev.ShapeMatrix(c, Vector3.One);
                    var center = frame.Translation;
                    var boneScale = b >= 0 ? ev.Pose.Local[b].Scale : Vector3.One;
                    if (Mode == GizmoMode.Move)
                    {
                        var g = View.Gizmo(dl, input, center, uiMouse, bm, false);
                        if (g != Vector3.Zero) c.Pos += ToLocal(g, bm) / boneScale;
                    }
                    else if (Mode == GizmoMode.Rotate)
                    {
                        // turn about the shape's own axes
                        var (ax, ang) = View.RotateGizmo(dl, input, center, uiMouse, frame);
                        if (ax >= 0 && ang != 0) c.Rot = ToEuler(Quaternion.Normalize(Animator.Euler(c.Rot) * Quaternion.CreateFromAxisAngle(Unit(ax), ang)));
                    }
                    else
                    {
                        var (ax, f) = View.ScaleGizmo(dl, input, center, uiMouse, frame);
                        if (ax == 3) c.Size *= f;
                        else if (ax >= 0) c.Size = Scale(c.Size, ax, f);
                    }
                    break;
                }
            case EditTarget.Rig:
                {
                    var bd = SelBone != null ? Def.Bone(SelBone) : null; int b = Sk.IndexOf(SelBone); if (bd == null || b < 0) return;
                    var parent = Sk.Parent[b] >= 0 ? ev.Pose.World[Sk.Parent[b]] : ev.Pose.Root;
                    var joint = ev.Pose.Joint(b);
                    if (Mode == GizmoMode.Move)
                    {
                        var g = View.Gizmo(dl, input, joint, uiMouse, parent, false);
                        if (g != Vector3.Zero) bd.Offset += ToLocal(g, parent);
                    }
                    else if (Mode == GizmoMode.Rotate)
                    {
                        // re-aim everything hanging off this joint: its children and its shapes turn about it
                        var (ax, ang) = View.RotateGizmo(dl, input, joint, uiMouse, ev.Pose.World[b]);
                        if (ax >= 0 && ang != 0) RotateRest(bd.Name, Quaternion.CreateFromAxisAngle(Unit(ax), ang));
                    }
                    else
                    {
                        var (ax, f) = View.ScaleGizmo(dl, input, joint, uiMouse, ev.Pose.World[b]);
                        if (ax >= 0) ScaleRest(bd.Name, ax == 3 ? new Vector3(f) : Scale(Vector3.One, ax, f));
                    }
                    break;
                }
            case EditTarget.Pose:
                {
                    var clip = CurClip; int b = Sk.IndexOf(SelBone); if (clip == null || b < 0) return;
                    var tr = clip.Track(SelBone);
                    var k = tr != null ? KeyValue(tr, clip) : new KeyDef { T = Playhead };
                    var parent = Sk.Parent[b] >= 0 ? ev.Pose.World[Sk.Parent[b]] : ev.Pose.Root;
                    var joint = ev.Pose.Joint(b);
                    if (Mode == GizmoMode.Move)
                    {
                        var g = View.Gizmo(dl, input, joint, uiMouse, parent, false);
                        if (g != Vector3.Zero) SetKey(clip, SelBone, k.Rot, k.Pos + ToLocal(g, parent), k.Scale);
                    }
                    else if (Mode == GizmoMode.Rotate)
                    {
                        var (ax, ang) = View.RotateGizmo(dl, input, joint, uiMouse, ev.Pose.World[b]);
                        if (ax >= 0 && ang != 0) SetKey(clip, SelBone, ToEuler(Quaternion.Normalize(Animator.Euler(k.Rot) * Quaternion.CreateFromAxisAngle(Unit(ax), ang))), k.Pos, k.Scale);
                    }
                    else
                    {
                        var (ax, f) = View.ScaleGizmo(dl, input, joint, uiMouse, ev.Pose.World[b]);
                        if (ax >= 0) SetKey(clip, SelBone, k.Rot, k.Pos, ax == 3 ? k.Scale * f : Scale(k.Scale, ax, f));
                    }
                    break;
                }
        }
    }

    static Vector3 Unit(int axis) => axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ;
    static Vector3 Scale(Vector3 v, int axis, float f) => axis == 0 ? v with { X = v.X * f } : axis == 1 ? v with { Y = v.Y * f } : v with { Z = v.Z * f };

    /// <summary>Turns a bone's rest pose: its child bones' offsets and its shapes rotate about the joint.</summary>
    public void RotateRest(string bone, Quaternion q)
    {
        foreach (var c in Def.Bones) if (c.Parent == bone) c.Offset = Vector3.Transform(c.Offset, q);
        foreach (var s in Def.Shapes)
            if (s.Bone == bone) { s.Pos = Vector3.Transform(s.Pos, q); s.Rot = ToEuler(Quaternion.Normalize(q * Animator.Euler(s.Rot))); }
    }

    /// <summary>Scales a bone's rest pose along its axes: child offsets, and its shapes' positions and sizes.</summary>
    public void ScaleRest(string bone, Vector3 f)
    {
        foreach (var c in Def.Bones) if (c.Parent == bone) c.Offset *= f;
        foreach (var s in Def.Shapes)
            if (s.Bone == bone)
            {
                s.Pos *= f;
                // the size stretches along whichever of the shape's own axes lines up with each bone axis
                var q = Animator.Euler(s.Rot); var size = s.Size;
                for (int k = 0; k < 3; k++)
                {
                    if (MathF.Abs(f.X - 1) < 1e-6f && MathF.Abs(f.Y - 1) < 1e-6f && MathF.Abs(f.Z - 1) < 1e-6f) break;
                    var la = Vector3.Abs(Vector3.Transform(Unit(k), q));
                    float fk = la.X * f.X + la.Y * f.Y + la.Z * f.Z;
                    size = Scale(size, k, fk / MathF.Max(1e-4f, la.X + la.Y + la.Z));
                }
                s.Size = size;
            }
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

    /// <summary>A click in the view: joints (on the Rig and Clips targets) or the piece of the model under the mouse.
    /// Clicking a piece selects that shape and its bone, like clicking a mesh in Blender.</summary>
    void Pick(UiInput input)
    {
        if (ShowBones && Target != EditTarget.Shape)
        {
            var m = new Vector2(input.MouseX, input.MouseY); int best = -1; float bd = 12;
            for (int i = 0; i < Sk.Count; i++)
                if (View.Project(ev.Pose.Joint(i), out var s) && Vector2.Distance(s, m) < bd) { bd = Vector2.Distance(s, m); best = i; }
            if (best >= 0) { SelBone = Sk.Names[best]; SelKey = null; return; }
        }
        int hit = ShapeUnder(input);
        if (hit < 0) return;
        SelShape = hit; SelBone = Def.Shapes[hit].Bone; SelKey = null;
        if (Target == EditTarget.Shape) { LeftTab = 2; OpenPartOf(Def.Shapes[hit]); }
        Status = Target == EditTarget.Shape ? $"Selected shape {Def.Shapes[hit].Name}" : $"Selected bone {SelBone} (via {Def.Shapes[hit].Name})";
    }

    /// <summary>The nearest visible shape under the mouse, tested against its real form (sphere, cylinder, capsule or box).</summary>
    public int ShapeUnder(UiInput input)
    {
        var (o, d) = View.Ray(input); float bt = float.MaxValue; int bs = -1;
        for (int i = 0; i < Def.Shapes.Count; i++)
        {
            var s = Def.Shapes[i]; if (s.Hidden) continue;
            if (RayShape(o, d, s, out float t) && t < bt) { bt = t; bs = i; }
        }
        return bs;
    }

    /// <summary>A box around a shape, taper and curve included (for picking and the selection outline).</summary>
    public Matrix4x4 ShapeBounds(ShapeDef s)
    {
        var size = Vector3.Abs(s.Size);
        if (MathF.Abs(s.Curve) < 0.004f) return ev.ShapeMatrix(s, size);
        float bend = s.Curve * size.Y;
        return Matrix4x4.CreateScale(size.X + MathF.Abs(bend), size.Y, size.Z) * Matrix4x4.CreateTranslation(bend / 2, 0, 0) * ev.ShapeMatrix(s, Vector3.One);
    }

    bool RayShape(Vector3 o, Vector3 d, ShapeDef s, out float t)
    {
        t = 0;
        bool deformed = MathF.Abs(s.Taper) > 0.004f || MathF.Abs(s.Curve) > 0.004f;
        if (deformed || s.Type is "Box" or "Cone") return View3D.RayBox(o, d, ShapeBounds(s), out t);
        // into the shape's frame at its real size
        if (!Matrix4x4.Invert(ev.ShapeMatrix(s, Vector3.One), out var inv)) return false;
        var lo = Vector3.Transform(o, inv); var ld = Vector3.TransformNormal(d, inv);
        var size = Vector3.Abs(s.Size);
        // squash the shape into a round one: x and z by their radii
        var k = new Vector3(2 / MathF.Max(1e-4f, size.X), 1, 2 / MathF.Max(1e-4f, size.Z));
        var po = lo * k; var pd = ld * k; float rr = 1;
        float best = float.MaxValue;
        void Sphere(Vector3 c, float r, float ys)
        {
            // a sphere stretched by ys along y (in the squashed frame)
            var q = new Vector3(1, 1 / ys, 1); var a = (po - c) * q; var b = pd * q;
            float A = Vector3.Dot(b, b), B = Vector3.Dot(a, b), C = Vector3.Dot(a, a) - r * r, disc = B * B - A * C;
            if (disc < 0 || A < 1e-12f) return;
            float tt = (-B - MathF.Sqrt(disc)) / A; if (tt >= 0 && tt < best) best = tt;
        }
        void Cyl(float y0, float y1)
        {
            float A = pd.X * pd.X + pd.Z * pd.Z, B = po.X * pd.X + po.Z * pd.Z, C = po.X * po.X + po.Z * po.Z - rr, disc = B * B - A * C;
            if (A > 1e-12f && disc >= 0) { float tt = (-B - MathF.Sqrt(disc)) / A; float y = po.Y + pd.Y * tt; if (tt >= 0 && y >= y0 && y <= y1 && tt < best) best = tt; }
            foreach (float yc in new[] { y0, y1 })
                if (MathF.Abs(pd.Y) > 1e-9f) { float tt = (yc - po.Y) / pd.Y; float x = po.X + pd.X * tt, z = po.Z + pd.Z * tt; if (tt >= 0 && x * x + z * z <= rr && tt < best) best = tt; }
        }
        switch (s.Type)
        {
            case "Sphere": case "Ball": Sphere(Vector3.Zero, 1, MathF.Max(1e-4f, size.Y / 2)); break;
            case "Cylinder": Cyl(-size.Y / 2, size.Y / 2); break;
            case "Capsule":
                {
                    float r = size.X / 2, seg = MathF.Max(0, size.Y / 2 - r);
                    Cyl(-seg, seg);
                    Sphere(new Vector3(0, seg, 0), 1, MathF.Max(1e-4f, r)); Sphere(new Vector3(0, -seg, 0), 1, MathF.Max(1e-4f, r));
                    break;
                }
        }
        if (best == float.MaxValue) return false;
        t = Vector3.Distance(o, Vector3.Transform(lo + ld * best, ev.ShapeMatrix(s, Vector3.One)));
        return true;
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
