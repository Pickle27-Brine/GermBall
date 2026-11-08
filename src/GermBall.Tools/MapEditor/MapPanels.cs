using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GermBall.Content;
using GermBall.Engine;
using GermBall.Sim;
using GermBall.Tools.Ui;

namespace GermBall.Tools.MapEditor;

public sealed partial class MapEditor
{
    string zoneKind = "Damage", germEntity = "coccus", pickupKind = "Health", filter = "all", search = "";
    float padTop = 3; Vector3 padTarget;
    bool hdrAdd = true, hdrList = true, doubleClicked;
    static readonly string[] ZoneKinds = Enum.GetNames(typeof(ZoneKind));
    static readonly string[] PickupKinds = Enum.GetNames(typeof(PickupKind));
    static readonly string[] Filters = new[] { "all" }.Concat(MapTypes.All).ToArray();
    static readonly string[] SnapSizes = { "0.1", "0.25", "0.5", "1", "2" };

    void Toolbar(Vector4 r)
    {
        Ui.BeginArea("toolbar", r, Gui.TitleBg);
        float bw = 8 * Ui.CharW;
        Ui.Button("MAP EDITOR##title", 12 * Ui.CharW, false); Ui.SameLine();
        if (Ui.Button("New", bw)) New();
        Ui.SameLine();
        string pick = null;
        if (Ui.ComboW("##open", ref pick, Store.MapNames().ToList(), 14 * Ui.CharW, "open...") && pick != null) Open(pick);
        Ui.SameLine(); if (Ui.Button("Save", bw)) Save();
        Ui.SameLine(); if (Ui.Button("Undo", bw)) Undo();
        Ui.SameLine(); if (Ui.Button("Redo", bw)) Redo();
        Ui.SameLine(); if (Ui.Button("Play test", 12 * Ui.CharW, true, Gui.AccentDim)) PlayTest();
        Ui.SameLine(); if (Ui.Toggle("Snap", Snap, bw)) Snap = !Snap;
        Ui.SameLine();
        string ss = SnapSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (Ui.ComboW("##snap", ref ss, SnapSizes, 7 * Ui.CharW)) SnapSize = float.Parse(ss, System.Globalization.CultureInfo.InvariantCulture);
        Ui.SameLine(); if (Ui.Toggle("Zones", ShowZones, bw)) ShowZones = !ShowZones;
        Ui.SameLine(); if (Ui.Toggle("Labels", ShowLabels, bw)) ShowLabels = !ShowLabels;
        Ui.SameLine(); Ui.Button($"{Map.Name}  ({Map.Objects.Count} objects){(Dirty ? " *" : "")}##name", 0, false);
        Ui.EndArea();
    }

    void LeftPanel(Vector4 r)
    {
        Ui.BeginPanel("left", r.X, r.Y, r.Z, r.W);
        // a double-click is seen on the press, but list rows report their click on the release
        if (Ui.In.Pressed[0]) doubleClicked = Ui.In.DoubleClick;
        if (Ui.Header("Add", ref hdrAdd))
        {
            Ui.Label("new things go where the view is pointing", Gui.Dim);
            if (Ui.Button("Box", Ui.Split(2))) Add(MapTypes.Box);
            Ui.SameLine(); if (Ui.Button("Ramp", Ui.Split(2))) Add(MapTypes.Ramp);
            Ui.Combo("Zone kind", ref zoneKind, ZoneKinds);
            if (Ui.Button("Zone: " + zoneKind)) Add(MapTypes.Zone, zoneKind);
            Ui.Combo("Germ", ref germEntity, Store.EntityNames().Where(n => Store.Entity(n).Kind != "player").ToList());
            if (Ui.Button("Germ: " + germEntity)) Add(MapTypes.Germ, germEntity);
            Ui.Combo("Pickup", ref pickupKind, PickupKinds);
            if (Ui.Button("Pickup: " + pickupKind)) Add(MapTypes.Pickup, pickupKind);
            if (Ui.Button("Dummy", Ui.Split(2))) Add(MapTypes.Dummy);
            Ui.SameLine(); if (Ui.Button("Ball", Ui.Split(2))) Add(MapTypes.Ball);
            if (Ui.Button("Sign", Ui.Split(2))) Add(MapTypes.Sign);
            Ui.SameLine(); if (Ui.Button("Landmark", Ui.Split(2))) Add(MapTypes.Landmark);
        }
        if (Ui.Header($"Objects ({Map.Objects.Count})", ref hdrList))
        {
            Ui.Combo("Show", ref filter, Filters);
            Ui.TextField("Search", ref search, "type to filter");
            if (Ui.Selectable("player spawn", Sel == -2, 0, Gui.Accent)) Sel = -2;
            int shown = 0;
            for (int i = 0; i < Map.Objects.Count; i++)
            {
                var o = Map.Objects[i];
                if (filter != "all" && o.Type != filter) continue;
                string d = Describe(o);
                if (!string.IsNullOrEmpty(search) && d.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (++shown > 400) { Ui.Label("... narrow the list with Show or Search", Gui.Dim); break; }
                if (Ui.Selectable($"{i}. {d}##o{i}", i == Sel)) { Sel = i; if (doubleClicked) Focus(); }
            }
        }
        Ui.EndPanel();
    }

    void RightPanel(Vector4 r)
    {
        Ui.BeginPanel("right", r.X, r.Y, r.Z, r.W, Sel == -1 ? "Map settings" : Sel == -2 ? "Player spawn" : "Properties");
        if (Sel == -1) MapSettings();
        else if (Sel == -2) SpawnProps();
        else if (Cur != null) ObjectProps(Cur);
        Ui.EndPanel();
    }

    void MapSettings()
    {
        string n = Map.Name; if (Ui.TextField("Name", ref n) && !string.IsNullOrWhiteSpace(n)) Map.Name = n.Trim().Replace(' ', '_');
        Ui.Label("saves as content/maps/" + Map.Name + ".map.json", Gui.Dim);
        Ui.Drag("Kill height", ref Map.KillY, 0.1f, "0.0");
        Ui.Label("anything below this dies", Gui.Dim);
        SpawnProps();
        Ui.Heading("Tips");
        Ui.Label("click things in the view to select them", Gui.Dim);
        Ui.Label("drag the arrows to move, the square on the ground", Gui.Dim);
        Ui.Label("Play test saves and starts the game on this map", Gui.Dim);
    }

    void SpawnProps()
    {
        Ui.Heading("Player spawn");
        var p = Map.Spawn; if (Ui.Vec3("Feet", ref p, 0.05f, "0.##")) Map.Spawn = p;
        float y = Map.SpawnYaw / MathX.Deg; if (Ui.Slider("Facing", ref y, -180, 180, "0")) Map.SpawnYaw = y * MathX.Deg;
        if (Ui.Button("Move spawn to the view")) Map.Spawn = DropPoint() + new Vector3(0, 0.1f, 0);
    }

    void ObjectProps(MapObject o)
    {
        Ui.Label(Describe(o), Gui.Accent);
        if (Ui.Button("Duplicate", Ui.Split(3))) Duplicate();
        Ui.SameLine(); if (Ui.Button("Delete", Ui.Split(3), true, Gui.Danger)) { Delete(); return; }
        Ui.SameLine(); if (Ui.Button("Focus", Ui.Split(3))) Focus();
        if (o.Type != MapTypes.Sign) Ui.TextField("Name", ref o.Name, "(optional)");
        Ui.Vec3("Position", ref o.Pos, 0.05f, "0.##");
        if (Ui.Button("Drop to the floor"))
        {
            var h = Preview?.Level.World.Raycast(o.Pos + new Vector3(0, 0.1f, 0), -Vector3.UnitY, 200);
            if (h is RayHit hit && hit.Hit) o.Pos.Y = hit.Point.Y + (o.Type == MapTypes.Box ? o.Size.Y / 2 : o.Type == MapTypes.Ball ? o.Radius : 0);
        }
        switch (o.Type)
        {
            case MapTypes.Box:
                Ui.Vec3("Size", ref o.Size, 0.05f, "0.##");
                Ui.Vec3("Rotation", ref o.Rot, 0.5f, "0.0");
                Ui.Slider("Yaw", ref o.Rot.Y, -180, 180, "0");
                Ui.Color("Colour", ref o.Color);
                Ui.Checkbox("Grid", ref o.Grid);
                break;
            case MapTypes.Ramp:
                Ui.Slider("Facing", ref o.Yaw, -180, 180, "0");
                Ui.Slider("Angle", ref o.Angle, 1, 89, "0.0");
                Ui.Slider("Height", ref o.Height, 0.1f, 20, "0.00");
                Ui.Slider("Width", ref o.Width, 0.5f, 30, "0.0");
                Ui.Slider("Landing", ref o.Landing, 0, 20, "0.0");
                Ui.Label($"walkable up to {Player.MaxSlopeDeg:0} degrees", Gui.Dim);
                Ui.Color("Colour", ref o.Color);
                break;
            case MapTypes.Zone:
                {
                    string k = o.Zone; if (Ui.Combo("Kind", ref k, ZoneKinds)) o.Zone = k;
                    Ui.Vec3("Size", ref o.Size, 0.05f, "0.##");
                    string what = o.Zone switch
                    {
                        "Damage" => "damage per second", "Heal" => "healing per second", "Boost" => "boost speed (m/s)",
                        "LowGravity" => "gravity scale", "Updraft" => "lift (m/s/s)", "JumpPad" => "(launch speed is the Y of Launch)", _ => null,
                    };
                    if (what != null && o.Zone != "JumpPad") { Ui.Drag("Power", ref o.Power, 0.05f, "0.00"); Ui.Label(what, Gui.Dim); }
                    if (o.Zone == "Boost") Ui.Vec3("Direction", ref o.Dir, 0.01f, "0.00");
                    if (o.Zone == "JumpPad")
                    {
                        Ui.Vec3("Launch", ref o.Dir, 0.05f, "0.##");
                        Ui.Heading("Aim the pad");
                        Ui.Slider("Ledge height", ref padTop, 0, 30, "0.0");
                        Ui.Vec3("Land at", ref padTarget, 0.05f, "0.#");
                        if (Ui.Button("Use the view's point", Ui.Split(2))) padTarget = DropPoint();
                        Ui.SameLine();
                        if (Ui.Button("Compute launch", Ui.Split(2)))
                        {
                            var feet = new Vector3(o.Pos.X, o.Pos.Y - o.Size.Y / 2 + 0.5f, o.Pos.Z);
                            o.Dir = Level.JumpPadVelocity(feet, padTop + feet.Y, padTarget); o.Power = o.Dir.Y;
                            Status = $"Pad aimed: {o.Dir.Length():0.0} m/s";
                        }
                    }
                    break;
                }
            case MapTypes.Germ:
                {
                    string e = o.Entity; if (Ui.Combo("Entity", ref e, Store.EntityNames().ToList())) o.Entity = e;
                    var d = Store.Entity(o.Entity);
                    Ui.Label($"{d.Stats.Hp:0} HP, {d.Stats.DashesToFloor} dash(es), scale {d.Stats.Scale:0.##}", Gui.Dim);
                    Ui.Slider("Facing", ref o.Yaw, -180, 180, "0");
                    break;
                }
            case MapTypes.Dummy:
                Ui.Checkbox("Heavy (400 HP)", ref o.Heavy);
                Ui.Slider("Rail speed", ref o.Speed, 0, 15, "0.0");
                if (o.Speed > 0)
                {
                    Ui.Vec3("Rail end", ref o.RailEnd, 0.05f, "0.##");
                    if (Ui.Button("Rail end 10 m ahead")) o.RailEnd = o.Pos + new Vector3(0, 0, -10);
                }
                break;
            case MapTypes.Ball:
                Ui.Slider("Radius", ref o.Radius, 0.1f, 3, "0.00");
                Ui.Color("Colour", ref o.Color);
                break;
            case MapTypes.Pickup:
                { string k = o.Pickup; if (Ui.Combo("Kind", ref k, PickupKinds)) o.Pickup = k; break; }
            case MapTypes.Sign:
                Ui.TextField("Text", ref o.Text);
                Ui.Slider("Seen within", ref o.Range, 5, 150, "0");
                break;
            case MapTypes.Landmark:
                Ui.Slider("Facing", ref o.Yaw, -180, 180, "0");
                Ui.Label("F6 / F7 in the game teleport between landmarks", Gui.Dim);
                break;
        }
        if (o.Type is MapTypes.Box or MapTypes.Ramp or MapTypes.Germ or MapTypes.Landmark or MapTypes.Zone)
        {
            if (Ui.Button("Rotate -15 (shift+R)", Ui.Split(2))) RotateYaw(-15);
            Ui.SameLine(); if (Ui.Button("Rotate +15 (R)", Ui.Split(2))) RotateYaw(15);
        }
    }
}
