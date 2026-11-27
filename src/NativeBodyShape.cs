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

    internal sealed class Input
    {
        internal SkinnedCollisionHelper Helper;
        internal SkinnedMeshRenderer Renderer;
        internal Mesh Source;
        internal NVector[] World;
        internal float[] Sums;
        internal int[] AbdomenPoints;
        internal GroupCache[] Groups = Array.Empty<GroupCache>();
        internal Il2CppReferenceArray<SkinnedCollisionHelper.WeightList> NativeGroups;
    }
    internal sealed class GroupCache
    {
        internal NMatrix Matrix;
        internal SkinnedCollisionHelper.VertexWeight[] Weights = Array.Empty<SkinnedCollisionHelper.VertexWeight>();
        internal Transform Bone;
        internal Il2CppSystem.Collections.Generic.List<SkinnedCollisionHelper.VertexWeight> NativeWeights;
        internal Il2CppStructArray<SkinnedCollisionHelper.VertexWeight> Items;
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
    private GameObject hitRoot, actorRoot, proxyRoot, breastBodyRoot;
    private bool disposed;
    private float nextDiscover;
    private float nextBreastDiscover;
    private float localHeight;
    private bool fitted;
    internal int FitCount { get; private set; }

    internal bool Ready { get; private set; }
    internal NativeBreastSurface BreastSurface { get; private set; }
    internal float Height { get; private set; } = 16f;
    internal IReadOnlyList<ColliderComponent> BellyColliders => colliders;

    internal NativeBreastSurface GetBreastSurface()
    {
        var bodyRoot = human.Body?._objBody;
        if (BreastSurface != null && (breastBodyRoot != bodyRoot || !BreastSurface.IsCurrent))
        {
            BreastSurface.Dispose(); BreastSurface = null; nextBreastDiscover = 0;
        }
        if (BreastSurface == null && Time.unscaledTime >= nextBreastDiscover)
        {
            breastBodyRoot = bodyRoot;
            BreastSurface = NativeBreastSurface.Discover(human);
            nextBreastDiscover = Time.unscaledTime + 1;
        }
        return BreastSurface;
    }

    internal void TraceInputs(CollisionTrace trace, string scope)
    {
        trace.Read("03-MeshDiscovery", scope, () =>
        {
            var rows = new List<string> { $"cachedBreastSurface={BreastSurface!=null}, cachedBellyInputs={inputs.Count}, bellyFrame={CollisionTrace.Name(frameBone)}, cachedHitRoot={CollisionTrace.Name(hitRoot)}, currentHitRoot={CollisionTrace.Name(human.Body?.objHitBody)}, nextDiscover={CollisionTrace.F(nextDiscover)}" };
            var seen = new HashSet<int>();
            foreach (var root in new[] { human.Body?.objHitBody, human.GameObject })
            {
                if (root == null) continue;
                foreach (var helper in root.GetComponentsInChildren<SkinnedCollisionHelper>(true))
                {
                    if (helper == null || !seen.Add(helper.GetInstanceID())) continue;
                    var cachedRenderer = helper._skinnedMeshRenderer;
                    var attachedRenderer = helper.GetComponent<SkinnedMeshRenderer>();
                    var mesh = cachedRenderer?.sharedMesh;
                    string result = cachedRenderer == null ? "SKIP cached renderer missing" : mesh == null ? "SKIP mesh missing" :
                        !mesh.isReadable ? "SKIP mesh unreadable" : IsAbdomen(cachedRenderer.name??"") ? "abdomen candidate" : "not abdomen; breast uses body renderer";
                    rows.Add($"helper={CollisionTrace.Name(helper)}, init={helper._isInit}, active={helper.gameObject.activeInHierarchy}, cachedRenderer={CollisionTrace.Name(cachedRenderer)}, attachedRenderer={CollisionTrace.Name(attachedRenderer)}, mesh={CollisionTrace.Name(mesh)}, vertices={mesh?.vertexCount??0}, nodes={helper._nodeWeights?.Length??0}, result={result}");
                }
            }
            rows.Add($"liveHelpers={seen.Count}");
            return string.Join("; ", rows);
        });
        if (BreastSurface != null) trace.Read("03-CachedBreastMesh", scope, BreastSurface.DiagnosticState);
    }

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
        inputs.Clear(); frameBone = null; localPoints = Array.Empty<NVector>(); fitted = false;
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

    internal static bool ReadInput(Input input, NMatrix inverse, out bool changed)
    {
        changed = false;
        var helper = input.Helper;
        if (helper == null || !helper._isInit || input.Renderer == null || input.Renderer.sharedMesh != input.Source) return false;
        // The pregnancy bridge refreshes only its existing low-poly input when
        // hit-body GOs are inactive; it does not force liquid collision/cooking.
        RefreshInput?.Invoke(helper);
        var groups = helper._nodeWeights;
        if (groups == null || helper._newVert == null || helper._newVert.Length != input.World.Length) return false;
        if (input.Groups.Length != groups.Length)
        {
            input.Groups = Enumerable.Range(0, groups.Length).Select(_ => new GroupCache()).ToArray();
            changed = true;
        }
        // Keep the native array and its referenced objects alive while reading
        // value-type entries directly. The interop array indexer boxes every
        // WeightList; its two live references can be read without those boxes.
        input.NativeGroups = groups;
        var groupData = NativeWeightGroups.Data(groups);
        for (int g = 0; g < groups.Length; g++)
        {
            var cache = input.Groups[g];
            NativeWeightGroups.Read(groupData, g, ref cache.Bone, ref cache.NativeWeights);
            var bone = cache.Bone;
            var weights = cache.NativeWeights;
            if (bone == null || weights == null) return false;
            var matrix = Matrix(bone.localToWorldMatrix) * inverse;
            // Compare in the anatomical frame, not world space. Walking and
            // turning rigidly must not reskin/refit the same abdomen. The tiny
            // matrix tolerance only absorbs float roundoff; compare against the
            // LAST FIT, so slow deformation accumulates and cannot be lost.
            bool groupChanged = !SameMatrix(cache.Matrix, matrix);
            int count = weights._size;
            var items = NativeWeightGroups.Items(weights, ref cache.Items);
            if (items == null) return false;
            try
            {
                var span = items.AsSpan();
                if (count < 0 || count > span.Length) return false;
                if (cache.Weights.Length != count) { cache.Weights = new SkinnedCollisionHelper.VertexWeight[count]; groupChanged = true; }
                for (int j = 0; j < count; j++)
                {
                    var weight = span[j]; int vertex = weight.Index;
                    if ((uint)vertex >= input.World.Length || !float.IsFinite(weight.Weight) || weight.Weight < 0) return false;
                    var old = cache.Weights[j];
                    var a = old.LocalPosition; var b = weight.LocalPosition;
                    if (!float.IsFinite(b.x) || !float.IsFinite(b.y) || !float.IsFinite(b.z)) return false;
                    // Dynamic carrier inputs also acquire roundoff from a rigid
                    // world/inverse-world round trip. Retain the last fitted
                    // values until a real change accumulates beyond that noise.
                    if (old.Index != vertex || old.Weight != weight.Weight || MathF.Abs(a.x-b.x)>1e-5f || MathF.Abs(a.y-b.y)>1e-5f || MathF.Abs(a.z-b.z)>1e-5f) groupChanged = true;
                }
                if (groupChanged) { span.Slice(0, count).CopyTo(cache.Weights); cache.Matrix = matrix; changed = true; }
            }
            finally { GC.KeepAlive(items); }
        }
        if (!changed) return true;
        Array.Clear(input.World, 0, input.World.Length); Array.Clear(input.Sums, 0, input.Sums.Length);
        foreach (var cache in input.Groups)
            foreach (var weight in cache.Weights)
            {
                input.World[weight.Index] += NVector.Transform(Vector(weight.LocalPosition), cache.Matrix) * weight.Weight;
                input.Sums[weight.Index] += weight.Weight;
            }
        for (int i = 0; i < input.World.Length; i++)
            if (!NativeBellyFit.Finite(input.World[i]) || MathF.Abs(input.Sums[i] - 1) > .01f) return false;
        return true;
    }

    private static bool SameMatrix(NMatrix a, NMatrix b)
    {
        const float e = 0.00001f;
        return MathF.Abs(a.M11-b.M11)<=e && MathF.Abs(a.M12-b.M12)<=e && MathF.Abs(a.M13-b.M13)<=e &&
            MathF.Abs(a.M21-b.M21)<=e && MathF.Abs(a.M22-b.M22)<=e && MathF.Abs(a.M23-b.M23)<=e &&
            MathF.Abs(a.M31-b.M31)<=e && MathF.Abs(a.M32-b.M32)<=e && MathF.Abs(a.M33-b.M33)<=e &&
            MathF.Abs(a.M41-b.M41)<=e && MathF.Abs(a.M42-b.M42)<=e && MathF.Abs(a.M43-b.M43)<=e;
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
            bool changed = actorRoot != human.GameObject || hitRoot != human.Body?.objHitBody;
            if (!changed)
                foreach (var input in inputs)
                    if (input.Helper == null || input.Renderer == null || input.Renderer.sharedMesh != input.Source)
                    { changed = true; break; }
            if (changed || ((inputs.Count == 0 || frameBone == null) && Time.unscaledTime >= nextDiscover)) Discover();
            RefreshHeight();
            if (inputs.Count == 0 || frameBone == null || proxyRoot == null) return;
            if (!NativeBellyFit.TryCreateFrame(frameBind * Matrix(frameBone.localToWorldMatrix), out var frame, out var inverse)) return;
            bool changedShape = !fitted;
            foreach (var input in inputs)
            {
                if (!ReadInput(input, inverse, out bool inputChanged)) { input.Groups = Array.Empty<GroupCache>(); fitted = false; return; }
                changedShape |= inputChanged;
            }
            if (changedShape)
            {
                int index = 0;
                foreach (var input in inputs)
                    foreach (int vertex in input.AbdomenPoints) localPoints[index++] = input.World[vertex];
                if (!NativeBellyFit.TryFit(localPoints, centers, radii)) { fitted = false; return; }
                for (int i = 0; i < spheres.Length; i++)
                {
                    spheres[i].transform.localPosition = Vector(centers[i]);
                    if (sizes[i] != radii[i])
                    {
                        spheres[i].SetSize(radii[i]); spheres[i].UpdateParameters(); sizes[i] = radii[i];
                    }
                }
                fitted = true; FitCount++;
            }
            proxyRoot.transform.SetPositionAndRotation(new Vector3(frame.M41,frame.M42,frame.M43),
                Quaternion.LookRotation(new Vector3(frame.M31,frame.M32,frame.M33),new Vector3(frame.M21,frame.M22,frame.M23)));
            Ready = true;
        }
        finally { if (proxyRoot != null && proxyRoot.activeSelf != Ready) proxyRoot.SetActive(Ready); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; Ready = false;
        BreastSurface?.Dispose(); BreastSurface = null;
        if (proxyRoot != null) { proxyRoot.SetActive(false); UnityEngine.Object.Destroy(proxyRoot); }
        inputs.Clear(); localPoints = Array.Empty<NVector>();
    }
}
