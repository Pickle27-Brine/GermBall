using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using GermBall.Content;
using GermBall.Engine;
using GermBall.Sim;
using GermBall.Tools.Ui;

namespace GermBall.Tools.EntityEditor;

/// <summary>
/// The Entity Editor's panels. Left: Model (shapes by body part), Rig (bones), Settings (gameplay, curl, limbs).
/// Right: Animate (clips and keys), Layers (the animation pipeline and which clip each state plays), Preview.
/// Help lives in tooltips (hover a control) rather than lines of text.
/// </summary>
public sealed partial class EntityEditor
{
    public const int TabModel = 0, TabRig = 1, TabSettings = 2;
    public const int TabAnimate = 0, TabLayers = 1, TabPreview = 2;
    string newShapeType = "Capsule";
    bool hdrForm, hdrPoseTools, hdrMask, hdrActive, hdrStateClips = true, hdrBlend = true, hdrLook = true, hdrLimbs = true, hdrGame = true, hdrRagdoll = true, hdrView, hdrTurn = true;
    static readonly string[] Kinds = { "player", "enemy", "prop" }, CurlStyles = { "ball", "pose" }, Eases = { "smooth", "linear", "step" };
    static readonly string[] Templates = { "humanoid", "hero copy", "germ copy", "empty" };
    string BoneLabel(string bone) { var f = BoneNames.Friendly(bone); return f == bone || bone == null ? f : $"{f}  ({bone})"; }
    List<string> BoneList() => Def.Bones.Select(b => b.Name).ToList();

    // ---------------------------------------------------------------- toolbar
    void Toolbar(Vector4 r)
    {
        // the music player takes what's left at the right end
        float musicW = Music != null ? MusicBar.Width(Ui, r.Z - (81 * Ui.CharW + 12 * Ui.Gap + 2 * Ui.Pad)) : 0;
        if (musicW > 0) MusicBar.Draw(Ui, Music, new Vector4(r.X + r.Z - musicW, r.Y, musicW, r.W));
        r.Z -= musicW;
        Ui.BeginArea("toolbar", r, Gui.TitleBg);
        float bw = 7 * Ui.CharW;
        var n = Ui.Menu("New...", Templates, 9 * Ui.CharW); if (n != null) New(n);
        Ui.SameLine(); var o = Ui.Menu("Open...", Store.EntityNames().ToList(), 10 * Ui.CharW); if (o != null) Load(o);
        Ui.SameLine(); if (Ui.Button("Save", bw)) Save(); Ui.Hint("Ctrl+S. Saves content/entities/<name>.entity.json");
        Ui.SameLine(); if (Ui.Button("Undo", bw)) Undo(); Ui.Hint("Ctrl+Z");
        Ui.SameLine(); if (Ui.Button("Redo", bw)) Redo(); Ui.Hint("Ctrl+Y");
        Ui.SameLine(); Ui.Button("##gap", 2 * Ui.CharW, false);
        Ui.SameLine(); if (Ui.Toggle("Move", Mode == GizmoMode.Move, bw)) Mode = GizmoMode.Move; Ui.Hint("W");
        Ui.SameLine(); if (Ui.Toggle("Rotate", Mode == GizmoMode.Rotate, 9 * Ui.CharW)) Mode = GizmoMode.Rotate; Ui.Hint("E");
        Ui.SameLine(); if (Ui.Toggle("Scale", Mode == GizmoMode.Scale, bw)) Mode = GizmoMode.Scale; Ui.Hint("R");
        Ui.SameLine(); Ui.Button("##gap2", 2 * Ui.CharW, false);
        Ui.SameLine(); if (Ui.Button("Play in game", 14 * Ui.CharW, true, Gui.AccentDim)) TestInGame();
        Ui.Hint(Def.Kind == "player" ? "saves, then plays the game as this entity" : "saves, then starts the game with one in front of you");
        Ui.EndArea();
    }

    // ---------------------------------------------------------------- left
    void LeftPanel(Vector4 r)
    {
        Ui.BeginPanel("left", r.X, r.Y, r.Z, r.W);
        if (Ui.Tabs(ref LeftTab, "Model", "Rig", "Settings")) { if (LeftTab == TabModel) Target = EditTarget.Shape; if (LeftTab == TabRig) Target = EditTarget.Rig; }
        switch (LeftTab)
        {
            case TabModel: ModelTab(); break;
            case TabRig: RigTab(); break;
            default: SettingsTab(); break;
        }
        Ui.EndPanel();
    }

    // ---- model: shapes by body part
    static readonly string[] PartNames = { "Head", "Torso", "Left arm", "Right arm", "Left leg", "Right leg", "Other" };
    readonly Dictionary<string, bool> partOpen = new();

    /// <summary>Which body part a bone belongs to: its own name or its nearest standard ancestor's.</summary>
    public string PartOf(string bone)
    {
        for (var b = bone != null ? Def.Bone(bone) : null; b != null; b = Def.Bone(b.Parent))
        {
            switch (b.Name)
            {
                case "neck": case "head": return "Head";
                case "pelvis": case "abdomen": case "chest": return "Torso";
                case "shoulder.L": case "elbow.L": case "hand.L": return "Left arm";
                case "shoulder.R": case "elbow.R": case "hand.R": return "Right arm";
                case "hip.L": case "knee.L": case "foot.L": return "Left leg";
                case "hip.R": case "knee.R": case "foot.R": return "Right leg";
            }
        }
        return "Other";
    }
    static string PartBone(string part) => part switch
    {
        "Head" => "head", "Torso" => "chest", "Left arm" => "elbow.L", "Right arm" => "elbow.R", "Left leg" => "knee.L", "Right leg" => "knee.R", _ => null,
    };
    void OpenPartOf(ShapeDef s) => partOpen[PartOf(s.Bone)] = true;

    void ModelTab()
    {
        float w4 = Ui.Split(4);
        if (Ui.Button("Add", w4)) AddShape(); Ui.Hint($"adds a {newShapeType} on the selected bone");
        Ui.SameLine(); if (Ui.Button("Copy", w4, CurShape != null)) DuplicateShape(); Ui.Hint("Ctrl+D");
        Ui.SameLine(); if (Ui.Button("Mirror", w4, CurShape != null)) MirrorShape(); Ui.Hint("makes (or updates) the matching shape on the other side");
        Ui.SameLine(); if (Ui.Button("Delete", w4, CurShape != null, Gui.Danger)) DeleteShape(); Ui.Hint("Del");
        Ui.Combo("New shape", ref newShapeType, ShapeTypes.All);

        var c = CurShape;
        if (c != null)
        {
            Ui.Heading(c.Name);
            Ui.TextField("Name", ref c.Name);
            string tp = c.Type; if (Ui.Combo("Shape", ref tp, ShapeTypes.All)) c.Type = tp;
            string bone = c.Bone; if (Ui.Combo("Attached to", ref bone, BoneList(), "(origin)")) c.Bone = bone;
            Ui.Hint(BoneLabel(c.Bone));
            Ui.Vec3("Position", ref c.Pos, 0.002f);
            Ui.Vec3("Rotation", ref c.Rot, 0.5f, "0.0");
            Ui.Vec3("Size", ref c.Size, 0.002f);
            Ui.Color("Colour", ref c.Color);
            if (Ui.Header("Form", ref hdrForm))
            {
                Ui.Slider("Taper", ref c.Taper, -1, 1); Ui.Hint("+ narrows the top end, - the bottom end");
                Ui.Slider("Curve", ref c.Curve, -1, 1); Ui.Hint("bends it along its length toward +X (rotate the shape to bend another way)");
                if (Ui.Button("Fit to bone", Ui.Split(2)) && c.Bone != null) FitToBone(c);
                Ui.Hint("lines it up along the bone, from its joint to the next");
                Ui.SameLine(); if (Ui.Button("Reset turn", Ui.Split(2))) c.Rot = Vector3.Zero;
                Ui.Checkbox("Glow (unlit)", ref c.Unlit);
                Ui.Checkbox("Hidden", ref c.Hidden);
            }
        }
        else Ui.Label("Click a piece of the model to edit it.", Gui.Dim);

        Space();
        foreach (var part in PartNames)
        {
            var idx = Enumerable.Range(0, Def.Shapes.Count).Where(i => PartOf(Def.Shapes[i].Bone) == part).ToList();
            if (idx.Count == 0) continue;
            bool open = partOpen.TryGetValue(part, out var po) && po;
            Ui.Header($"{part} ({idx.Count})##part", ref open);
            partOpen[part] = open;
            if (!open) continue;
            foreach (int i in idx)
            {
                var sh = Def.Shapes[i];
                if (Ui.Selectable($"{sh.Name}##s{i}", i == SelShape, 2 * Ui.S, sh.Hidden ? Gui.Dim : null)) { SelShape = i; SelBone = sh.Bone; Target = EditTarget.Shape; }
            }
        }
    }
    void Space() => Ui.Space(Ui.Gap * 2);

    void FitToBone(ShapeDef c)
    {
        var kid = TailChild(c.Bone);
        if (kid == null) return;
        c.Rot = RotFor(kid.Offset); c.Pos = kid.Offset / 2;
        if (c.Type is "Capsule" or "Cylinder") c.Size.Y = kid.Offset.Length() + (c.Type == "Capsule" ? c.Size.X : 0);
    }

    void AddShape(string onBone = null)
    {
        var bone = onBone ?? SelBone ?? (Def.Bones.Count > 0 ? Def.Bones[0].Name : null);
        var s = new ShapeDef { Name = newShapeType.ToLowerInvariant() + (Def.Shapes.Count + 1), Bone = bone, Type = newShapeType, Size = newShapeType is "Capsule" or "Cylinder" ? new Vector3(0.15f, 0.4f, 0.15f) : new Vector3(0.2f), Color = "#D0D4DA" };
        Def.Shapes.Add(s); SelShape = Def.Shapes.Count - 1; SelBone = bone; Target = EditTarget.Shape; partOpen[PartOf(bone)] = true;
        Status = $"Added a {s.Type} on {BoneNames.Friendly(bone)}";
    }
    void DuplicateShape()
    {
        var c = CurShape; if (c == null) return;
        var d = Json.Clone(c); d.Name = c.Name + "_copy"; d.Pos += new Vector3(0.05f, 0, 0);
        Def.Shapes.Insert(SelShape + 1, d); SelShape++;
    }
    void DeleteShape() { if (CurShape == null) return; Def.Shapes.RemoveAt(SelShape); SelShape = Math.Min(SelShape, Def.Shapes.Count - 1); }
    void MirrorShape()
    {
        var c = CurShape; if (c == null) return;
        var m = Json.Clone(c);
        string mb = c.Bone != null ? MirrorName(c.Bone) : null;
        if (mb != null && Def.Bone(mb) != null) m.Bone = mb;
        m.Pos = new Vector3(-c.Pos.X, c.Pos.Y, c.Pos.Z); m.Rot = new Vector3(c.Rot.X, -c.Rot.Y, -c.Rot.Z); m.Curve = -c.Curve;
        m.Name = MirrorName(c.Name) ?? c.Name + "_mirror";
        var existing = Def.Shapes.FindIndex(x => x.Name == m.Name && x != c);
        if (existing >= 0) Def.Shapes[existing] = m; else Def.Shapes.Insert(SelShape + 1, m);
        Status = $"Mirrored {c.Name} to {m.Name}";
    }

    // ---- rig
    void RigTab()
    {
        float w4 = Ui.Split(4);
        if (Ui.Button("Add", w4)) AddBone(); Ui.Hint("adds a child bone to the selected one (tails, antennae...)");
        Ui.SameLine(); if (Ui.Button("Mirror", w4)) MirrorRig(); Ui.Hint("copies every left (.L) bone to the right side");
        Ui.SameLine(); if (Ui.Button("Reset", w4)) ResetHumanoid(); Ui.Hint("back to the standard humanoid rig (extra bones are kept)");
        Ui.SameLine(); if (Ui.Button("Delete", w4, SelBone != null, Gui.Danger)) DeleteBone();
        Ui.Checkbox("Show bones", ref ShowBones);
        var bd = SelBone != null ? Def.Bone(SelBone) : null;
        if (bd != null)
        {
            Ui.Heading(BoneNames.Friendly(bd.Name));
            string nm = bd.Name;
            if (Ui.TextField("Name", ref nm) && !string.IsNullOrWhiteSpace(nm)) RenameBone(bd, nm.Trim());
            var parents = Def.Bones.Where(x => x != bd && !IsUnder(x.Name, bd.Name)).Select(x => x.Name).ToList();
            string par = bd.Parent; if (Ui.Combo("Parent", ref par, parents, "(origin)")) bd.Parent = par;
            Ui.Vec3("Joint", ref bd.Offset, 0.005f); Ui.Hint("where this bone's joint (its pivot) sits, from its parent's joint");
            var kid = TailChild(bd.Name);
            if (kid != null)
            {
                float len = kid.Offset.Length();
                if (Ui.Slider("Length", ref len, 0.01f, 2) && kid.Offset.LengthSquared() > 1e-8f) kid.Offset = Vector3.Normalize(kid.Offset) * len;
                Ui.Hint($"from this joint to {BoneNames.Friendly(kid.Name)}");
            }
        }
        Space();
        foreach (var (b, depth) in Tree())
        {
            bool std = Array.IndexOf(Skeleton.StdNames, b.Name) >= 0;
            if (Ui.Selectable(BoneNames.Friendly(b.Name) + "##" + b.Name, b.Name == SelBone, depth * 3 * Ui.S, std ? null : Gui.Accent)) { SelBone = b.Name; Target = EditTarget.Rig; }
        }
    }

    IEnumerable<(BoneDef b, int depth)> Tree()
    {
        var roots = Def.Bones.Where(b => string.IsNullOrEmpty(b.Parent) || Def.Bone(b.Parent) == null).ToList();
        var stack = new Stack<(BoneDef, int)>();
        for (int i = roots.Count - 1; i >= 0; i--) stack.Push((roots[i], 0));
        var seen = new HashSet<string>();
        while (stack.Count > 0)
        {
            var (b, d) = stack.Pop();
            if (!seen.Add(b.Name)) continue;
            yield return (b, d);
            var kids = Def.Bones.Where(x => x.Parent == b.Name).ToList();
            for (int i = kids.Count - 1; i >= 0; i--) stack.Push((kids[i], d + 1));
        }
    }
    bool IsUnder(string bone, string ancestor)
    {
        for (var b = Def.Bone(bone); b != null; b = Def.Bone(b.Parent)) if (b.Name == ancestor) return true;
        return false;
    }

    void AddBone()
    {
        string n = "bone"; int k = 1; while (Def.Bone(n + k) != null) k++;
        Def.Bones.Add(new BoneDef { Name = n + k, Parent = SelBone, Offset = new Vector3(0, 0.2f, 0) });
        SelBone = n + k; Target = EditTarget.Rig; Status = $"Added {SelBone}";
    }
    void DeleteBone()
    {
        var bd = Def.Bone(SelBone); if (bd == null) return;
        foreach (var c in Def.Bones.Where(x => x.Parent == bd.Name)) { c.Parent = bd.Parent; c.Offset += bd.Offset; }
        foreach (var s in Def.Shapes.Where(x => x.Bone == bd.Name)) { s.Bone = bd.Parent; s.Pos += bd.Offset; }
        foreach (var c in Def.Clips) c.Tracks.RemoveAll(t => t.Bone == bd.Name);
        foreach (var l in Def.Layers) l.Mask.Remove(bd.Name);
        Def.Limbs.Parts.RemoveAll(p => p.Bone == bd.Name);
        Def.Bones.Remove(bd); Status = $"Deleted {bd.Name}";
        SelBone = bd.Parent ?? (Def.Bones.Count > 0 ? Def.Bones[0].Name : null);
    }
    void RenameBone(BoneDef bd, string to)
    {
        if (Def.Bone(to) != null) { Status = $"There's already a bone called {to}"; return; }
        string from = bd.Name; bd.Name = to;
        foreach (var c in Def.Bones) if (c.Parent == from) c.Parent = to;
        foreach (var s in Def.Shapes) if (s.Bone == from) s.Bone = to;
        foreach (var c in Def.Clips) foreach (var t in c.Tracks) if (t.Bone == from) t.Bone = to;
        foreach (var l in Def.Layers) for (int i = 0; i < l.Mask.Count; i++) if (l.Mask[i] == from) l.Mask[i] = to;
        foreach (var p in Def.Limbs.Parts) if (p.Bone == from) p.Bone = to;
        SelBone = to;
    }
    public static string MirrorName(string n) => n == null ? null : n.EndsWith(".L") ? n[..^2] + ".R" : n.EndsWith(".R") ? n[..^2] + ".L" : null;
    void MirrorRig()
    {
        int n = 0;
        foreach (var b in Def.Bones.Where(b => b.Name.EndsWith(".L")).ToList())
        {
            var m = Def.Bone(MirrorName(b.Name));
            var mp = b.Parent != null && b.Parent.EndsWith(".L") ? MirrorName(b.Parent) : b.Parent;
            if (m == null) { m = new BoneDef { Name = MirrorName(b.Name) }; Def.Bones.Add(m); }
            m.Parent = mp; m.Offset = new Vector3(-b.Offset.X, b.Offset.Y, b.Offset.Z); n++;
        }
        Status = $"Mirrored {n} bones from left to right";
    }
    void ResetHumanoid()
    {
        var keep = Def.Bones.Where(b => Array.IndexOf(Skeleton.StdNames, b.Name) < 0).ToList();
        Def.Bones = DefaultEntities.HumanoidBones(abdomen: true);
        foreach (var b in keep) Def.Bones.Add(b);
        Status = "Rig reset to the standard humanoid (extra bones kept)";
    }

    // ---- settings: gameplay, curl, limbs
    void SettingsTab()
    {
        string name = Def.Name;
        if (Ui.TextField("Name", ref name) && !string.IsNullOrWhiteSpace(name)) Def.Name = name.Trim().Replace(' ', '_');
        Ui.Hint("saves as content/entities/<name>.entity.json");
        string kind = Def.Kind; if (Ui.Combo("Kind", ref kind, Kinds)) Def.Kind = kind;
        Ui.Hint("player: the hero you play. enemy: placed as a germ in maps. prop: scenery");
        if (Ui.Header("Gameplay", ref hdrGame))
        {
            var s = Def.Stats;
            Ui.Slider("Health", ref s.Hp, 1, 2000, "0");
            Ui.Slider("Scale", ref s.Scale, 0.1f, 5); Ui.Hint("sizes the whole entity: model, rig and collision");
            Ui.Slider("Contact damage", ref s.ContactDamage, 0, 100, "0"); Ui.Hint("what walking into it costs the player");
            Ui.SliderInt("Dash hits", ref s.DashesToFloor, 1, 10); Ui.Hint("jump dashes it takes to knock it over");
            Ui.Slider("Radius", ref s.Radius, 0.05f, 2); Ui.Hint("collision cylinder, before scale");
            Ui.Slider("Height", ref s.Height, 0.1f, 5);
            Ui.Checkbox("Show collision", ref ShowCollision);
        }
        if (Ui.Header("Curled up", ref hdrLook))
        {
            string cs = Def.CurlStyle; if (Ui.Combo("Looks like", ref cs, CurlStyles)) Def.CurlStyle = cs;
            Ui.Hint("ball: a striped ball. pose: its own shapes, spun");
            Ui.Color("Ball colour", ref Def.BallColor);
        }
        if (Ui.Header("Turning", ref hdrTurn))
        {
            var T = Def.Turn;
            Ui.Checkbox("Upper body leads", ref T.Enabled); Ui.Hint("the upper body faces where it looks; the hips and legs follow behind");
            if (T.Enabled)
            {
                Ui.Slider("Twist limit", ref T.MaxTwist, 5, 90, "0"); Ui.Hint("degrees the upper body turns, standing still, before the legs step round");
                Ui.Slider("Step speed", ref T.StepSpeed, 1, 20, "0.0"); Ui.Hint("how quickly the legs catch up when they step round (the turn clip plays meanwhile)");
                Ui.Slider("Follow speed", ref T.MoveSpeed, 1, 30, "0.0"); Ui.Hint("how quickly the legs follow while running or in the air");
                Ui.Slider("Chest share", ref T.ChestShare, 0, 1); Ui.Hint("how much of the twist the chest takes; the abdomen takes the rest");
            }
        }
        if (Ui.Header("Limbs", ref hdrLimbs)) LimbsSection();
    }

    void LimbsSection()
    {
        var L = Def.Limbs;
        Ui.Checkbox("Limbs can come off", ref L.Enabled);
        if (!L.Enabled) return;
        Ui.Slider("Smallest hit", ref L.MinHit, 1, 100, "0"); Ui.Hint("hits weaker than this never hurt a limb (keeps it hard to trigger)");
        Ui.SliderInt("Most lost", ref L.MaxLost, 1, 6); Ui.Hint("after this many, limbs stay on");
        Ui.Slider("Debris time", ref L.DebrisLife, 1, 20, "0.0"); Ui.Hint("seconds a severed limb lies around");
        for (int i = 0; i < L.Parts.Count; i++)
        {
            var p = L.Parts[i];
            Ui.Label(BoneNames.Friendly(p.Bone) + " and below", Gui.Accent);
            Ui.Slider("  health##limb" + i, ref p.Hp, 1, 1000, "0"); Ui.Hint("damage from qualifying hits before it comes off");
            Ui.Checkbox("  losing it kills##limb" + i, ref p.Critical);
            if (Ui.Button("Remove##limb" + i, Ui.Split(2))) { L.Parts.RemoveAt(i); i--; }
        }
        var free = BoneList().Where(b => !L.Parts.Exists(p => p.Bone == b)).ToList();
        var add = Ui.Menu("Add a limb at...", free, Ui.Width); if (add != null) L.Parts.Add(new LimbDef { Bone = add, Hp = MathF.Round(MathF.Max(Def.Stats.Hp * 0.4f, 2 * L.MinHit)) });
        if (Ui.Button("Arms and legs")) foreach (var b in new[] { "shoulder.L", "shoulder.R", "hip.L", "hip.R" })
                if (Def.Bone(b) != null && !L.Parts.Exists(p => p.Bone == b)) L.Parts.Add(new LimbDef { Bone = b, Hp = MathF.Round(MathF.Max(Def.Stats.Hp * 0.4f, 2 * L.MinHit)) });
        Ui.Hint("adds both arms and both legs");
    }

    // ---------------------------------------------------------------- right
    void RightPanel(Vector4 r)
    {
        Ui.BeginPanel("right", r.X, r.Y, r.Z, r.W);
        if (Ui.Tabs(ref RightTab, "Animate", "Layers", "Preview") && RightTab == TabAnimate) Target = EditTarget.Pose;
        switch (RightTab)
        {
            case TabAnimate: AnimateTab(); break;
            case TabLayers: LayersTab(); break;
            default: PreviewTab(); break;
        }
        Ui.EndPanel();
    }

    void AnimateTab()
    {
        string cur = CurClip?.Name;
        if (Ui.Combo("Clip", ref cur, Def.Clips.Select(c => c.Name).ToList(), "(none)") && cur != null) { SelClip = Def.Clips.FindIndex(c => c.Name == cur); Playhead = 0; SelKey = null; }
        float w3 = Ui.Split(3);
        if (Ui.Button("New", w3)) { var c = new ClipDef { Name = UniqueClip("clip") }; Def.Clips.Add(c); SelClip = Def.Clips.Count - 1; Playhead = 0; }
        Ui.SameLine(); if (Ui.Button("Copy", w3, CurClip != null)) { var c = Json.Clone(CurClip); c.Name = UniqueClip(CurClip.Name); Def.Clips.Add(c); SelClip = Def.Clips.Count - 1; }
        Ui.SameLine(); if (Ui.Button("Delete", w3, CurClip != null, Gui.Danger)) DeleteClip();
        var clip = CurClip;
        if (clip == null) { Ui.Label("Make a clip to start animating.", Gui.Dim); return; }
        string nm = clip.Name; if (Ui.TextField("Name", ref nm) && !string.IsNullOrWhiteSpace(nm)) RenameClip(clip, nm.Trim());
        Ui.Slider("Length (s)", ref clip.Length, 0.05f, 10);
        Ui.Checkbox("Loop", ref clip.Loop);
        Ui.Checkbox("Preview this clip alone", ref SoloClip); Ui.Hint("off: the full layer stack plays (Preview tab sets the state)");
        if (SoloClip) { Ui.Checkbox("Plant the feet (foot IK)", ref ClipOverProcedural); Ui.Hint("shows the clip as the game does, feet on the ground"); }

        Ui.Heading(SelBone != null ? "Pose: " + BoneNames.Friendly(SelBone) : "Pose");
        if (SelBone == null) { Ui.Label("Click a body part to pose it.", Gui.Dim); return; }
        Ui.Label($"at {Playhead:0.00}s, frame {MathF.Round(Playhead * Fps)}", Gui.Dim);
        var tr = clip.Track(SelBone);
        var k = tr != null ? KeyValue(tr, clip) : new KeyDef { T = Playhead };
        var rot = k.Rot; var pos = k.Pos; var scl = k.Scale;
        bool ch = false;
        ch |= Ui.Vec3("Rotate", ref rot, 0.5f, "0.0"); Ui.Hint("turns the bone about its own joint (degrees)");
        ch |= Ui.Vec3("Move", ref pos, 0.002f);
        ch |= Ui.Vec3("Scale", ref scl, 0.005f); Ui.Hint("squash and stretch its shapes");
        if (ch) SetKey(clip, SelBone, rot, pos, scl);   // editing a value keys it at the playhead
        var at = tr?.Keys.Find(x => MathF.Abs(x.T - Playhead) < 0.5f / Fps);
        float w2 = Ui.Split(2);
        if (Ui.Button(at == null ? "Add key" : "Key set", w2, at == null)) SetKey(clip, SelBone, rot, pos, scl);
        Ui.SameLine(); if (Ui.Button("Delete key", w2, at != null)) { tr.Keys.Remove(at); if (tr.Keys.Count == 0) clip.Tracks.Remove(tr); SelKey = null; }
        if (at != null) { string e = at.Ease; if (Ui.Combo("Ease", ref e, Eases)) at.Ease = e; Ui.Hint("how it moves on to the next key"); }
        if (Ui.Header("Pose tools", ref hdrPoseTools))
        {
            if (Ui.Button("Copy pose", w2)) CopyPose(clip);
            Ui.SameLine(); if (Ui.Button("Paste pose", w2, poseClipboard != null)) PastePose(clip);
            if (Ui.Button("Mirror pose", w2)) MirrorPose(clip); Ui.Hint("left and right swap at the playhead");
            Ui.SameLine(); if (Ui.Button("Key all bones", w2)) KeyAll(clip);
            if (Ui.Button("Clear this bone", Ui.Width, tr != null)) { clip.Tracks.Remove(tr); SelKey = null; }
        }
    }

    string UniqueClip(string baseName) { string n = baseName; int k = 2; while (Def.Clip(n) != null) n = baseName + k++; return n; }
    void DeleteClip()
    {
        var c = CurClip; Def.Clips.Remove(c);
        foreach (var l in Def.Layers) { if (l.Clip == c.Name) l.Clip = null; l.Blend.RemoveAll(b => b.Clip == c.Name); }
        foreach (var s in Def.States) if (s.Clip == c.Name) s.Clip = null;
        SelClip = Math.Min(SelClip, Def.Clips.Count - 1);
    }
    void RenameClip(ClipDef c, string to)
    {
        if (Def.Clip(to) != null) { Status = $"There's already a clip called {to}"; return; }
        foreach (var l in Def.Layers) { if (l.Clip == c.Name) l.Clip = to; foreach (var b in l.Blend) if (b.Clip == c.Name) b.Clip = to; }
        foreach (var s in Def.States) if (s.Clip == c.Name) s.Clip = to;
        c.Name = to;
    }

    /// <summary>The value a track has at the playhead (a key there, or the interpolated pose), as degrees.</summary>
    KeyDef KeyValue(TrackDef tr, ClipDef clip)
    {
        var k = tr.Keys.Find(x => MathF.Abs(x.T - Playhead) < 0.5f / Fps);
        if (k != null) return k;
        var x = Animator.SampleTrack(tr, Playhead, clip.Length, clip.Loop);
        return new KeyDef { T = Playhead, Rot = ToEuler(x.Rot), Pos = x.Pos, Scale = x.Scale };
    }

    public void SetKey(ClipDef clip, string bone, Vector3 rot, Vector3 pos, Vector3 scale)
    {
        float t = MathF.Round(Playhead * Fps) / Fps;
        var tr = clip.Track(bone);
        if (tr == null) { tr = new TrackDef { Bone = bone }; clip.Tracks.Add(tr); }
        var k = tr.Keys.Find(x => MathF.Abs(x.T - t) < 0.5f / Fps);
        if (k == null) { k = new KeyDef { T = t }; tr.Keys.Add(k); tr.Keys.Sort((a, b) => a.T.CompareTo(b.T)); }
        k.Rot = rot; k.Pos = pos; k.Scale = scale;
        SelKey = k;
    }

    List<(string bone, KeyDef key)> poseClipboard;
    void CopyPose(ClipDef clip) { poseClipboard = clip.Tracks.Select(t => (t.Bone, KeyValue(t, clip).Clone())).ToList(); Status = $"Copied {poseClipboard.Count} bones"; }
    void PastePose(ClipDef clip) { foreach (var (b, k) in poseClipboard) SetKey(clip, b, k.Rot, k.Pos, k.Scale); Status = $"Pasted {poseClipboard.Count} bones"; }
    void KeyAll(ClipDef clip)
    {
        foreach (var b in Def.Bones) { var tr = clip.Track(b.Name); var v = tr != null ? KeyValue(tr, clip) : new KeyDef(); SetKey(clip, b.Name, v.Rot, v.Pos, v.Scale); }
        Status = $"Keyed every bone at {Playhead:0.00}s";
    }
    void MirrorPose(ClipDef clip)
    {
        var vals = clip.Tracks.Select(t => (t.Bone, KeyValue(t, clip).Clone())).ToList();
        foreach (var (b, k) in vals)
        {
            string target = MirrorName(b) ?? b;
            if (Def.Bone(target) != null) SetKey(clip, target, new Vector3(k.Rot.X, -k.Rot.Y, -k.Rot.Z), new Vector3(-k.Pos.X, k.Pos.Y, k.Pos.Z), k.Scale);
        }
        Status = "Mirrored the pose left to right";
    }
    void DeleteSelectedKey()
    {
        var c = CurClip; if (c == null || SelKey == null) return;
        foreach (var t in c.Tracks) if (t.Keys.Remove(SelKey)) { if (t.Keys.Count == 0) c.Tracks.Remove(t); break; }
        SelKey = null;
    }

    // ---- layers
    static readonly string[] SourceNames = { "state", "clip", "blend", "footik" };
    static string SourceLabel(string s) => s switch { "state" => "state clips", "clip" => "one clip", "blend" => "blend", "footik" => "foot IK", "procedural" => "old procedural idle", _ => s };

    void LayersTab()
    {
        for (int i = 0; i < Def.Layers.Count; i++)
        {
            var l = Def.Layers[i];
            string what = l.Source switch { "clip" => l.Clip ?? "-", "blend" => "blend " + l.Param, "state" => "states", "footik" => "feet on ground", _ => l.Source };
            if (Ui.Selectable($"{i + 1}. {l.Name}  - {what}{(l.Mode == "additive" ? " (+)" : "")}##l{i}", i == SelLayer, 0, l.Enabled ? null : Gui.Dim)) SelLayer = i;
        }
        Ui.Hint("top to bottom: each layer overrides or adds to the ones above it");
        float w4 = Ui.Split(4);
        var add = Ui.Menu("Add", SourceNames.Select(SourceLabel).ToList(), w4);
        if (add != null)
        {
            string src = SourceNames[Array.FindIndex(SourceNames, s => SourceLabel(s) == add)];
            Def.Layers.Add(new LayerDef { Name = src == "footik" ? "Foot IK" : add, Source = src, Clip = Def.Clips.FirstOrDefault()?.Name });
            SelLayer = Def.Layers.Count - 1;
        }
        Ui.SameLine(); if (Ui.Button("Up", w4, SelLayer > 0)) { (Def.Layers[SelLayer], Def.Layers[SelLayer - 1]) = (Def.Layers[SelLayer - 1], Def.Layers[SelLayer]); SelLayer--; }
        Ui.SameLine(); if (Ui.Button("Down", w4, SelLayer >= 0 && SelLayer < Def.Layers.Count - 1)) { (Def.Layers[SelLayer], Def.Layers[SelLayer + 1]) = (Def.Layers[SelLayer + 1], Def.Layers[SelLayer]); SelLayer++; }
        Ui.SameLine(); if (Ui.Button("Delete", w4, CurLayer != null, Gui.Danger)) { Def.Layers.RemoveAt(SelLayer); SelLayer = Math.Min(SelLayer, Def.Layers.Count - 1); }

        var L = CurLayer; if (L == null) return;
        Ui.Heading(L.Name);
        Ui.TextField("Name", ref L.Name);
        Ui.Checkbox("On", ref L.Enabled);
        string src2 = SourceLabel(L.Source);
        if (Ui.Combo("Plays", ref src2, SourceNames.Select(SourceLabel).ToList())) L.Source = SourceNames[Array.FindIndex(SourceNames, s => SourceLabel(s) == src2)];
        var clips = Def.Clips.Select(c => c.Name).ToList();
        if (L.Source == "footik") { Ui.Slider("Strength", ref L.Weight, 0, 1); Ui.Hint("keeps the feet on the ground under them (stairs, slopes)"); return; }
        if (L.Source == "clip") { string c = L.Clip; if (Ui.Combo("Clip", ref c, clips, "(none)")) L.Clip = c; }
        string m = L.Mode; if (Ui.Combo("Blend", ref m, LayerSources.Modes)) L.Mode = m; Ui.Hint("override replaces the layers above; additive adds on top");
        Ui.Slider("Weight", ref L.Weight, 0, 1);
        Ui.Slider("Speed", ref L.Speed, 0, 4);
        if (L.Source == "blend" && Ui.Header("Blend", ref hdrBlend)) BlendSection(L, clips);
        if (L.Source == "state" && Ui.Header("Clip for each state", ref hdrStateClips)) StateClips(clips);
        if (Ui.Header($"Plays in: {(L.ActiveIn.Count == 0 ? "always" : string.Join(", ", L.ActiveIn))}##active", ref hdrActive))
        {
            Ui.Slider("Fade (s)", ref L.Fade, 0, 1); Ui.Hint("how quickly it fades in and out when the state changes");
            if (Ui.Button("Always")) L.ActiveIn.Clear();
            foreach (var st in AnimStates.All)
            {
                bool on = L.ActiveIn.Contains(st);
                if (Ui.Checkbox(st + "##in", ref on)) { if (on) L.ActiveIn.Add(st); else L.ActiveIn.Remove(st); }
            }
        }
        if (Ui.Header($"Bones: {(L.Mask.Count == 0 ? "all" : string.Join(", ", L.Mask.Select(BoneNames.Friendly)))}##mask", ref hdrMask))
        {
            Ui.Checkbox("Include everything below", ref L.MaskChildren);
            float w2 = Ui.Split(2);
            if (Ui.Button("All bones", w2)) L.Mask.Clear();
            Ui.SameLine(); if (Ui.Button("Arms", w2)) L.Mask = new List<string> { "shoulder.L", "shoulder.R" };
            foreach (var b in Def.Bones)
            {
                bool on = L.Mask.Contains(b.Name);
                if (Ui.Checkbox(BoneNames.Friendly(b.Name) + "##mask" + b.Name, ref on)) { if (on) L.Mask.Add(b.Name); else L.Mask.Remove(b.Name); }
            }
        }
    }

    void BlendSection(LayerDef L, List<string> clips)
    {
        string p = L.Param; if (Ui.Combo("By", ref p, LayerSources.Params)) L.Param = p;
        Ui.Hint("speed: m/s. aimPitch: degrees up. vspeed: m/s up");
        for (int i = 0; i < L.Blend.Count; i++)
        {
            var b = L.Blend[i];
            string c = b.Clip; if (Ui.Combo($"  clip {i + 1}##bp{i}", ref c, clips, "(none)")) b.Clip = c;
            Ui.Drag($"  at##bpv{i}", ref b.Value, 0.5f, "0.#");
            if (Ui.Button($"Remove##bpr{i}")) { L.Blend.RemoveAt(i); i--; }
        }
        if (Ui.Button("Add a clip to the blend")) L.Blend.Add(new BlendPoint { Clip = clips.FirstOrDefault(), Value = L.Blend.Count > 0 ? L.Blend.Max(x => x.Value) + 30 : 0 });
    }

    void StateClips(List<string> clips)
    {
        var add = Ui.Menu("Give a state a clip...", AnimStates.All.Where(st => !Def.States.Exists(x => x.State == st)).ToList(), Ui.Width);
        Ui.Hint("states without a clip keep playing the clip for the movement underneath");
        if (add != null) Def.States.Add(new StateClip { State = add, Clip = clips.FirstOrDefault() });
        foreach (var st in AnimStates.All)
        {
            var sc = Def.States.Find(x => x.State == st);
            if (sc == null) continue;
            string c = sc?.Clip;
            if (Ui.Combo(st, ref c, clips, "(movement)"))
            {
                if (sc == null) { sc = new StateClip { State = st }; Def.States.Add(sc); }
                sc.Clip = c;
                if (c == null) Def.States.Remove(sc);
            }
            if (sc == null) continue;
            Ui.Slider("  fade in##" + st, ref sc.Fade, 0, 1);
            bool follow = !string.IsNullOrEmpty(sc.SpeedParam);
            if (Ui.Checkbox("  plays faster when running##" + st, ref follow)) sc.SpeedParam = follow ? "speed" : null;
            if (follow) { Ui.Drag("  at full rate at##" + st, ref sc.SpeedRef, 0.05f, "0.0 m/s"); Ui.Hint("the speed the clip was made for; faster runs play it faster, so feet don't slide"); }
        }
    }

    // ---- preview
    void PreviewTab()
    {
        if (Ui.Header("State", ref hdrLook))
        {
            string mv = Ctx.Move; if (Ui.Combo("Movement", ref mv, AnimStates.All)) Ctx.Move = mv;
            string act = Ctx.State == Ctx.Move ? null : Ctx.State;
            if (Ui.Combo("Action", ref act, AnimStates.All, "(none)")) Ctx.State = act ?? Ctx.Move;
            if (Ctx.State != Ctx.Move && act == null) Ctx.State = Ctx.Move;
            Ui.Slider("Speed", ref Ctx.Speed, 0, Player.TopSpeed, "0.0"); Ui.Hint("running speed, m/s");
            Ui.Slider("Aim up/down", ref Ctx.AimPitch, -80, 80, "0");
            Ui.Slider("Body twist", ref PreviewTwist, -90, 90, "0"); Ui.Hint("degrees the upper body is turned from the legs (the game sets this as you look round)");
            Ui.Checkbox("On the ground", ref Ctx.Grounded);
            Ui.Checkbox("Curled up", ref Ctx.Rolling);
            Ui.Checkbox("Holding a gun", ref Ctx.Armed);
        }
        if (Ui.Header("Ragdoll and limbs", ref hdrRagdoll))
        {
            float w2 = Ui.Split(2);
            if (Ui.Button(RagdollActive ? "Drop again" : "Drop ragdoll", w2)) DropRagdoll();
            Ui.SameLine(); if (Ui.Button("Stand up", w2, RagdollActive)) StopRagdoll();
            if (Def.Limbs.Enabled && Def.Limbs.Parts.Count > 0)
            {
                var cut = Ui.Menu("Cut off...", Def.Limbs.Parts.Select(p => BoneNames.Friendly(p.Bone)).ToList(), Ui.Width);
                if (cut != null) CutLimb(Def.Limbs.Parts.First(p => BoneNames.Friendly(p.Bone) == cut).Bone);
                if (Ui.Button("Put limbs back", Ui.Width, ev != null && ev.HiddenBones.Count > 0)) { ev.HiddenBones.Clear(); debris.Clear(); }
            }
        }
        if (Ui.Header("View", ref hdrView))
        {
            Ui.Checkbox("Turntable", ref Turntable);
            Ui.Slider("Facing", ref PreviewYaw, -MathF.PI, MathF.PI);
            Ui.Checkbox("Grid", ref ShowGrid);
            Ui.Checkbox("Bones", ref ShowBones);
            Ui.Checkbox("Shapes", ref ShowShapes);
            Ui.Checkbox("Collision", ref ShowCollision);
            if (Ui.Button("Frame (F)")) FrameSelection();
        }
    }

    // ---------------------------------------------------------------- timeline
    void Timeline(Vector4 r, float dt)
    {
        Ui.BeginPanel("timeline", r.X, r.Y, r.Z, r.W);
        var clip = CurClip;
        if (clip == null) { Ui.Label("Timeline: make a clip on the Animate tab", Gui.Dim); Ui.EndPanel(); return; }
        float bw = 7 * Ui.CharW;
        if (Ui.Button(Playing ? "Pause" : "Play", bw)) { Playing = !Playing; RightTab = TabAnimate; }
        Ui.Hint("Space");
        Ui.SameLine(); if (Ui.Button("|<", 4 * Ui.CharW)) Playhead = 0;
        Ui.SameLine(); if (Ui.Button("<", 4 * Ui.CharW)) Playhead = MathF.Max(0, MathF.Round(Playhead * Fps - 1) / Fps);
        Ui.SameLine(); if (Ui.Button(">", 4 * Ui.CharW)) Playhead = MathF.Min(clip.Length, MathF.Round(Playhead * Fps + 1) / Fps);
        Ui.SameLine(); if (Ui.Button(">|", 4 * Ui.CharW)) Playhead = clip.Length;
        Ui.SameLine(); Ui.Button($"{clip.Name}   {Playhead:0.00}s / {clip.Length:0.00}s##info", 0, false);

        var bones = clip.Tracks.Select(t => t.Bone).ToList();
        if (SelBone != null && !bones.Contains(SelBone)) bones.Add(SelBone);
        float rowH = Ui.RowH, nameW = 14 * Ui.CharW;
        var ruler = Ui.Custom(rowH, out bool rulerHover);
        float tx = ruler.X + nameW, tw = ruler.Z - nameW - Ui.Pad;
        float X(float t) => tx + t / MathF.Max(1e-3f, clip.Length) * tw;
        float T(float x) => MathX.Clamp((x - tx) / tw, 0, 1) * clip.Length;
        var o = Ui.O;
        o.Rect(tx, ruler.Y, tw, ruler.W, Gui.WidgetBg);
        int frames = (int)MathF.Round(clip.Length * Fps);
        int step = frames > 120 ? 10 : frames > 40 ? 5 : 1;
        for (int f = 0; f <= frames; f += step)
        {
            float x = X(f / Fps); bool major = f % (step * (step == 1 ? 5 : 2)) == 0;
            o.Rect(x, ruler.Y + (major ? 0 : ruler.W * 0.5f), Ui.S, major ? ruler.W : ruler.W * 0.5f, Gui.Dim);
            if (major) o.Text(x + 2 * Ui.S, ruler.Y + Ui.S, f.ToString(CultureInfo.InvariantCulture), MathF.Max(1, Ui.S - 1), Gui.Dim);
        }
        if (rulerHover && Ui.In.Pressed[0] && Ui.In.MouseX >= tx) Ui.SetActive("timeline/scrub");
        if (Ui.IsActive("timeline/scrub") && Ui.In.Down[0]) { Playhead = MathF.Round(T(Ui.In.MouseX) * Fps) / Fps; Playing = false; RightTab = TabAnimate; Target = EditTarget.Pose; }

        foreach (var b in bones)
        {
            var row = Ui.Custom(rowH, out bool hover);
            bool sel = b == SelBone;
            o.Rect(row.X, row.Y, row.Z, row.W, sel ? Gui.Selected with { W = 0.6f } : Gui.WidgetBg with { W = 0.4f });
            var label = BoneNames.Friendly(b);
            o.Text(row.X + Ui.Pad, row.Y + (row.W - 7 * Ui.S) / 2, label.Length > 13 ? label[..12] + "~" : label, Ui.S, sel ? Gui.TextCol : Gui.Dim);
            if (hover && Ui.In.Pressed[0] && Ui.In.MouseX < tx) { SelBone = b; Target = EditTarget.Pose; }
            var tr = clip.Track(b); if (tr == null) continue;
            foreach (var k in tr.Keys)
            {
                float x = X(k.T), half = 4 * Ui.S;
                var kr = new Vector4(x - half, row.Y + row.W / 2 - half, 2 * half, 2 * half);
                bool kh = Ui.HoverRect(kr);
                string kid = "timeline/key/" + b + "/" + tr.Keys.IndexOf(k);
                if (kh && Ui.In.Pressed[0]) { Ui.SetActive(kid); SelKey = k; SelBone = b; Playhead = k.T; Playing = false; RightTab = TabAnimate; Target = EditTarget.Pose; }
                if (Ui.IsActive(kid) && Ui.In.Down[0] && Ui.In.MouseDX != 0)
                {
                    k.T = MathF.Round(T(Ui.In.MouseX) * Fps) / Fps; Playhead = k.T;
                    tr.Keys.Sort((p, q) => p.T.CompareTo(q.T));
                }
                var kc = k == SelKey ? Gui.Accent : kh ? Gui.TextCol : new Vector4(0.8f, 0.82f, 0.88f, 1);
                o.Rect(kr.X + Ui.S, kr.Y + Ui.S, kr.Z - 2 * Ui.S, kr.W - 2 * Ui.S, kc);
            }
        }
        float px = X(Playhead);
        o.Clip = null;
        o.Rect(px - Ui.S / 2, ruler.Y, Ui.S, (bones.Count + 1) * (rowH + Ui.Gap), Gui.Accent);
        Ui.EndPanel();
    }
}
