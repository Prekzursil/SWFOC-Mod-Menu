// =============================================================================
// swfoc_overlay/overlay_cursor_ray.h — Phase 5 cursor -> world-space kernel.
//
// Phase 5 (iter 297-303) makes the overlay click-aware: the operator clicks an
// in-game unit and an inspector panel opens. iter-297 (the Phase 5 kickoff RE
// pass) pinned the engine's global D3D transform matrices at 3-tool consensus
// — see knowledge-base/overlay_phase5_projection_matrix_2026-05-21.md:
//
//   0xA6EEE4  global VIEW matrix        (D3DXMATRIX, 64 B)
//   0xA6EF24  global PROJECTION matrix  (D3DXMATRIX, 64 B)
//   0xA6F49C  cached VIEW*PROJECTION    (D3DXMATRIX, 64 B)
//
// iter-298 (spec line 56) is the PLUMBING iter: turn a cursor pixel into a
// world-space pick ray. There is no UI yet — iter-299 raycasts unit AABBs
// against the ray, iter-300 draws the inspector panel. This header is that
// pure kernel: the math that has a right and a wrong answer and so must be
// pinned by a unit test before any glue depends on it.
//
//   1. ScreenToNdc()  — fold a screen pixel into D3D normalized device
//      coordinates. Screen Y grows DOWN, NDC Y grows UP — the flip is the
//      classic off-by-a-mirror bug, so it gets its own pin.
//   2. Mat4Inverse()  — a true general 4x4 inverse. The unprojection runs the
//      cursor backwards through the inverse of the view-projection matrix; a
//      wrong cofactor sign would bend every pick.
//   3. CursorRay()    — the headline: cursor + viewport + view-projection
//      matrix -> world-space pick ray (origin on the near plane, unit
//      direction toward the far plane). It unprojects the near (z_ndc=0) and
//      far (z_ndc=1) D3D-depth clip points and joins them.
//   4. RayPlaneZ0()   — intersect the pick ray with the z=0 ground plane.
//      This is the Phase 4/5 "2D Z=0 plane interim" (spec line 32 / 72): a
//      cursor pick and a Phase 4 drag-drop drop both resolve onto the same
//      ground plane, so they share one world coordinate system.
//
// RED-GREEN REGRESSION PINS (overlay_cursor_ray_test.cpp)
// ------------------------------------------------------
//   - UNPROJECT ROUND-TRIPS : project a known world point through a real
//                             RH view-projection, feed the screen pixel to
//                             CursorRay — the world point must lie back on
//                             the returned ray. A transposed transform or a
//                             column-major inverse misses by tens of units.
//   - NDC Y IS FLIPPED      : ScreenToNdc(top-left) is (-1, +1), not
//                             (-1, -1). Dropping the flip mirrors every pick.
//   - INVERSE IS A TRUE INVERSE : M * Mat4Inverse(M) == identity. A wrong
//                             cofactor sign fails this round trip.
//   - SINGULAR MATRIX REJECTED  : Mat4Inverse of a rank-deficient matrix
//                             returns false; CursorRay with an un-invertible
//                             view-projection returns valid=false, never NaN.
//   - GROUND PICK ON Z=0    : RayPlaneZ0 resolves the world (X, Y) the pick
//                             ray crosses the ground plane at — the same
//                             plane Phase 4 drag-drop spawning uses.
//   - PARALLEL RAY NO HIT   : a ray parallel to the ground reports no hit and
//                             never divides by a zero direction.z.
//
// HONEST DEFER (overlay-interactive.md line 76): cursor-hit-UNIT detection
// still has no exposed engine wire. iter-298 only produces the world RAY;
// iter-299 walks per-unit AABBs against it (Mitigation A — client-side
// raycast). This header deliberately stops at the ray + ground-plane pick.
//
// Pure, header-only, std-only (<cmath> for sqrt/tan/fabs) — no ImGui, no
// Windows, no bridge. Unit-tested with a plain g++ (build_cursor_ray_test.bat).
// =============================================================================

#pragma once

#include <cmath>

namespace swfoc_overlay
{
    // |direction.z| at or below this is treated as a ray running parallel to
    // the z=0 ground plane — RayPlaneZ0 reports no hit rather than dividing by
    // a near-zero denominator.
    inline constexpr float kRayParallelEpsilon = 1e-6f;

    // |det| at or below this means the matrix is singular and has no inverse.
    // A real engine view-projection matrix has |det| of order 1; only a
    // degenerate (e.g. all-zero or rank-deficient) matrix trips this.
    inline constexpr float kMatrixSingularEpsilon = 1e-12f;

    // A 3-component vector. World-space positions and ray directions.
    struct Vec3
    {
        float x;
        float y;
        float z;
    };

    // A homogeneous 4-component vector. Used for the row-vector transform
    // through a Mat4 before the perspective divide.
    struct Vec4
    {
        float x;
        float y;
        float z;
        float w;
    };

    // A 4x4 matrix in D3DXMATRIX memory order: ROW-MAJOR, element [row][col]
    // lives at m[row*4 + col]. The engine stores its global view / projection
    // / view*projection matrices in exactly this layout, so the overlay glue
    // can memcpy 64 bytes straight from engine memory into Mat4::m with no
    // transpose. D3D uses ROW vectors: a point transforms as v' = v * M.
    struct Mat4
    {
        float m[16];
    };

    // A point in D3D normalized device coordinates. x and y are each in
    // [-1, +1] for an on-screen pixel; the center of the screen is (0, 0).
    struct NdcPoint
    {
        float x;
        float y;
    };

    // A world-space pick ray. `origin` sits on the camera near plane, and
    // `direction` is a UNIT vector pointing from the near plane toward the
    // far plane (into the scene). `valid` is false when the ray could not be
    // built — a degenerate viewport or a non-invertible view-projection — so
    // callers never consume a garbage ray.
    struct WorldRay
    {
        Vec3 origin;
        Vec3 direction;
        bool valid;
    };

    // The intersection of a WorldRay with the z = 0 ground plane. `hit` is
    // true only when the ray actually crosses the plane AHEAD of its origin
    // (t >= 0) and is not parallel to it. `t` is the ray parameter at the
    // hit (P = origin + t*direction); `x` / `y` are the world coordinates.
    struct PlaneHit
    {
        bool  hit;
        float t;
        float x;
        float y;
    };

    // ---- Vec3 primitives ---------------------------------------------------
    // Small, pure vector math. iter-299's client-side AABB raycast reuses
    // these, so they live in the kernel rather than the test file.

    inline Vec3 Vec3Sub(const Vec3& a, const Vec3& b)
    {
        return Vec3{ a.x - b.x, a.y - b.y, a.z - b.z };
    }

    inline Vec3 Vec3Add(const Vec3& a, const Vec3& b)
    {
        return Vec3{ a.x + b.x, a.y + b.y, a.z + b.z };
    }

    inline Vec3 Vec3Scale(const Vec3& v, float s)
    {
        return Vec3{ v.x * s, v.y * s, v.z * s };
    }

    inline float Vec3Dot(const Vec3& a, const Vec3& b)
    {
        return a.x * b.x + a.y * b.y + a.z * b.z;
    }

    inline Vec3 Vec3Cross(const Vec3& a, const Vec3& b)
    {
        return Vec3{ a.y * b.z - a.z * b.y,
                     a.z * b.x - a.x * b.z,
                     a.x * b.y - a.y * b.x };
    }

    inline float Vec3Length(const Vec3& v)
    {
        return std::sqrt(Vec3Dot(v, v));
    }

    // Unit-length copy of `v`. A zero-length vector has no direction, so it
    // is returned as (0, 0, 0) rather than dividing by zero.
    inline Vec3 Vec3Normalize(const Vec3& v)
    {
        const float len = Vec3Length(v);
        if (len <= 0.0f)
        {
            return Vec3{ 0.0f, 0.0f, 0.0f };
        }
        const float inv = 1.0f / len;
        return Vec3{ v.x * inv, v.y * inv, v.z * inv };
    }

    // ---- Mat4 primitives --------------------------------------------------

    // The 4x4 identity matrix.
    inline Mat4 Mat4Identity()
    {
        Mat4 r{};
        r.m[0]  = 1.0f;
        r.m[5]  = 1.0f;
        r.m[10] = 1.0f;
        r.m[15] = 1.0f;
        return r;
    }

    // Row-major matrix product a * b. With D3D row vectors, applying view
    // then projection is v * view * proj, so the engine's view-projection
    // matrix is Mat4Multiply(view, proj) — exactly what its accessor at RVA
    // 0x17F810 computes into the cache at 0xA6F49C.
    inline Mat4 Mat4Multiply(const Mat4& a, const Mat4& b)
    {
        Mat4 r{};
        for (int i = 0; i < 4; ++i)
        {
            for (int j = 0; j < 4; ++j)
            {
                float s = 0.0f;
                for (int k = 0; k < 4; ++k)
                {
                    s += a.m[i * 4 + k] * b.m[k * 4 + j];
                }
                r.m[i * 4 + j] = s;
            }
        }
        return r;
    }

    // Transform a homogeneous row vector: result = v * m. This is the D3D
    // convention (row vectors on the left), so it matches how the engine's
    // shaders consume these very matrices.
    inline Vec4 Mat4TransformRow(const Vec4& v, const Mat4& m)
    {
        return Vec4{
            v.x * m.m[0] + v.y * m.m[4] + v.z * m.m[8]  + v.w * m.m[12],
            v.x * m.m[1] + v.y * m.m[5] + v.z * m.m[9]  + v.w * m.m[13],
            v.x * m.m[2] + v.y * m.m[6] + v.z * m.m[10] + v.w * m.m[14],
            v.x * m.m[3] + v.y * m.m[7] + v.z * m.m[11] + v.w * m.m[15]
        };
    }

    // General 4x4 matrix inverse via the cofactor / adjugate method (the
    // canonical MESA gluInvertMatrix expansion). Returns false — leaving
    // `out` untouched — when the matrix is singular (|det| <= the singular
    // epsilon). The cofactor algebra is layout-agnostic: fed a row-major
    // matrix it yields the row-major inverse, because inverse(M^T) is
    // transpose(inverse(M)) and the two transposes cancel.
    inline bool Mat4Inverse(const Mat4& in, Mat4& out)
    {
        const float* m = in.m;
        float inv[16];

        inv[0]  =  m[5] * m[10] * m[15] - m[5] * m[11] * m[14]
                 - m[9] * m[6]  * m[15] + m[9] * m[7]  * m[14]
                 + m[13] * m[6] * m[11] - m[13] * m[7] * m[10];

        inv[4]  = -m[4] * m[10] * m[15] + m[4] * m[11] * m[14]
                 + m[8] * m[6]  * m[15] - m[8] * m[7]  * m[14]
                 - m[12] * m[6] * m[11] + m[12] * m[7] * m[10];

        inv[8]  =  m[4] * m[9]  * m[15] - m[4] * m[11] * m[13]
                 - m[8] * m[5]  * m[15] + m[8] * m[7]  * m[13]
                 + m[12] * m[5] * m[11] - m[12] * m[7] * m[9];

        inv[12] = -m[4] * m[9]  * m[14] + m[4] * m[10] * m[13]
                 + m[8] * m[5]  * m[14] - m[8] * m[6]  * m[13]
                 - m[12] * m[5] * m[10] + m[12] * m[6] * m[9];

        inv[1]  = -m[1] * m[10] * m[15] + m[1] * m[11] * m[14]
                 + m[9] * m[2]  * m[15] - m[9] * m[3]  * m[14]
                 - m[13] * m[2] * m[11] + m[13] * m[3] * m[10];

        inv[5]  =  m[0] * m[10] * m[15] - m[0] * m[11] * m[14]
                 - m[8] * m[2]  * m[15] + m[8] * m[3]  * m[14]
                 + m[12] * m[2] * m[11] - m[12] * m[3] * m[10];

        inv[9]  = -m[0] * m[9]  * m[15] + m[0] * m[11] * m[13]
                 + m[8] * m[1]  * m[15] - m[8] * m[3]  * m[13]
                 - m[12] * m[1] * m[11] + m[12] * m[3] * m[9];

        inv[13] =  m[0] * m[9]  * m[14] - m[0] * m[10] * m[13]
                 - m[8] * m[1]  * m[14] + m[8] * m[2]  * m[13]
                 + m[12] * m[1] * m[10] - m[12] * m[2] * m[9];

        inv[2]  =  m[1] * m[6]  * m[15] - m[1] * m[7]  * m[14]
                 - m[5] * m[2]  * m[15] + m[5] * m[3]  * m[14]
                 + m[13] * m[2] * m[7]  - m[13] * m[3] * m[6];

        inv[6]  = -m[0] * m[6]  * m[15] + m[0] * m[7]  * m[14]
                 + m[4] * m[2]  * m[15] - m[4] * m[3]  * m[14]
                 - m[12] * m[2] * m[7]  + m[12] * m[3] * m[6];

        inv[10] =  m[0] * m[5]  * m[15] - m[0] * m[7]  * m[13]
                 - m[4] * m[1]  * m[15] + m[4] * m[3]  * m[13]
                 + m[12] * m[1] * m[7]  - m[12] * m[3] * m[5];

        inv[14] = -m[0] * m[5]  * m[14] + m[0] * m[6]  * m[13]
                 + m[4] * m[1]  * m[14] - m[4] * m[2]  * m[13]
                 - m[12] * m[1] * m[6]  + m[12] * m[2] * m[5];

        inv[3]  = -m[1] * m[6]  * m[11] + m[1] * m[7]  * m[10]
                 + m[5] * m[2]  * m[11] - m[5] * m[3]  * m[10]
                 - m[9] * m[2]  * m[7]  + m[9] * m[3]  * m[6];

        inv[7]  =  m[0] * m[6]  * m[11] - m[0] * m[7]  * m[10]
                 - m[4] * m[2]  * m[11] + m[4] * m[3]  * m[10]
                 + m[8] * m[2]  * m[7]  - m[8] * m[3]  * m[6];

        inv[11] = -m[0] * m[5]  * m[11] + m[0] * m[7]  * m[9]
                 + m[4] * m[1]  * m[11] - m[4] * m[3]  * m[9]
                 - m[8] * m[1]  * m[7]  + m[8] * m[3]  * m[5];

        inv[15] =  m[0] * m[5]  * m[10] - m[0] * m[6]  * m[9]
                 - m[4] * m[1]  * m[10] + m[4] * m[2]  * m[9]
                 + m[8] * m[1]  * m[6]  - m[8] * m[2]  * m[5];

        float det = m[0] * inv[0] + m[1] * inv[4]
                  + m[2] * inv[8] + m[3] * inv[12];

        if (std::fabs(det) <= kMatrixSingularEpsilon)
        {
            return false;
        }

        const float idet = 1.0f / det;
        for (int i = 0; i < 16; ++i)
        {
            out.m[i] = inv[i] * idet;
        }
        return true;
    }

    // ---- Cursor -> world conversion ---------------------------------------

    // Fold a screen pixel into D3D normalized device coordinates. The screen
    // origin is top-left with Y growing DOWN; NDC is centered with Y growing
    // UP — hence the Y axis is flipped. Caller must pass a non-degenerate
    // viewport (vw > 0, vh > 0); CursorRay guards that before calling here.
    inline NdcPoint ScreenToNdc(float sx, float sy, float vw, float vh)
    {
        return NdcPoint{ 2.0f * sx / vw - 1.0f,
                         1.0f - 2.0f * sy / vh };
    }

    // Build a world-space pick ray from a cursor pixel.
    //
    // `sx` / `sy`  : cursor position in screen pixels (top-left origin).
    // `vw` / `vh`  : viewport size in pixels (the host window client area).
    // `viewProj`   : the engine's global view*projection matrix — read
    //                directly from RVA 0xA6F49C, or composed from the view
    //                (0xA6EEE4) and projection (0xA6EF24) matrices.
    //
    // The cursor is unprojected at the D3D near depth (z_ndc = 0) and the far
    // depth (z_ndc = 1) by transforming both clip points through the inverse
    // of `viewProj` and applying the perspective divide. The near point is
    // the ray origin; the normalized near->far vector is the ray direction.
    //
    // `valid` is false — and origin/direction are zeroed — when the viewport
    // is degenerate, `viewProj` is singular, or the near and far unprojected
    // points coincide. The handedness of the projection does not matter here:
    // inverting whatever matrix the engine actually built recovers correct
    // world coordinates for a right-handed or left-handed pipeline alike.
    inline WorldRay CursorRay(float sx, float sy, float vw, float vh,
                              const Mat4& viewProj)
    {
        WorldRay ray{};
        ray.origin    = Vec3{ 0.0f, 0.0f, 0.0f };
        ray.direction = Vec3{ 0.0f, 0.0f, 0.0f };
        ray.valid     = false;

        if (vw <= 0.0f || vh <= 0.0f)
        {
            return ray;  // degenerate viewport
        }

        Mat4 invVP{};
        if (!Mat4Inverse(viewProj, invVP))
        {
            return ray;  // singular view-projection matrix
        }

        const NdcPoint ndc = ScreenToNdc(sx, sy, vw, vh);

        // D3D clip-space depth runs [0, 1]: z_ndc = 0 is the near plane,
        // z_ndc = 1 the far plane.
        const Vec4 nearH =
            Mat4TransformRow(Vec4{ ndc.x, ndc.y, 0.0f, 1.0f }, invVP);
        const Vec4 farH =
            Mat4TransformRow(Vec4{ ndc.x, ndc.y, 1.0f, 1.0f }, invVP);

        if (nearH.w == 0.0f || farH.w == 0.0f)
        {
            return ray;  // unprojected point at infinity
        }

        const Vec3 nearW{ nearH.x / nearH.w,
                          nearH.y / nearH.w,
                          nearH.z / nearH.w };
        const Vec3 farW{ farH.x / farH.w,
                         farH.y / farH.w,
                         farH.z / farH.w };

        const Vec3 dir = Vec3Sub(farW, nearW);
        if (Vec3Length(dir) <= 0.0f)
        {
            return ray;  // near and far coincide — no ray
        }

        ray.origin    = nearW;
        ray.direction = Vec3Normalize(dir);
        ray.valid     = true;
        return ray;
    }

    // Convenience overload matching the spec's stated inputs (cursor +
    // viewport + view matrix + projection matrix). Composes the
    // view-projection matrix and delegates to the primary overload.
    inline WorldRay CursorRay(float sx, float sy, float vw, float vh,
                              const Mat4& view, const Mat4& proj)
    {
        return CursorRay(sx, sy, vw, vh, Mat4Multiply(view, proj));
    }

    // Intersect a world-space pick ray with the z = 0 ground plane — the
    // Phase 4/5 "2D Z=0 plane interim" (spec line 32 / 72). This is the same
    // plane Phase 4 drag-drop spawning resolves onto, so a cursor pick and a
    // drag-drop drop land in one shared world coordinate system.
    //
    // `hit` is true only when the ray is not parallel to the plane AND
    // crosses it ahead of the origin (t >= 0 — the ground is in front of the
    // camera near plane). For a `hit`, `x` / `y` are the world coordinates;
    // the world Z is 0 by construction.
    inline PlaneHit RayPlaneZ0(const WorldRay& ray)
    {
        PlaneHit h{};
        h.hit = false;
        h.t   = 0.0f;
        h.x   = 0.0f;
        h.y   = 0.0f;

        if (!ray.valid)
        {
            return h;
        }

        const float dz = ray.direction.z;
        if (std::fabs(dz) <= kRayParallelEpsilon)
        {
            return h;  // ray runs parallel to the ground plane
        }

        const float t = -ray.origin.z / dz;
        if (t < 0.0f)
        {
            return h;  // the plane is behind the camera
        }

        h.t   = t;
        h.x   = ray.origin.x + t * ray.direction.x;
        h.y   = ray.origin.y + t * ray.direction.y;
        h.hit = true;
        return h;
    }
}
