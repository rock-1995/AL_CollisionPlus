using System.Numerics;
using L=ALCollisionPlus.Legacy018;
namespace ALCollisionPlus;

internal static class RegressionAdapter
{
    internal static Vector3 OldAbdomen(ContactProbe[] probes,ContactTarget[] targets,ContactSettings s)
    {
        // Reconstruct the old tree from explicit triangle topology supplied by
        // the test, so old/new queries use exactly the same geometry.
        var legacyTargets=targets.Select(t=>new L.ContactTarget(Trees[t.Tree],t.Excluded,t.Group)).ToArray();
        foreach(var target in targets)Trees[target.Tree].Enabled=target.Tree.Enabled;
        var settings=new L.ProjectionSettings(s.Radius,0,s.Limit,0,0,s.Strength,s.Compression,s.Coverage,s.NormalAlignment,s.Transition,s.Capture,s.MinInfluence,s.ConflictTolerance,s.Iterations);
        return L.ContactProjection.SolveParts(probes.Select(p=>new L.ContactProbe(p.Position,p.Influence)).ToArray(),legacyTargets,L.Region.Breast,new(false,false,false,false,true),settings,new long[6]).Chest;
    }
    internal static readonly Dictionary<SurfaceTree,L.SurfaceTree> Trees=new();
    internal static void Register(SurfaceTree tree,int[] triangles)
    {
        var old=new L.SurfaceTree(tree.Vertices,triangles,tree.Regions.Select(r=>(L.Region)(int)r).ToArray());
        old.Normals=(Vector3[])tree.Normals.Clone();Trees[tree]=old;
    }
}
