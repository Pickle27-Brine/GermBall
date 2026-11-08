using System;
using System.Collections.Generic;
using GermBall.Core;
using static GermBall.Core.M;

namespace GermBall.Game;

public sealed partial class Brain
{
    /// <summary>A planned route (waypoints) around obstacles, the goal it was planned for, and when to re-plan.</summary>
    public List<(float x, float z)> path; public int pathI; public float pathGx, pathGz, repathT, blockedT;
    /// <summary>Companions: the breadcrumb they are following along your trail.</summary>
    public int crumbI = -1;
}

/// <summary>
/// Getting around: a coarse occupancy grid with A* for anyone who is blocked or stuck (companions and germs alike), and a
/// breadcrumb trail of where you have walked, which companions retrace when they fall behind, so they come round
/// obstacles and up stairs the way you did instead of teleporting to you.
/// </summary>
public sealed partial class World
{
    const float NAV_CELL = 1.5f; const int NAV_R = 40;   // a 120 m square around the start, 1.5 m cells
    readonly Dictionary<long, bool> navBlocked = new();
    bool NavBlocked(int cx, int cz, float y)
    {
        long key = ((long)cx << 32) ^ (uint)cz ^ ((long)MathF.Round(y / 3) << 56);
        if (navBlocked.TryGetValue(key, out var b)) return b;
        float x = cx * NAV_CELL, z = cz * NAV_CELL, gy = GroundY(x, z);
        b = MathF.Abs(gy - y) > 2.2f || InsideStatic(x, z, MathF.Max(gy, y - 0.5f), 0.55f);
        if (navBlocked.Count > 60000) navBlocked.Clear();
        navBlocked[key] = b; return b;
    }
    /// <summary>Forget cached cells near a change (a wall smashed, for instance).</summary>
    public void NavInvalidate() => navBlocked.Clear();

    readonly PriorityQueue<(int, int), float> navOpen = new();
    readonly Dictionary<(int, int), (int, int)> navFrom = new(); readonly Dictionary<(int, int), float> navG = new();
    public List<(float x, float z)> FindPath(float sx, float sz, float sy, float gx, float gz, int maxIter = 3500)
    {
        int s0 = FloorI(sx / NAV_CELL + 0.5f), s1 = FloorI(sz / NAV_CELL + 0.5f), g0 = FloorI(gx / NAV_CELL + 0.5f), g1 = FloorI(gz / NAV_CELL + 0.5f);
        g0 = Math.Clamp(g0, s0 - NAV_R, s0 + NAV_R); g1 = Math.Clamp(g1, s1 - NAV_R, s1 + NAV_R);
        navOpen.Clear(); navFrom.Clear(); navG.Clear();
        var start = (s0, s1); var goal = (g0, g1); navOpen.Enqueue(start, 0); navG[start] = 0;
        (int, int) best = start; float bestH = float.MaxValue; int iter = 0;
        ReadOnlySpan<(int, int, float)> nb = stackalloc (int, int, float)[0];
        while (navOpen.Count > 0 && iter++ < maxIter)
        {
            var c = navOpen.Dequeue();
            float h = Hypot(c.Item1 - g0, c.Item2 - g1); if (h < bestH) { bestH = h; best = c; }
            if (c == goal) break;
            float gc = navG[c];
            for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0) continue;
                    var n = (c.Item1 + dx, c.Item2 + dz);
                    if (Math.Abs(n.Item1 - s0) > NAV_R || Math.Abs(n.Item2 - s1) > NAV_R) continue;
                    if (NavBlocked(n.Item1, n.Item2, sy)) continue;
                    if (dx != 0 && dz != 0 && (NavBlocked(c.Item1 + dx, c.Item2, sy) || NavBlocked(c.Item1, c.Item2 + dz, sy))) continue;   // no cutting corners
                    float ng = gc + (dx != 0 && dz != 0 ? 1.414f : 1);
                    if (navG.TryGetValue(n, out var og) && og <= ng) continue;
                    navG[n] = ng; navFrom[n] = c; navOpen.Enqueue(n, ng + Hypot(n.Item1 - g0, n.Item2 - g1) * 1.05f);
                }
        }
        // walk back from the goal (or the closest we got), then smooth out the zig-zags
        var cells = new List<(int, int)>(); var at = navG.ContainsKey(goal) ? goal : best;
        while (true) { cells.Add(at); if (!navFrom.TryGetValue(at, out var p)) break; at = p; }
        cells.Reverse();
        var pts = new List<(float x, float z)>();
        int i0 = 0;
        while (i0 < cells.Count - 1)
        {
            int j = cells.Count - 1;
            while (j > i0 + 1 && !NavLine(cells[i0], cells[j], sy)) j--;
            pts.Add((cells[j].Item1 * NAV_CELL, cells[j].Item2 * NAV_CELL)); i0 = j;
        }
        return pts;
    }
    bool NavLine((int, int) a, (int, int) b, float y)
    {
        int n = Math.Max(Math.Abs(b.Item1 - a.Item1), Math.Abs(b.Item2 - a.Item2)) * 2;
        for (int k = 1; k < n; k++) { float t = (float)k / n; if (NavBlocked((int)MathF.Round(Lerp(a.Item1, b.Item1, t)), (int)MathF.Round(Lerp(a.Item2, b.Item2, t)), y)) return false; }
        return true;
    }
    /// <summary>Is the straight walk from here to there clear?</summary>
    bool ClearWalk(float x0, float z0, float y, float x1, float z1, float pad)
    {
        float d = Hypot(x1 - x0, z1 - z0); int n = Math.Max(1, (int)(d / 1.2f));
        for (int k = 1; k <= n; k++) { float t = (float)k / n; if (InsideStatic(Lerp(x0, x1, t), Lerp(z0, z1, t), y, pad)) return false; }
        return true;
    }

    /// <summary>Where a brain should actually head to reach (gx, gz): the goal itself when the way is clear, otherwise the next
    /// waypoint of a planned route (planned when blocked or stuck, refreshed now and then).</summary>
    (float, float) NavGoal(Actor A, float gx, float gz, float dt)
    {
        var G = A.P; var B = A.brain; if (B == null || G.vehicle != null) return (gx, gz);
        float dg = Hypot(gx - G.x, gz - G.z); if (dg < 3) { B.path = null; return (gx, gz); }
        B.repathT -= dt;
        bool goalMoved = B.path != null && Hypot(B.pathGx - gx, B.pathGz - gz) > 6;
        if (B.path == null || goalMoved || B.repathT <= 0)
        {
            bool blocked = B.stuckT > 0.35f || !ClearWalk(G.x, G.z, G.y, gx, gz, G.R + 0.1f);
            B.blockedT = blocked ? B.blockedT + MathF.Max(dt, 0.2f) : 0;
            if (blocked && (B.blockedT > 0.3f || B.stuckT > 0.35f)) { B.path = FindPath(G.x, G.z, G.y, gx, gz); B.pathI = 0; B.pathGx = gx; B.pathGz = gz; B.repathT = 1.5f + R(); }
            else if (!blocked) { B.path = null; B.repathT = 0.6f; }
            else B.repathT = 0.3f;
        }
        if (B.path == null || B.path.Count == 0) return (gx, gz);
        while (B.pathI < B.path.Count && Hypot(B.path[B.pathI].x - G.x, B.path[B.pathI].z - G.z) < 1.4f) B.pathI++;
        if (B.pathI >= B.path.Count) { B.path = null; return (gx, gz); }
        return B.path[B.pathI];
    }

    // ---- your trail, for companions
    readonly List<(float x, float y, float z)> crumbs = new();
    void StepCrumbs()
    {
        var H = HUMAN?.P; if (H == null || !H.alive || HUMAN.inMecha != null) return;
        if (!H.ground && H.vehicle == null) return;
        if (crumbs.Count == 0 || Hypot(crumbs[^1].x - H.x, crumbs[^1].z - H.z) > 1.8f || MathF.Abs(crumbs[^1].y - H.y) > 1.2f)
        {
            crumbs.Add((H.x, H.y, H.z)); if (crumbs.Count > 240) crumbs.RemoveAt(0);
            foreach (var A in ALLIES) if (A.brain != null && A.brain.crumbI > 0) A.brain.crumbI = Math.Max(0, A.brain.crumbI - (crumbs.Count == 240 ? 1 : 0));
        }
    }
    /// <summary>The next point on your trail for a companion that has fallen behind; null when it should just come straight.</summary>
    (float, float)? CrumbGoal(Actor A)
    {
        var G = A.P; var B = A.brain; if (crumbs.Count < 2) return null;
        if (B.crumbI < 0 || B.crumbI >= crumbs.Count)
        {   // join the trail at the nearest crumb it can walk straight to
            int bi = -1; float bd = 40;
            for (int i = crumbs.Count - 1; i >= 0; i--) { var c = crumbs[i]; float d = Hypot(c.x - G.x, c.z - G.z); if (d < bd && MathF.Abs(c.y - G.y) < 2.5f && ClearWalk(G.x, G.z, G.y, c.x, c.z, G.R)) { bd = d; bi = i; } }
            if (bi < 0) return null; B.crumbI = bi;
        }
        while (B.crumbI < crumbs.Count - 1 && Hypot(crumbs[B.crumbI].x - G.x, crumbs[B.crumbI].z - G.z) < 1.5f) B.crumbI++;
        // skip ahead while the straight line to a later crumb is clear
        for (int j = Math.Min(crumbs.Count - 1, B.crumbI + 6); j > B.crumbI; j--) if (MathF.Abs(crumbs[j].y - G.y) < 1.5f && ClearWalk(G.x, G.z, G.y, crumbs[j].x, crumbs[j].z, G.R)) { B.crumbI = j; break; }
        var t = crumbs[B.crumbI]; return (t.x, t.z);
    }
}
