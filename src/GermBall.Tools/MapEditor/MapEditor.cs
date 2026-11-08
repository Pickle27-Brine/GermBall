using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using GermBall.Content;
using GermBall.Engine;
using GermBall.Sim;
using GermBall.Tools.Ui;

namespace GermBall.Tools.MapEditor;

/// <summary>
/// The Map Editor: build levels from boxes, ramps, trigger zones, germs, dummies, balls, pickups, signs and landmarks.
/// Left: the add palette and the outliner. Right: properties of the selection (or the map's settings).
/// Centre: a fly-through view drawn exactly as the game draws it, with picking, a move gizmo and grid snapping.
/// Saved to content/maps/&lt;name&gt;.map.json, which the game loads with --map.
/// </summary>
public sealed partial class MapEditor
{
    public readonly ContentStore Store;
    public readonly Gui Ui = new();
    public readonly View3D View = new() { Fly = true, Yaw = 0, Pitch = -0.45f, Eye = new Vector3(0, 18, 34) };
    public MapDef Map;
    public Action<string> LaunchGame;
    public string Status = "Ready";

    /// <summary>-1: nothing (map settings), -2: the player spawn, otherwise an index into Map.Objects.</summary>
    public int Sel = -1;
    public bool Snap = true, ShowZones = true, ShowLabels = true;
    public float SnapSize = 0.5f;
    Game preview; string previewJson;
    readonly List<string> undo = new(), redo = new();
    string committed, savedJson;
    Vector3 dragRaw; bool wasDragging;
    float time;

    public MapEditor(ContentStore store, string open = null)
    {
        Store = store;
        var m = open != null ? store.Map(open) : store.Map("debug");
        if (m != null) { Map = m; Map.Name = open ?? "debug"; }
        else { Map = Level.BuildDebug().Map; Map.Name = "debug"; }
        AfterLoad();
    }

    public MapObject Cur => Sel >= 0 && Sel < Map.Objects.Count ? Map.Objects[Sel] : null;
    public bool Dirty => Json.Write(Map) != savedJson;
    public Game Preview => preview;

    // ---------------------------------------------------------------- files
    public void Open(string name)
    {
        var m = Store.Map(name);
        if (m == null) { Status = $"Couldn't open {name}"; return; }
        Map = m; Map.Name = name; AfterLoad(); Status = $"Opened {name}";
    }
    public void New(bool withFloor = true)
    {
        Map = new MapDef { Name = "new_map", Spawn = new Vector3(0, 0.1f, 0) };
        if (withFloor) Map.Objects.Add(new MapObject { Type = MapTypes.Box, Name = "floor", Pos = new Vector3(0, -1, 0), Size = new Vector3(60, 2, 60), Color = "#8A8F96" });
        AfterLoad(); savedJson = null; Status = "New map";
        View.Eye = new Vector3(0, 14, 26); View.Yaw = 0; View.Pitch = -0.45f;
    }
    void AfterLoad()
    {
        undo.Clear(); redo.Clear(); committed = Json.Write(Map); savedJson = committed; Sel = -1; previewJson = null;
    }
    public void Save()
    {
        if (string.IsNullOrWhiteSpace(Map.Name)) { Status = "Give the map a name first"; return; }
        Map.Name = Map.Name.Trim().Replace(' ', '_');
        Store.SaveMap(Map); savedJson = Json.Write(Map);
        Status = $"Saved {Store.MapPath(Map.Name)}";
    }
    public void PlayTest()
    {
        Save();
        if (LaunchGame == null) { Status = "Can't start the game from here"; return; }
        LaunchGame($"--map {Map.Name}");
        Status = $"Started the game on {Map.Name}";
    }

    // ---------------------------------------------------------------- undo
    void TrackUndo()
    {
        if (Ui.Dragging || View.GizmoActive) return;
        var now = Json.Write(Map);
        if (now == committed) return;
        undo.Add(committed); if (undo.Count > 200) undo.RemoveAt(0);
        redo.Clear(); committed = now;
    }
    public void Undo() { if (undo.Count == 0) { Status = "Nothing to undo"; return; } redo.Add(Json.Write(Map)); Map = Json.Read<MapDef>(undo[^1]); undo.RemoveAt(undo.Count - 1); committed = Json.Write(Map); FixSel(); Status = "Undone"; }
    public void Redo() { if (redo.Count == 0) { Status = "Nothing to redo"; return; } undo.Add(Json.Write(Map)); Map = Json.Read<MapDef>(redo[^1]); redo.RemoveAt(redo.Count - 1); committed = Json.Write(Map); FixSel(); Status = "Redone"; }
    void FixSel() { if (Sel >= Map.Objects.Count) Sel = -1; }

    // ---------------------------------------------------------------- editing
    public static float Snapped(float v, float step) => step > 0 ? MathF.Round(v / step) * step : v;
    Vector3 SnapV(Vector3 v) => Snap ? new Vector3(Snapped(v.X, SnapSize), Snapped(v.Y, SnapSize), Snapped(v.Z, SnapSize)) : v;

    /// <summary>Where new things go: the floor under the middle of the view, or 12 m ahead.</summary>
    public Vector3 DropPoint()
    {
        var o = View.CamPos; var d = View.Forward;
        if (preview != null) { var h = preview.Level.World.Raycast(o, d, 200); if (h.Hit) return SnapV(h.Point); }
        return SnapV(o + d * 12);
    }

    public MapObject Add(string type, string sub = null)
    {
        var p = DropPoint();
        var o = new MapObject { Type = type, Pos = p };
        switch (type)
        {
            case MapTypes.Box: o.Size = new Vector3(4, 1, 4); o.Pos = p + new Vector3(0, 0.5f, 0); o.Color = "#6F8FB8"; break;
            case MapTypes.Ramp: o.Angle = 20; o.Height = 2; o.Width = 3; o.Landing = 3; o.Color = "#6F8FB8"; break;
            case MapTypes.Zone:
                o.Zone = sub ?? "Damage"; o.Size = new Vector3(4, 2, 4); o.Pos = p + new Vector3(0, 0.5f, 0); o.Name = o.Zone;
                (o.Power, o.Dir) = o.Zone switch
                {
                    "Damage" => (20f, Vector3.Zero), "Heal" => (30f, Vector3.Zero), "JumpPad" => (12f, new Vector3(0, 12, 0)),
                    "Boost" => (28f, new Vector3(1, 0, 0)), "LowGravity" => (0.3f, Vector3.Zero), "Updraft" => (40f, Vector3.Zero), _ => (0f, Vector3.Zero),
                };
                if (o.Zone == "JumpPad" || o.Zone == "Boost") { o.Size = new Vector3(2, 0.9f, 2); o.Pos = p + new Vector3(0, -0.05f, 0); }
                if (o.Zone == "Area") { o.Size = new Vector3(20, 10, 20); o.Pos = p + new Vector3(0, 4, 0); }
                break;
            case MapTypes.Germ: o.Entity = sub ?? "coccus"; o.Yaw = 0; break;
            case MapTypes.Ball: o.Radius = 0.5f; o.Pos = p + new Vector3(0, 0.55f, 0); o.Color = "#E05050"; break;
            case MapTypes.Pickup: o.Pickup = sub ?? "Health"; o.Pos = p + new Vector3(0, 0.6f, 0); break;
            case MapTypes.Sign: o.Text = "SIGN"; o.Pos = p + new Vector3(0, 2.5f, 0); o.Range = 45; break;
            case MapTypes.Landmark: o.Name = "Landmark " + (Map.Objects.Count(x => x.Type == MapTypes.Landmark) + 1); o.Pos = p + new Vector3(0, 0.1f, 0); break;
            case MapTypes.Dummy: break;
        }
        Map.Objects.Add(o); Sel = Map.Objects.Count - 1;
        Status = $"Added a {type}{(sub != null ? " (" + sub + ")" : "")}";
        return o;
    }
    public void Duplicate()
    {
        if (Cur == null) return;
        var c = Cur.Clone(); c.Pos += new Vector3(SnapSize > 0 && Snap ? MathF.Max(SnapSize, 1) : 1, 0, 0);
        if (c.Type == MapTypes.Dummy && c.Speed > 0) c.RailEnd += new Vector3(1, 0, 0);
        Map.Objects.Insert(Sel + 1, c); Sel++; Status = "Duplicated";
    }
    public void Delete()
    {
        if (Cur == null) return;
        Map.Objects.RemoveAt(Sel); Sel = Math.Min(Sel, Map.Objects.Count - 1); if (Map.Objects.Count == 0) Sel = -1;
        Status = "Deleted";
    }
    public void RotateYaw(float deg)
    {
        var o = Cur; if (o == null) { if (Sel == -2) Map.SpawnYaw = MathX.AngleWrap(Map.SpawnYaw + deg * MathX.Deg); return; }
        switch (o.Type)
        {
            case MapTypes.Box: o.Rot.Y = Wrap180(o.Rot.Y + deg); break;
            case MapTypes.Ramp: case MapTypes.Germ: case MapTypes.Landmark: o.Yaw = Wrap180(o.Yaw + deg); break;
            case MapTypes.Zone: o.Size = new Vector3(o.Size.Z, o.Size.Y, o.Size.X); if (o.Dir != Vector3.Zero) o.Dir = Vector3.Transform(o.Dir, Quaternion.CreateFromAxisAngle(Vector3.UnitY, deg * MathX.Deg)); break;
        }
    }
    static float Wrap180(float d) { while (d > 180) d -= 360; while (d < -180) d += 360; return d; }

    // ---------------------------------------------------------------- bounds (picking and highlight)
    /// <summary>A unit-cube transform that encloses the object (for picking and the selection box).</summary>
    public Matrix4x4 Bounds(MapObject o)
    {
        switch (o.Type)
        {
            case MapTypes.Box: return Matrix4x4.CreateScale(Vector3.Abs(o.Size)) * Matrix4x4.CreateFromQuaternion(Animator.Euler(o.Rot)) * Matrix4x4.CreateTranslation(o.Pos);
            case MapTypes.Ramp:
                {
                    float a = Math.Clamp(o.Angle, 1, 89) * MathX.Deg, run = o.Height / MathF.Tan(a), len = run + MathF.Max(0, o.Landing);
                    float yaw = o.Yaw * MathX.Deg; var fwd = MathX.Forward(yaw, 0);
                    return Matrix4x4.CreateScale(o.Width, o.Height, len) * Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(o.Pos + fwd * (len / 2) + new Vector3(0, o.Height / 2, 0));
                }
            case MapTypes.Zone: return Matrix4x4.CreateScale(Vector3.Abs(o.Size)) * Matrix4x4.CreateTranslation(o.Pos);
            case MapTypes.Germ:
                {
                    var e = Store.Entity(o.Entity); float s = e.Stats.Scale, r = e.Stats.Radius * s * 2.4f, h = e.Stats.Height * s;
                    return Matrix4x4.CreateScale(r, h, r) * Matrix4x4.CreateTranslation(o.Pos + new Vector3(0, h / 2, 0));
                }
            case MapTypes.Dummy: return Matrix4x4.CreateScale(o.Heavy ? 1.6f : 1f, o.Heavy ? 2.4f : 1.9f, o.Heavy ? 1.6f : 1f) * Matrix4x4.CreateTranslation(o.Pos + new Vector3(0, o.Heavy ? 1.2f : 0.95f, 0));
            case MapTypes.Ball: return Matrix4x4.CreateScale(o.Radius * 2) * Matrix4x4.CreateTranslation(o.Pos);
            case MapTypes.Pickup: return Matrix4x4.CreateScale(0.9f) * Matrix4x4.CreateTranslation(o.Pos);
            case MapTypes.Sign: return Matrix4x4.CreateScale(0.8f) * Matrix4x4.CreateTranslation(o.Pos);
            case MapTypes.Landmark: return Matrix4x4.CreateScale(0.7f, 2.2f, 0.7f) * Matrix4x4.CreateTranslation(o.Pos + new Vector3(0, 1.1f, 0));
        }
        return Matrix4x4.CreateTranslation(o.Pos);
    }
    static Matrix4x4 SpawnBounds(MapDef m) => Matrix4x4.CreateScale(0.9f, 1.8f, 0.9f) * Matrix4x4.CreateTranslation(m.Spawn + new Vector3(0, 0.9f, 0));

    public string Describe(MapObject o) => o.Type switch
    {
        MapTypes.Box => $"box {o.Size.X:0.#}x{o.Size.Y:0.#}x{o.Size.Z:0.#}" + (string.IsNullOrEmpty(o.Name) ? "" : " " + o.Name),
        MapTypes.Ramp => $"ramp {o.Angle:0}deg {o.Height:0.#}m",
        MapTypes.Zone => $"zone {o.Zone}" + (string.IsNullOrEmpty(o.Name) || o.Name == o.Zone ? "" : " " + o.Name),
        MapTypes.Germ => $"germ {o.Entity}",
        MapTypes.Dummy => o.Heavy ? "dummy (heavy)" : o.Speed > 0 ? $"dummy on rail {o.Speed:0.#}m/s" : "dummy",
        MapTypes.Ball => $"ball r{o.Radius:0.##}",
        MapTypes.Pickup => $"pickup {o.Pickup}",
        MapTypes.Sign => $"sign \"{o.Text}\"",
        MapTypes.Landmark => $"landmark {o.Name}",
        _ => o.Type,
    };

    // ---------------------------------------------------------------- per frame
    public void Frame(UiInput input, float dt, int w, int h, DrawList dl)
    {
        time += dt;
        Ui.S = h >= 860 ? 2 : 1;
        dl.Clear();
        Ui.Begin(input, dl.Overlay, w, h);
        float top = Ui.RowH + 2 * Ui.Pad, bottom = Ui.RowH + Ui.Pad, leftW = MathF.Max(300, w * 0.23f), rightW = MathF.Max(330, w * 0.26f);
        View.Rect = new Vector4(leftW, top, w - leftW - rightW, h - top - bottom);
        Toolbar(new Vector4(0, 0, w, top));
        LeftPanel(new Vector4(0, top, leftW, h - top - bottom));
        RightPanel(new Vector4(w - rightW, top, rightW, h - top - bottom));
        Ui.Bar(new Vector4(0, h - bottom, w, bottom), (Dirty ? "* unsaved   " : "") + Status +
            "      view: hold right mouse + WASD/QE to fly, wheel speed, click select, R rotate, ctrl+D duplicate, Del delete, F focus");
        Shortcuts(input);
        Scene(input, dt, w, h, dl);
        Ui.End();
        TrackUndo();
    }

    void Shortcuts(UiInput i)
    {
        if (Ui.TextFocused || View.Looking) return;
        if (i.Ctrl && i.Key('Z')) { if (i.Shift) Redo(); else Undo(); }
        if (i.Ctrl && i.Key('Y')) Redo();
        if (i.Ctrl && i.Key('S')) Save();
        if (i.Ctrl && i.Key('D')) Duplicate();
        if (i.Key(Keys.Delete)) Delete();
        if (i.Key('R') && !i.Ctrl) RotateYaw(i.Shift ? -15 : 15);
        if (i.Key('F') && !i.Ctrl) Focus();
        if (i.Key(Keys.Escape)) Sel = -1;
    }

    public void Focus()
    {
        Vector3 c; float r;
        if (Cur != null) { var b = Bounds(Cur); c = b.Translation; r = MathF.Max(1.5f, new Vector3(b.M11, b.M12, b.M13).Length() * 0.6f); }
        else { c = Map.Spawn; r = 3; }
        View.Frame(c, r);
    }

    void Scene(UiInput input, float dt, int w, int h, DrawList dl)
    {
        View.Controls(input, Ui.MouseOverUi, !Ui.TextFocused, dt);
        View.Setup(dl, w, h);

        // the preview is the real game world, rebuilt whenever the map changes
        var json = Json.Write(Map);
        if (json != previewJson)
        {
            try { preview = new Game(Level.FromMap(Json.Read<MapDef>(json)), Store); } catch (Exception e) { Status = "Map error: " + e.Message; }
            previewJson = json;
        }
        if (preview == null) return;
        preview.ShowZones = ShowZones;
        foreach (var g in preview.Germs) g.Update(preview, dt);
        preview.DrawWorld(dl, false);

        // things the game doesn't draw: the spawn, signs and landmarks, zone outlines
        DrawSpawn(dl);
        for (int i = 0; i < Map.Objects.Count; i++)
        {
            var o = Map.Objects[i];
            if (o.Type == MapTypes.Landmark) DrawLandmark(dl, o);
            if (o.Type == MapTypes.Sign) { dl.Rod(o.Pos - new Vector3(0, 2.5f, 0), o.Pos - new Vector3(0, 0.3f, 0), 0.05f, new Vector4(0.8f, 0.8f, 0.8f, 1)); dl.Box(o.Pos, new Vector3(0.6f, 0.4f, 0.05f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, View.Yaw), new Vector4(0.95f, 0.85f, 0.4f, 1), DrawFlags.Unlit); }
            if (o.Type == MapTypes.Zone && ShowZones && o.Zone != "Area") View3D.WireBox(dl, Bounds(o), ZoneCol(o.Zone), 0.03f);
            if (o.Type == MapTypes.Dummy && o.Speed > 0) View3D.Line(dl, o.Pos + new Vector3(0, 0.05f, 0), o.RailEnd + new Vector3(0, 0.05f, 0), new Vector4(1, 0.8f, 0.3f, 0.9f), 0.04f);
        }
        if (ShowLabels) Labels(dl, w, h);

        // selection, gizmo, picking
        bool uiMouse = Ui.MouseOverUi;
        if (Cur != null || Sel == -2)
        {
            View3D.WireBox(dl, Sel == -2 ? SpawnBounds(Map) : Bounds(Cur), new Vector4(1, 0.85f, 0.2f, 1), 0.035f);
            var pos = Sel == -2 ? Map.Spawn : Cur.Pos;
            var giz = View.Gizmo(dl, input, pos, uiMouse);
            if (View.GizmoActive && !wasDragging) dragRaw = pos;
            wasDragging = View.GizmoActive;
            if (giz != Vector3.Zero)
            {
                dragRaw += giz;
                var np = SnapV(dragRaw); var moved = np - pos;
                if (Sel == -2) Map.Spawn = np;
                else { Cur.Pos = np; if (Cur.Type == MapTypes.Dummy) Cur.RailEnd += moved; }
            }
        }
        if (!uiMouse && View.MouseIn(input) && input.Pressed[0] && !View.GizmoActive) Pick(input);
    }

    void Pick(UiInput input)
    {
        var (o, d) = View.Ray(input);
        float best = float.MaxValue; int bi = -1;
        if (View3D.RayBox(o, d, SpawnBounds(Map), out float ts) && ts < best) { best = ts; bi = -2; }
        for (int i = 0; i < Map.Objects.Count; i++)
        {
            var ob = Map.Objects[i];
            if (ob.Type == MapTypes.Zone && (ob.Zone == "Area" || !ShowZones)) continue;
            if (!View3D.RayBox(o, d, Bounds(ob), out float t)) continue;
            // small things win over the big boxes they stand on
            float bias = ob.Type == MapTypes.Box || ob.Type == MapTypes.Ramp ? 0.5f : ob.Type == MapTypes.Zone ? 0.3f : 0;
            if (t + bias < best) { best = t + bias; bi = i; }
        }
        Sel = bi;
        if (bi >= 0) Status = "Selected " + Describe(Map.Objects[bi]); else if (bi == -2) Status = "Selected the player spawn"; else Status = "Nothing selected: map settings";
    }

    public static Vector4 ZoneCol(string kind) => kind switch
    {
        "Damage" => MathX.Rgb(0x9CFF30), "Heal" => MathX.Rgb(0xFF70B0), "Kill" => MathX.Rgb(0xFF2020), "JumpPad" => MathX.Rgb(0xFF9A30),
        "Boost" => MathX.Rgb(0x30E0FF), "LowGravity" => MathX.Rgb(0xB080FF), "Updraft" => MathX.Rgb(0x90D0FF), "Checkpoint" => MathX.Rgb(0x40FF80), _ => MathX.Rgb(0xFFFFFF, 0.5f),
    };

    EntityView heroView;
    void DrawSpawn(DrawList dl)
    {
        heroView ??= new EntityView(Store.Entity("hero"));
        var ctx = new AnimContext { Feet = Map.Spawn, Yaw = Map.SpawnYaw, Time = time, Dt = 0, GroundAt = null };
        heroView.Update(ctx); heroView.Evaluate(ctx); heroView.Draw(dl, false, 1, -1, 0.8f);
        var f = MathX.Forward(Map.SpawnYaw, 0);
        dl.Rod(Map.Spawn + new Vector3(0, 0.05f, 0), Map.Spawn + f * 1.6f + new Vector3(0, 0.05f, 0), 0.06f, new Vector4(0.3f, 1, 0.5f, 1), DrawFlags.Unlit);
        dl.Rod(Map.Spawn + f * 1.6f + new Vector3(0, 0.05f, 0), Map.Spawn + f * 2.0f + new Vector3(0, 0.05f, 0), 0.15f, new Vector4(0.3f, 1, 0.5f, 1), DrawFlags.Unlit, MeshId.Cone);
    }

    void DrawLandmark(DrawList dl, MapObject o)
    {
        var c = new Vector4(0.4f, 0.8f, 1f, 1);
        dl.Rod(o.Pos, o.Pos + new Vector3(0, 2.2f, 0), 0.04f, c, DrawFlags.Unlit);
        var f = MathX.Forward(o.Yaw * MathX.Deg, 0);
        dl.Box(o.Pos + new Vector3(0, 1.95f, 0) + f * 0.25f, new Vector3(0.05f, 0.4f, 0.5f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, o.Yaw * MathX.Deg), c, DrawFlags.Unlit);
        dl.Rod(o.Pos + new Vector3(0, 0.05f, 0), o.Pos + f * 1.2f + new Vector3(0, 0.05f, 0), 0.05f, c, DrawFlags.Unlit);
    }

    void Labels(DrawList dl, int w, int h)
    {
        float s = MathF.Max(1, Ui.S - 1);
        foreach (var o in Map.Objects)
        {
            string text = o.Type == MapTypes.Sign ? o.Text : o.Type == MapTypes.Landmark ? "> " + o.Name : null;
            if (text == null) continue;
            var p = o.Type == MapTypes.Landmark ? o.Pos + new Vector3(0, 2.5f, 0) : o.Pos + new Vector3(0, 0.45f, 0);
            if (Vector3.Distance(p, View.CamPos) > 90) continue;
            if (!View.Project(p, out var sp) || sp.X < View.Rect.X || sp.X > View.Rect.X + View.Rect.Z || sp.Y < View.Rect.Y || sp.Y > View.Rect.Y + View.Rect.W) continue;
            dl.Overlay.TextCentered(sp.X, sp.Y, text, s, o.Type == MapTypes.Landmark ? new Vector4(0.5f, 0.85f, 1, 1) : new Vector4(1, 1, 1, 0.95f));
        }
    }
}
