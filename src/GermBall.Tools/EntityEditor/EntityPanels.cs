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

public sealed partial class EntityEditor
{
    string newTemplate = "humanoid", newShapeType = "Capsule", newLayerSource = "clip";
    bool hdrStats = true, hdrLook = true, hdrKey = true, hdrMask = true, hdrDrive = true, hdrShow = true;
    public bool PreviewAdditive;
    static readonly string[] Kinds = { "player", "enemy", "prop" }, CurlStyles = { "ball", "pose" }, Eases = { "smooth", "linear", "step" };
    static readonly string[] Templates = { "humanoid", "hero copy", "germ copy", "empty" };

    // ---------------------------------------------------------------- toolbar
    void Toolbar(Vector4 r)
    {
        Ui.BeginArea("toolbar", r, Gui.TitleBg);
        float bw = 8 * Ui.CharW;
        Ui.Button("ENTITY EDITOR##title", 15 * Ui.CharW, false); Ui.SameLine();
        if (Ui.Button("New", bw)) New(newTemplate);
        Ui.SameLine(); Ui.ComboW("##template", ref newTemplate, Templates, 12 * Ui.CharW);
        Ui.SameLine();
        string pick = null;
        if (Ui.ComboW("##open", ref pick, Store.EntityNames().ToList(), 14 * Ui.CharW, "open...") && pick != null) Load(pick);
        Ui.SameLine(); if (Ui.Button("Save", bw)) Save();
        Ui.SameLine(); if (Ui.Button("Undo", bw)) Undo();
        Ui.SameLine(); if (Ui.Button("Redo", bw)) Redo();
        Ui.SameLine(); if (Ui.Button("Test in game", 14 * Ui.CharW, true, Gui.AccentDim)) TestInGame();
        Ui.SameLine(); if (Ui.Toggle("Move (W)", Mode == GizmoMode.Move, 10 * Ui.CharW)) Mode = GizmoMode.Move;
        Ui.SameLine(); if (Ui.Toggle("Rotate (E)", Mode == GizmoMode.Rotate, 12 * Ui.CharW)) Mode = GizmoMode.Rotate;
        Ui.SameLine(); if (Ui.Toggle("Scale (R)", Mode == GizmoMode.Scale, 11 * Ui.CharW)) Mode = GizmoMode.Scale;
        Ui.SameLine(); Ui.Button($"{Def.Name} ({Def.Kind}){(Dirty ? " *" : "")}##name", 0, false);
        Ui.EndArea();
    }

    // ---------------------------------------------------------------- left: entity, rig, shapes
    void LeftPanel(Vector4 r)
    {
        Ui.BeginPanel("left", r.X, r.Y, r.Z, r.W);
        if (Ui.Tabs(ref LeftTab, "Entity", "Rig", "Shapes")) { if (LeftTab == 1) Target = EditTarget.Rig; if (LeftTab == 2) Target = EditTarget.Shape; }
        switch (LeftTab)
        {
            case 0: EntityTab(); break;
            case 1: RigTab(); break;
            default: ShapesTab(); break;
        }
        Ui.EndPanel();
    }

    void EntityTab()
    {
        string name = Def.Name;
        if (Ui.TextField("Name", ref name) && !string.IsNullOrWhiteSpace(name)) Def.Name = name.Trim().Replace(' ', '_');
        Ui.Label("saves as content/entities/" + Def.Name + ".entity.json", Gui.Dim);
        string kind = Def.Kind; if (Ui.Combo("Kind", ref kind, Kinds)) Def.Kind = kind;
        Ui.Label(Def.Kind == "player" ? "player: procedural run cycle, aim and IK" : "enemy/prop: procedural idle sway", Gui.Dim);
        if (Ui.Header("Gameplay", ref hdrStats))
        {
            var s = Def.Stats;
            Ui.Slider("Health", ref s.Hp, 1, 2000, "0");
            Ui.Slider("Scale", ref s.Scale, 0.1f, 5);
            Ui.Slider("Contact damage", ref s.ContactDamage, 0, 100, "0");
            Ui.SliderInt("Dashes to floor", ref s.DashesToFloor, 1, 10);
            Ui.Slider("Collision radius", ref s.Radius, 0.05f, 2);
            Ui.Slider("Collision height", ref s.Height, 0.1f, 5);
            Ui.Checkbox("Show collision", ref ShowCollision);
        }
        if (Ui.Header("Curled up", ref hdrLook))
        {
            string cs = Def.CurlStyle; if (Ui.Combo("Style", ref cs, CurlStyles)) Def.CurlStyle = cs;
            Ui.Color("Ball colour", ref Def.BallColor);
            Ui.Checkbox("Preview curled", ref Ctx.Rolling);
        }
        Ui.Heading("Summary");
        Ui.Label($"{Def.Bones.Count} bones, {Def.Shapes.Count} shapes", Gui.Dim);
        Ui.Label($"{Def.Clips.Count} clips, {Def.Layers.Count} layers", Gui.Dim);
        Ui.Label(Sk != null && Sk.Humanoid ? "humanoid rig: procedural motion + ragdoll" : "custom rig: clips only, no ragdoll", Gui.Dim);
    }

    void RigTab()
    {
        if (Ui.Button("Add child bone", Ui.Split(2))) AddBone();
        Ui.SameLine(); if (Ui.Button("Delete bone", Ui.Split(2), SelBone != null)) DeleteBone();
        if (Ui.Button("Mirror L to R", Ui.Split(2))) MirrorRig();
        Ui.SameLine(); if (Ui.Button("Reset to humanoid", Ui.Split(2))) ResetHumanoid();
        Ui.Checkbox("Show bones", ref ShowBones);
        Ui.Heading("Bones");
        // the hierarchy, indented by depth
        foreach (var (b, depth) in Tree())
        {
            bool std = Array.IndexOf(Skeleton.StdNames, b.Name) >= 0;
            if (Ui.Selectable(b.Name + (std ? "" : "  *"), b.Name == SelBone, depth * 3 * Ui.S, std ? null : Gui.Accent)) SelBone = b.Name;
        }
        Ui.Label("* extra bone (animate it with clips)", Gui.Dim);
        var bd = SelBone != null ? Def.Bone(SelBone) : null;
        if (bd == null) return;
        Ui.Heading("Selected bone");
        string nm = bd.Name;
        if (Ui.TextField("Name", ref nm) && !string.IsNullOrWhiteSpace(nm)) RenameBone(bd, nm.Trim());
        var parents = Def.Bones.Where(x => x != bd && !IsUnder(x.Name, bd.Name)).Select(x => x.Name).ToList();
        string par = bd.Parent; if (Ui.Combo("Parent", ref par, parents, "(origin)")) bd.Parent = par;
        Ui.Vec3("Offset", ref bd.Offset, 0.005f);
        Ui.Label("offset from the parent joint (drag the gizmo too)", Gui.Dim);
        float len = bd.Offset.Length();
        if (Ui.Slider("Length", ref len, 0.001f, 2) && bd.Offset.LengthSquared() > 1e-8f) bd.Offset = Vector3.Normalize(bd.Offset) * len;
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
        SelBone = n + k; Status = $"Added {SelBone}";
    }
    void DeleteBone()
    {
        var bd = Def.Bone(SelBone); if (bd == null) return;
        // children and shapes move up to the parent, keeping where they are
        foreach (var c in Def.Bones.Where(x => x.Parent == bd.Name)) { c.Parent = bd.Parent; c.Offset += bd.Offset; }
        foreach (var s in Def.Shapes.Where(x => x.Bone == bd.Name)) { s.Bone = bd.Parent; s.Pos += bd.Offset; }
        foreach (var c in Def.Clips) c.Tracks.RemoveAll(t => t.Bone == bd.Name);
        foreach (var l in Def.Layers) l.Mask.Remove(bd.Name);
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
        SelBone = to;
    }
    public static string MirrorName(string n) => n.EndsWith(".L") ? n[..^2] + ".R" : n.EndsWith(".R") ? n[..^2] + ".L" : null;
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

    // ---------------------------------------------------------------- shapes, by body part
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

    void ShapesTab()
    {
        var types = ShapeTypes.All;
        string t = newShapeType; if (Ui.Combo("New shape", ref t, types)) newShapeType = t;
        if (Ui.Button("Duplicate", Ui.Split(3), CurShape != null)) DuplicateShape();
        Ui.SameLine(); if (Ui.Button("Delete", Ui.Split(3), CurShape != null)) DeleteShape();
        Ui.SameLine(); if (Ui.Button("Mirror", Ui.Split(3), CurShape != null)) MirrorShape();
        if (Ui.Button("Up", Ui.Split(3), SelShape > 0)) MoveShape(-1);
        Ui.SameLine(); if (Ui.Button("Down", Ui.Split(3), SelShape >= 0 && SelShape < Def.Shapes.Count - 1)) MoveShape(1);
        Ui.SameLine(); Ui.Checkbox("Show", ref ShowShapes);

        // the selected shape first, so its controls are always at hand
        var c = CurShape;
        if (c != null)
        {
            Ui.Heading($"{c.Name}  ({PartOf(c.Bone)})");
            Ui.TextField("Name", ref c.Name);
            string tp = c.Type; if (Ui.Combo("Type", ref tp, types)) c.Type = tp;
            string bone = c.Bone; if (Ui.Combo("Bone", ref bone, Def.Bones.Select(b => b.Name).ToList(), "(origin)")) c.Bone = bone;
            Ui.Vec3("Position", ref c.Pos, 0.002f);
            Ui.Vec3("Rotation", ref c.Rot, 0.5f, "0.0");
            Ui.Vec3("Size", ref c.Size, 0.002f);
            if (Ui.Button("Size -10%", Ui.Split(2))) c.Size *= 0.9f;
            Ui.SameLine(); if (Ui.Button("Size +10%", Ui.Split(2))) c.Size *= 1.1f;
            Ui.Slider("Taper", ref c.Taper, -1, 1);
            Ui.Label("+ narrows the top end, - the bottom end", Gui.Dim);
            Ui.Slider("Curve", ref c.Curve, -1, 1);
            Ui.Label("bends along its length, toward +X", Gui.Dim);
            if (Ui.Button("Point along bone", Ui.Split(2)) && c.Bone != null)
            {
                var kid = Def.Bones.FirstOrDefault(b => b.Parent == c.Bone);
                if (kid != null) { c.Rot = RotFor(kid.Offset); c.Pos = kid.Offset / 2; if (c.Type is "Capsule" or "Cylinder") c.Size.Y = kid.Offset.Length() + (c.Type == "Capsule" ? c.Size.X : 0); }
            }
            Ui.SameLine(); if (Ui.Button("Reset rotation", Ui.Split(2))) c.Rot = Vector3.Zero;
            Ui.Color("Colour", ref c.Color);
            Ui.Checkbox("Glow (unlit)", ref c.Unlit);
            Ui.Checkbox("Hidden", ref c.Hidden);
        }
        else Ui.Label("Click a piece of the model, or pick one below.", Gui.Dim);

        // every shape, grouped by the body part its bone belongs to
        foreach (var part in PartNames)
        {
            var idx = Enumerable.Range(0, Def.Shapes.Count).Where(i => PartOf(Def.Shapes[i].Bone) == part).ToList();
            string pb = PartBone(part);
            if (idx.Count == 0 && (pb == null || Def.Bone(pb) == null)) continue;
            bool open = !partOpen.TryGetValue(part, out var o) || o;
            Ui.Header($"{part} ({idx.Count})##part", ref open);
            partOpen[part] = open;
            if (!open) continue;
            foreach (int i in idx)
            {
                var sh = Def.Shapes[i];
                if (Ui.Selectable($"{sh.Name}  [{sh.Type}] {sh.Bone ?? "origin"}##{i}", i == SelShape, 2 * Ui.S, sh.Hidden ? Gui.Dim : null)) { SelShape = i; SelBone = sh.Bone; Target = EditTarget.Shape; }
            }
            if (Ui.Button($"+ {newShapeType} on {part.ToLowerInvariant()}##add{part}"))
            {
                string onBone = SelBone != null && PartOf(SelBone) == part ? SelBone : pb != null && Def.Bone(pb) != null ? pb : SelBone;
                AddShape(onBone);
            }
        }
    }

    void AddShape(string onBone = null)
    {
        var bone = onBone ?? SelBone ?? (Def.Bones.Count > 0 ? Def.Bones[0].Name : null);
        var s = new ShapeDef { Name = newShapeType.ToLowerInvariant() + (Def.Shapes.Count + 1), Bone = bone, Type = newShapeType, Size = newShapeType is "Capsule" or "Cylinder" ? new Vector3(0.15f, 0.4f, 0.15f) : new Vector3(0.2f), Color = "#D0D4DA" };
        Def.Shapes.Add(s); SelShape = Def.Shapes.Count - 1; SelBone = bone; Target = EditTarget.Shape; partOpen[PartOf(bone)] = true; Status = $"Added a {s.Type} on {bone}";
    }
    void DuplicateShape()
    {
        var c = CurShape; if (c == null) return;
        var d = Json.Clone(c); d.Name = c.Name + "_copy"; d.Pos += new Vector3(0.05f, 0, 0);
        Def.Shapes.Insert(SelShape + 1, d); SelShape++;
    }
    void DeleteShape() { if (CurShape == null) return; Def.Shapes.RemoveAt(SelShape); SelShape = Math.Min(SelShape, Def.Shapes.Count - 1); }
    void MoveShape(int d) { var s = Def.Shapes[SelShape]; Def.Shapes.RemoveAt(SelShape); SelShape += d; Def.Shapes.Insert(SelShape, s); }
    void MirrorShape()
    {
        var c = CurShape; if (c == null) return;
        var m = Json.Clone(c);
        string mb = c.Bone != null ? MirrorName(c.Bone) : null;
        if (mb != null && Def.Bone(mb) != null) m.Bone = mb;
        m.Pos = new Vector3(-c.Pos.X, c.Pos.Y, c.Pos.Z); m.Rot = new Vector3(c.Rot.X, -c.Rot.Y, -c.Rot.Z);
        m.Name = MirrorName(c.Name) ?? c.Name + "_mirror";
        var existing = Def.Shapes.FindIndex(x => x.Name == m.Name && x != c);
        if (existing >= 0) Def.Shapes[existing] = m; else { Def.Shapes.Insert(SelShape + 1, m); }
        Status = $"Mirrored {c.Name} to {m.Name}";
    }

    // ---------------------------------------------------------------- right: clips, layers, states, preview
    void RightPanel(Vector4 r)
    {
        Ui.BeginPanel("right", r.X, r.Y, r.Z, r.W);
        if (Ui.Tabs(ref RightTab, "Clips", "Layers", "States", "Preview") && RightTab == 0) Target = EditTarget.Pose;
        switch (RightTab)
        {
            case 0: ClipsTab(); break;
            case 1: LayersTab(); break;
            case 2: StatesTab(); break;
            default: PreviewTab(); break;
        }
        Ui.EndPanel();
    }

    void ClipsTab()
    {
        if (Ui.Button("New clip", Ui.Split(3))) { var c = new ClipDef { Name = UniqueClip("clip") }; Def.Clips.Add(c); SelClip = Def.Clips.Count - 1; Playhead = 0; }
        Ui.SameLine(); if (Ui.Button("Duplicate", Ui.Split(3), CurClip != null)) { var c = Json.Clone(CurClip); c.Name = UniqueClip(CurClip.Name); Def.Clips.Add(c); SelClip = Def.Clips.Count - 1; }
        Ui.SameLine(); if (Ui.Button("Delete", Ui.Split(3), CurClip != null)) DeleteClip();
        for (int i = 0; i < Def.Clips.Count; i++)
            if (Ui.Selectable($"{Def.Clips[i].Name}  ({Def.Clips[i].Length:0.00}s{(Def.Clips[i].Loop ? ", loop" : "")})##{i}", i == SelClip)) { SelClip = i; Playhead = 0; SelKey = null; }
        var clip = CurClip;
        if (clip == null) { Ui.Label("No clips yet: make one with New clip", Gui.Dim); return; }
        Ui.Heading("Clip");
        string nm = clip.Name; if (Ui.TextField("Name", ref nm) && !string.IsNullOrWhiteSpace(nm)) RenameClip(clip, nm.Trim());
        Ui.Slider("Length (s)", ref clip.Length, 0.05f, 10);
        Ui.Checkbox("Loop", ref clip.Loop);
        Ui.Checkbox("Preview this clip alone", ref SoloClip);
        if (SoloClip) { Ui.Checkbox("  over the procedural motion", ref ClipOverProcedural); Ui.Checkbox("  as additive", ref PreviewAdditive); }

        if (Ui.Header("Key the selected bone", ref hdrKey))
        {
            if (SelBone == null) { Ui.Label("Pick a bone (click a joint in the view)", Gui.Dim); return; }
            Ui.Label($"{SelBone} at {Playhead:0.000}s (frame {MathF.Round(Playhead * Fps)})", Gui.TextCol);
            var tr = clip.Track(SelBone);
            var cur = tr != null ? KeyValue(tr, clip) : new KeyDef { T = Playhead };
            var rot = cur.Rot; var pos = cur.Pos; var scl = cur.Scale;
            bool ch = false;
            ch |= Ui.Vec3("Rotate", ref rot, 0.5f, "0.0");
            ch |= Ui.Vec3("Move", ref pos, 0.002f);
            ch |= Ui.Vec3("Scale", ref scl, 0.005f);
            if (ch) SetKey(clip, SelBone, rot, pos, scl);   // editing a value keys it at the playhead
            var at = tr?.Keys.Find(k => MathF.Abs(k.T - Playhead) < 0.5f / Fps);
            if (at != null) { string e = at.Ease; if (Ui.Combo("Ease to next", ref e, Eases)) at.Ease = e; }
            if (Ui.Button("Set key", Ui.Split(2))) SetKey(clip, SelBone, rot, pos, scl);
            Ui.SameLine(); if (Ui.Button("Delete key", Ui.Split(2), at != null)) { tr.Keys.Remove(at); if (tr.Keys.Count == 0) clip.Tracks.Remove(tr); SelKey = null; }
            if (Ui.Button("Copy pose", Ui.Split(3))) CopyPose(clip);
            Ui.SameLine(); if (Ui.Button("Paste pose", Ui.Split(3), poseClipboard != null)) PastePose(clip);
            Ui.SameLine(); if (Ui.Button("Clear track", Ui.Split(3), tr != null)) { clip.Tracks.Remove(tr); SelKey = null; }
            if (Ui.Button("Mirror pose L/R", Ui.Split(2))) MirrorPose(clip);
            Ui.SameLine(); if (Ui.Button("Key all bones", Ui.Split(2))) KeyAll(clip);
        }
    }

    string UniqueClip(string baseName) { string n = baseName; int k = 2; while (Def.Clip(n) != null) n = baseName + k++; return n; }
    void DeleteClip()
    {
        var c = CurClip; Def.Clips.Remove(c);
        foreach (var l in Def.Layers) if (l.Clip == c.Name) l.Clip = null;
        foreach (var s in Def.States) if (s.Clip == c.Name) s.Clip = null;
        SelClip = Math.Min(SelClip, Def.Clips.Count - 1);
    }
    void RenameClip(ClipDef c, string to)
    {
        if (Def.Clip(to) != null) { Status = $"There's already a clip called {to}"; return; }
        foreach (var l in Def.Layers) if (l.Clip == c.Name) l.Clip = to;
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
    void CopyPose(ClipDef clip)
    {
        poseClipboard = clip.Tracks.Select(t => (t.Bone, KeyValue(t, clip).Clone())).ToList();
        Status = $"Copied {poseClipboard.Count} bones at {Playhead:0.00}s";
    }
    void PastePose(ClipDef clip)
    {
        foreach (var (b, k) in poseClipboard) SetKey(clip, b, k.Rot, k.Pos, k.Scale);
        Status = $"Pasted {poseClipboard.Count} bones at {Playhead:0.00}s";
    }
    void KeyAll(ClipDef clip)
    {
        foreach (var b in Def.Bones)
        {
            var tr = clip.Track(b.Name); var v = tr != null ? KeyValue(tr, clip) : new KeyDef();
            SetKey(clip, b.Name, v.Rot, v.Pos, v.Scale);
        }
        Status = $"Keyed every bone at {Playhead:0.00}s";
    }
    void MirrorPose(ClipDef clip)
    {
        var vals = clip.Tracks.Select(t => (t.Bone, KeyValue(t, clip).Clone())).ToList();
        foreach (var (b, k) in vals)
        {
            string target = MirrorName(b) ?? b;
            var rot = new Vector3(k.Rot.X, -k.Rot.Y, -k.Rot.Z); var pos = new Vector3(-k.Pos.X, k.Pos.Y, k.Pos.Z);
            if (Def.Bone(target) != null) SetKey(clip, target, rot, pos, k.Scale);
        }
        Status = "Mirrored the pose left to right";
    }
    void DeleteSelectedKey()
    {
        var c = CurClip; if (c == null || SelKey == null) return;
        foreach (var t in c.Tracks) if (t.Keys.Remove(SelKey)) { if (t.Keys.Count == 0) c.Tracks.Remove(t); break; }
        SelKey = null;
    }

    void LayersTab()
    {
        Ui.Label("Bottom to top: each layer overrides or adds to the ones above it in this list.", Gui.Dim);
        string src = newLayerSource; if (Ui.Combo("New layer from", ref src, LayerSources.All)) newLayerSource = src;
        if (Ui.Button("Add layer", Ui.Split(2))) { Def.Layers.Add(new LayerDef { Name = newLayerSource + " layer", Source = newLayerSource, Clip = Def.Clips.FirstOrDefault()?.Name }); SelLayer = Def.Layers.Count - 1; }
        Ui.SameLine(); if (Ui.Button("Delete", Ui.Split(2), CurLayer != null)) { Def.Layers.RemoveAt(SelLayer); SelLayer = Math.Min(SelLayer, Def.Layers.Count - 1); }
        if (Ui.Button("Move up", Ui.Split(2), SelLayer > 0)) { (Def.Layers[SelLayer], Def.Layers[SelLayer - 1]) = (Def.Layers[SelLayer - 1], Def.Layers[SelLayer]); SelLayer--; }
        Ui.SameLine(); if (Ui.Button("Move down", Ui.Split(2), SelLayer >= 0 && SelLayer < Def.Layers.Count - 1)) { (Def.Layers[SelLayer], Def.Layers[SelLayer + 1]) = (Def.Layers[SelLayer + 1], Def.Layers[SelLayer]); SelLayer++; }
        Ui.Heading("Stack");
        for (int i = 0; i < Def.Layers.Count; i++)
        {
            var l = Def.Layers[i];
            string what = l.Source == "clip" ? l.Clip ?? "-" : l.Source;
            if (Ui.Selectable($"{i + 1}. {l.Name}   {l.Mode} {what} x{l.Weight:0.##}##{i}", i == SelLayer, 0, l.Enabled ? null : Gui.Dim)) SelLayer = i;
        }
        var L = CurLayer; if (L == null) return;
        Ui.Heading("Layer");
        Ui.TextField("Name", ref L.Name);
        Ui.Checkbox("Enabled", ref L.Enabled);
        string s = L.Source; if (Ui.Combo("Source", ref s, LayerSources.All)) L.Source = s;
        if (L.Source == "clip") { string c = L.Clip; if (Ui.Combo("Clip", ref c, Def.Clips.Select(x => x.Name).ToList(), "(none)")) L.Clip = c; }
        Ui.Label(L.Source switch
        {
            "procedural" => Def.Kind == "player" ? "run cycle, foot IK, aim, punch, reload" : "idle sway",
            "state" => "plays the clip mapped to the game state (States tab)",
            _ => "loops one clip on its own clock",
        }, Gui.Dim);
        string m = L.Mode; if (Ui.Combo("Blend", ref m, LayerSources.Modes)) L.Mode = m;
        Ui.Slider("Weight", ref L.Weight, 0, 1);
        Ui.Slider("Speed", ref L.Speed, 0, 4);
        if (Ui.Header($"Bone mask ({(L.Mask.Count == 0 ? "all bones" : L.Mask.Count + " bones")})", ref hdrMask))
        {
            Ui.Checkbox("Include children", ref L.MaskChildren);
            if (Ui.Button("All bones", Ui.Split(2))) L.Mask.Clear();
            Ui.SameLine(); if (Ui.Button("Upper body", Ui.Split(2))) L.Mask = new List<string> { "chest" };
            foreach (var b in Def.Bones)
            {
                bool on = L.Mask.Contains(b.Name);
                if (Ui.Checkbox(b.Name + "##mask", ref on)) { if (on) L.Mask.Add(b.Name); else L.Mask.Remove(b.Name); }
            }
        }
    }

    void StatesTab()
    {
        Ui.Label("Which clip plays on \"state\" layers in each game state.", Gui.Dim);
        var clips = Def.Clips.Select(c => c.Name).ToList();
        foreach (var st in AnimStates.All)
        {
            var sc = Def.States.Find(x => x.State == st);
            string c = sc?.Clip;
            if (Ui.Combo(st, ref c, clips, "(nothing)"))
            {
                if (sc == null) { sc = new StateClip { State = st }; Def.States.Add(sc); }
                sc.Clip = c;
                if (c == null) Def.States.Remove(sc);
            }
            if (sc != null) Ui.Slider("  crossfade##" + st, ref sc.Fade, 0, 1);
        }
    }

    void PreviewTab()
    {
        Ui.Label("Drives the full layer stack (turn off \"preview alone\" on Clips).", Gui.Dim);
        if (Ui.Header("Game state", ref hdrDrive))
        {
            string st = Ctx.State; if (Ui.Combo("State", ref st, AnimStates.All)) Ctx.State = st;
            Ui.Slider("Run speed", ref Ctx.Speed, 0, Player.TopSpeed);
            Ui.Checkbox("On the ground", ref Ctx.Grounded);
            Ui.Checkbox("Skidding", ref Ctx.Skidding);
            Ui.Checkbox("Hovering", ref Ctx.Hovering);
            Ui.Checkbox("Curled up", ref Ctx.Rolling);
            Ui.Checkbox("Holding a gun", ref Ctx.Armed);
            Ui.Checkbox("Two-handed gun", ref Ctx.TwoHanded);
            Ui.Checkbox("Aiming", ref Ctx.Aiming);
            Ui.Slider("Aim left/right", ref aimYaw, -80, 80, "0");
            Ui.Slider("Aim up/down", ref aimPitch, -70, 70, "0");
            if (Ui.Button("Punch", Ui.Split(2))) PunchT = 0.2f;
            Ui.SameLine(); if (Ui.Button("Reload", Ui.Split(2))) ReloadT = 1.2f;
            Ui.Slider("Idle sway", ref Ctx.Sway, 0, 0.5f);
            Ui.Slider("Arms raised", ref Ctx.ArmsUp, 0, 1);
        }
        if (Ui.Header("Ragdoll", ref hdrShow))
        {
            if (Ui.Button(RagdollActive ? "Drop again" : "Drop ragdoll", Ui.Split(2))) DropRagdoll();
            Ui.SameLine(); if (Ui.Button("Stop ragdoll", Ui.Split(2), RagdollActive)) StopRagdoll();
        }
        Ui.Heading("View");
        Ui.Checkbox("Turntable", ref Turntable);
        Ui.Slider("Facing", ref PreviewYaw, -MathF.PI, MathF.PI);
        Ui.Checkbox("Grid", ref ShowGrid);
        Ui.Checkbox("Bones", ref ShowBones);
        Ui.Checkbox("Shapes", ref ShowShapes);
        Ui.Checkbox("Collision", ref ShowCollision);
        if (Ui.Button("Frame (F)")) FrameSelection();
    }

    // ---------------------------------------------------------------- timeline
    void Timeline(Vector4 r, float dt)
    {
        Ui.BeginPanel("timeline", r.X, r.Y, r.Z, r.W);
        var clip = CurClip;
        if (clip == null) { Ui.Label("Timeline: make a clip on the Clips tab", Gui.Dim); Ui.EndPanel(); return; }
        float bw = 7 * Ui.CharW;
        if (Ui.Button(Playing ? "Pause" : "Play", bw)) { Playing = !Playing; RightTab = 0; }
        Ui.SameLine(); if (Ui.Button("|<", 4 * Ui.CharW)) Playhead = 0;
        Ui.SameLine(); if (Ui.Button("<", 4 * Ui.CharW)) Playhead = MathF.Max(0, MathF.Round(Playhead * Fps - 1) / Fps);
        Ui.SameLine(); if (Ui.Button(">", 4 * Ui.CharW)) Playhead = MathF.Min(clip.Length, MathF.Round(Playhead * Fps + 1) / Fps);
        Ui.SameLine(); if (Ui.Button(">|", 4 * Ui.CharW)) Playhead = clip.Length;
        Ui.SameLine(); Ui.Button($"{clip.Name}   {Playhead:0.000}s / {clip.Length:0.00}s   frame {MathF.Round(Playhead * Fps)}##info", 0, false);

        // rows: the ruler, then one per bone that has keys (and the selected bone)
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
        if (Ui.IsActive("timeline/scrub") && Ui.In.Down[0]) { Playhead = MathF.Round(T(Ui.In.MouseX) * Fps) / Fps; Playing = false; RightTab = 0; Target = EditTarget.Pose; }

        foreach (var b in bones)
        {
            var row = Ui.Custom(rowH, out bool hover);
            bool sel = b == SelBone;
            o.Rect(row.X, row.Y, row.Z, row.W, sel ? Gui.Selected with { W = 0.6f } : Gui.WidgetBg with { W = 0.4f });
            o.Text(row.X + Ui.Pad, row.Y + (row.W - 7 * Ui.S) / 2, b.Length > 13 ? b[..12] + "~" : b, Ui.S, sel ? Gui.TextCol : Gui.Dim);
            if (hover && Ui.In.Pressed[0] && Ui.In.MouseX < tx) { SelBone = b; Target = EditTarget.Pose; }
            var tr = clip.Track(b); if (tr == null) continue;
            foreach (var k in tr.Keys)
            {
                float x = X(k.T), half = 4 * Ui.S;
                var kr = new Vector4(x - half, row.Y + row.W / 2 - half, 2 * half, 2 * half);
                bool kh = Ui.HoverRect(kr);
                string kid = "timeline/key/" + b + "/" + tr.Keys.IndexOf(k);
                if (kh && Ui.In.Pressed[0]) { Ui.SetActive(kid); SelKey = k; SelBone = b; Playhead = k.T; Playing = false; RightTab = 0; Target = EditTarget.Pose; }
                if (Ui.IsActive(kid) && Ui.In.Down[0] && Ui.In.MouseDX != 0)
                {
                    // drag a key along the timeline (snapped to frames)
                    k.T = MathF.Round(T(Ui.In.MouseX) * Fps) / Fps; Playhead = k.T;
                    tr.Keys.Sort((p, q) => p.T.CompareTo(q.T));
                }
                var kc = k == SelKey ? Gui.Accent : kh ? Gui.TextCol : new Vector4(0.8f, 0.82f, 0.88f, 1);
                o.Rect(kr.X + Ui.S, kr.Y + Ui.S, kr.Z - 2 * Ui.S, kr.W - 2 * Ui.S, kc);
            }
        }
        // the playhead line over everything
        float px = X(Playhead);
        o.Clip = null;
        o.Rect(px - Ui.S / 2, ruler.Y, Ui.S, (bones.Count + 1) * (rowH + Ui.Gap), Gui.Accent);
        Ui.EndPanel();
    }
}
