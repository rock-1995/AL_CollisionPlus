using Character;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using NVector = System.Numerics.Vector3;
using NMatrix = System.Numerics.Matrix4x4;

namespace ALCollisionPlus;

// Snapshot the actual body only when a sphere needs fitting. The native solver
// continues to own animation and collision; no liquid helper is required.
internal sealed class NativeBreastSurface : IDisposable
{
    private readonly SkinnedMeshRenderer renderer;
    private readonly Mesh mesh;
    private Mesh snapshot;
    private readonly bool wasVisible;
    private readonly NVector[] world;
    private readonly BreastSurfaceIndex surfaceIndex;
    private readonly record struct SkinWeight(int Bone0, int Bone1, int Bone2, int Bone3,
        float Weight0, float Weight1, float Weight2, float Weight3);
    private readonly SkinWeight[] weights;
    private readonly Transform[] bones;
    private readonly NMatrix[] binds, matrices;
    private readonly int[] usedBones;
    private readonly int[] triangles, sampledVertices, leftRegion, rightRegion;
    internal bool Ready { get; private set; }
    internal bool IsCurrent => renderer != null && renderer.sharedMesh == mesh &&
        wasVisible == (renderer.enabled && renderer.gameObject.activeInHierarchy);
    internal int PrepareCount { get; private set; }

    private static NVector V(Vector3 v) => new(v.x, v.y, v.z);
    private static NMatrix M(Matrix4x4 m) => new(m.m00,m.m10,m.m20,m.m30,m.m01,m.m11,m.m21,m.m31,
        m.m02,m.m12,m.m22,m.m32,m.m03,m.m13,m.m23,m.m33);

    internal static NativeBreastSurface Discover(Human human)
    {
        var candidates = new List<SkinnedMeshRenderer>();
        var registered = human.Body?.BodyRenderers;
        if (registered != null) foreach (var entry in registered)
        {
            var renderer = entry?.Renderer?.TryCast<SkinnedMeshRenderer>();
            if (renderer != null) candidates.Add(renderer);
        }
        if (human.GameObject != null)
            foreach (var renderer in human.GameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if ((renderer.name ?? "").StartsWith("o_body", StringComparison.OrdinalIgnoreCase) &&
                    !candidates.Any(candidate => candidate == renderer)) candidates.Add(renderer);
        // Prefer the body part currently displayed, but hidden skin remains a
        // valid source when clothes hide all chest renderers.
        foreach (var renderer in candidates.OrderByDescending(r => r.enabled && r.gameObject.activeInHierarchy))
        {
            var mesh = renderer.sharedMesh;
            if (mesh == null || !mesh.isReadable) continue;
            var weights = mesh.boneWeights.ToArray();
            if (weights.Length != mesh.vertexCount) continue;
            var sides = renderer.bones.Select(bone =>
            {
                string name = bone?.name ?? "";
                return !name.Contains("bust", StringComparison.OrdinalIgnoreCase) ? 0 :
                    name.EndsWith("_L") ? -1 : name.EndsWith("_R") ? 1 : 0;
            }).ToArray();
            var left = new List<int>(); var right = new List<int>();
            var chest = new bool[weights.Length];
            for (int i = 0; i < weights.Length; i++)
            {
                var w = weights[i]; float l = 0, r = 0;
                void Add(int bone, float weight)
                {
                    if (weight == 0) return;
                    if (sides[bone] < 0) l += weight; else if (sides[bone] > 0) r += weight;
                }
                Add(w.boneIndex0,w.weight0); Add(w.boneIndex1,w.weight1);
                Add(w.boneIndex2,w.weight2); Add(w.boneIndex3,w.weight3);
                chest[i] = l > 0 || r > 0;
                // Preserve the existing sphere search region's majority weight.
                if (l > .5f) left.Add(i); if (r > .5f) right.Add(i);
            }
            if (left.Count == 0 && right.Count == 0) continue;
            var sourceTriangles = mesh.triangles.ToArray();
            var triangles = new List<int>();
            for (int i = 0; i + 2 < sourceTriangles.Length; i += 3)
            {
                int a = sourceTriangles[i], b = sourceTriangles[i + 1], c = sourceTriangles[i + 2];
                // Keep breast-influenced body surface and boundary triangles.
                // Nearby arms must not shrink the fitted sphere.
                if (!chest[a] && !chest[b] && !chest[c]) continue;
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
            }
            return new NativeBreastSurface(renderer, mesh, triangles.ToArray(), left.ToArray(), right.ToArray());
        }
        return null;
    }

    private NativeBreastSurface(SkinnedMeshRenderer renderer, Mesh mesh, int[] triangles, int[] left, int[] right)
    {
        this.renderer = renderer; this.mesh = mesh; this.triangles = triangles;
        wasVisible = renderer.enabled && renderer.gameObject.activeInHierarchy;
        leftRegion = left; rightRegion = right;
        sampledVertices = triangles.Concat(left).Concat(right).Distinct().ToArray();
        world = new NVector[mesh.vertexCount];
        surfaceIndex = new BreastSurfaceIndex(mesh.vertices.Select(V).ToArray(), triangles);
        // Unity's BoneWeight properties cross the IL2CPP boundary. Copy them
        // once so repeated shape fits use only managed values per vertex.
        weights = mesh.boneWeights.Select(w => new SkinWeight(w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3,
            w.weight0,w.weight1,w.weight2,w.weight3)).ToArray();
        bones = renderer.bones.ToArray();
        binds = mesh.bindposes.Select(M).ToArray(); matrices = new NMatrix[bones.Length];
        var used = new HashSet<int>();
        foreach (int i in sampledVertices)
        {
            var w = weights[i];
            if (w.Weight0 > 0) used.Add(w.Bone0); if (w.Weight1 > 0) used.Add(w.Bone1);
            if (w.Weight2 > 0) used.Add(w.Bone2); if (w.Weight3 > 0) used.Add(w.Bone3);
        }
        usedBones = used.ToArray();
    }

    internal string DiagnosticState() =>
        $"source=body, renderer={CollisionTrace.Name(renderer)}, cachedMesh={CollisionTrace.Name(mesh)}, currentMesh={CollisionTrace.Name(renderer?.sharedMesh)}, vertices={world.Length}, sampledVertices={sampledVertices.Length}, triangles={triangles.Length/3}, leftRegion={leftRegion.Length}, rightRegion={rightRegion.Length}, ready={Ready}, prepareCount={PrepareCount}";

    internal bool Prepare(CollisionTrace trace = null, string scope = null)
    {
        Ready = false;
        if (!IsCurrent)
        { trace?.Write("03-SkinningPreparation", scope, "WAIT: body renderer or mesh changed"); return false; }
        string path;
        if (mesh.blendShapeCount == 0)
        {
            path = "body-skinning";
            // Read current mesh positions, including in-place body morphs. Only
            // skin chest samples and the bones they use, not the whole renderer.
            var storage = mesh.vertices;
            foreach (int bone in usedBones)
            {
                if (bones[bone] == null)
                { trace?.Write("03-SkinningPreparation", scope, $"WAIT: body bone[{bone}] missing"); return false; }
                matrices[bone] = binds[bone] * M(bones[bone].localToWorldMatrix);
            }
            try
            {
                var vertices = storage.AsSpan();
                foreach (int i in sampledVertices)
                {
                    var p = V(vertices[i]); var w = weights[i]; var result = NVector.Zero;
                    void Add(int bone, float weight) { if (weight != 0) result += NVector.Transform(p, matrices[bone]) * weight; }
                    Add(w.Bone0,w.Weight0); Add(w.Bone1,w.Weight1);
                    Add(w.Bone2,w.Weight2); Add(w.Bone3,w.Weight3);
                    world[i] = result;
                }
            }
            finally { GC.KeepAlive(storage); }
        }
        else
        {
            path = "body-blendshape-snapshot";
            // Unity applies blendshapes. In AL's Unity 6, true compensates for
            // renderer scale; localToWorld applies it once, verified in-game.
            // The snapshot is native but owned by managed state, so it must
            // survive scene UnloadUnusedAssets until explicitly disposed.
            if (snapshot == null) snapshot = new Mesh { name = "ALCollisionPlus_BodySnapshot", hideFlags = HideFlags.HideAndDontSave };
            renderer.BakeMesh(snapshot, true);
            var storage = snapshot.vertices;
            if (storage.Length != world.Length)
            { trace?.Write("03-SkinningPreparation", scope, "WAIT: body snapshot topology changed"); return false; }
            var toWorld = M(renderer.transform.localToWorldMatrix);
            try
            {
                var vertices = storage.AsSpan();
                foreach (int i in sampledVertices) world[i] = NVector.Transform(V(vertices[i]), toWorld);
            }
            finally { GC.KeepAlive(storage); }
        }
        foreach (int i in sampledVertices)
        {
            if (!NativeBellyFit.Finite(world[i]))
            { trace?.Write("03-SkinningPreparation", scope, $"FAIL body vertex={i}: non-finite position"); return false; }
        }
        surfaceIndex.Refit(world);
        PrepareCount++;
        trace?.Write("03-SkinningPreparation", scope, $"PASS path={path}, vertices={world.Length}, sampledVertices={sampledVertices.Length}, triangles={triangles.Length/3}, prepareCount={PrepareCount}");
        Ready = true; return true;
    }

    internal BreastSurfaceFit.Sphere Fit(Transform root, Transform middle, Transform tip)
    {
        var q = root.rotation;
        return BreastSurfaceFit.Fit(world, triangles, root.name.EndsWith("_L") ? leftRegion : rightRegion,
            V(root.position), V(middle.position), V(tip.position), V(q*Vector3.right), V(q*Vector3.up), V(q*Vector3.forward), surfaceIndex.Clearance, surfaceIndex.ClearanceAbove);
    }

    public void Dispose()
    {
        Ready = false;
        if (snapshot != null) UnityEngine.Object.Destroy(snapshot);
    }
}
