using Character;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MagicaCloth2;
using UnityEngine;
using NMatrix = System.Numerics.Matrix4x4;
using NVector = System.Numerics.Vector3;

namespace ALCollisionPlus;

// Supplies simple geometry to the game's Magica solver. No collision query,
// projection, mesh cooking, or breast-bone position write happens here.
internal sealed class NativeBodyShape : IDisposable
{
    internal static Action<SkinnedCollisionHelper> RefreshInput;

    private sealed class Input
    {
        internal SkinnedCollisionHelper Helper;
        internal SkinnedMeshRenderer Renderer;
        internal Mesh Source;
        internal NVector[] World;
        internal float[] Sums;
        internal int[] AbdomenPoints;
    }

    private readonly Human human;
    private readonly List<Input> inputs = new();
    private readonly ColliderComponent[] colliders = new ColliderComponent[NativeBellyFit.SphereCount];
    private readonly MagicaSphereCollider[] spheres = new MagicaSphereCollider[NativeBellyFit.SphereCount];
    private readonly float[] sizes = new float[NativeBellyFit.SphereCount];
    private readonly NVector[] centers = new NVector[NativeBellyFit.SphereCount];
    private readonly float[] radii = new float[NativeBellyFit.SphereCount];
    private NVector[] localPoints = Array.Empty<NVector>();
    private Transform frameBone;
    private NMatrix frameBind;
    private GameObject hitRoot, actorRoot, proxyRoot;
    private bool disposed;
    private float nextDiscover;
    private float localHeight;

    internal bool Ready { get; private set; }
    internal float Height { get; private set; } = 16f;
    internal IReadOnlyList<ColliderComponent> BellyColliders => colliders;

    internal NativeBodyShape(Human human)
    {
        this.human = human ?? throw new ArgumentNullException(nameof(human));
        Discover();
        try { CreateProxies(); Update(); }
        catch { Dispose(); throw; }
    }

    private static NVector Vector(Vector3 v) => new(v.x, v.y, v.z);
    private static Vector3 Vector(NVector v) => new(v.X, v.Y, v.Z);
    private static NMatrix Matrix(Matrix4x4 m) => new(m.m00, m.m10, m.m20, m.m30, m.m01, m.m11, m.m21, m.m31, m.m02, m.m12, m.m22, m.m32, m.m03, m.m13, m.m23, m.m33);
    private static bool IsAbdomen(string name) => name.StartsWith("o_hit_hara", StringComparison.OrdinalIgnoreCase) || name.StartsWith("o_hit_belly", StringComparison.OrdinalIgnoreCase);

    private void CreateProxies()
    {
        proxyRoot = new GameObject("ALCollisionPlus_NativeAbdomen");
        proxyRoot.SetActive(false);
        // Independent world transform avoids Magica's nonuniform parent-scale
        // ambiguity. Runtime ownership remains this actor's and Dispose removes it.
        UnityEngine.Object.DontDestroyOnLoad(proxyRoot);
        for (int i = 0; i < spheres.Length; i++)
        {
            var go = new GameObject("BellySphere" + i);
            go.transform.SetParent(proxyRoot.transform, false);
            spheres[i] = go.AddComponent<MagicaSphereCollider>();
            spheres[i].center = Vector3.zero;
            colliders[i] = spheres[i];
        }
    }

    private void Discover()
    {
        inputs.Clear(); frameBone = null; localPoints = Array.Empty<NVector>();
        actorRoot = human.GameObject; hitRoot = human.Body?.objHitBody;
        nextDiscover = Time.unscaledTime + 1;
        if (actorRoot == null || human.Transform == null) return;
        var seen = new HashSet<int>();
        float low = float.PositiveInfinity, high = float.NegativeInfinity;
        var inverseRoot = Matrix(human.Transform.worldToLocalMatrix);
        foreach (var root in new[] { hitRoot, actorRoot })
        {
            if (root == null) continue;
            foreach (var helper in root.GetComponentsInChildren<SkinnedCollisionHelper>(true))
            {
                if (helper == null || !seen.Add(helper.GetInstanceID())) continue;
                var renderer = helper._skinnedMeshRenderer;
                var mesh = renderer?.sharedMesh;
                if (renderer == null || mesh == null || !mesh.isReadable) continue;
                string name = renderer.name ?? "";
                if (!name.StartsWith("o_hit_", StringComparison.OrdinalIgnoreCase)) continue;
                var toRoot = Matrix(renderer.transform.localToWorldMatrix) * inverseRoot;
                foreach (var point in mesh.vertices)
                {
                    var p = NVector.Transform(Vector(point), toRoot);
                    if (!NativeBellyFit.Finite(p)) continue;
                    low = MathF.Min(low, p.Y); high = MathF.Max(high, p.Y);
                }
                if (!IsAbdomen(name)) continue;
                int count = mesh.vertexCount;
                if (count == 0) continue;
                var abdomen = AbdomenPoints(renderer, mesh, count);
                if (abdomen.Length == 0) continue;
                inputs.Add(new Input { Helper = helper, Renderer = renderer, Source = mesh, World = new NVector[count], Sums = new float[count], AbdomenPoints = abdomen });
                if (frameBone == null) FindFrame(renderer, mesh);
            }
        }
        if (float.IsFinite(high - low) && high > low) localHeight = high - low;
        localPoints = new NVector[inputs.Sum(input => input.AbdomenPoints.Length)];
    }

    private static int[] AbdomenPoints(SkinnedMeshRenderer renderer, Mesh mesh, int count)
    {
        var bones = renderer.bones;
        var weights = mesh.boneWeights;
        if (weights.Length != count) return Array.Empty<int>();
        var breast = new bool[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            string name = bones[i]?.name ?? "";
            breast[i] = name.Contains("bust", StringComparison.OrdinalIgnoreCase) || name.Contains("mune", StringComparison.OrdinalIgnoreCase);
        }
        bool MovesWithBreast(int bone, float weight) => weight > 0 && (uint)bone < breast.Length && breast[bone];
        var points = new List<int>(count);
        for (int i = 0; i < count; i++)
        {
            var weight = weights[i];
            // hara_f includes the breast/abdomen seam. Its breast-driven vertices
            // cannot define an obstacle against the same breast without feedback.
            if (MovesWithBreast(weight.boneIndex0, weight.weight0) || MovesWithBreast(weight.boneIndex1, weight.weight1) ||
                MovesWithBreast(weight.boneIndex2, weight.weight2) || MovesWithBreast(weight.boneIndex3, weight.weight3)) continue;
            points.Add(i);
        }
        return points.ToArray();
    }

    private void FindFrame(SkinnedMeshRenderer renderer, Mesh mesh)
    {
        var bones = renderer.bones;
        var binds = mesh.bindposes;
        foreach (string wanted in new[] { "cf_j_waist01", "cf_s_waist01", "cf_j_hips01", "cm_j_waist01", "cm_s_waist01", "cm_j_hips01" })
            for (int i = 0; i < bones.Length && i < binds.Length; i++)
                if (bones[i] != null && string.Equals(bones[i].name, wanted, StringComparison.OrdinalIgnoreCase))
                { frameBone = bones[i]; frameBind = Matrix(binds[i]); return; }
    }

    private bool ReadInput(Input input)
    {
        var helper = input.Helper;
        if (helper == null || !helper._isInit || input.Renderer == null || input.Renderer.sharedMesh != input.Source) return false;
        // The pregnancy bridge refreshes only its existing low-poly input when
        // hit-body GOs are inactive; it does not force liquid collision/cooking.
        RefreshInput?.Invoke(helper);
        var groups = helper._nodeWeights;
        if (groups == null || helper._newVert == null || helper._newVert.Length != input.World.Length) return false;
        Array.Clear(input.World, 0, input.World.Length); Array.Clear(input.Sums, 0, input.Sums.Length);
        for (int g = 0; g < groups.Length; g++)
        {
            var group = groups[g];
            var bone = group?.Transform;
            var weights = group?.Weights;
            if (bone == null || weights == null) return false;
            var matrix = Matrix(bone.localToWorldMatrix);
            int count = weights._size;
            var items = weights._items as Il2CppStructArray<SkinnedCollisionHelper.VertexWeight>;
            if (items == null) return false;
            try
            {
                var span = items.AsSpan();
                if (count < 0 || count > span.Length) return false;
                for (int j = 0; j < count; j++)
                {
                    var weight = span[j]; int vertex = weight.Index;
                    if ((uint)vertex >= input.World.Length || !float.IsFinite(weight.Weight) || weight.Weight < 0) return false;
                    input.World[vertex] += NVector.Transform(Vector(weight.LocalPosition), matrix) * weight.Weight;
                    input.Sums[vertex] += weight.Weight;
                }
            }
            finally { GC.KeepAlive(items); }
        }
        for (int i = 0; i < input.World.Length; i++)
            if (!NativeBellyFit.Finite(input.World[i]) || MathF.Abs(input.Sums[i] - 1) > .01f) return false;
        return true;
    }

    internal void RefreshHeight()
    {
        if (disposed || human.Transform == null) return;
        float height = localHeight * MathF.Abs(human.Transform.lossyScale.y);
        if (float.IsFinite(height) && height > 0) Height = height;
    }

    internal void Update()
    {
        Ready = false;
        if (disposed) return;
        try
        {
            if (human.Disposed || human.IsReloading || !human.IsActiveAndEnabled || !human.HiPoly || human.Transform == null) return;
            bool changed = actorRoot != human.GameObject || hitRoot != human.Body?.objHitBody ||
                inputs.Any(input => input.Helper == null || input.Renderer == null || input.Renderer.sharedMesh != input.Source);
            if (changed || ((inputs.Count == 0 || frameBone == null) && Time.unscaledTime >= nextDiscover)) Discover();
            RefreshHeight();
            if (inputs.Count == 0 || frameBone == null || proxyRoot == null) return;
            if (!NativeBellyFit.TryCreateFrame(frameBind * Matrix(frameBone.localToWorldMatrix), out var frame, out var inverse)) return;
            int index = 0;
            foreach (var input in inputs)
            {
                if (!ReadInput(input)) return;
                foreach (int vertex in input.AbdomenPoints) localPoints[index++] = NVector.Transform(input.World[vertex], inverse);
            }
            if (!NativeBellyFit.TryFit(localPoints, centers, radii)) return;
            for (int i = 0; i < spheres.Length; i++)
            {
                spheres[i].transform.position = Vector(NVector.Transform(centers[i], frame));
                if (sizes[i] != radii[i])
                {
                    spheres[i].SetSize(radii[i]); spheres[i].UpdateParameters(); sizes[i] = radii[i];
                }
            }
            Ready = true;
        }
        finally { if (proxyRoot != null && proxyRoot.activeSelf != Ready) proxyRoot.SetActive(Ready); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; Ready = false;
        if (proxyRoot != null) { proxyRoot.SetActive(false); UnityEngine.Object.Destroy(proxyRoot); }
        inputs.Clear(); localPoints = Array.Empty<NVector>();
    }
}
