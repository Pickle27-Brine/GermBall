using System;
using System.Collections.Generic;
using GermBall.Core;
using static GermBall.Core.M;

namespace GermBall.Game;

/// <summary>A static collider: an axis-aligned box, an upright cylinder, or a height-field patch (ramps, stairs).</summary>
public sealed class Static
{
    public int Kind;   // World.BOXK, CYLK, PATCHK
    public float MinX, MaxX, MinY, MaxY, MinZ, MaxZ;
    public float X, Z, R, Top, Bottom, Bounce;
    public Func<float, float, float> Fn;
    public int Id; internal int Stamp; public bool Dead;
    // breakable building chunks
    public Chunk Chunk;
    public object Tag;
}

public class Pairish { public Body A, B; public readonly V3 RA = new(), RB = new(); }

public sealed class Contact : Pairish
{
    public readonly V3 P = new(), N = new(), T1 = new(), T2 = new();
    public float Depth, Eo, Jn, Jt1, Jt2, Pj, KN, KT1, KT2, BounceV, Mu; public long Key;
}

public sealed class Joint : Pairish
{
    public V3 La, Lb; public readonly V3 Err = new(); public readonly float[] K = new float[3]; public bool On = true; public Q4 Qrel; public float Lim = 1, Tone;
}

public sealed class Body
{
    public int Id, Kind; public V3 He; public float R;
    public readonly V3 Pos = new(), Vel = new(), W = new(), Pv = new(), Pw = new(), IIn = new(), Pp = new(), Home = new();
    public readonly Q4 Q = new(), Pq = new(), HomeQ = new();
    public float M, Im, E = 0.2f, Mu = 0.6f, Roll = 0.6f, Idle, Bound; public int Nc; public bool Sleep, VertsOk;
    public V3[] Verts = Array.Empty<V3>();
    public object Grp, OldGrp, IgnGrp; public HashSet<Body> RagCol; public float SepT, IgnT, ThrownT; public Actor Held;
    public bool NoGrav, Ctrl, Fixed, Spear, Bomb; public float Fuse; public Actor ThrownBy, IgnA;
    internal float SapK; internal int QStamp;
    // game data riding on the body
    public Prop Prop; public Actor Actor; public int Part = -1; public object Tag;
    public bool IsDebris; public string Mk; public V3 Ell; public float Life, Age, Lazy; public Actor RagdollOf, LastTouch; public float LastTouchT = -1;
    public bool Removed;
}

public sealed partial class World
{
    public const int BOXK = 0, CYLK = 1, PATCHK = 2;
    public const int SPH = 1, BOXB = 2;
    public float PGRAV = 22.05f; const float GRAV0 = 22.05f;

    // ---------------------------------------------------------------- static collision world (spatial hash)
    const int SG = 16, SOFF = 64;
    readonly Dictionary<int, List<Static>> sgrid = new();
    readonly List<Static> statics = new();
    int stampC;
    static int GKey(int i, int j) => (i + SOFF) * 512 + (j + SOFF);
    static int FloorI(float v) => (int)MathF.Floor(v);
    public Static AddStatic(Static o)
    {
        o.Id = statics.Count + 1; o.Stamp = 0; statics.Add(o);
        for (int i = FloorI(o.MinX / SG); i <= FloorI(o.MaxX / SG); i++) for (int j = FloorI(o.MinZ / SG); j <= FloorI(o.MaxZ / SG); j++)
            {
                int k = GKey(i, j); if (!sgrid.TryGetValue(k, out var a)) sgrid[k] = a = new List<Static>(); a.Add(o);
            }
        return o;
    }
    public void RemoveStatic(Static o)
    {
        for (int i = FloorI(o.MinX / SG); i <= FloorI(o.MaxX / SG); i++) for (int j = FloorI(o.MinZ / SG); j <= FloorI(o.MaxZ / SG); j++)
                if (sgrid.TryGetValue(GKey(i, j), out var a)) a.Remove(o);
        o.Dead = true;
    }
    public List<Static> QueryStatics(float minX, float maxX, float minZ, float maxZ, List<Static> outL)
    {
        outL.Clear(); int s = ++stampC;
        for (int i = FloorI(minX / SG); i <= FloorI(maxX / SG); i++) for (int j = FloorI(minZ / SG); j <= FloorI(maxZ / SG); j++)
            {
                if (!sgrid.TryGetValue(GKey(i, j), out var a)) continue;
                foreach (var o in a) if (o.Stamp != s) { o.Stamp = s; outL.Add(o); }
            }
        return outL;
    }
    struct PNrm { public float nx, ny, nz; }
    static PNrm PatchNormal(Static p, float x, float z)
    {
        const float ex = 0.03f; float cx = Clamp(x, p.MinX + ex, p.MaxX - ex), cz = Clamp(z, p.MinZ + ex, p.MaxZ - ex);
        float gx = (p.Fn(cx + ex, cz) - p.Fn(cx - ex, cz)) / (2 * ex), gz = (p.Fn(cx, cz + ex) - p.Fn(cx, cz - ex)) / (2 * ex), nl = Hypot(gx, 1, gz);
        return new PNrm { nx = -gx / nl, ny = 1 / nl, nz = -gz / nl };
    }

    // ---------------------------------------------------------------- rigid bodies
    public readonly List<Body> bodies = new();
    static readonly int[][] SAMP;
    static World()
    {
        var l = new List<int[]>();
        for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++) if (x != 0 || y != 0 || z != 0) l.Add(new[] { x, y, z });
        l.Sort((a, b) => (Math.Abs(b[0]) + Math.Abs(b[1]) + Math.Abs(b[2])) - (Math.Abs(a[0]) + Math.Abs(a[1]) + Math.Abs(a[2])));
        SAMP = l.ToArray();
    }
    int bodiesStamp, nextBodyId = 1;

    public Body MakeBody(int kind, float x, float y, float z, float m, V3 he = null, float r = 0, float e = 0.2f, float mu = 0.6f, float roll = 0.6f, float yaw = 0)
    {
        var b = new Body { Id = nextBodyId++, Kind = kind, He = he, R = r, M = m, Im = 1 / m, E = e, Mu = mu, Roll = roll };
        b.Pos.Set(x, y, z); b.Pp.Set(x, y, z); b.Home.Set(x, y, z);
        if (kind == BOXB)
        {
            float hx = he.x, hy = he.y, hz = he.z;
            b.IIn.Set(3 / (m * (hy * hy + hz * hz)), 3 / (m * (hx * hx + hz * hz)), 3 / (m * (hx * hx + hy * hy)));
            b.Bound = Hypot(hx, hy, hz); b.Verts = new V3[SAMP.Length]; for (int i = 0; i < SAMP.Length; i++) b.Verts[i] = new V3();
        }
        else { float k = 2.5f / (m * r * r); b.IIn.Set(k, k, k); b.Bound = r; }
        if (yaw != 0) { b.Q.y = MathF.Sin(yaw / 2); b.Q.w = MathF.Cos(yaw / 2); }
        b.Pq.Copy(b.Q); b.HomeQ.Copy(b.Q);
        bodies.Add(b); bodiesStamp++; return b;
    }
    public void RemoveBody(Body b) { if (b == null || b.Removed) return; b.Removed = true; bodies.Remove(b); bodiesStamp++; joints.RemoveAll(j => j.A == b || j.B == b); }
    static bool Dyn(Body b) => b != null && !b.Sleep;

    // ball-and-socket joints (ragdolls) solved alongside contacts
    public readonly List<Joint> joints = new();
    static readonly V3[] JN = { new(1, 0, 0), new(0, 1, 0), new(0, 0, 1) };
    public Joint AddJoint(Body a, Body b, float[] la, float[] lb) { var j = new Joint { A = a, B = b, La = new V3(la[0], la[1], la[2]), Lb = new V3(lb[0], lb[1], lb[2]) }; joints.Add(j); return j; }

    // groups: bodies in the same group (one enemy, or a severed limb chain) skip colliding with each other
    static bool NoPair(Body a, Body b)
    {
        if (a.Grp != null && a.Grp == b.Grp) return !(a.RagCol != null && a.RagCol.Contains(b));
        if (a.SepT > 0 && b.Grp != null && b.Grp == a.OldGrp) return true;
        if (b.SepT > 0 && a.Grp != null && a.Grp == b.OldGrp) return true;
        // (as in the original) a held or just-dropped body passes through every ungrouped body
        if (a.Held != null && b.Grp == null) return true;
        if (b.Held != null && a.Grp == null) return true;
        if (a.IgnT > 0 && b.Grp == a.IgnGrp) return true;
        if (b.IgnT > 0 && a.Grp == b.IgnGrp) return true;
        return false;
    }
    public void Wake(Body b) { if (b != null && b.Sleep) { b.Sleep = false; b.Idle = 0; if (b.Kind == BOXB) BoxVerts(b); } }
    readonly V3 TA = new(), TB = new(), TC = new(), TD = new();
    V3 InvIw(Body b, V3 v, V3 o) { QRot(b.Q, v, o, true); o.x *= b.IIn.x; o.y *= b.IIn.y; o.z *= b.IIn.z; return QRot(b.Q, o, o, false); }
    public void ApplyImpulse(Body b, float jx, float jy, float jz, V3 r)
    {
        b.Vel.x += jx * b.Im; b.Vel.y += jy * b.Im; b.Vel.z += jz * b.Im;
        TA.x = jx; TA.y = jy; TA.z = jz; Cross(r, TA, TB); InvIw(b, TB, TC); b.W.x += TC.x; b.W.y += TC.y; b.W.z += TC.z;
    }
    void ApplyPseudo(Body b, float jx, float jy, float jz, V3 r)
    {
        b.Pv.x += jx * b.Im; b.Pv.y += jy * b.Im; b.Pv.z += jz * b.Im;
        TA.x = jx; TA.y = jy; TA.z = jz; Cross(r, TA, TB); InvIw(b, TB, TC); b.Pw.x += TC.x; b.Pw.y += TC.y; b.Pw.z += TC.z;
    }
    void BoxVerts(Body b)
    {
        for (int k = 0; k < SAMP.Length; k++)
        {
            var s = SAMP[k]; TA.x = s[0] * b.He.x; TA.y = s[1] * b.He.y; TA.z = s[2] * b.He.z; var v = b.Verts[k];
            QRot(b.Q, TA, v, false); v.x += b.Pos.x; v.y += b.Pos.y; v.z += b.Pos.z;
        }
        b.VertsOk = true;
    }

    readonly List<Contact> contacts = new(); int nC;
    Dictionary<long, (float, float, float)> warm = new(), warmNext = new();
    Contact AddContact(Body a, Body b, float px, float py, float pz, float nx, float ny, float nz, float depth, long key, float eo = 0)
    {
        if (nC >= contacts.Count) contacts.Add(new Contact());
        var c = contacts[nC++];
        c.A = a; c.B = b; c.P.x = px; c.P.y = py; c.P.z = pz; c.N.x = nx; c.N.y = ny; c.N.z = nz; c.Depth = depth; c.Key = key; c.Eo = eo;
        c.Jn = 0; c.Jt1 = 0; c.Jt2 = 0; c.Pj = 0;
        return c;
    }

    // deepest penetration of a point into terrain, cylinders and ramps (static boxes use SAT instead)
    struct PointQ { public float d, nx, ny, nz, eo; public int f; }
    PointQ PQ;
    readonly List<Static> one = new() { null };
    PointQ PointStatic(float px, float py, float pz, List<Static> list, bool withBoxes, bool noTerrain = false)
    {
        PQ.d = 0; PQ.f = -1; PQ.eo = 0;
        var t = TerrainSample(px, pz);
        if (!noTerrain && py < t.h) { float nl = Hypot(t.gx, 1, t.gz), d = (t.h - py) / nl; PQ.d = d; PQ.nx = -t.gx / nl; PQ.ny = 1 / nl; PQ.nz = -t.gz / nl; PQ.f = 0; }
        foreach (var o in list)
        {
            if (o.Kind == BOXK)
            {
                if (!withBoxes || px <= o.MinX || px >= o.MaxX || py <= o.MinY || py >= o.MaxY || pz <= o.MinZ || pz >= o.MaxZ) continue;
                float d = py - o.MinY, nx = 0, ny = -1, nz = 0;
                if (o.MaxY - py < d) { d = o.MaxY - py; ny = 1; }
                if (px - o.MinX < d) { d = px - o.MinX; nx = -1; ny = 0; }
                if (o.MaxX - px < d) { d = o.MaxX - px; nx = 1; ny = 0; nz = 0; }
                if (pz - o.MinZ < d) { d = pz - o.MinZ; nx = 0; ny = 0; nz = -1; }
                if (o.MaxZ - pz < d) { d = o.MaxZ - pz; nx = 0; ny = 0; nz = 1; }
                if (d > PQ.d) { PQ.d = d; PQ.nx = nx; PQ.ny = ny; PQ.nz = nz; PQ.f = o.Id; }
            }
            else if (o.Kind == CYLK)
            {
                if (py <= o.Bottom || py >= o.Top) continue;
                float dx = px - o.X, dz = pz - o.Z, hd = Hypot(dx, dz); if (hd >= o.R) continue;
                if (o.Top - py < o.R - hd) { if (o.Top - py > PQ.d) { PQ.d = o.Top - py; PQ.nx = 0; PQ.ny = 1; PQ.nz = 0; PQ.f = o.Id; PQ.eo = 0; } }
                else if (o.R - hd > PQ.d) { float h1 = hd == 0 ? 1 : hd; PQ.d = o.R - hd; PQ.nx = dx / h1; PQ.ny = 0; PQ.nz = dz / h1; PQ.f = o.Id; PQ.eo = o.Bounce; }
            }
            else
            {
                if (px <= o.MinX || px >= o.MaxX || pz <= o.MinZ || pz >= o.MaxZ || py <= o.MinY) continue;
                float s = o.Fn(px, pz); if (py >= s) continue;
                var PN = PatchNormal(o, px, pz);
                float d = (s - py) * PN.ny, nx = PN.nx, ny = PN.ny, nz = PN.nz;
                if (px - o.MinX < d) { d = px - o.MinX; nx = -1; ny = 0; nz = 0; }
                if (o.MaxX - px < d) { d = o.MaxX - px; nx = 1; ny = 0; nz = 0; }
                if (pz - o.MinZ < d) { d = pz - o.MinZ; nx = 0; ny = 0; nz = -1; }
                if (o.MaxZ - pz < d) { d = o.MaxZ - pz; nx = 0; ny = 0; nz = 1; }
                if (d > PQ.d) { PQ.d = d; PQ.nx = nx; PQ.ny = ny; PQ.nz = nz; PQ.f = o.Id; }
            }
        }
        return PQ;
    }

    void SphereStatic(Body b, List<Static> list)
    {
        var c = b.Pos; float r = b.R;
        var t = TerrainSample(c.x, c.z); float nl = Hypot(t.gx, 1, t.gz), nx = -t.gx / nl, ny = 1 / nl, nz = -t.gz / nl, d = (c.y - t.h) * ny;
        if (d < r) AddContact(b, null, c.x - nx * r, c.y - ny * r, c.z - nz * r, nx, ny, nz, r - d, b.Id * 8192L);
        foreach (var o in list)
        {
            if (o.Kind == BOXK)
            {
                float qx = Clamp(c.x, o.MinX, o.MaxX), qy = Clamp(c.y, o.MinY, o.MaxY), qz = Clamp(c.z, o.MinZ, o.MaxZ);
                float dx = c.x - qx, dy = c.y - qy, dz = c.z - qz, dd = Hypot(dx, dy, dz);
                if (dd >= r) continue;
                if (dd > 1e-6f) AddContact(b, null, qx, qy, qz, dx / dd, dy / dd, dz / dd, r - dd, b.Id * 8192L + o.Id);
                else { one[0] = o; var pq = PointStatic(c.x, c.y, c.z, one, true); if (pq.d > 0) AddContact(b, null, c.x, c.y, c.z, pq.nx, pq.ny, pq.nz, pq.d + r, b.Id * 8192L + o.Id); }
            }
            else if (o.Kind == CYLK)
            {
                float dx = c.x - o.X, dz = c.z - o.Z, hd = Hypot(dx, dz);
                if (c.y > o.Top)
                {
                    float qx = hd > o.R ? o.X + dx / hd * o.R : c.x, qz = hd > o.R ? o.Z + dz / hd * o.R : c.z;
                    float ex = c.x - qx, ey = c.y - o.Top, ez = c.z - qz, dd = Hypot(ex, ey, ez);
                    if (dd < r && dd > 1e-6f) AddContact(b, null, qx, o.Top, qz, ex / dd, ey / dd, ez / dd, r - dd, b.Id * 8192L + o.Id);
                }
                else if (c.y > o.Bottom && hd < o.R + r && hd > 1e-6f) AddContact(b, null, o.X + dx / hd * o.R, c.y, o.Z + dz / hd * o.R, dx / hd, 0, dz / hd, o.R + r - hd, b.Id * 8192L + o.Id, o.Bounce);
            }
            else
            {
                if (c.x > o.MinX && c.x < o.MaxX && c.z > o.MinZ && c.z < o.MaxZ)
                {
                    float s = o.Fn(c.x, c.z); var PN = PatchNormal(o, c.x, c.z);
                    float dd = (c.y - s) * PN.ny;
                    if (dd < r && c.y > o.MinY - r) AddContact(b, null, c.x - PN.nx * r, c.y - PN.ny * r, c.z - PN.nz * r, PN.nx, PN.ny, PN.nz, r - dd, b.Id * 8192L + o.Id);
                }
                else
                {
                    float qx = Clamp(c.x, o.MinX, o.MaxX), qz = Clamp(c.z, o.MinZ, o.MaxZ), dx = c.x - qx, dz = c.z - qz, hd = Hypot(dx, dz);
                    if (hd < r && hd > 1e-6f && c.y < o.Fn(qx, qz) && c.y > o.MinY) AddContact(b, null, qx, c.y, qz, dx / hd, 0, dz / hd, r - hd, b.Id * 8192L + o.Id);
                }
            }
        }
    }

    // --- oriented boxes and the separating-axis test with full contact manifolds
    sealed class OBB { public readonly V3 c = new(); public readonly V3[] u = { new(), new(), new() }; public readonly float[] e = new float[3]; public Body body; }
    readonly OBB OA = new(), OB = new();
    static readonly V3 UX = new(1, 0, 0), UY = new(0, 1, 0), UZ = new(0, 0, 1);
    static OBB ObbFromBody(OBB o, Body b)
    {
        o.c.x = b.Pos.x; o.c.y = b.Pos.y; o.c.z = b.Pos.z;
        QRot(b.Q, UX, o.u[0]); QRot(b.Q, UY, o.u[1]); QRot(b.Q, UZ, o.u[2]);
        o.e[0] = b.He.x; o.e[1] = b.He.y; o.e[2] = b.He.z; o.body = b; return o;
    }
    static OBB ObbFromStatic(OBB o, Static s)
    {
        o.c.x = (s.MinX + s.MaxX) / 2; o.c.y = (s.MinY + s.MaxY) / 2; o.c.z = (s.MinZ + s.MaxZ) / 2;
        o.u[0].Set(1, 0, 0); o.u[1].Set(0, 1, 0); o.u[2].Set(0, 0, 1);
        o.e[0] = (s.MaxX - s.MinX) / 2; o.e[1] = (s.MaxY - s.MinY) / 2; o.e[2] = (s.MaxZ - s.MinZ) / 2; o.body = null; return o;
    }
    static float ProjR(OBB o, V3 L) => o.e[0] * MathF.Abs(Dot(o.u[0], L)) + o.e[1] * MathF.Abs(Dot(o.u[1], L)) + o.e[2] * MathF.Abs(Dot(o.u[2], L));
    readonly V3 SAT_T = new(), SAT_N = new(), SAT_L = new(), SAT_NN = new();
    void BoxBox(OBB A, OBB B, long key)
    {
        var T = SAT_T; var Nn = SAT_N; var L = SAT_L;
        T.x = B.c.x - A.c.x; T.y = B.c.y - A.c.y; T.z = B.c.z - A.c.z;
        float best = float.PositiveInfinity; int type = -1, ai = 0, bj = 0;
        for (int i = 0; i < 3; i++)
        {
            var u = A.u[i]; float d = Dot(T, u), pen = A.e[i] + ProjR(B, u) - MathF.Abs(d);
            if (pen < 0) return;
            if (pen < best) { best = pen; type = 0; ai = i; float s = d < 0 ? -1 : 1; Nn.x = u.x * s; Nn.y = u.y * s; Nn.z = u.z * s; }
        }
        for (int j = 0; j < 3; j++)
        {
            var u = B.u[j]; float d = Dot(T, u), pen = B.e[j] + ProjR(A, u) - MathF.Abs(d);
            if (pen < 0) return;
            if (pen < best * 0.98f - 0.001f) { best = pen; type = 1; ai = j; float s = d < 0 ? -1 : 1; Nn.x = u.x * s; Nn.y = u.y * s; Nn.z = u.z * s; }
        }
        for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++)
            {
                Cross(A.u[i], B.u[j], L); float l = Hypot(L.x, L.y, L.z); if (l < 1e-4f) continue;
                L.x /= l; L.y /= l; L.z /= l;
                float d = Dot(T, L), pen = ProjR(A, L) + ProjR(B, L) - MathF.Abs(d);
                if (pen < 0) return;
                if (pen < best * 0.9f - 0.005f) { best = pen; type = 2; ai = i; bj = j; float s = d < 0 ? -1 : 1; Nn.x = L.x * s; Nn.y = L.y * s; Nn.z = L.z * s; }
            }
        if (type == 0) FaceContact(A, B, ai, Nn, key);
        else if (type == 1) { SAT_NN.x = -Nn.x; SAT_NN.y = -Nn.y; SAT_NN.z = -Nn.z; FaceContact(B, A, ai, SAT_NN, key); }
        else EdgeContact(A, B, ai, bj, Nn, best, key);
    }
    struct P3 { public float x, y, z; public P3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; } }
    static List<P3> ClipPoly(List<P3> poly, V3 n, float off)
    {
        var o = new List<P3>(8);
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i]; var b = poly[(i + 1) % poly.Count]; float da = n.x * a.x + n.y * a.y + n.z * a.z - off, db = n.x * b.x + n.y * b.y + n.z * b.z - off;
            if (da <= 0) o.Add(a);
            if ((da <= 0) != (db <= 0)) { float t = da / (da - db); o.Add(new P3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t)); }
        }
        return o;
    }
    readonly V3 PLN = new();
    static readonly int[][] QUADS = { new[] { 1, 1 }, new[] { -1, 1 }, new[] { -1, -1 }, new[] { 1, -1 } };
    void FaceContact(OBB R, OBB I, int ri, V3 n, long key)   // n: unit normal of R's reference face, pointing from R toward I
    {
        int k = 0; float md = -1;
        for (int j = 0; j < 3; j++) { float d = MathF.Abs(Dot(I.u[j], n)); if (d > md) { md = d; k = j; } }
        float s = Dot(I.u[k], n) > 0 ? -1 : 1; int a1 = (k + 1) % 3, a2 = (k + 2) % 3;
        float fx = I.c.x + I.u[k].x * I.e[k] * s, fy = I.c.y + I.u[k].y * I.e[k] * s, fz = I.c.z + I.u[k].z * I.e[k] * s;
        var poly = new List<P3>(8);
        foreach (var sq in QUADS)
        {
            float ea = I.e[a1] * sq[0], eb = I.e[a2] * sq[1];
            poly.Add(new P3(fx + I.u[a1].x * ea + I.u[a2].x * eb, fy + I.u[a1].y * ea + I.u[a2].y * eb, fz + I.u[a1].z * ea + I.u[a2].z * eb));
        }
        foreach (int ax in new[] { (ri + 1) % 3, (ri + 2) % 3 })
            for (int sg = 1; sg >= -1; sg -= 2)
            {
                var u = R.u[ax]; PLN.x = u.x * sg; PLN.y = u.y * sg; PLN.z = u.z * sg;
                poly = ClipPoly(poly, PLN, Dot(PLN, R.c) + R.e[ax]);
                if (poly.Count == 0) return;
            }
        // keep the deepest point in each quadrant of the reference face: at most 4 contacts, with keys that stay
        // attached to the same corner from step to step so warm starting stays consistent
        float refOff = Dot(n, R.c) + R.e[ri]; var u1 = R.u[(ri + 1) % 3]; var u2 = R.u[(ri + 2) % 3];
        Span<int> has = stackalloc int[4]; Span<float> bd = stackalloc float[4]; var best = new P3[4];
        for (int q = 0; q < 4; q++) bd[q] = -1;
        foreach (var p in poly)
        {
            float depth = refOff - (n.x * p.x + n.y * p.y + n.z * p.z); if (depth < -0.002f) continue;
            float rx = p.x - R.c.x, ry = p.y - R.c.y, rz = p.z - R.c.z;
            int q = (rx * u1.x + ry * u1.y + rz * u1.z > 0 ? 1 : 0) + (rx * u2.x + ry * u2.y + rz * u2.z > 0 ? 2 : 0);
            if (depth > bd[q]) { bd[q] = depth; best[q] = p; has[q] = 1; }
        }
        for (int q = 0; q < 4; q++)
        {
            if (has[q] == 0) continue; var p = best[q]; float d = MathF.Max(0, bd[q]);
            if (I.body != null) AddContact(I.body, R.body, p.x, p.y, p.z, n.x, n.y, n.z, d, key * 16 + ri * 4 + q);
            else AddContact(R.body, null, p.x + n.x * d, p.y + n.y * d, p.z + n.z * d, -n.x, -n.y, -n.z, d, key * 16 + ri * 4 + q);
        }
    }
    void EdgeContact(OBB A, OBB B, int i, int j, V3 n, float pen, long key)   // n points from A to B
    {
        float ax = A.c.x, ay = A.c.y, az = A.c.z, bx = B.c.x, by = B.c.y, bz = B.c.z;
        for (int k = 0; k < 3; k++)
        {
            if (k != i) { float sg = Dot(A.u[k], n) > 0 ? 1 : -1, e = A.e[k] * sg; ax += A.u[k].x * e; ay += A.u[k].y * e; az += A.u[k].z * e; }
            if (k != j) { float sg = Dot(B.u[k], n) > 0 ? -1 : 1, e = B.e[k] * sg; bx += B.u[k].x * e; by += B.u[k].y * e; bz += B.u[k].z * e; }
        }
        var d1 = A.u[i]; var d2 = B.u[j]; float rx = ax - bx, ry = ay - by, rz = az - bz;
        float b = Dot(d1, d2), c = d1.x * rx + d1.y * ry + d1.z * rz, f = d2.x * rx + d2.y * ry + d2.z * rz, den = 1 - b * b;
        float s = den > 1e-6f ? Clamp((b * f - c) / den, -A.e[i], A.e[i]) : 0;
        float t = Clamp(b * s + f, -B.e[j], B.e[j]);
        s = Clamp(b * t - c, -A.e[i], A.e[i]);
        float mx = (ax + d1.x * s + bx + d2.x * t) / 2, my = (ay + d1.y * s + by + d2.y * t) / 2, mz = (az + d1.z * s + bz + d2.z * t) / 2;
        if (A.body != null) AddContact(A.body, B.body, mx, my, mz, -n.x, -n.y, -n.z, pen, key * 16 + 12);
        else AddContact(B.body, null, mx, my, mz, n.x, n.y, n.z, pen, key * 16 + 12);
    }
    readonly V3 LOC = new(), LOC2 = new(), NB = new(), SC = new();
    float PointInBox(Body b, V3 p, V3 o)
    {
        LOC.x = p.x - b.Pos.x; LOC.y = p.y - b.Pos.y; LOC.z = p.z - b.Pos.z; QRot(b.Q, LOC, LOC, true);
        float dx = b.He.x - MathF.Abs(LOC.x), dy = b.He.y - MathF.Abs(LOC.y), dz = b.He.z - MathF.Abs(LOC.z);
        if (dx <= 0 || dy <= 0 || dz <= 0) return 0;
        LOC2.x = 0; LOC2.y = 0; LOC2.z = 0; float d;
        if (dx < dy && dx < dz) { d = dx; LOC2.x = LOC.x < 0 ? -1 : 1; } else if (dy < dz) { d = dy; LOC2.y = LOC.y < 0 ? -1 : 1; } else { d = dz; LOC2.z = LOC.z < 0 ? -1 : 1; }
        QRot(b.Q, LOC2, o, false); return d;
    }
    Contact SphereSphere(Body owner, float cx, float cy, float cz, float r, Body b, long key)
    {
        float dx = cx - b.Pos.x, dy = cy - b.Pos.y, dz = cz - b.Pos.z, d = Hypot(dx, dy, dz);
        if (d >= r + b.R || d < 1e-6f) return null;
        float nx = dx / d, ny = dy / d, nz = dz / d;
        return AddContact(owner, b, b.Pos.x + nx * b.R, b.Pos.y + ny * b.R, b.Pos.z + nz * b.R, nx, ny, nz, r + b.R - d, key);
    }
    Contact SphereBox(Body owner, float cx, float cy, float cz, float r, Body x, long key)
    {
        LOC.x = cx - x.Pos.x; LOC.y = cy - x.Pos.y; LOC.z = cz - x.Pos.z; QRot(x.Q, LOC, LOC, true);
        float qx = Clamp(LOC.x, -x.He.x, x.He.x), qy = Clamp(LOC.y, -x.He.y, x.He.y), qz = Clamp(LOC.z, -x.He.z, x.He.z);
        float ex = LOC.x - qx, ey = LOC.y - qy, ez = LOC.z - qz, d = Hypot(ex, ey, ez);
        if (d >= r) return null;
        float depth;
        if (d > 1e-6f) { LOC2.x = ex / d; LOC2.y = ey / d; LOC2.z = ez / d; depth = r - d; }
        else { SC.x = cx; SC.y = cy; SC.z = cz; depth = PointInBox(x, SC, NB) + r; QRot(x.Q, NB, LOC2, true); }
        QRot(x.Q, LOC2, NB, false); SC.x = qx; SC.y = qy; SC.z = qz; QRot(x.Q, SC, SC, false);
        return AddContact(owner, x, SC.x + x.Pos.x, SC.y + x.Pos.y, SC.z + x.Pos.z, NB.x, NB.y, NB.z, depth, key);
    }
    void CollidePair(Body a, Body b)
    {
        long key = a.Id * 8192L + 4096 + b.Id;
        if (a.Kind == SPH && b.Kind == SPH) SphereSphere(a, a.Pos.x, a.Pos.y, a.Pos.z, a.R, b, key);
        else if (a.Kind == BOXB && b.Kind == BOXB) BoxBox(ObbFromBody(OA, a), ObbFromBody(OB, b), key);
        else if (a.Kind == SPH) SphereBox(a, a.Pos.x, a.Pos.y, a.Pos.z, a.R, b, key);
        else SphereBox(b, b.Pos.x, b.Pos.y, b.Pos.z, b.R, a, b.Id * 8192L + 4096 + a.Id);
    }
    readonly V3 RV = new();
    static V3 RelVel(Pairish c, V3 o, bool pseudo)
    {
        var a = c.A; var b = c.B; float x = 0, y = 0, z = 0;
        if (Dyn(a)) { var v = pseudo ? a.Pv : a.Vel; var w = pseudo ? a.Pw : a.W; x += v.x + w.y * c.RA.z - w.z * c.RA.y; y += v.y + w.z * c.RA.x - w.x * c.RA.z; z += v.z + w.x * c.RA.y - w.y * c.RA.x; }
        if (Dyn(b)) { var v = pseudo ? b.Pv : b.Vel; var w = pseudo ? b.Pw : b.W; x -= v.x + w.y * c.RB.z - w.z * c.RB.y; y -= v.y + w.z * c.RB.x - w.x * c.RB.z; z -= v.z + w.x * c.RB.y - w.y * c.RB.x; }
        o.x = x; o.y = y; o.z = z; return o;
    }
    float KAlong(Pairish c, V3 n)
    {
        float k = 0;
        if (Dyn(c.A)) { k += c.A.Im; Cross(c.RA, n, TA); InvIw(c.A, TA, TB); Cross(TB, c.RA, TC); k += Dot(TC, n); }
        if (Dyn(c.B)) { k += c.B.Im; Cross(c.RB, n, TA); InvIw(c.B, TA, TB); Cross(TB, c.RB, TC); k += Dot(TC, n); }
        return k;
    }
    void Impulse(Pairish c, V3 dir, float j, bool pseudo = false)
    {
        float jx = dir.x * j, jy = dir.y * j, jz = dir.z * j;
        if (Dyn(c.A)) { if (pseudo) ApplyPseudo(c.A, jx, jy, jz, c.RA); else ApplyImpulse(c.A, jx, jy, jz, c.RA); }
        if (Dyn(c.B)) { if (pseudo) ApplyPseudo(c.B, -jx, -jy, -jz, c.RB); else ApplyImpulse(c.B, -jx, -jy, -jz, c.RB); }
    }

    int sapStamp = -1;
    const int ITER = 10, PITER = 6; const float SLOP = 0.004f, BAUM = 0.35f;
    readonly List<Body> ACT = new(), SAP = new(); readonly List<Static> QB = new(); readonly List<Joint> ACTJ = new();
    static readonly V3 UPV = new(0, 1, 0), XAX = new(1, 0, 0);

    void StepBodies(float dt)
    {
        nC = 0; ACT.Clear();
        foreach (var b in bodies)
        {
            b.Nc = 0;
            if (b.Sleep) { if (b.Kind == BOXB && !b.VertsOk) BoxVerts(b); continue; }
            if (!b.NoGrav) b.Vel.y -= PGRAV * dt; ACT.Add(b);
            if (b.SepT > 0) b.SepT -= dt; if (b.IgnT > 0) b.IgnT -= dt; if (b.ThrownT > 0) b.ThrownT -= dt;
            if (b.Kind == BOXB) BoxVerts(b);
        }
        // against the static world
        foreach (var b in ACT)
        {
            QueryStatics(b.Pos.x - b.Bound - 1, b.Pos.x + b.Bound + 1, b.Pos.z - b.Bound - 1, b.Pos.z + b.Bound + 1, QB);
            if (b.Kind == SPH) { SphereStatic(b, QB); continue; }
            for (int i = 0; i < b.Verts.Length; i++)
            {
                var v = b.Verts[i]; var pq = PointStatic(v.x, v.y, v.z, QB, false, b.Ctrl);
                if (pq.d > 0) AddContact(b, null, v.x, v.y, v.z, pq.nx, pq.ny, pq.nz, pq.d, (b.Id * 8192L + (pq.f & 4095)) * 32 + i, pq.eo);
            }
            ObbFromBody(OA, b);
            foreach (var o in QB) if (o.Kind == BOXK && o.MaxY > b.Pos.y - b.Bound && o.MinY < b.Pos.y + b.Bound) BoxBox(OA, ObbFromStatic(OB, o), b.Id * 8192L + o.Id);
        }
        ActorsVsProps();
        // body pairs: sweep and prune along x; the order barely changes between steps, so an insertion sort is nearly linear
        if (SAP.Count != bodies.Count || sapStamp != bodiesStamp) { SAP.Clear(); SAP.AddRange(bodies); sapStamp = bodiesStamp; }
        foreach (var b in SAP) b.SapK = b.Pos.x - b.Bound;
        for (int i = 1; i < SAP.Count; i++) { var b = SAP[i]; float k = b.SapK; int j = i - 1; while (j >= 0 && SAP[j].SapK > k) { SAP[j + 1] = SAP[j]; j--; } SAP[j + 1] = b; }
        for (int i = 0; i < SAP.Count; i++)
        {
            var a = SAP[i]; float ax = a.Pos.x + a.Bound;
            for (int j = i + 1; j < SAP.Count; j++)
            {
                var b = SAP[j];
                if (b.Pos.x - b.Bound > ax) break;
                if (a.Sleep && b.Sleep) continue;
                float r = a.Bound + b.Bound;
                if (MathF.Abs(a.Pos.y - b.Pos.y) > r || MathF.Abs(a.Pos.z - b.Pos.z) > r) continue;
                if (NoPair(a, b)) continue;
                int before = nC; CollidePair(a, b);
                if (nC > before && a.Sleep != b.Sleep)
                {
                    var s = a.Sleep ? a : b; var o = a.Sleep ? b : a;
                    if (Dot(o.Vel, o.Vel) + Dot(o.W, o.W) > 0.5f) Wake(s);
                }
            }
        }
        // prepare
        for (int i = 0; i < nC; i++)
        {
            var c = contacts[i]; var a = c.A; var b = c.B;
            c.RA.x = c.P.x - a.Pos.x; c.RA.y = c.P.y - a.Pos.y; c.RA.z = c.P.z - a.Pos.z;
            if (b != null) { c.RB.x = c.P.x - b.Pos.x; c.RB.y = c.P.y - b.Pos.y; c.RB.z = c.P.z - b.Pos.z; } else c.RB.Set(0, 0, 0);
            var n = c.N;
            Cross(n, MathF.Abs(n.y) < 0.9f ? UPV : XAX, c.T1);
            float tl = Hypot(c.T1.x, c.T1.y, c.T1.z); c.T1.x /= tl; c.T1.y /= tl; c.T1.z /= tl; Cross(n, c.T1, c.T2);
            c.KN = KAlong(c, n); c.KT1 = KAlong(c, c.T1); c.KT2 = KAlong(c, c.T2);
            RelVel(c, RV, false); float vn = Dot(RV, n);
            float e = c.Eo != 0 ? c.Eo : MathF.Max(a.E, b != null ? b.E : 0.1f);
            c.BounceV = vn < -1.4f ? -e * vn : 0;
            c.Mu = MathF.Sqrt(a.Mu * (b != null ? b.Mu : 0.7f));
            a.Nc++; if (b != null) b.Nc++;
        }
        // warm start only after every contact has measured its approach speed, so old impulses cannot fake a bounce
        for (int i = 0; i < nC; i++)
        {
            var c = contacts[i];
            if (c.KN > 0 && warm.TryGetValue(c.Key, out var w)) { c.Jn = w.Item1 * 0.9f; c.Jt1 = w.Item2 * 0.9f; c.Jt2 = w.Item3 * 0.9f; Impulse(c, c.N, c.Jn); Impulse(c, c.T1, c.Jt1); Impulse(c, c.T2, c.Jt2); }
        }
        // joints
        ACTJ.Clear();
        foreach (var j in joints)
        {
            if (!j.On) continue;
            if (j.A.Sleep != j.B.Sleep) { Wake(j.A); Wake(j.B); }
            if (j.A.Sleep && j.B.Sleep) continue;
            QRot(j.A.Q, j.La, j.RA); QRot(j.B.Q, j.Lb, j.RB);
            j.Err.x = j.A.Pos.x + j.RA.x - j.B.Pos.x - j.RB.x; j.Err.y = j.A.Pos.y + j.RA.y - j.B.Pos.y - j.RB.y; j.Err.z = j.A.Pos.z + j.RA.z - j.B.Pos.z - j.RB.z;
            for (int k = 0; k < 3; k++) j.K[k] = KAlong(j, JN[k]);
            ACTJ.Add(j);
        }
        // velocity solve
        for (int it = 0; it < ITER; it++)
        {
            for (int i = 0; i < nC; i++)
            {
                var c = contacts[i]; if (!(c.KN > 0)) continue;
                RelVel(c, RV, false);
                float j = (-Dot(RV, c.N) + c.BounceV) / c.KN, nj = MathF.Max(0, c.Jn + j); j = nj - c.Jn; c.Jn = nj; Impulse(c, c.N, j);
                RelVel(c, RV, false);
                float lim = c.Mu * c.Jn;
                float t = -Dot(RV, c.T1) / c.KT1, nt = Clamp(c.Jt1 + t, -lim, lim); t = nt - c.Jt1; c.Jt1 = nt; Impulse(c, c.T1, t);
                t = -Dot(RV, c.T2) / c.KT2; nt = Clamp(c.Jt2 + t, -lim, lim); t = nt - c.Jt2; c.Jt2 = nt; Impulse(c, c.T2, t);
            }
            foreach (var j in ACTJ)
                for (int k = 0; k < 3; k++)
                {
                    if (!(j.K[k] > 0)) continue;
                    RelVel(j, RV, false);
                    float vn = k == 0 ? RV.x : k == 1 ? RV.y : RV.z, er = k == 0 ? j.Err.x : k == 1 ? j.Err.y : j.Err.z;
                    Impulse(j, JN[k], (-vn - Clamp(0.3f / dt * er, -10, 10)) / j.K[k]);
                }
        }
        warmNext.Clear();
        for (int i = 0; i < nC; i++) { var c = contacts[i]; warmNext[c.Key] = (c.Jn, c.Jt1, c.Jt2); }
        (warm, warmNext) = (warmNext, warm);
        // split-impulse position correction: pushes overlap out without adding bounce
        for (int it = 0; it < PITER; it++)
            for (int i = 0; i < nC; i++)
            {
                var c = contacts[i]; if (!(c.KN > 0)) continue;
                float target = BAUM / dt * MathF.Max(0, c.Depth - SLOP); if (target == 0 && c.Pj == 0) continue;
                RelVel(c, RV, true);
                float j = (target - Dot(RV, c.N)) / c.KN, nj = MathF.Max(0, c.Pj + j); j = nj - c.Pj; c.Pj = nj; Impulse(c, c.N, j, true);
            }
        ActorsWriteback(dt);
        // integrate
        foreach (var b in ACT)
        {
            if (b.Sleep) { b.Pv.Set(0, 0, 0); b.Pw.Set(0, 0, 0); continue; }
            float sp = Hypot(b.Vel.x, b.Vel.y, b.Vel.z); if (sp > 60) { b.Vel.x *= 60 / sp; b.Vel.y *= 60 / sp; b.Vel.z *= 60 / sp; }
            b.Pos.x += (b.Vel.x + b.Pv.x) * dt; b.Pos.y += (b.Vel.y + b.Pv.y) * dt; b.Pos.z += (b.Vel.z + b.Pv.z) * dt;
            QIntegrate(b.Q, b.W.x + b.Pw.x, b.W.y + b.Pw.y, b.W.z + b.Pw.z, dt);
            b.Pv.Set(0, 0, 0); b.Pw.Set(0, 0, 0);
            float ld = MathF.Exp(-0.05f * dt), ad = MathF.Exp(-(b.Nc > 0 ? (b.Kind == SPH ? 0.2f : 1.2f) : 0.1f) * dt);
            b.Vel.x *= ld; b.Vel.y *= ld; b.Vel.z *= ld; b.W.x *= ad; b.W.y *= ad; b.W.z *= ad;
            if (b.Nc > 0 && b.Kind == SPH)
            {
                float v = Hypot(b.Vel.x, b.Vel.y, b.Vel.z), k = v > 1e-6f ? MathF.Max(0, v - b.Roll * 0.6f * dt) / v : 0;
                b.Vel.x *= k; b.Vel.y *= k; b.Vel.z *= k; b.W.x *= k; b.W.y *= k; b.W.z *= k;
            }
            float en = Dot(b.Vel, b.Vel) + Dot(b.W, b.W) * (b.Kind == SPH ? b.R * b.R : b.Bound * b.Bound);
            if (b.Nc > 0 && en < 0.05f && !b.Ctrl && b.Held == null) { b.Idle += dt; if (b.Idle > 0.45f) { b.Sleep = true; b.Vel.Set(0, 0, 0); b.W.Set(0, 0, 0); b.VertsOk = false; } } else b.Idle = 0;
            if (b.Pos.y < -60 || MathF.Abs(b.Pos.x) > HALF || MathF.Abs(b.Pos.z) > HALF) ResetBody(b);
        }
    }

    void PhysicsStep(float dt)
    {
        // substeps follow the fastest loose body; actors only count when they're right next to one
        float vmax = 0;
        foreach (var A in actors) if (A.InSim && A.NearProp) vmax = MathF.Max(vmax, Hypot(A.P.vx, A.P.vy, A.P.vz) * 0.6f);
        foreach (var b in bodies) if (!b.Sleep) vmax = MathF.Max(vmax, Hypot(b.Vel.x, b.Vel.y, b.Vel.z) + (b.Kind == BOXB ? Hypot(b.W.x, b.W.y, b.W.z) * b.Bound : 0));
        // no browser frame budget to protect, so fast bodies get up to 6 substeps instead of 4
        int n = (int)Clamp(MathF.Ceiling(vmax * dt / 0.12f), 1, 6);
        for (int i = 0; i < n; i++) StepBodies(dt / n);
    }
    void ResetBody(Body b)
    {
        b.Pos.x = b.Home.x; b.Pos.y = b.Home.y + 0.3f; b.Pos.z = b.Home.z; b.Vel.Set(0, 0, 0); b.W.Set(0, 0, 0);
        b.Q.Copy(b.HomeQ); b.Sleep = false; b.Idle = 0; b.Pp.Copy(b.Pos); b.Pq.Copy(b.Q);
    }
    readonly V3 RN = new(), RO = new(), RDIR = new();
    /// <summary>Height where a downward ray from (x, top, z) hits a body, or -inf. The surface normal is left in RN.</summary>
    float RayDownBody(Body b, float x, float top, float z)
    {
        if (MathF.Abs(x - b.Pos.x) > b.Bound || MathF.Abs(z - b.Pos.z) > b.Bound || b.Pos.y - b.Bound > top) return float.NegativeInfinity;
        if (b.Kind == SPH)
        {
            float dx = x - b.Pos.x, dz = z - b.Pos.z, q = b.R * b.R - dx * dx - dz * dz; if (q <= 0) return float.NegativeInfinity;
            float y = b.Pos.y + MathF.Sqrt(q); if (y > top) return float.NegativeInfinity;
            RN.x = dx / b.R; RN.y = (y - b.Pos.y) / b.R; RN.z = dz / b.R; return y;
        }
        RO.x = x - b.Pos.x; RO.y = top - b.Pos.y; RO.z = z - b.Pos.z; QRot(b.Q, RO, RO, true);
        RDIR.Set(0, -1, 0); QRot(b.Q, RDIR, RDIR, true);
        float tmin = float.NegativeInfinity, tmax = float.PositiveInfinity, sgn = 0; int axis = 0;
        for (int k = 0; k < 3; k++)
        {
            float o = k == 0 ? RO.x : k == 1 ? RO.y : RO.z, d = k == 0 ? RDIR.x : k == 1 ? RDIR.y : RDIR.z, h = k == 0 ? b.He.x : k == 1 ? b.He.y : b.He.z;
            if (MathF.Abs(d) < 1e-9f) { if (o < -h || o > h) return float.NegativeInfinity; continue; }
            float t1 = (-h - o) / d, t2 = (h - o) / d, s = -1; if (t1 > t2) { (t1, t2) = (t2, t1); s = 1; }
            if (t1 > tmin) { tmin = t1; axis = k; sgn = s; }
            if (t2 < tmax) tmax = t2; if (tmin > tmax) return float.NegativeInfinity;
        }
        if (tmin < 0) return float.NegativeInfinity;
        RO.Set(axis == 0 ? sgn : 0, axis == 1 ? sgn : 0, axis == 2 ? sgn : 0); QRot(b.Q, RO, RN);
        return top - tmin;
    }

    // uniform grid over bodies, rebuilt once per step, so ground probes and actor contacts only look nearby
    const int BGC = 4;
    readonly Dictionary<int, List<Body>> bgrid = new(); int bgStamp;
    void BuildBodyGrid()
    {
        foreach (var a in bgrid.Values) a.Clear();
        foreach (var b in bodies)
        {
            float r = b.Bound;
            for (int i = FloorI((b.Pos.x - r) / BGC); i <= FloorI((b.Pos.x + r) / BGC); i++) for (int j = FloorI((b.Pos.z - r) / BGC); j <= FloorI((b.Pos.z + r) / BGC); j++)
                {
                    int k = (i + 512) * 1024 + (j + 512); if (!bgrid.TryGetValue(k, out var a)) bgrid[k] = a = new List<Body>(); a.Add(b);
                }
        }
    }
    public List<Body> QueryBodies(float minX, float maxX, float minZ, float maxZ, List<Body> outL)
    {
        outL.Clear(); int s = ++bgStamp;
        for (int i = FloorI(minX / BGC); i <= FloorI(maxX / BGC); i++) for (int j = FloorI(minZ / BGC); j <= FloorI(maxZ / BGC); j++)
            {
                if (!bgrid.TryGetValue((i + 512) * 1024 + (j + 512), out var a)) continue;
                foreach (var b in a) if (b.QStamp != s) { b.QStamp = s; outL.Add(b); }
            }
        return outL;
    }
}
