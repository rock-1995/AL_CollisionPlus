using System.Numerics;

namespace ALCollisionPlus;

// One support plane for a sphere or tapered capsule, local to this receiver.
// The previous completed pose chooses the approach side; animation resets must
// not turn a front contact into a back contact during the next spring solve.
internal static class ContactGeometry
{
    internal readonly record struct Shape(Vector3 A, Vector3 B, float RadiusA, float RadiusB)
    {
        internal Vector3 Closest(Vector3 p, out float radius)
        {
            var axis = B - A;
            float length = axis.LengthSquared();
            float t = length > 0 ? Math.Clamp(Vector3.Dot(p - A, axis) / length, 0, 1) : 0;
            radius = RadiusA + (RadiusB - RadiusA) * t;
            return A + axis * t;
        }
    }

    internal static bool Plane(in Shape oldShape, in Shape shape, Vector3 previous, Vector3 current,
        float radius, ref Vector3 lastNormal, out Vector3 point, out Vector3 normal)
    {
        var oldCenter = oldShape.Closest(previous, out float oldRadius);
        var center = shape.Closest(current, out float obstacleRadius);
        var before = previous - oldCenter;
        var now = current - center;
        float reach = radius + MathF.Max(oldRadius, obstacleRadius);
        var travel = now - before;
        float travel2 = travel.LengthSquared();
        float t = travel2 > 0 ? Math.Clamp(-Vector3.Dot(before, travel) / travel2, 0, 1) : 0;
        bool touching = (before + travel * t).LengthSquared() <= reach * reach;
        point = normal = default;
        // Remember the entry side across a transit, but do not keep an infinite
        // support plane behind a finite obstacle. Once the animated receiver is
        // completely beyond the far surface, remember the side without pushing.
        // A return into that volume still resolves toward the original entry.
        if (lastNormal.LengthSquared() == 0)
        {
            if (!touching) return false;
            if (before.LengthSquared() > 1e-12f) lastNormal = Vector3.Normalize(before);
            else
            {
                if (now.LengthSquared() <= 1e-12f) return false;
                lastNormal = Vector3.Normalize(now);
            }
        }
        // Apply the same finite bounds on the first frame and subsequent frames.
        // A swept path may intersect while its endpoint has already slid clear.
        // Accepting that contact for one frame repeatedly acquires/releases it
        // when another obstacle holds the completed pose across the old sweep.
        float signed = Vector3.Dot(now, lastNormal);
        var tangent = now - lastNormal * signed;
        if (signed > reach || tangent.LengthSquared() > reach * reach)
        {
            lastNormal = default;
            return false;
        }
        if (signed < 0 && now.LengthSquared() > reach * reach) return false;
        // Sliding normals must not overwrite the remembered entry hemisphere.
        normal = signed > 0 && now.LengthSquared() > 1e-12f ? Vector3.Normalize(now) : lastNormal;
        point = center + normal * obstacleRadius;
        return true;
    }
}
