using System.Numerics;

namespace ALCollisionPlus;

internal static class BreastSurfaceFit
{
    // The largest sphere at this EXISTING native particle center that touches
    // the sampled body surface. No arbitrary height/bust-slider ratio.
    internal static float Radius(Vector3 center, Vector3[] vertices, int[] triangles)
        => MathF.Max(0, Clearance(center, vertices, triangles));

    private static float Clearance(Vector3 center, Vector3[] vertices, int[] triangles)
    {
        float squared = float.PositiveInfinity, signed = 0;
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            var a = vertices[triangles[i]]; var b = vertices[triangles[i + 1]]; var c = vertices[triangles[i + 2]];
            var delta = center - Closest(center, a, b, c);
            float distance = delta.LengthSquared();
            if (distance < squared)
            {
                squared = distance;
                signed = Vector3.Dot(delta, Vector3.Cross(b - a, c - a));
            }
        }
        // Some extreme shapes put the native tip particle beyond the front
        // surface already. No positive sphere fits there: retain point contact.
        return float.IsFinite(squared) ? MathF.Sqrt(squared) * (signed <= 0 ? 1 : -1) : 0;
    }

    internal readonly record struct Sphere(Vector3 Center, float Radius);

    // Bounded search for an inscribed breast-body sphere. The attachment plane
    // closes the sampled chest's back; the skin-weight region bounds the
    // search so a breast sphere cannot migrate into the torso or other breast.
    internal static Sphere Fit(Vector3[] vertices, int[] triangles, int[] region,
        Vector3 root, Vector3 seed, Vector3 tip, Vector3 x, Vector3 y, Vector3 z,
        Func<Vector3, float> clearance = null, Func<Vector3, float, float> clearanceAbove = null)
    {
        clearance ??= p => Clearance(p, vertices, triangles);
        if (region.Length == 0) return new(seed, MathF.Max(0, clearance(seed)));
        Vector3 Local(Vector3 p) { var d=p-root;return new(Vector3.Dot(d,x),Vector3.Dot(d,y),Vector3.Dot(d,z)); }
        Vector3 World(Vector3 p)=>root+x*p.X+y*p.Y+z*p.Z;
        var low=new Vector3(float.PositiveInfinity);var high=new Vector3(float.NegativeInfinity);
        foreach(int i in region) {var p=Local(vertices[i]);low=Vector3.Min(low,p);high=Vector3.Max(high,p);}
        var axis=Vector3.Normalize(tip-root);
        float Score(Vector3 p, float threshold)
        {
            var world=World(p);
            float attachment = Vector3.Dot(world-root,axis);
            if (attachment <= threshold) return attachment;
            return MathF.Min(clearanceAbove != null ? clearanceAbove(world,threshold) : clearance(world), attachment);
        }
        var center=Vector3.Clamp(Local(seed),low,high);float best=Score(center,float.NegativeInfinity);
        var boxCenter=(low+high)*.5f;float boxScore=Score(boxCenter,best);
        if(boxScore>best) {center=boxCenter;best=boxScore;}
        var step=(high-low)*.25f;
        for(int level=0;level<6;level++,step*=.5f)
            for(int pass=0;pass<3;pass++)
            {
                var chosen=center;float score=best;
                for(int direction=0;direction<3;direction++)
                {
                    var d=direction==0?new Vector3(step.X,0,0):direction==1?new Vector3(0,step.Y,0):new Vector3(0,0,step.Z);
                    for(int sign=-1;sign<=1;sign+=2)
                    {
                        var candidate=Vector3.Clamp(center+d*sign,low,high);float r=Score(candidate,score);
                        if(r>score) {chosen=candidate;score=r;}
                    }
                }
                if(chosen==center)break;
                center=chosen;best=score;
            }
        return new(World(center),MathF.Max(0,best));
    }

    internal static Vector3 Closest(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a; var ac = c - a;
        if (Vector3.Cross(ab, ac).LengthSquared() < 1e-16f)
        {
            var x = Segment(p, a, b); var y = Segment(p, b, c); var z = Segment(p, c, a);
            var q = Vector3.DistanceSquared(p, x) < Vector3.DistanceSquared(p, y) ? x : y;
            return Vector3.DistanceSquared(p, q) < Vector3.DistanceSquared(p, z) ? q : z;
        }
        var ap = p - a; float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return a;
        var bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return b;
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));
        var cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return c;
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));
        float va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
            return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
        float inverse = 1 / (va + vb + vc);
        return a + ab * (vb * inverse) + ac * (vc * inverse);
    }

    private static Vector3 Segment(Vector3 p, Vector3 a, Vector3 b)
    {
        var v = b - a; float length = v.LengthSquared();
        return length > 0 ? a + v * Math.Clamp(Vector3.Dot(p - a, v) / length, 0, 1) : a;
    }

    // Magica samples the depth curve at 16 locations. A straight line preserves
    // both measured values even when the middle bone falls between samples.
    internal static float CurveStart(float middle, float tip, float depth) =>
        (middle - tip * depth) / (1 - depth);
}
