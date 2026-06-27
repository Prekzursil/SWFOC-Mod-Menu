// =============================================================================
// swfoc_overlay/overlay_cursor_ray_test.cpp — unit test for
// overlay_cursor_ray.h (Phase 5 cont., iter 537 / spec iter-298).
//
// iter-298 is the Phase 5 PLUMBING iter: turn a cursor pixel into a world-space
// pick ray. overlay_cursor_ray.h holds the pure math — Vec3/Mat4 primitives, a
// general 4x4 inverse, the screen->NDC fold, the cursor->ray unprojection, and
// the z=0 ground-plane intersection. This test pins all of it so the iter-299
// AABB raycast and the iter-300 inspector glue can depend on it build-only.
//
// The headline test is a ROUND TRIP: build a real right-handed view-projection
// matrix (D3DXMatrixLookAtRH * D3DXMatrixPerspectiveFovRH, the engine's own
// render path), project a known world point to a screen pixel, then feed that
// pixel back through CursorRay — the world point must land back on the ray. A
// transposed transform, a column-major inverse, or a dropped NDC-Y flip all
// miss the round trip by tens of world units.
//
// overlay_cursor_ray.h is header-only and std-only (<cmath>). Build + run via
// build_cursor_ray_test.bat — no game, no pipe, no ImGui.
//
// RED-GREEN REGRESSION PINS
// ------------------------
//   - UNPROJECT ROUND-TRIPS : a world point projected to a pixel and fed back
//                             through CursorRay must lie on the returned ray.
//   - NDC Y IS FLIPPED      : ScreenToNdc(top-left) is (-1, +1), not (-1, -1).
//   - INVERSE IS A TRUE INVERSE : M * Mat4Inverse(M) == identity.
//   - SINGULAR MATRIX REJECTED  : Mat4Inverse of a rank-deficient matrix
//                             returns false; CursorRay with such a matrix
//                             returns valid=false.
//   - GROUND PICK ON Z=0    : RayPlaneZ0 resolves the world (X, Y) the pick
//                             ray crosses z=0 at — back to the projected point.
//   - PARALLEL RAY NO HIT   : a ray parallel to the ground reports no hit.
// =============================================================================

#include "overlay_cursor_ray.h"

#include <cmath>
#include <cstdio>

namespace
{
    int g_checks = 0;
    int g_failures = 0;

    void ExpectTrue(const char* name, bool cond)
    {
        ++g_checks;
        if (cond)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    expected true\n", name);
        }
    }

    // Float compare with an explicit absolute epsilon. Pure matrix / NDC math
    // is exact to ~1e-4; world-space unprojection round trips accumulate a
    // float-precision error that scales with the far-plane distance, so those
    // checks pass a looser epsilon — still tens of units tighter than any
    // transposed-matrix or wrong-handedness bug would land.
    void ExpectNearEps(const char* name, float got, float want, float eps)
    {
        ++g_checks;
        const float diff = got - want;
        const float absdiff = diff < 0.0f ? -diff : diff;
        if (absdiff <= eps)
        {
            std::printf("  ok   %s\n", name);
        }
        else
        {
            ++g_failures;
            std::printf("  FAIL %s\n    got : %.5f\n    want: %.5f\n",
                        name, static_cast<double>(got),
                        static_cast<double>(want));
        }
    }

    void ExpectNear(const char* name, float got, float want)
    {
        ExpectNearEps(name, got, want, 0.001f);
    }

    // Assert a Vec3 equals (wx, wy, wz) within `eps`. One named sub-check per
    // axis so a failure report says which axis drifted.
    void ExpectVec3(const char* name, const swfoc_overlay::Vec3& got,
                    float wx, float wy, float wz, float eps)
    {
        char nm[160];
        std::snprintf(nm, sizeof(nm), "%s [x]", name);
        ExpectNearEps(nm, got.x, wx, eps);
        std::snprintf(nm, sizeof(nm), "%s [y]", name);
        ExpectNearEps(nm, got.y, wy, eps);
        std::snprintf(nm, sizeof(nm), "%s [z]", name);
        ExpectNearEps(nm, got.z, wz, eps);
    }

    // ---- Test-only fixtures: build the matrices the engine builds ----------
    // These mirror D3DX's matrix constructors so the test exercises the kernel
    // against the exact transform conventions the engine's render path uses.

    using swfoc_overlay::Mat4;
    using swfoc_overlay::Vec3;
    using swfoc_overlay::Vec4;

    Mat4 MakeTranslation(float tx, float ty, float tz)
    {
        Mat4 r = swfoc_overlay::Mat4Identity();
        r.m[12] = tx;
        r.m[13] = ty;
        r.m[14] = tz;
        return r;
    }

    Mat4 MakeScale(float sx, float sy, float sz)
    {
        Mat4 r{};
        r.m[0]  = sx;
        r.m[5]  = sy;
        r.m[10] = sz;
        r.m[15] = 1.0f;
        return r;
    }

    // Row-major D3DXMatrixPerspectiveFovRH equivalent (D3D depth range [0,1]).
    Mat4 MakePerspectiveFovRH(float fovY, float aspect, float zn, float zf)
    {
        const float yScale = 1.0f / std::tan(fovY * 0.5f);
        const float xScale = yScale / aspect;
        Mat4 r{};
        r.m[0]  = xScale;
        r.m[5]  = yScale;
        r.m[10] = zf / (zn - zf);
        r.m[11] = -1.0f;
        r.m[14] = zn * zf / (zn - zf);
        r.m[15] = 0.0f;
        return r;
    }

    // Row-major D3DXMatrixLookAtRH equivalent.
    Mat4 MakeLookAtRH(const Vec3& eye, const Vec3& at, const Vec3& up)
    {
        const Vec3 zaxis =
            swfoc_overlay::Vec3Normalize(swfoc_overlay::Vec3Sub(eye, at));
        const Vec3 xaxis =
            swfoc_overlay::Vec3Normalize(swfoc_overlay::Vec3Cross(up, zaxis));
        const Vec3 yaxis = swfoc_overlay::Vec3Cross(zaxis, xaxis);
        Mat4 r{};
        r.m[0]  = xaxis.x; r.m[1]  = yaxis.x; r.m[2]  = zaxis.x; r.m[3]  = 0.0f;
        r.m[4]  = xaxis.y; r.m[5]  = yaxis.y; r.m[6]  = zaxis.y; r.m[7]  = 0.0f;
        r.m[8]  = xaxis.z; r.m[9]  = yaxis.z; r.m[10] = zaxis.z; r.m[11] = 0.0f;
        r.m[12] = -swfoc_overlay::Vec3Dot(xaxis, eye);
        r.m[13] = -swfoc_overlay::Vec3Dot(yaxis, eye);
        r.m[14] = -swfoc_overlay::Vec3Dot(zaxis, eye);
        r.m[15] = 1.0f;
        return r;
    }

    // The screen pixel a world point projects to, plus whether it is in front
    // of the camera. Mirrors the engine's world->clip->screen path — the exact
    // inverse of what CursorRay does.
    struct Projected
    {
        float sx;
        float sy;
        bool  inFront;
    };

    Projected ProjectWorld(const Vec3& w, const Mat4& viewProj,
                           float vw, float vh)
    {
        const Vec4 clip = swfoc_overlay::Mat4TransformRow(
            Vec4{ w.x, w.y, w.z, 1.0f }, viewProj);
        Projected p{};
        p.inFront = clip.w > 0.0f;
        if (clip.w == 0.0f)
        {
            p.sx = 0.0f;
            p.sy = 0.0f;
            return p;
        }
        const float ndcx = clip.x / clip.w;
        const float ndcy = clip.y / clip.w;
        p.sx = (ndcx + 1.0f) * 0.5f * vw;
        p.sy = (1.0f - ndcy) * 0.5f * vh;  // screen Y grows down
        return p;
    }

    // Perpendicular distance from a world point to the infinite line of a ray.
    // ~0 means the point lies on the ray — the round-trip success criterion.
    float PointRayDistance(const Vec3& w, const swfoc_overlay::WorldRay& ray)
    {
        const Vec3 d = swfoc_overlay::Vec3Sub(w, ray.origin);
        const float t = swfoc_overlay::Vec3Dot(d, ray.direction);
        const Vec3 closest = swfoc_overlay::Vec3Add(
            ray.origin, swfoc_overlay::Vec3Scale(ray.direction, t));
        return swfoc_overlay::Vec3Length(swfoc_overlay::Vec3Sub(w, closest));
    }
}

int main()
{
    using namespace swfoc_overlay;

    std::printf("== overlay_cursor_ray unit test ==\n");

    // ---- Section A: Vec3 primitives ---------------------------------------
    ExpectVec3("vec3: Sub", Vec3Sub(Vec3{ 5, 7, 9 }, Vec3{ 1, 2, 3 }),
               4.0f, 5.0f, 6.0f, 0.0001f);
    ExpectVec3("vec3: Add", Vec3Add(Vec3{ 5, 7, 9 }, Vec3{ 1, 2, 3 }),
               6.0f, 9.0f, 12.0f, 0.0001f);
    ExpectVec3("vec3: Scale", Vec3Scale(Vec3{ 2, -3, 4 }, 2.5f),
               5.0f, -7.5f, 10.0f, 0.0001f);
    ExpectNear("vec3: Dot", Vec3Dot(Vec3{ 1, 2, 3 }, Vec3{ 4, 5, 6 }), 32.0f);
    ExpectNear("vec3: Dot of perpendicular axes is 0",
               Vec3Dot(Vec3{ 1, 0, 0 }, Vec3{ 0, 1, 0 }), 0.0f);
    // Cross of the X and Y axes is the +Z axis (right-handed).
    ExpectVec3("vec3: Cross(X, Y) = Z",
               Vec3Cross(Vec3{ 1, 0, 0 }, Vec3{ 0, 1, 0 }),
               0.0f, 0.0f, 1.0f, 0.0001f);
    ExpectVec3("vec3: Cross(Y, Z) = X",
               Vec3Cross(Vec3{ 0, 1, 0 }, Vec3{ 0, 0, 1 }),
               1.0f, 0.0f, 0.0f, 0.0001f);
    // Cross is anti-commutative.
    ExpectVec3("vec3: Cross(Y, X) = -Z",
               Vec3Cross(Vec3{ 0, 1, 0 }, Vec3{ 1, 0, 0 }),
               0.0f, 0.0f, -1.0f, 0.0001f);
    ExpectNear("vec3: Length of (3,4,0) is 5", Vec3Length(Vec3{ 3, 4, 0 }),
               5.0f);
    ExpectNear("vec3: Length of (0,0,0) is 0", Vec3Length(Vec3{ 0, 0, 0 }),
               0.0f);
    {
        const Vec3 n = Vec3Normalize(Vec3{ 0, 0, 7 });
        ExpectVec3("vec3: Normalize(0,0,7) is unit +Z", n,
                   0.0f, 0.0f, 1.0f, 0.0001f);
        ExpectNear("vec3: Normalize result has unit length",
                   Vec3Length(n), 1.0f);
    }
    {
        const Vec3 n = Vec3Normalize(Vec3{ 6, -8, 0 });
        ExpectNear("vec3: Normalize(6,-8,0) has unit length",
                   Vec3Length(n), 1.0f);
    }
    // PIN: a zero vector has no direction — Normalize returns (0,0,0), not NaN.
    ExpectVec3("PIN vec3: Normalize(0,0,0) is (0,0,0), not NaN",
               Vec3Normalize(Vec3{ 0, 0, 0 }), 0.0f, 0.0f, 0.0f, 0.0001f);

    // ---- Section B: Mat4Identity / Mat4Multiply ---------------------------
    {
        const Mat4 id = Mat4Identity();
        bool idOk = true;
        for (int i = 0; i < 16; ++i)
        {
            const float want = (i % 5 == 0) ? 1.0f : 0.0f;
            if (id.m[i] != want)
            {
                idOk = false;
            }
        }
        ExpectTrue("mat4: Identity has 1s on the diagonal, 0s elsewhere",
                   idOk);
    }
    {
        // M * I == M and I * M == M for a non-trivial M.
        const Mat4 t = MakeTranslation(3, 5, 7);
        const Mat4 mi = Mat4Multiply(t, Mat4Identity());
        const Mat4 im = Mat4Multiply(Mat4Identity(), t);
        bool miOk = true;
        bool imOk = true;
        for (int i = 0; i < 16; ++i)
        {
            if (std::fabs(mi.m[i] - t.m[i]) > 0.0001f) miOk = false;
            if (std::fabs(im.m[i] - t.m[i]) > 0.0001f) imOk = false;
        }
        ExpectTrue("mat4: M * I == M", miOk);
        ExpectTrue("mat4: I * M == M", imOk);
    }
    {
        // Translations compose by addition.
        const Mat4 prod =
            Mat4Multiply(MakeTranslation(1, 2, 3), MakeTranslation(10, 20, 30));
        ExpectNear("mat4: T(1,2,3) * T(10,20,30) -> tx 11", prod.m[12], 11.0f);
        ExpectNear("mat4: T(1,2,3) * T(10,20,30) -> ty 22", prod.m[13], 22.0f);
        ExpectNear("mat4: T(1,2,3) * T(10,20,30) -> tz 33", prod.m[14], 33.0f);
    }
    {
        // Scales compose by multiplication.
        const Mat4 prod =
            Mat4Multiply(MakeScale(2, 3, 4), MakeScale(5, 6, 7));
        ExpectNear("mat4: S(2,3,4) * S(5,6,7) -> sx 10", prod.m[0], 10.0f);
        ExpectNear("mat4: S(2,3,4) * S(5,6,7) -> sy 18", prod.m[5], 18.0f);
        ExpectNear("mat4: S(2,3,4) * S(5,6,7) -> sz 28", prod.m[10], 28.0f);
    }

    // ---- Section C: Mat4TransformRow --------------------------------------
    {
        const Vec4 v = Mat4TransformRow(Vec4{ 1, 2, 3, 1 }, Mat4Identity());
        ExpectNear("transform: v * I keeps x", v.x, 1.0f);
        ExpectNear("transform: v * I keeps y", v.y, 2.0f);
        ExpectNear("transform: v * I keeps z", v.z, 3.0f);
        ExpectNear("transform: v * I keeps w", v.w, 1.0f);
    }
    {
        // A w=1 point picks up the translation row.
        const Vec4 v =
            Mat4TransformRow(Vec4{ 1, 2, 3, 1 }, MakeTranslation(10, 20, 30));
        ExpectNear("transform: (1,2,3,1) * T(10,20,30) -> x 11", v.x, 11.0f);
        ExpectNear("transform: (1,2,3,1) * T(10,20,30) -> y 22", v.y, 22.0f);
        ExpectNear("transform: (1,2,3,1) * T(10,20,30) -> z 33", v.z, 33.0f);
    }
    {
        // A w=0 direction is immune to translation.
        const Vec4 v =
            Mat4TransformRow(Vec4{ 1, 2, 3, 0 }, MakeTranslation(10, 20, 30));
        ExpectNear("transform: w=0 direction ignores translation x", v.x,
                   1.0f);
        ExpectNear("transform: w=0 direction ignores translation z", v.z,
                   3.0f);
    }
    {
        const Vec4 v =
            Mat4TransformRow(Vec4{ 1, 2, 3, 1 }, MakeScale(2, 3, 4));
        ExpectNear("transform: (1,2,3,1) * S(2,3,4) -> x 2", v.x, 2.0f);
        ExpectNear("transform: (1,2,3,1) * S(2,3,4) -> y 6", v.y, 6.0f);
        ExpectNear("transform: (1,2,3,1) * S(2,3,4) -> z 12", v.z, 12.0f);
    }

    // ---- Section D: Mat4Inverse -------------------------------------------
    {
        Mat4 inv{};
        const bool ok = Mat4Inverse(Mat4Identity(), inv);
        ExpectTrue("inverse: identity is invertible", ok);
        bool idOk = true;
        for (int i = 0; i < 16; ++i)
        {
            const float want = (i % 5 == 0) ? 1.0f : 0.0f;
            if (std::fabs(inv.m[i] - want) > 0.0001f) idOk = false;
        }
        ExpectTrue("inverse: inverse of identity is identity", idOk);
    }
    {
        // Inverse of a translation negates the translation.
        Mat4 inv{};
        const bool ok = Mat4Inverse(MakeTranslation(10, 20, 30), inv);
        ExpectTrue("inverse: translation is invertible", ok);
        ExpectNear("inverse: T(10,20,30)^-1 has tx -10", inv.m[12], -10.0f);
        ExpectNear("inverse: T(10,20,30)^-1 has ty -20", inv.m[13], -20.0f);
        ExpectNear("inverse: T(10,20,30)^-1 has tz -30", inv.m[14], -30.0f);
    }
    {
        // Inverse of a scale reciprocates the scale.
        Mat4 inv{};
        const bool ok = Mat4Inverse(MakeScale(2, 4, 8), inv);
        ExpectTrue("inverse: scale is invertible", ok);
        ExpectNear("inverse: S(2,4,8)^-1 has sx 0.5", inv.m[0], 0.5f);
        ExpectNear("inverse: S(2,4,8)^-1 has sy 0.25", inv.m[5], 0.25f);
        ExpectNear("inverse: S(2,4,8)^-1 has sz 0.125", inv.m[10], 0.125f);
    }
    {
        // PIN inverse-is-a-true-inverse: M * M^-1 == identity for a
        // non-trivial view-projection matrix.
        const Mat4 view = MakeLookAtRH(Vec3{ 40, -120, 90 }, Vec3{ 0, 0, 0 },
                                       Vec3{ 0, 0, 1 });
        const Mat4 proj = MakePerspectiveFovRH(0.9f, 1280.0f / 720.0f,
                                               1.0f, 600.0f);
        const Mat4 vp = Mat4Multiply(view, proj);
        Mat4 inv{};
        const bool ok = Mat4Inverse(vp, inv);
        ExpectTrue("inverse: view-projection matrix is invertible", ok);
        const Mat4 fwd = Mat4Multiply(vp, inv);
        const Mat4 bwd = Mat4Multiply(inv, vp);
        bool fwdOk = true;
        bool bwdOk = true;
        for (int i = 0; i < 16; ++i)
        {
            const float want = (i % 5 == 0) ? 1.0f : 0.0f;
            if (std::fabs(fwd.m[i] - want) > 0.01f) fwdOk = false;
            if (std::fabs(bwd.m[i] - want) > 0.01f) bwdOk = false;
        }
        ExpectTrue("PIN inverse: viewProj * viewProj^-1 == identity", fwdOk);
        ExpectTrue("PIN inverse: viewProj^-1 * viewProj == identity", bwdOk);
    }
    {
        // PIN singular-matrix-rejected: an all-zero matrix has no inverse.
        Mat4 zero{};
        Mat4 inv = Mat4Identity();
        const bool ok = Mat4Inverse(zero, inv);
        ExpectTrue("PIN inverse: all-zero matrix is rejected (returns false)",
                   !ok);
    }
    {
        // A matrix with a zero row is rank-deficient — also rejected.
        Mat4 deficient = MakeScale(2, 3, 4);
        deficient.m[4] = 0.0f;
        deficient.m[5] = 0.0f;
        deficient.m[6] = 0.0f;
        deficient.m[7] = 0.0f;
        Mat4 inv = Mat4Identity();
        const bool ok = Mat4Inverse(deficient, inv);
        ExpectTrue("inverse: matrix with a zero row is rejected", !ok);
    }

    // ---- Section E: ScreenToNdc -------------------------------------------
    const float kVw = 1280.0f;
    const float kVh = 720.0f;
    {
        const NdcPoint c = ScreenToNdc(640.0f, 360.0f, kVw, kVh);
        ExpectNear("ndc: screen center -> (0, 0) x", c.x, 0.0f);
        ExpectNear("ndc: screen center -> (0, 0) y", c.y, 0.0f);
    }
    {
        // PIN ndc-y-is-flipped: top-left is (-1, +1). Screen Y grows DOWN,
        // NDC Y grows UP — a missing flip would report (-1, -1) here.
        const NdcPoint tl = ScreenToNdc(0.0f, 0.0f, kVw, kVh);
        ExpectNear("PIN ndc: top-left -> x -1", tl.x, -1.0f);
        ExpectNear("PIN ndc: top-left -> y +1 (NOT -1 — Y is flipped)",
                   tl.y, 1.0f);
    }
    {
        const NdcPoint br = ScreenToNdc(kVw, kVh, kVw, kVh);
        ExpectNear("ndc: bottom-right -> x +1", br.x, 1.0f);
        ExpectNear("ndc: bottom-right -> y -1", br.y, -1.0f);
    }
    {
        const NdcPoint tr = ScreenToNdc(kVw, 0.0f, kVw, kVh);
        ExpectNear("ndc: top-right -> x +1", tr.x, 1.0f);
        ExpectNear("ndc: top-right -> y +1", tr.y, 1.0f);
    }
    {
        const NdcPoint bl = ScreenToNdc(0.0f, kVh, kVw, kVh);
        ExpectNear("ndc: bottom-left -> x -1", bl.x, -1.0f);
        ExpectNear("ndc: bottom-left -> y -1", bl.y, -1.0f);
    }
    {
        const NdcPoint q = ScreenToNdc(320.0f, 180.0f, kVw, kVh);
        ExpectNear("ndc: quarter point -> x -0.5", q.x, -0.5f);
        ExpectNear("ndc: quarter point -> y +0.5", q.y, 0.5f);
    }

    // ---- Section F: CursorRay degenerate inputs ---------------------------
    {
        const Mat4 vp = Mat4Multiply(
            MakeLookAtRH(Vec3{ 40, -120, 90 }, Vec3{ 0, 0, 0 },
                         Vec3{ 0, 0, 1 }),
            MakePerspectiveFovRH(0.9f, kVw / kVh, 1.0f, 600.0f));
        ExpectTrue("cursor: zero viewport width -> invalid ray",
                   !CursorRay(640.0f, 360.0f, 0.0f, kVh, vp).valid);
        ExpectTrue("cursor: zero viewport height -> invalid ray",
                   !CursorRay(640.0f, 360.0f, kVw, 0.0f, vp).valid);
        ExpectTrue("cursor: negative viewport width -> invalid ray",
                   !CursorRay(640.0f, 360.0f, -10.0f, kVh, vp).valid);
        // PIN singular-matrix-rejected: a degenerate view-projection yields an
        // invalid ray, never a NaN-filled one.
        Mat4 zero{};
        const WorldRay bad = CursorRay(640.0f, 360.0f, kVw, kVh, zero);
        ExpectTrue("PIN cursor: singular viewProj -> invalid ray", !bad.valid);
        ExpectTrue("cursor: invalid ray origin is zeroed",
                   bad.origin.x == 0.0f && bad.origin.y == 0.0f &&
                   bad.origin.z == 0.0f);
        ExpectTrue("cursor: invalid ray direction is zeroed",
                   bad.direction.x == 0.0f && bad.direction.y == 0.0f &&
                   bad.direction.z == 0.0f);
        // A valid viewport + matrix gives a valid ray at the screen center.
        ExpectTrue("cursor: valid viewport + matrix -> valid ray",
                   CursorRay(640.0f, 360.0f, kVw, kVh, vp).valid);
    }

    // ---- Section G: CursorRay round trip (the headline pins) --------------
    // Build the engine's own render path: a right-handed look-at view matrix
    // times a right-handed perspective projection. Project a known world
    // point to its screen pixel, then unproject that pixel — the point must
    // land back on the returned ray (UNPROJECT ROUND-TRIPS pin).
    {
        const Mat4 view = MakeLookAtRH(Vec3{ 40, -120, 90 }, Vec3{ 0, 0, 0 },
                                       Vec3{ 0, 0, 1 });
        const Mat4 proj = MakePerspectiveFovRH(0.9f, kVw / kVh,
                                               1.0f, 600.0f);
        const Mat4 viewProj = Mat4Multiply(view, proj);

        // Four ground-plane (z=0) world points to round-trip.
        const Vec3 ground[4] = {
            Vec3{ 0.0f, 0.0f, 0.0f },
            Vec3{ 50.0f, 30.0f, 0.0f },
            Vec3{ -40.0f, 70.0f, 0.0f },
            Vec3{ 15.0f, -25.0f, 0.0f }
        };
        const char* groundName[4] = {
            "world origin", "(+50,+30,0)", "(-40,+70,0)", "(+15,-25,0)"
        };

        for (int i = 0; i < 4; ++i)
        {
            const Vec3 w = ground[i];
            char nm[160];

            const Projected p = ProjectWorld(w, viewProj, kVw, kVh);
            std::snprintf(nm, sizeof(nm),
                          "round-trip: %s projects in front of camera",
                          groundName[i]);
            ExpectTrue(nm, p.inFront);

            // Primary overload: pre-multiplied view-projection matrix.
            const WorldRay ray = CursorRay(p.sx, p.sy, kVw, kVh, viewProj);
            std::snprintf(nm, sizeof(nm),
                          "round-trip: %s -> valid ray (viewProj overload)",
                          groundName[i]);
            ExpectTrue(nm, ray.valid);

            std::snprintf(nm, sizeof(nm),
                          "PIN round-trip: %s lies on the unprojected ray",
                          groundName[i]);
            ExpectNearEps(nm, PointRayDistance(w, ray), 0.0f, 0.2f);

            // The pick ray points into the scene — its direction must have a
            // unit length the iter-299 AABB raycast can rely on.
            std::snprintf(nm, sizeof(nm),
                          "round-trip: %s ray direction is unit length",
                          groundName[i]);
            ExpectNearEps(nm, Vec3Length(ray.direction), 1.0f, 0.001f);

            // Camera is above the battlefield -> the ray descends (dir.z < 0).
            std::snprintf(nm, sizeof(nm),
                          "round-trip: %s ray descends toward the ground",
                          groundName[i]);
            ExpectTrue(nm, ray.direction.z < 0.0f);

            // Convenience overload: separate view + projection matrices must
            // agree with the pre-multiplied one bit-for-bit.
            const WorldRay ray2 =
                CursorRay(p.sx, p.sy, kVw, kVh, view, proj);
            std::snprintf(nm, sizeof(nm),
                          "round-trip: %s view+proj overload agrees on origin",
                          groundName[i]);
            ExpectTrue(nm,
                       std::fabs(ray.origin.x - ray2.origin.x) < 0.001f &&
                       std::fabs(ray.origin.y - ray2.origin.y) < 0.001f &&
                       std::fabs(ray.origin.z - ray2.origin.z) < 0.001f);

            // GROUND PICK ON Z=0 pin: the ray crosses the ground plane back
            // at the world point it was projected from.
            const PlaneHit hit = RayPlaneZ0(ray);
            std::snprintf(nm, sizeof(nm),
                          "PIN ground-pick: %s ray hits the z=0 plane",
                          groundName[i]);
            ExpectTrue(nm, hit.hit);

            std::snprintf(nm, sizeof(nm),
                          "PIN ground-pick: %s hit recovers world X",
                          groundName[i]);
            ExpectNearEps(nm, hit.x, w.x, 0.3f);
            std::snprintf(nm, sizeof(nm),
                          "PIN ground-pick: %s hit recovers world Y",
                          groundName[i]);
            ExpectNearEps(nm, hit.y, w.y, 0.3f);
        }

        // An off-ground world point (z != 0) still round-trips onto the ray —
        // unprojection is not limited to the ground plane.
        {
            const Vec3 w = Vec3{ 25.0f, 20.0f, 18.0f };
            const Projected p = ProjectWorld(w, viewProj, kVw, kVh);
            ExpectTrue("round-trip: off-ground point projects in front",
                       p.inFront);
            const WorldRay ray = CursorRay(p.sx, p.sy, kVw, kVh, viewProj);
            ExpectTrue("round-trip: off-ground point -> valid ray", ray.valid);
            ExpectNearEps("PIN round-trip: off-ground point lies on the ray",
                          PointRayDistance(w, ray), 0.0f, 0.2f);
        }

        // The ray origin sits on the camera near plane — well in front of the
        // far plane and near the camera, not out past the battlefield.
        {
            const WorldRay ray = CursorRay(640.0f, 360.0f, kVw, kVh, viewProj);
            ExpectTrue("round-trip: center-screen ray is valid", ray.valid);
            // Near-plane origin is within ~2 units of the eye-to-target line;
            // it is far closer to the camera (z~90) than to the ground.
            ExpectTrue("round-trip: center ray origin is above the ground",
                       ray.origin.z > 0.0f);
        }
    }

    // ---- Section H: RayPlaneZ0 with hand-built rays -----------------------
    {
        // A ray dropped straight down hits z=0 directly below its origin.
        WorldRay down{};
        down.origin = Vec3{ 5.0f, 7.0f, 50.0f };
        down.direction = Vec3{ 0.0f, 0.0f, -1.0f };
        down.valid = true;
        const PlaneHit h = RayPlaneZ0(down);
        ExpectTrue("plane: straight-down ray hits z=0", h.hit);
        ExpectNear("plane: straight-down hit t = origin.z (50)", h.t, 50.0f);
        ExpectNear("plane: straight-down hit keeps world X", h.x, 5.0f);
        ExpectNear("plane: straight-down hit keeps world Y", h.y, 7.0f);
    }
    {
        // A 45-degree ray: from (0,0,100) along (1,0,-1) normalized.
        WorldRay diag{};
        diag.origin = Vec3{ 0.0f, 0.0f, 100.0f };
        diag.direction = Vec3Normalize(Vec3{ 1.0f, 0.0f, -1.0f });
        diag.valid = true;
        const PlaneHit h = RayPlaneZ0(diag);
        ExpectTrue("plane: 45-degree ray hits z=0", h.hit);
        // Descends 100 in Z, so it travels 100 in X -> lands at x=100.
        ExpectNearEps("plane: 45-degree hit lands at x=100", h.x, 100.0f,
                      0.01f);
        ExpectNearEps("plane: 45-degree hit keeps y=0", h.y, 0.0f, 0.01f);
    }
    {
        // A ray climbing UP from below the plane still meets it ahead.
        WorldRay up{};
        up.origin = Vec3{ 12.0f, -8.0f, -30.0f };
        up.direction = Vec3{ 0.0f, 0.0f, 1.0f };
        up.valid = true;
        const PlaneHit h = RayPlaneZ0(up);
        ExpectTrue("plane: ray from below climbs to z=0", h.hit);
        ExpectNear("plane: from-below hit t = 30", h.t, 30.0f);
        ExpectNear("plane: from-below hit keeps world X", h.x, 12.0f);
    }
    {
        // PIN parallel-ray-no-hit: a ray parallel to the ground never meets
        // it — and the kernel must not divide by the zero direction.z.
        WorldRay flat{};
        flat.origin = Vec3{ 0.0f, 0.0f, 50.0f };
        flat.direction = Vec3{ 1.0f, 0.0f, 0.0f };
        flat.valid = true;
        ExpectTrue("PIN plane: ray parallel to z=0 reports no hit",
                   !RayPlaneZ0(flat).hit);
    }
    {
        // A direction.z below the parallel epsilon also counts as parallel.
        WorldRay nearlyFlat{};
        nearlyFlat.origin = Vec3{ 0.0f, 0.0f, 50.0f };
        nearlyFlat.direction = Vec3{ 1.0f, 0.0f, 1e-8f };
        nearlyFlat.valid = true;
        ExpectTrue("plane: near-parallel ray (|dz| < epsilon) reports no hit",
                   !RayPlaneZ0(nearlyFlat).hit);
    }
    {
        // A ray pointing away from the plane meets it only behind the origin
        // (t < 0) -> reported as no hit.
        WorldRay away{};
        away.origin = Vec3{ 0.0f, 0.0f, 50.0f };
        away.direction = Vec3{ 0.0f, 0.0f, 1.0f };
        away.valid = true;
        ExpectTrue("plane: ray climbing away from z=0 reports no hit (t<0)",
                   !RayPlaneZ0(away).hit);
    }
    {
        // An invalid ray never produces a hit, whatever its fields hold.
        WorldRay invalid{};
        invalid.origin = Vec3{ 5.0f, 7.0f, 50.0f };
        invalid.direction = Vec3{ 0.0f, 0.0f, -1.0f };
        invalid.valid = false;
        ExpectTrue("plane: invalid ray reports no hit", !RayPlaneZ0(invalid).hit);
    }

    // ---- Section I: integration — the iter-300 ground-pick pipeline -------
    // The exact sequence the iter-300 click-to-inspect glue will run: take a
    // cursor pixel, build the pick ray, intersect the ground plane, and treat
    // the result as a world location — the same z=0 plane Phase 4 drag-drop
    // spawning resolves onto.
    {
        const Mat4 view = MakeLookAtRH(Vec3{ 0, -100, 120 }, Vec3{ 0, 0, 0 },
                                       Vec3{ 0, 0, 1 });
        const Mat4 proj = MakePerspectiveFovRH(1.0f, kVw / kVh, 1.0f, 800.0f);
        const Mat4 viewProj = Mat4Multiply(view, proj);

        // The operator clicks the exact center of the screen.
        const WorldRay ray = CursorRay(kVw * 0.5f, kVh * 0.5f, kVw, kVh,
                                       viewProj);
        ExpectTrue("integration: center click yields a valid pick ray",
                   ray.valid);
        const PlaneHit hit = RayPlaneZ0(ray);
        ExpectTrue("integration: center pick ray reaches the ground plane",
                   hit.hit);
        // Camera looks straight at the origin -> a center click lands at ~0,0.
        ExpectNearEps("integration: center click lands near world origin X",
                      hit.x, 0.0f, 1.0f);
        ExpectNearEps("integration: center click lands near world origin Y",
                      hit.y, 0.0f, 1.0f);
        ExpectTrue("integration: ground-pick t is ahead of the camera (t>0)",
                   hit.t > 0.0f);

        // A click round-trips: project the picked ground point back and the
        // pixel returns to the cursor it came from.
        const Projected back =
            ProjectWorld(Vec3{ hit.x, hit.y, 0.0f }, viewProj, kVw, kVh);
        ExpectNearEps("integration: picked point re-projects to cursor X",
                      back.sx, kVw * 0.5f, 1.0f);
        ExpectNearEps("integration: picked point re-projects to cursor Y",
                      back.sy, kVh * 0.5f, 1.0f);
    }

    std::printf("\n%d checks, %d failures\n", g_checks, g_failures);
    return g_failures == 0 ? 0 : 1;
}
