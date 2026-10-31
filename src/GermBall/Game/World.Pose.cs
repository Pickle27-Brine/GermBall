using System;
using System.Collections.Generic;
using System.Linq;
using GermBall.Core;
using static GermBall.Core.M;

namespace GermBall.Game;

/// <summary>Per-actor secondary motion for the pose (the original's A.dyn).</summary>
public sealed class Dyn
{
    public float lf, vf, ls, vs, by, vb, hx, hz, vhx, vhz, pvx, pvz, pvy, upYaw, prevF, look, lookT, lookTo; public bool gr, wInit;
    public float[][] wr = { new float[3], new float[3] }, wv = { new float[3], new float[3] };
}

public sealed partial class Actor { public string brainState; public bool wasDown, saidHurt, isBoss; public float curlT; public (float, float)? dodge; public Dyn dyn; public float animFist, animFlail, animBreath = 1, animFlatPop; public List<float[]> trail; }

public sealed partial class World
{
    // =====================================================================
    // Men: round parts posed procedurally every frame (walk and run cycles, a heavy menacing stalk for enemies, aiming,
    // carrying, throwing, curling into a ball, getting knocked down)
    // =====================================================================
    public float clockT;
    readonly Dictionary<string, float[]> J = new[] { "hipC", "hipL", "hipR", "kneeL", "kneeR", "ankL", "ankR", "toeL", "toeR", "pelC", "torC", "shC", "shL", "shR", "elL", "elR", "wrL", "wrR", "headC" }.ToDictionary(k => k, _ => new float[3]);
    readonly float[] BASr = new float[3], BASu = new float[3], BASf = new float[3], BASo = new float[3];
    float[] ToW(float lx, float ly, float lz, float[] o)
    {   // local (right, up, forward) to world, around BAS.o
        o[0] = BASo[0] + BASr[0] * lx + BASu[0] * ly + BASf[0] * lz;
        o[1] = BASo[1] + BASr[1] * lx + BASu[1] * ly + BASf[1] * lz;
        o[2] = BASo[2] + BASr[2] * lx + BASu[2] * ly + BASf[2] * lz;
        return o;
    }
    static Q4 QuatYF(float yx, float yy, float yz, float fx, float fy, float fz, Q4 o)
    {   // local Y along y, local -Z toward f
        float yl = Hypot(yx, yy, yz); if (yl == 0) yl = 1; yx /= yl; yy /= yl; yz /= yl;
        float d = fx * yx + fy * yy + fz * yz, zx = -(fx - d * yx), zy = -(fy - d * yy), zz = -(fz - d * yz), zl = Hypot(zx, zy, zz);
        if (zl < 1e-4f) { zx = 0; zy = 0; zz = 1; float d2 = zz * yz; zx -= d2 * yx; zy -= d2 * yy; zz -= d2 * yz; zl = Hypot(zx, zy, zz); if (zl == 0) zl = 1; }
        zx /= zl; zy /= zl; zz /= zl;
        return QFromBasis(yy * zz - yz * zy, yz * zx - yx * zz, yx * zy - yy * zx, yx, yy, yz, zx, zy, zz, o);
    }
    // two-bone IK in world space: root a, target t, pole direction p; writes the middle joint into m
    static void Ik(float[] a, float[] t, float L1, float L2, float px, float py, float pz, float[] m)
    {
        float dx = t[0] - a[0], dy = t[1] - a[1], dz = t[2] - a[2], d = Hypot(dx, dy, dz);
        if (d < 1e-4f) { dx = 0; dy = -1; dz = 0; d = 1; }
        float ux = dx / d, uy = dy / d, uz = dz / d, D = Clamp(d, MathF.Abs(L1 - L2) + 0.01f, L1 + L2 - 0.002f);
        float ca = (L1 * L1 - L2 * L2 + D * D) / (2 * D), h = MathF.Sqrt(MathF.Max(0, L1 * L1 - ca * ca));
        float pd = px * ux + py * uy + pz * uz, qx = px - pd * ux, qy = py - pd * uy, qz = pz - pd * uz, ql = Hypot(qx, qy, qz); if (ql == 0) ql = 1; qx /= ql; qy /= ql; qz /= ql;
        m[0] = a[0] + ux * ca + qx * h; m[1] = a[1] + uy * ca + qy * h; m[2] = a[2] + uz * ca + qz * h;
        if (d > L1 + L2) { t[0] = a[0] + ux * (L1 + L2); t[1] = a[1] + uy * (L1 + L2); t[2] = a[2] + uz * (L1 + L2); }
    }
    // part radii for a type: chunky build: big round head, barrel chest, thick limbs, mitt hands, boot feet
    static void PartRadii(Actor A, int i, float[] o)
    {
        float s = A.P.s, f = A.P.f, hf = 1 + 0.15f * (f - 1);
        switch (i)
        {
            case 0: o[0] = 0.16f * s * hf; o[1] = 0.17f * s * hf; o[2] = 0.165f * s * hf; break;
            case 1: o[0] = 0.24f * s * f; o[1] = 0.29f * s; o[2] = 0.19f * s * f; break;
            case 2: o[0] = 0.21f * s * f; o[1] = 0.14f * s; o[2] = 0.16f * s * f; break;
            case 3: case 6: o[0] = o[2] = 0.088f * s * f; o[1] = 0.19f * s; break;
            case 4: case 7: o[0] = o[2] = 0.08f * s * f; o[1] = 0.175f * s; break;
            case 5: case 8: o[0] = o[1] = o[2] = 0.09f * s * MathF.Sqrt(f); break;
            case 9: case 12: o[0] = o[2] = 0.11f * s * f; o[1] = 0.26f * s; break;
            case 10: case 13: o[0] = o[2] = 0.095f * s * f; o[1] = 0.255f * s; break;
            default: o[0] = 0.1f * s * MathF.Sqrt(f); o[1] = 0.07f * s; o[2] = 0.16f * s; break;
        }
    }
    // where each part sits when curled into a ball (unit ball, local -Z forward)
    static readonly float[][] BALLP = { new[] { 0, 0.5f, -0.5f }, new[] { 0, 0.08f, 0.28f }, new[] { 0, -0.42f, 0.3f }, new[] { -0.6f, 0.15f, -0.05f }, new[] { -0.55f, -0.12f, -0.48f }, new[] { -0.28f, -0.3f, -0.72f }, new[] { 0.6f, 0.15f, -0.05f }, new[] { 0.55f, -0.12f, -0.48f }, new[] { 0.28f, -0.3f, -0.72f },
        new[] { -0.36f, -0.42f, -0.35f }, new[] { -0.42f, 0.1f, -0.68f }, new[] { -0.3f, 0.48f, -0.6f }, new[] { 0.36f, -0.42f, -0.35f }, new[] { 0.42f, 0.1f, -0.68f }, new[] { 0.3f, 0.48f, -0.6f } };
    static readonly Q4[] BALLQ = BuildBallQ();
    static Q4[] BuildBallQ()
    {
        var kids = new Dictionary<int, int> { [3] = 4, [4] = 5, [6] = 7, [7] = 8, [9] = 10, [10] = 11, [12] = 13, [13] = 14 };
        return BALLP.Select((p, i) =>
        {
            var o = new Q4();
            if (kids.TryGetValue(i, out var kid)) { var q = BALLP[kid]; QuatYF(q[0] - p[0], q[1] - p[1], q[2] - p[2], 0, 0, -1, o); }
            else if (i == 1 || i == 2) QuatYF(0, 0.3f, 1, 0, -1, 0, o);
            return o;
        }).ToArray();
    }
    readonly float[] RAD = new float[3]; readonly V3 BQV = new(), BQO = new();
    readonly Q4 PTQ = new(), PTQ2 = new(), PTQ3 = new(), BALLQ_W = new(); readonly float[] BALLC = new float[3];
    void SetPart(Actor A, int i, float cx, float cy, float cz, Q4 q, float ballK)
    {
        var ps = A.pose; int k = i * 13;
        PartRadii(A, i, RAD);
        float x = cx, y = cy, z = cz, qx = q.x, qy = q.y, qz = q.z, qw = q.w, sk = 1;
        if (ballK > 0.001f)
        {   // blend toward the curled-up arrangement, spinning with the ball
            var G = A.P; float br = G.BR; var bp = BALLP[i];
            BQV.x = bp[0] * br * 0.62f; BQV.y = bp[1] * br * 0.62f; BQV.z = bp[2] * br * 0.62f; QRot(BALLQ_W, BQV, BQO);
            float bx = BALLC[0] + BQO.x, by = BALLC[1] + BQO.y, bz = BALLC[2] + BQO.z;
            x = Lerp(x, bx, ballK); y = Lerp(y, by, ballK); z = Lerp(z, bz, ballK);
            QMul(BALLQ_W, BALLQ[i], PTQ2);   // world quat = ball * part
            PTQ.Set(qx, qy, qz, qw); QNlerp(PTQ, PTQ2, ballK, PTQ); qx = PTQ.x; qy = PTQ.y; qz = PTQ.z; qw = PTQ.w;
            sk = Lerp(1, 0.82f, ballK);
        }
        ps[k] = x; ps[k + 1] = y; ps[k + 2] = z; ps[k + 3] = qx; ps[k + 4] = qy; ps[k + 5] = qz; ps[k + 6] = qw;
        ps[k + 7] = RAD[0] * sk; ps[k + 8] = RAD[1] * sk; ps[k + 9] = RAD[2] * sk;
        // the part's long axis in world space (for hit capsules)
        BQV.Set(0, 1, 0); PTQ3.Set(qx, qy, qz, qw); QRot(PTQ3, BQV, BQO); ps[k + 10] = BQO.x; ps[k + 11] = BQO.y; ps[k + 12] = BQO.z;
    }
    readonly Q4 SQ = new();
    void SegPart(Actor A, int i, float[] a, float[] b, float fx, float fy, float fz, float ballK)
    {   // a limb segment from joint a to joint b
        QuatYF(b[0] - a[0], b[1] - a[1], b[2] - a[2], fx, fy, fz, SQ);
        SetPart(A, i, (a[0] + b[0]) / 2, (a[1] + b[1]) / 2, (a[2] + b[2]) / 2, SQ, ballK);
    }
    void PosePartsNow(Actor A) => PoseActor(A, 1, 0);
    static bool W_AIMING(Actor A) => A.W.aimT > 0 || A.P.ads || (A.team == "enemy" && A.brain != null && A.brain.aiming) || (A.human && App_FP);
    static bool App_FP;
    static readonly HashSet<string> TWO_HAND = new() { "bat", "hammer", "sledge" };
    // the swing paths: hand target (right, up, forward, in arm lengths from the shoulder), torso twist and lean,
    // at the wind-up and at the end of the strike. The hit lands just past the middle.
    static readonly Dictionary<string, (float[] a, float[] b)> SWING = new()
    {
        ["chop"] = (new[] { 0.25f, 0.8f, -0.3f, 0.35f, -0.2f }, new[] { -0.05f, -0.35f, 0.85f, -0.15f, 0.4f }),
        ["sweep"] = (new[] { 0.8f, 0.25f, 0.0f, 0.75f, 0.05f }, new[] { -0.8f, 0.05f, 0.55f, -0.75f, 0.2f }),
        ["sweep2"] = (new[] { -0.65f, 0.3f, 0.2f, -0.65f, 0.05f }, new[] { 0.8f, 0.0f, 0.45f, 0.65f, 0.2f }),
        ["slam"] = (new[] { 0.1f, 1.05f, -0.55f, 0.15f, -0.4f }, new[] { 0.0f, -0.7f, 0.85f, 0.0f, 0.7f }),
        ["jab"] = (new[] { 0.15f, 0.15f, 0.05f, 0.25f, 0 }, new[] { 0.0f, 0.1f, 1.0f, -0.25f, 0.15f }),
        ["hook"] = (new[] { 0.7f, 0.15f, 0.15f, 0.6f, 0 }, new[] { -0.35f, 0.12f, 0.8f, -0.6f, 0.15f }),
        ["upper"] = (new[] { 0.15f, -0.35f, 0.3f, 0.3f, 0.2f }, new[] { 0.0f, 0.75f, 0.65f, -0.2f, -0.2f }),
        ["slash"] = (new[] { 0.7f, 0.45f, 0.1f, 0.6f, 0 }, new[] { -0.6f, -0.1f, 0.7f, -0.6f, 0.15f }),
        ["slash2"] = (new[] { -0.5f, 0.5f, 0.2f, -0.5f, 0 }, new[] { 0.7f, -0.15f, 0.6f, 0.6f, 0.15f }),
        ["stab"] = (new[] { 0.2f, 0.2f, -0.2f, 0.35f, -0.05f }, new[] { 0.0f, 0.05f, 1.05f, -0.2f, 0.3f }),
    };
    struct SwingP { public float rr, up, fw, tw, lean; }
    static float EaseOut(float x) => 1 - (1 - x) * (1 - x) * (1 - x);
    static float EaseIn(float x) => x * x;
    static SwingP SwingPose(Actor A)
    {
        var an = A.anim; var S = SWING.TryGetValue(A.animSwingStyle ?? "chop", out var s0) ? s0 : SWING["chop"];
        float k = Clamp(1 - an.swingT / A.animSwingDur, 0, 1), u; float[] Rr = { 0.35f, 0.18f, 0.55f, 0, 0 }, from, to;   // rest: hand low and forward
        if (k < 0.4f) { u = EaseOut(k / 0.4f); from = Rr; to = S.a; }                  // wind-up
        else if (k < 0.62f) { u = EaseIn((k - 0.4f) / 0.22f); from = S.a; to = S.b; }  // the strike: slow out of the wind-up, fast into the hit
        else { u = EaseOut((k - 0.62f) / 0.38f) * 0.5f; from = S.b; to = Rr; }         // follow-through, easing halfway home
        float fin = A.animSwingFin ? 1.3f : 1;
        return new SwingP { rr = Lerp(from[0], to[0], u), up = Lerp(from[1], to[1], u), fw = Lerp(from[2], to[2], u), tw = Lerp(from[3], to[3], u) * fin, lean = Lerp(from[4], to[4], u) * fin };
    }

    readonly float[] AIMD = new float[3], PJ = new float[3]; readonly Q4 SEATQ = new(); readonly V3 SEATV = new(), SEATO = new();
    public void PoseActor(Actor A, float alpha, float fdt)
    {
        var G = A.P; var an = A.anim; float s = G.s, f = G.f;
        if (!G.alive) return;
        if (G.ragdoll != null) { PoseFromRagdoll(A, alpha); return; }
        A.poseT = clockT;
        var riding = G.vehicle; bool berserk = G.berserk;
        float rx = Lerp(G.px, G.x, alpha), ry = Lerp(G.py, G.y, alpha), rz = Lerp(G.pz, G.z, alpha);
        float hs = riding != null ? 0 : Hypot(G.vx, G.vz), menace = A.type == "normal" || A.type == "brute" || A.type == "mech" ? 1 : A.type == "small" ? 0.55f : A.type == "assassin" ? 0.7f : 0;
        bool hero = A.team == "hero" || TYPES[A.type].Mimic != null;
        // smooth state blends
        if (G.rolling && G.curl < 0.04f) { float h = G.facing / 2; G.ballQ.Set(0, MathF.Sin(h), 0, MathF.Cos(h)); G.pBallQ.Copy(G.ballQ); }
        if (fdt > 0)
        {
            G.curl = Clamp(G.curl + Clamp((G.rolling ? 1 : 0) - G.curl, -fdt * 8, fdt * 8), 0, 1);
            float downT = (G.stunT > 0 && !G.rolling) || G.grabbed != null ? 1 : 0;
            an.down += (downT - an.down) * MathF.Min(1, fdt * (downT != 0 ? 9 : 4));
            an.aimK += ((W_AIMING(A) ? 1 : 0) - an.aimK) * MathF.Min(1, fdt * 10);
            an.holdK += ((G.hold != null || G.holdA != null ? 1 : 0) - an.holdK) * MathF.Min(1, fdt * 9);
            if (an.throwT > 0) an.throwT -= fdt; if (an.punchT > 0) an.punchT -= fdt; if (an.hurtT > 0) an.hurtT -= fdt; if (an.swingT > 0) an.swingT -= fdt; if (an.squashT > 0) an.squashT -= fdt;
            G.squash += (0 - G.squash) * MathF.Min(1, fdt * 10);
            float stride = s * Clamp(0.62f + 0.085f * hs, 0.62f, 1.9f) * (A.type == "small" ? 0.75f : A.type == "brute" ? 1.15f : A.type == "mech" ? 1.25f : A.type == "mecha" ? 2.4f : 1);
            float before = MathF.Sin(an.phase);
            if (G.ground && !G.rolling) an.phase += hs * fdt / stride * PI;
            else if (!G.ground) an.phase += fdt * 6;
            if ((A.type == "brute" || A.type == "mech") && G.ground && hs > 1 && MathF.Sign(before) != MathF.Sign(MathF.Sin(an.phase)))
            {   // heavy footfalls
                bool big = A.type == "mech";
                Sfx("stomp", rx, ry, rz, big ? 1 : 0.8f);
                float d = Hypot(rx - camPos.X, rz - camPos.Z), Rr = big ? 45 : 26; if (d < Rr) cam.shake = MathF.Max(cam.shake, (big ? 0.35f : 0.12f) * (1 - d / Rr));
                if (big) for (int i = 0; i < 3; i++) Puff(rx + (R() - 0.5f) * 2, ry + 0.2f, rz + (R() - 0.5f) * 2, 1.5f, 0.6f, 0.3f);
            }
        }
        float curl = G.curl, cK = curl * curl * (3 - 2 * curl), down = an.down;
        // ---- physics of the body: the torso lags and leans with acceleration, landings and footfalls compress it, hits knock it back
        var D = A.dyn ??= new Dyn { pvx = G.vx, pvz = G.vz, pvy = G.vy, gr = G.ground, upYaw = G.facing, prevF = G.facing };
        string T = A.type; float fear = T == "small" && A.brain != null && (A.brain.fearT > 0 || A.brain.suppT > 0) ? 1 : 0;
        float rage = T == "brute" ? (berserk ? 1.6f : A.brain != null && A.brain.provoked ? 1 : 0.55f) : 0;
        if (fdt > 0)
        {
            float yaw0 = G.facing, ffx = -MathF.Sin(yaw0), ffz = -MathF.Cos(yaw0);
            float ax = (G.vx - D.pvx) / fdt, az = (G.vz - D.pvz) / fdt; D.pvx = G.vx; D.pvz = G.vz;
            float accF = Clamp(ax * ffx + az * ffz, -40, 40), accS = Clamp(ax * -ffz + az * ffx, -40, 40);
            float heavy = T == "brute" ? 0.6f : T == "small" ? 1.5f : 1;
            D.vf += ((Clamp(accF * 0.012f * heavy, -0.4f, 0.4f) - D.lf) * 70 - D.vf * 9) * fdt; D.lf += D.vf * fdt;
            D.vs += ((Clamp(accS * 0.01f * heavy, -0.35f, 0.35f) - D.ls) * 70 - D.vs * 9) * fdt; D.ls += D.vs * fdt;
            if (G.ground && !D.gr && D.pvy < -3) D.vb -= MathF.Min(4, -D.pvy * 0.25f) * (T == "brute" ? 1.4f : 1);   // landing squash
            D.gr = G.ground; D.pvy = G.vy;
            D.vb += (-D.by * 160 - D.vb * 14) * fdt; D.by += D.vb * fdt;
            D.vhx += (-D.hx * 90 - D.vhx * 8) * fdt; D.hx += D.vhx * fdt; D.vhz += (-D.hz * 90 - D.vhz * 8) * fdt; D.hz += D.vhz * fdt;
            // the chest leads turns: it swings ahead in the direction the hips are turning (or to the aim)
            float turn = WrapAngle(G.facing - D.prevF) / fdt; D.prevF = G.facing;
            float upT = an.aimK > 0.3f ? A.IN.aimYaw : G.facing + Clamp(turn * 0.2f, -0.8f, 0.8f);
            D.upYaw = TurnAngle(D.upYaw, upT, fdt * 12);
            // where the head looks: nervous glances, a slow tactical sweep, or locked on
            D.lookT -= fdt;
            if (D.lookT <= 0) { D.lookT = T == "small" ? 0.35f + R() * 0.8f : T == "brute" ? 1.5f + R() * 2 : 2 + R() * 2; D.lookTo = T == "small" ? (R() - 0.5f) * 1.6f : (R() - 0.5f) * 0.9f; }
            D.look += (D.lookTo - D.look) * MathF.Min(1, fdt * (T == "small" ? 14 : 3));
            A.animFist = T == "brute" ? 0.95f : T == "normal" ? 0.7f : 0.32f;
            A.animFlail = fear; A.animBreath = T == "brute" ? 2.2f : fear != 0 ? 1.8f : 1;
        }
        // fine tremor: brutes shake with rage, small men tremble when scared
        float tremor = (rage * 0.011f + fear * 0.006f) * s, trA = MathF.Sin(clockT * 23.1f + A.uid) * tremor, trB = MathF.Sin(clockT * 31.7f + A.uid * 2) * tremor;
        // frame: right, up, forward around the feet
        float yaw = G.facing, fx = -MathF.Sin(yaw), fz = -MathF.Cos(yaw);
        BASo[0] = rx; BASo[1] = ry; BASo[2] = rz;
        BASr[0] = -fz; BASr[1] = 0; BASr[2] = fx; BASu[0] = 0; BASu[1] = 1; BASu[2] = 0; BASf[0] = fx; BASf[1] = 0; BASf[2] = fz;
        var seat = riding?.def?.Seat;
        if (seat != null)
        {   // the rider's frame is the machine's: yaw, pitch and roll, shifted back to the seat
            VehicleQuat(riding, SEATQ, alpha);
            SEATV.Set(1, 0, 0); QRot(SEATQ, SEATV, SEATO); BASr[0] = SEATO.x; BASr[1] = SEATO.y; BASr[2] = SEATO.z;
            SEATV.Set(0, 1, 0); QRot(SEATQ, SEATV, SEATO); BASu[0] = SEATO.x; BASu[1] = SEATO.y; BASu[2] = SEATO.z;
            SEATV.Set(0, 0, -1); QRot(SEATQ, SEATV, SEATO); BASf[0] = SEATO.x; BASf[1] = SEATO.y; BASf[2] = SEATO.z;
            for (int k = 0; k < 3; k++) BASo[k] -= BASf[k] * seat.Hip[1];
        }
        if (down > 0.001f)
        {   // knocked onto his back (or held up, lying across someone's hands)
            float th = down * PI / 2 * (G.grabbed != null ? 1 : 0.95f), c = MathF.Cos(th), sn = MathF.Sin(th);
            BASu[0] = -fx * sn; BASu[1] = c; BASu[2] = -fz * sn; BASf[0] = fx * c; BASf[1] = sn; BASf[2] = fz * c;
            BASo[1] += down * (G.grabbed != null ? 0 : 0.16f * s);
        }
        // ball center and spin
        QNlerp(G.pBallQ, G.ballQ, alpha, BALLQ_W);
        BALLC[0] = rx; BALLC[1] = ry + G.BR; BALLC[2] = rz;
        // ---- legs and hips
        int legs = LegsOf(A); float runK = Smooth(2.5f, 8, hs), walkK = Clamp(hs / 1.2f, 0, 1);
        bool airborne = !G.ground && G.grabbed == null && riding == null; float ph = an.phase;
        float hipW = 0.1f * s * f * (1 + 0.25f * menace), L1 = 0.44f * s, L2 = 0.44f * s, ank = 0.07f * s;
        float crouch = 0.94f - 0.06f * menace - 0.05f * runK - (an.throwT > 0 ? 0.04f : 0) + G.squash * -0.3f - (berserk ? 0.2f : 0) - (A.type == "assassin" ? 0.06f : 0);
        float hipY = (L1 + L2) * crouch + ank;
        if (seat != null) hipY = seat.Hip[0];
        float bob = (0.025f + 0.012f * hs) * s * walkK * (1 + menace * 0.6f);
        hipY -= bob * MathF.Abs(MathF.Cos(ph));
        if (legs == 0) hipY = 0.32f * s;
        if (legs == 1) hipY -= MathF.Abs(MathF.Sin(ph * 2)) * 0.12f * s * walkK;
        float roll = (0.05f + 0.1f * (A.type == "small" ? 1 : 0) + (hero ? 0.04f : 0)) * MathF.Sin(ph) * walkK, twist = (hero ? 0.24f : 0.16f) * MathF.Cos(ph) * walkK * (1 - runK * 0.3f);
        if (hero && walkK > 0) hipY += 0.025f * s * MathF.Abs(MathF.Sin(ph)) * walkK;   // a little spring in the step
        if (T == "small") hipY += (0.045f + 0.04f * fear) * s * MathF.Abs(MathF.Sin(ph)) * walkK;   // the little ones bounce along
        if (T == "normal") hipY += bob * 0.55f * MathF.Abs(MathF.Cos(ph));   // troopers glide: most of the bob ironed out
        if (T == "brute") hipY -= 0.05f * s * MathF.Pow(MathF.Abs(MathF.Cos(ph)), 6) * walkK * (0.6f + rage * 0.3f);   // the weight drops onto each foot
        hipY += D.by * s * 0.1f;
        float sway = (A.type == "small" ? 0.1f + 0.04f * fear : A.type == "brute" ? 0.04f : 0.02f) * s * MathF.Sin(ph) * walkK + trA;
        ToW(sway, hipY, 0, J["hipC"]);
        // feet: stride targets in local space
        float strideL = s * Clamp(0.26f + 0.04f * hs, 0.26f, 0.6f) * (A.type == "small" ? 0.7f : 1), lift = s * Clamp(0.1f + 0.03f * hs, 0.1f, 0.36f);
        for (int side = -1; side <= 1; side += 2)
        {
            string L = side < 0 ? "L" : "R"; float p = ph + (side < 0 ? 0 : PI); var hip = J["hip" + L]; var knee = J["knee" + L]; var ankle = J["ank" + L]; var toe = J["toe" + L];
            ToW(sway + side * hipW, hipY - 0.02f * s + side * roll * hipW * 0.6f, -side * MathF.Sin(twist) * hipW * 0.5f, hip);
            float lx = side * hipW * (1.05f + 0.15f * menace), ly, lz;
            if (seat != null) { lx = side * seat.Foot[0]; ly = seat.Foot[1]; lz = -seat.Foot[2] - seat.Hip[1]; }
            else if (airborne) { lz = (side < 0 ? 0.25f : -0.12f) * s; ly = hipY - (L1 + L2) * (side < 0 ? 0.62f : 0.78f); }
            else if (legs == 1) { lx = 0; lz = -MathF.Cos(ph * 2) * strideL * 0.4f * walkK; ly = ank + MathF.Max(0, MathF.Sin(ph * 2)) * lift * walkK; }
            else { lz = -MathF.Cos(p) * strideL * walkK; ly = ank + MathF.Max(0, MathF.Sin(p)) * lift * walkK + runK * MathF.Max(0, MathF.Cos(p)) * MathF.Max(0, MathF.Sin(p) + 0.6f) * 0.3f * s; }
            ToW(lx, ly, lz, ankle);
            if (G.ground && down < 0.5f && legs > 0)
            {   // keep planted feet on the slope
                float dxw = ankle[0] - rx, dzw = ankle[2] - rz; ankle[1] -= (G.nx * dxw + G.nz * dzw) / MathF.Max(0.3f, G.ny);
            }
            Ik(hip, ankle, L1, L2, BASf[0], 0.15f * BASu[1], BASf[2], knee);
            toe[0] = ankle[0] + BASf[0] * 0.09f * s; toe[1] = ankle[1] - 0.03f * s + BASf[1] * 0.09f * s; toe[2] = ankle[2] + BASf[2] * 0.09f * s;
        }
        // ---- spine, chest, head
        float leanF = D.lf + (T == "normal" ? -0.08f : 0) + (T == "brute" ? 0.12f * rage + MathF.Sin(clockT * 3.4f * A.animBreath) * 0.03f : 0) + fear * 0.25f + (hero ? 0.03f : 0.07f) + 0.2f * menace + 0.28f * runK + (airborne ? -0.1f : 0) + (berserk ? 0.85f : 0)
            + (seat != null ? seat.Lean + Clamp(-riding.speed * 0.008f, -0.2f, 0) * -1 : 0) + (an.holdK * -0.1f) + (an.hurtT > 0 ? -0.25f : 0) + (an.throwT > 0 ? (an.throwT > 0.18f ? -0.35f : 0.45f) : 0) + (legs == 0 ? 1.0f : 0);
        // the chest turns to the aim before the legs do
        float aimTw = Clamp(-WrapAngle(D.upYaw - G.facing), -1.1f, 1.1f) * (G.rolling ? 0 : 1);
        // a melee swing drives the whole torso: coil back on the wind-up, unwind through the strike
        SwingP? SW = an.swingT > 0 && A.animSwingDur > 0 ? SwingPose(A) : null;
        float tw = -twist * 1.5f * (1 - an.aimK) + aimTw + (SW?.tw ?? 0);
        float leanS = leanF + (SW?.lean ?? 0), cl = MathF.Cos(leanS), sl = MathF.Sin(leanS);
        // spine direction in local: up tilted forward by lean and sideways by sway
        float sx = -roll * 0.5f - D.ls * 0.8f + trB * 2, sy = cl, sz = sl;
        var hipC = J["hipC"]; var pelC = J["pelC"]; var torC = J["torC"]; var shC = J["shC"];
        for (int k = 0; k < 3; k++) pelC[k] = hipC[k] + BASu[k] * 0.05f * s;
        float[] spine = { BASr[0] * sx + BASu[0] * sy + BASf[0] * sz, BASr[1] * sx + BASu[1] * sy + BASf[1] * sz, BASr[2] * sx + BASu[2] * sy + BASf[2] * sz };
        for (int k = 0; k < 3; k++) { torC[k] = pelC[k] + spine[k] * 0.33f * s; shC[k] = pelC[k] + spine[k] * 0.52f * s; }
        // chest twist: forward/right of the chest
        float ct = MathF.Cos(tw), st = MathF.Sin(tw);
        float cfx = BASf[0] * ct + BASr[0] * st, cfy = BASf[1] * ct + BASr[1] * st, cfz = BASf[2] * ct + BASr[2] * st;
        float crx = BASr[0] * ct - BASf[0] * st, cry = BASr[1] * ct - BASf[1] * st, crz = BASr[2] * ct - BASf[2] * st;
        float[] CR3 = { crx, cry, crz }, CF3 = { cfx, cfy, cfz };
        float shW = (0.19f * f + 0.03f) * s, hunch = 0.05f * s * menace;
        for (int side = -1; side <= 1; side += 2)
        {
            var sh = side < 0 ? J["shL"] : J["shR"];
            float heave = (T == "brute" ? 0.02f * s * MathF.Sin(clockT * 3.4f * A.animBreath) : 0) + (T == "small" ? 0.03f * s * (0.5f + fear) : 0) + (side > 0 ? trA : trB);
            for (int k = 0; k < 3; k++) sh[k] = shC[k] + CR3[k] * side * shW + CF3[k] * hunch - spine[k] * 0.03f * s * menace + BASu[k] * heave;
        }
        // head: on the neck, looking where he aims (or at his target)
        float hfx, hfy, hfz;
        {   // the head turns within the neck's reach of the chest: never all the way round
            float chestYaw = MathF.Atan2(-cfx, -cfz);
            float want = D.look * (an.aimK > 0.5f ? 0.15f : 1), pitch = (T == "brute" ? -0.12f : 0) + (fear != 0 ? 0.15f : 0) + D.hz * 0.6f;
            if (A.AIM.ok && down < 0.5f && (an.aimK > 0.2f || !A.human)) { want = WrapAngle(MathF.Atan2(-A.AIM.fx, -A.AIM.fz) - chestYaw); pitch = MathF.Asin(Clamp(A.AIM.fy, -1, 1)) * 0.8f; }
            else if (A.human && A.AIM.ok && down < 0.5f) want = WrapAngle(MathF.Atan2(-A.AIM.fx, -A.AIM.fz) - chestYaw) * 0.5f;
            if (fear != 0) want += MathF.Sin(clockT * 5 + A.uid) * 0.9f;   // looking back over his shoulder as he runs
            float lim = T == "small" ? 1.1f : 0.75f; want = Clamp(want + D.hx, -lim, lim); pitch = Clamp(pitch, -0.6f, 0.55f);
            float hy = chestYaw + want, cp = MathF.Cos(pitch);
            hfx = -MathF.Sin(hy) * cp; hfz = -MathF.Cos(hy) * cp; hfy = MathF.Sin(pitch);
        }
        var headC = J["headC"];
        for (int k = 0; k < 3; k++) headC[k] = shC[k] + spine[k] * 0.21f * s + CF3[k] * 0.04f * s * menace + BASu[k] * 0.02f * s;
        // ---- arms
        float uaL = 0.29f * s, faL = 0.27f * s, abd = 0.12f + 0.32f * menace + (airborne ? 0.6f : 0);
        AIMD[0] = A.AIM.ok ? A.AIM.fx : fx; AIMD[1] = A.AIM.ok ? A.AIM.fy : 0; AIMD[2] = A.AIM.ok ? A.AIM.fz : fz;
        var cw = CurWpn(A);
        for (int side = -1; side <= 1; side += 2)
        {
            string L = side < 0 ? "L" : "R"; var sh = J["sh" + L]; var el = J["el" + L]; var wr = J["wr" + L]; float p = ph + (side < 0 ? PI : 0);
            float tx, ty, tz;
            float swing = -MathF.Cos(p) * ((hero ? 0.5f : 0.35f) + 0.35f * runK) * walkK;
            // relaxed: hang down and out, swinging opposite the legs, elbows bent more at speed
            float hx = crx * side * MathF.Sin(abd), hyy = -MathF.Cos(abd), hzF = MathF.Sin(swing);
            tx = sh[0] + hx * (uaL + faL * 0.9f) + cfx * hzF * (uaL + faL) * 0.8f; ty = sh[1] + hyy * (uaL + faL * (0.92f - 0.25f * runK)); tz = sh[2] + crz * side * MathF.Sin(abd) * (uaL + faL * 0.9f) + cfz * hzF * (uaL + faL) * 0.8f;
            ty += 0.12f * s * runK + 0.05f * s * menace + (hero ? 0.04f * s * MathF.Max(0, swing) : 0);
            float Lr = uaL + faL;
            if (T == "brute" && !berserk)
            {   // heavy arms hanging wide, fists clenched and trembling
                tx += crx * side * 0.1f * Lr + cfx * 0.08f * Lr + (side > 0 ? trA : trB) * 3; ty -= 0.04f * Lr; tz += crz * side * 0.1f * Lr + cfz * 0.08f * Lr + (side > 0 ? trB : trA) * 3;
            }
            if (T == "normal" && an.aimK < 0.6f && cw != null && WPN[cw.id].Type != "melee" && an.holdK < 0.3f)
            {   // low ready: the rifle held across the body
                float k = 1 - an.aimK / 0.6f; var shR = J["shR"];
                float gx = shR[0] + cfx * 0.28f * s - BASu[0] * 0.22f * s - crx * 0.12f * s, gy = shR[1] + cfy * 0.28f * s - BASu[1] * 0.22f * s, gz = shR[2] + cfz * 0.28f * s - BASu[2] * 0.22f * s - crz * 0.12f * s;
                float ox = side > 0 ? 0 : cfx * 0.2f * s - crx * 0.04f * s + BASu[0] * 0.1f * s, oy = side > 0 ? 0 : cfy * 0.2f * s + BASu[1] * 0.1f * s, oz = side > 0 ? 0 : cfz * 0.2f * s - crz * 0.04f * s + BASu[2] * 0.1f * s;
                tx = Lerp(tx, gx + ox, k); ty = Lerp(ty, gy + oy, k); tz = Lerp(tz, gz + oz, k);
            }
            if (fear != 0 && an.aimK < 0.5f)
            {   // panic: arms up over his head, flapping
                float w = clockT * 15 + (side > 0 ? 0 : 1.7f);
                tx = sh[0] + (BASu[0] * 0.75f + crx * side * (0.35f + 0.2f * MathF.Sin(w)) + cfx * 0.2f * MathF.Cos(w)) * Lr; ty = sh[1] + (BASu[1] * 0.75f + 0.1f * MathF.Sin(w * 1.3f)) * Lr; tz = sh[2] + (BASu[2] * 0.75f + crz * side * (0.35f + 0.2f * MathF.Sin(w)) + cfz * 0.2f * MathF.Cos(w)) * Lr;
            }
            if (T == "assassin" && runK > 0.4f && an.aimK < 0.3f)
            {   // arms swept back as he sprints
                tx = sh[0] - cfx * 0.55f * Lr + crx * side * 0.18f * Lr - BASu[0] * 0.35f * Lr; ty = sh[1] - cfy * 0.55f * Lr - BASu[1] * 0.35f * Lr; tz = sh[2] - cfz * 0.55f * Lr + crz * side * 0.18f * Lr - BASu[2] * 0.35f * Lr;
            }
            if (seat != null && !(riding.kind == "rover" && an.aimK > 0.5f))
            {
                float hx2 = side * seat.Hand[0], hy2 = seat.Hand[1], hz2 = -seat.Hand[2] - seat.Hip[1];
                tx = BASo[0] + BASr[0] * hx2 + BASu[0] * hy2 + BASf[0] * hz2; ty = BASo[1] + BASr[1] * hx2 + BASu[1] * hy2 + BASf[1] * hz2; tz = BASo[2] + BASr[2] * hx2 + BASu[2] * hy2 + BASf[2] * hz2;
            }
            if (berserk)
            {   // arms windmilling as he charges
                float a = clockT * 13 + (side > 0 ? 0 : PI), Lb = (uaL + faL) * 0.95f;
                tx = sh[0] + (cfx * MathF.Cos(a) + BASu[0] * MathF.Sin(a)) * Lb * 0.8f + crx * side * 0.3f * Lb; ty = sh[1] + (cfy * MathF.Cos(a) + BASu[1] * MathF.Sin(a)) * Lb * 0.8f; tz = sh[2] + (cfz * MathF.Cos(a) + BASu[2] * MathF.Sin(a)) * Lb * 0.8f + crz * side * 0.3f * Lb;
            }
            if (an.holdK > 0.01f)
            {   // both hands up over the head
                float hx2 = headC[0] + crx * side * 0.16f * s + BASu[0] * 0.42f * s, hy2 = headC[1] + BASu[1] * 0.42f * s + 0.05f * s, hz2 = headC[2] + crz * side * 0.16f * s + BASu[2] * 0.42f * s;
                tx = Lerp(tx, hx2, an.holdK); ty = Lerp(ty, hy2, an.holdK); tz = Lerp(tz, hz2, an.holdK);
            }
            if (an.throwT > 0) { float k2 = an.throwT > 0.18f ? -0.4f : 0.9f; tx = sh[0] + cfx * k2 * (uaL + faL) + BASu[0] * 0.3f * s; ty = sh[1] + 0.35f * s; tz = sh[2] + cfz * k2 * (uaL + faL) + BASu[2] * 0.3f * s; }
            if (an.punchT > 0 && side > 0) { float k2 = an.punchT > 0.2f ? -0.2f : 1; tx = sh[0] + AIMD[0] * k2 * (uaL + faL) * 0.98f; ty = sh[1] + AIMD[1] * k2 * (uaL + faL) * 0.98f + (k2 < 0 ? 0.1f * s : 0); tz = sh[2] + AIMD[2] * k2 * (uaL + faL) * 0.98f; }
            if (an.aimK > 0.01f && an.holdK < 0.5f && an.throwT <= 0 && !(an.punchT > 0 && side > 0))
            {   // right arm points the blaster, left arm braces
                float reach = side > 0 ? (uaL + faL) * 0.97f : (uaL + faL) * 0.7f;
                float ax2 = sh[0] + AIMD[0] * reach + (side < 0 ? crx * 0.12f * s : 0), ay2 = sh[1] + AIMD[1] * reach - (side < 0 ? 0.08f * s : 0), az2 = sh[2] + AIMD[2] * reach + (side < 0 ? crz * 0.12f * s : 0);
                tx = Lerp(tx, ax2, an.aimK); ty = Lerp(ty, ay2, an.aimK); tz = Lerp(tz, az2, an.aimK);
            }
            bool twoHand = TWO_HAND.Contains(cw?.id ?? "");
            if (SW is SwingP sw0 && (side > 0 || twoHand))
            {   // melee swing: the hand follows the swing's path (both hands on a two-hander)
                float Lw = uaL + faL, ox = side > 0 ? 0 : 0.18f * s; var sh0 = twoHand ? J["shR"] : sh;
                tx = sh0[0] + (BASu[0] * sw0.up + cfx * sw0.fw + crx * sw0.rr) * Lw - crx * ox; ty = sh0[1] + (BASu[1] * sw0.up + cfy * sw0.fw + cry * sw0.rr) * Lw; tz = sh0[2] + (BASu[2] * sw0.up + cfz * sw0.fw + crz * sw0.rr) * Lw - crz * ox;
            }
            else if (SW is SwingP sw1 && !twoHand && side < 0)
            {   // the free hand balances the swing
                tx = sh[0] + (BASu[0] * -0.2f - cfx * sw1.fw * 0.4f + crx * -0.5f) * (uaL + faL); ty = sh[1] + BASu[1] * -0.2f * (uaL + faL); tz = sh[2] + (BASu[2] * -0.2f - cfz * sw1.fw * 0.4f + crz * -0.5f) * (uaL + faL);
            }
            if (G.grabbed != null) { float fl = MathF.Sin(clockT * 18 + side) * 0.3f; tx = sh[0] + crx * side * 0.4f * s; ty = sh[1] + (0.2f + fl) * s; tz = sh[2] + crz * side * 0.4f * s; }
            // a free arm swings on a spring around the shoulder, so it lags, overshoots and settles
            int si = side > 0 ? 1 : 0; var Sx = D.wr[si]; var SV = D.wv[si]; float ox2 = tx - sh[0], oy2 = ty - sh[1], oz2 = tz - sh[2];
            bool freeArm = an.aimK < 0.4f && an.holdK < 0.3f && an.throwT <= 0 && an.punchT <= 0 && an.swingT <= 0 && G.grabbed == null && riding == null && !(T == "normal" && cw != null);
            if (!D.wInit || !freeArm || fdt <= 0) { if (fdt > 0 || !D.wInit) { Sx[0] = ox2; Sx[1] = oy2; Sx[2] = oz2; SV[0] = SV[1] = SV[2] = 0; } }
            else for (int k = 0; k < 3; k++) { float o = k == 0 ? ox2 : k == 1 ? oy2 : oz2; SV[k] += ((o - Sx[k]) * (T == "brute" ? 90 : 160) - SV[k] * (T == "brute" ? 9 : 13)) * fdt; Sx[k] += SV[k] * fdt; }
            if (freeArm && D.wInit) { tx = sh[0] + Sx[0]; ty = sh[1] + Sx[1]; tz = sh[2] + Sx[2]; }
            if (side > 0) D.wInit = true;
            wr[0] = tx; wr[1] = ty; wr[2] = tz;
            Ik(sh, wr, uaL, faL, -cfx * 0.6f + crx * side * 0.4f, -0.5f, -cfz * 0.6f + crz * side * 0.4f, el);
        }
        // ---- write parts
        float fwx = cfx, fwy = cfy, fwz = cfz;
        if (A.has[2]) SegPart(A, 2, hipC, new[] { hipC[0] + spine[0] * 0.1f * s, hipC[1] + spine[1] * 0.1f * s, hipC[2] + spine[2] * 0.1f * s }, BASf[0], BASf[1], BASf[2], cK);
        if (A.has[1]) { QuatYF(spine[0], spine[1], spine[2], fwx, fwy, fwz, SQ); SetPart(A, 1, torC[0], torC[1], torC[2], SQ, cK); }
        if (A.has[0]) { QuatYF(spine[0] * 0.5f + BASu[0] * 0.5f, spine[1] * 0.5f + BASu[1] * 0.5f, spine[2] * 0.5f + BASu[2] * 0.5f, hfx, hfy, hfz, SQ); SetPart(A, 0, headC[0], headC[1], headC[2], SQ, cK); }
        for (int side = -1; side <= 1; side += 2)
        {
            string L = side < 0 ? "L" : "R"; int o = side < 0 ? 0 : 3;
            if (A.has[3 + o]) SegPart(A, 3 + o, J["sh" + L], J["el" + L], fwx, fwy, fwz, cK);
            if (A.has[4 + o]) SegPart(A, 4 + o, J["el" + L], J["wr" + L], fwx, fwy, fwz, cK);
            if (A.has[5 + o])
            {
                var w = J["wr" + L]; var e = J["el" + L]; float d = Hypot(w[0] - e[0], w[1] - e[1], w[2] - e[2]); if (d == 0) d = 1; float k = 0.05f * s / d;
                QuatYF(w[0] - e[0], w[1] - e[1], w[2] - e[2], -side * BASr[0], -side * BASr[1], -side * BASr[2], SQ); SetPart(A, 5 + o, w[0] + (w[0] - e[0]) * k, w[1] + (w[1] - e[1]) * k, w[2] + (w[2] - e[2]) * k, SQ, cK);
            }
            int lo = side < 0 ? 0 : 3;
            if (A.has[9 + lo]) SegPart(A, 9 + lo, J["hip" + L], J["knee" + L], fwx, fwy, fwz, cK);
            if (A.has[10 + lo]) SegPart(A, 10 + lo, J["knee" + L], J["ank" + L], fwx, fwy, fwz, cK);
            if (A.has[11 + lo]) { QuatYF(BASu[0], BASu[1], BASu[2], BASf[0], BASf[1], BASf[2], SQ); var a = J["ank" + L]; SetPart(A, 11 + lo, a[0] + BASf[0] * 0.05f * s, a[1] - 0.02f * s, a[2] + BASf[2] * 0.05f * s, SQ, cK); }
        }
        // flattened by the cartoon hammer: slammed into a pancake, a wobble while it lies there, then a springy pop back up
        float flatT = G.flatT > 0 ? G.flatT : 0;
        if (flatT > 0 || A.animFlatPop > 0)
        {
            float k;
            float e = (A.flatMax != 0 ? A.flatMax : 2) - flatT;
            if (flatT > 0) { k = MathF.Min(1, e / 0.07f); if (flatT < 0.35f) k = flatT / 0.35f; k *= 1 + 0.04f * MathF.Sin(clockT * 20); A.animFlatPop = 0.5f; }
            else { A.animFlatPop -= fdt; float u = 1 - A.animFlatPop / 0.5f; k = -0.35f * MathF.Sin(u * PI * 2.5f) * (1 - u); }   // overshoot: stretched tall, then settle
            var ps = A.pose; float hk = k > 0 ? 1 - 0.88f * k : 1 - k, wk = k > 0 ? 1 + 1.1f * k : 1 + k * 0.5f;
            for (int i = 0; i < NPART; i++)
            {
                int q = i * 13;
                ps[q + 1] = ry + 0.04f * s + (ps[q + 1] - ry) * hk; ps[q] = rx + (ps[q] - rx) * wk; ps[q + 2] = rz + (ps[q + 2] - rz) * wk;
                ps[q + 7] *= wk; ps[q + 9] *= wk; ps[q + 8] *= MathF.Max(0.15f, hk);
            }
        }
    }
}
