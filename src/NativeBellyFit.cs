using System.Numerics;

namespace ALCollisionPlus;

// Geometry only: these spheres are inexpensive inner abdomen proxies, not a
// replacement triangle surface. Coordinates already contain the actor's scale.
internal static class NativeBellyFit
{
    internal const int SphereCount = 5;

    internal static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);

    internal static bool TryCreateFrame(Matrix4x4 skinnedFrame, out Matrix4x4 frame, out Matrix4x4 inverse)
    {
        frame = inverse = Matrix4x4.Identity;
        var origin = new Vector3(skinnedFrame.M41, skinnedFrame.M42, skinnedFrame.M43);
        var sourceX = Vector3.TransformNormal(Vector3.UnitX, skinnedFrame);
        var y = Vector3.TransformNormal(Vector3.UnitY, skinnedFrame);
        var z = Vector3.TransformNormal(Vector3.UnitZ, skinnedFrame);
        if (!Finite(origin) || !Finite(sourceX) || !Finite(y) || !Finite(z) || y.LengthSquared() <= 1e-20f) return false;
        y = Vector3.Normalize(y);
        z -= y * Vector3.Dot(z, y);
        if (!Finite(z) || z.LengthSquared() <= 1e-20f) return false;
        z = Vector3.Normalize(z);
        var x = Vector3.Cross(y, z);
        float handedness = Vector3.Dot(sourceX, x);
        if (!float.IsFinite(handedness) || MathF.Abs(handedness) <= 1e-20f) return false;
        frame = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, origin.X, origin.Y, origin.Z, 1);
        return Matrix4x4.Invert(frame, out inverse);
    }

    internal static bool TryFit(ReadOnlySpan<Vector3> points, Span<Vector3> centers, Span<float> radii)
    {
        if (points.Length < 4 || centers.Length < SphereCount || radii.Length < SphereCount) return false;
        var low = new Vector3(float.PositiveInfinity);
        var high = new Vector3(float.NegativeInfinity);
        foreach (var p in points)
        {
            if (!Finite(p)) return false;
            low = Vector3.Min(low, p); high = Vector3.Max(high, p);
        }
        var extent = high - low;
        if (!Finite(extent) || extent.X <= 0 || extent.Y <= 0 || extent.Z <= 0) return false;
        // Work in the anatomical Y axis, including when the character lies down.
        // A broad band avoids unstable nearest-vertex changes on these small meshes.
        float spacing = extent.Y / SphereCount;
        Span<Vector3> pendingCenters = stackalloc Vector3[SphereCount];
        Span<float> pendingRadii = stackalloc float[SphereCount];
        for (int sphere = 0; sphere < SphereCount; sphere++)
        {
            float y = low.Y + spacing * (sphere + .5f);
            float nearest = float.PositiveInfinity;
            foreach (var p in points) nearest = MathF.Min(nearest, MathF.Abs(p.Y - y));
            // Include a ring lying exactly on a band edge despite roundoff from
            // world-to-anatomical-frame rotation; the same pose must fit alike.
            float band = MathF.Max(spacing, nearest) + extent.Y * 1e-6f;
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
            foreach (var p in points)
            {
                if (MathF.Abs(p.Y - y) > band) continue;
                minX = MathF.Min(minX, p.X); maxX = MathF.Max(maxX, p.X);
                minZ = MathF.Min(minZ, p.Z); maxZ = MathF.Max(maxZ, p.Z);
            }
            // Keep each sphere within the sampled width/depth and overall height.
            // Align its front with the sampled front instead of inflating the
            // whole torso to the radius needed by its widest dimension.
            float radius = MathF.Min(MathF.Min((maxX - minX) * .5f, (maxZ - minZ) * .5f), MathF.Min(y - low.Y, high.Y - y));
            var center = new Vector3((minX + maxX) * .5f, y, maxZ - radius);
            if (!Finite(center) || !float.IsFinite(radius) || radius <= 0) return false;
            pendingCenters[sphere] = center; pendingRadii[sphere] = radius;
        }
        pendingCenters.CopyTo(centers); pendingRadii.CopyTo(radii);
        return true;
    }
}
