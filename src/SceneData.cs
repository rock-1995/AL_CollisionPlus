using Character;
using MagicaCloth2;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using V = System.Numerics.Vector3;
using M = System.Numerics.Matrix4x4;

namespace ALCollisionPlus;

internal static class Convert
{
    internal static V Vec(Vector3 v) => new(v.x, v.y, v.z);
    internal static Vector3 Vec(V v) => new(v.X, v.Y, v.Z);
    internal static M Matrix(Matrix4x4 m) => new(m.m00,m.m10,m.m20,m.m30,m.m01,m.m11,m.m21,m.m31,m.m02,m.m12,m.m22,m.m32,m.m03,m.m13,m.m23,m.m33);
    internal static int Depth(Transform t) { int d = 0; for (; t != null && d < 128; t = t.parent) d++; return d; }
}

internal sealed class BoneNode
{
    internal int Id, Depth;
    internal Transform Bone = null!;
    internal MagicaCloth Cloth = null!;
    internal Region Region;
    internal readonly List<BodySample> Samples = new();
    internal readonly List<ContactProbe> Probes = new();
    internal readonly List<ContactTarget> Targets = new();
    internal readonly ProjectionWorkspace Projection = new();
    internal V OffsetLocal, SavedLocal, AppliedLocal;
    internal int MotionKinds;
    internal bool Applied;
    internal V BaseWorld;
    internal bool Active => Bone != null && ClothActive(Cloth);
    internal static bool ClothActive(MagicaCloth cloth)
    {
        if(cloth==null || !cloth.isActiveAndEnabled)return false;
        var process=cloth.Process;
        return process!=null && process.IsRunning() && !process.IsSkipWriting() && !process.IsCameraCullingInvisible() && !process.IsDistanceCullingInvisible() && cloth.SerializeData.blendWeight>.001f;
    }
    internal RestoreResult Restore()
    {
        if (!Applied) return RestoreResult.None;
        Applied = false;
        if (Bone == null) return RestoreResult.Missing;
        // An animator/mod may already have replaced this pose. Never undo another writer's pose.
        if (V.DistanceSquared(Convert.Vec(Bone.localPosition), AppliedLocal) < 1e-10f)
        { Bone.localPosition = Convert.Vec(SavedLocal);return RestoreResult.Restored; }
        return RestoreResult.Replaced;
    }
}
internal enum RestoreResult { None,Restored,Replaced,Missing }
internal readonly record struct BodySample(BodySurface Surface, int Vertex, float Weight);

// Only shared during the read-only mesh phase of ONE collision pass. Never cache
// the live pregnancy vertex weights, or reuse matrices after bone pose writes.
internal sealed class MeshPoseCache
{
    private readonly Dictionary<IntPtr,(M World,M Normal,bool Valid)> poses=new();
    internal int Reads {get;private set;}
    internal void Clear(){poses.Clear();Reads=0;}
    internal bool TryGet(Transform bone,out M world,out M normal)
    {
        if(!poses.TryGetValue(bone.Pointer,out var pose))
        {
            world=Convert.Matrix(bone.localToWorldMatrix);Reads++;
            bool valid=M.Invert(world,out var inverse);
            pose=(world,M.Transpose(inverse),valid);poses.Add(bone.Pointer,pose);
        }
        world=pose.World;normal=pose.Normal;return pose.Valid;
    }
}

internal sealed class FrameWork
{
    internal readonly List<(ActorData Actor,BoneNode Node)> Nodes=new();
    private readonly List<ActorData> activeActors=new();
    private readonly Dictionary<MagicaCloth,bool> clothStates=new(ReferenceEqualityComparer.Instance);
    internal readonly MeshPoseCache Poses=new();
    internal int ClothChecks {get;private set;}
    internal int MeshUpdates {get;private set;}
    internal int NativeSurfaces {get;private set;}
    internal void Prepare(IEnumerable<ActorData> actors,Switches switches)
    {
        Nodes.Clear();activeActors.Clear();clothStates.Clear();ClothChecks=0;
        int targetMask=0;
        foreach(var actor in actors)
        {
            foreach(var surface in actor.Surfaces){surface.Needed=false;surface.Valid=false;surface.Tree.Enabled=false;}
            if(!actor.Active)continue;
            activeActors.Add(actor);
            foreach(var node in actor.Nodes)
            {
                // Toggle and sample checks are managed reads; avoid querying an
                // IL2CPP cloth process for unused body regions and empty rigs.
                if(!switches.Source(node.Region) || node.Samples.Count==0 || node.Bone==null || ReferenceEquals(node.Cloth,null))
                {node.OffsetLocal=V.Zero;node.MotionKinds=0;continue;}
                if(!clothStates.TryGetValue(node.Cloth,out bool active))
                {active=BoneNode.ClothActive(node.Cloth);clothStates.Add(node.Cloth,active);ClothChecks++;}
                if(!active){node.OffsetLocal=V.Zero;node.MotionKinds=0;continue;}
                Nodes.Add((actor,node));targetMask|=switches.TargetRegions(node.Region);
                foreach(var sample in node.Samples)sample.Surface.Needed=true;
                node.BaseWorld=Convert.Vec(node.Bone.position);
            }
        }
        // Include targets from ALL active actors, even if their own physics is
        // paused. Region masks use classified triangles, never mesh-name guesses.
        foreach(var actor in activeActors)foreach(var surface in actor.Surfaces)
            surface.Needed|=(surface.TargetRegions&targetMask)!=0;
    }
    internal void UpdateSurfaces()
    {
        Poses.Clear();MeshUpdates=NativeSurfaces=0;
        foreach(var actor in activeActors)foreach(var surface in actor.Surfaces)if(surface.Needed)
        {
            MeshUpdates++;
            if(surface.Update(Poses) && surface.UsesNativeCache)NativeSurfaces++;
        }
    }
    internal void Clear(){Nodes.Clear();activeActors.Clear();clothStates.Clear();Poses.Clear();}
}

internal sealed class BodySurface : IDisposable
{
    internal readonly SkinnedMeshRenderer Renderer;
    internal readonly Mesh Source, Baked;
    internal readonly Transform[] Bones;
    internal readonly BoneWeight[] Weights;
    internal readonly int[] Triangles;
    internal readonly V[] World, WorldNormals;
    internal readonly SurfaceTree Tree;
    internal readonly SkinnedCollisionHelper Helper;
    internal bool UsesNativeCache;
    internal readonly Dictionary<int,bool[]> Exclusions = new();
    internal readonly int[] Owners;
    internal readonly float[] OwnerWeights;
    private readonly float[] nativeWeightSums;
    internal readonly Region[] VertexRegions;
    internal bool Valid;
    internal bool Needed;
    internal readonly int TargetRegions;
    private readonly Region defaultRegion;
    internal BodySurface(SkinnedMeshRenderer renderer, Dictionary<int,BoneNode> nodes, SkinnedCollisionHelper helper = null)
    {
        Renderer = renderer; Source = renderer.sharedMesh; Helper = helper ?? renderer.GetComponent<SkinnedCollisionHelper>();
        Baked = new Mesh { name = "ALCollisionPlus_Scratch" }; Baked.MarkDynamic();
        Bones = renderer.bones; Weights = Source.boneWeights; Triangles = Source.triangles;
        World = new V[Source.vertexCount]; WorldNormals = new V[World.Length]; Owners = new int[World.Length]; OwnerWeights = new float[World.Length]; VertexRegions = new Region[World.Length];
        nativeWeightSums=new float[World.Length];
        Array.Fill(Owners, -1);
        string name = renderer.name.ToLowerInvariant();
        defaultRegion = name.Contains("mune") || name.Contains("bust") ? Region.Breast : name.Contains("hara") || name.Contains("belly") ? Region.Abdomen : Region.Other;
        var boneNodes = new BoneNode[Bones.Length];
        for (int i=0;i<Bones.Length;i++) for (var t = Bones[i]; t != null; t = t.parent)
            if (nodes.TryGetValue(t.GetInstanceID(), out var node)) { boneNodes[i] = node; break; }
        var influenced = new Dictionary<int,HashSet<int>>();
        for (int v=0;v<World.Length;v++)
        {
            VertexRegions[v] = defaultRegion;
            if (v >= Weights.Length) continue;
            var w = Weights[v]; var sums = new Dictionary<int,float>();
            void Add(int index, float amount)
            {
                if (amount <= .01f || index < 0 || index >= boneNodes.Length) return;
                // Exclude all physical ancestors, including nested spring bones, to avoid self-contact.
                for(var t = Bones[index]; t != null; t = t.parent)
                    if(nodes.TryGetValue(t.GetInstanceID(),out var ancestor))
                    { if(!influenced.TryGetValue(ancestor.Id,out var set)) influenced[ancestor.Id]=set=new(); set.Add(v); }
                var n = boneNodes[index]; if (n == null) return;
                sums[n.Id] = sums.GetValueOrDefault(n.Id) + amount;
            }
            Add(w.boneIndex0,w.weight0); Add(w.boneIndex1,w.weight1); Add(w.boneIndex2,w.weight2); Add(w.boneIndex3,w.weight3);
            if (sums.Count > 0)
            {
                var best = sums.MaxBy(x=>x.Value); var node = nodes[best.Key];
                if (best.Value >= .25f) { Owners[v]=best.Key; OwnerWeights[v]=best.Value; VertexRegions[v]=node.Region; }
            }
        }
        var triangleRegions = new Region[Triangles.Length/3];
        for (int t=0;t<triangleRegions.Length;t++)
        {
            Region a=VertexRegions[Triangles[t*3]], b=VertexRegions[Triangles[t*3+1]], c=VertexRegions[Triangles[t*3+2]];
            triangleRegions[t] = a==b || a==c ? a : b==c ? b : defaultRegion;
            TargetRegions|=1<<(int)triangleRegions[t];
        }
        foreach (var item in influenced)
        {
            var excluded = new bool[triangleRegions.Length];
            for(int t=0;t<excluded.Length;t++) excluded[t]=item.Value.Contains(Triangles[t*3]) || item.Value.Contains(Triangles[t*3+1]) || item.Value.Contains(Triangles[t*3+2]);
            Exclusions[item.Key]=excluded;

        }
        // Initial bake supplies spatially useful BVH partitions; the same topology is refitted subsequently.
        BakeVertices(); Tree = new SurfaceTree(World,Triangles,triangleRegions); Tree.Refit(WorldNormals);
    }
    private void BakeVertices(MeshPoseCache poses=null)
    {
        // Pregnancy 0.2.39/0.2.40 replaces _nodeWeights and updates carrier-local
        // positions, while the source SMR still contains the undeformed liquid mesh.
        // Re-evaluate THAT cache with the current bones. Do not call UpdateCollisionMesh:
        // pregnancy owns its dispatch/cooking schedule and rejects unsolicited calls.
        UsesNativeCache = ReadNativeCollisionInputs(poses);
        if (UsesNativeCache) return;
        // An initialized helper with a broken cache must not silently fall back to a flat belly.
        if (Helper != null && Helper._isInit) { Valid=false; return; }
        Renderer.BakeMesh(Baked, false);
        Vector3[] vertices = Baked.vertices, normals = Baked.normals;
        if(vertices.Length != World.Length) { Valid=false; return; }
        M matrix=Convert.Matrix(Renderer.transform.localToWorldMatrix);
        M.Invert(matrix,out var inverse); var normalMatrix=M.Transpose(inverse);
        Valid = true;
        for(int i=0;i<World.Length;i++)
        {
            World[i]=V.Transform(Convert.Vec(vertices[i]),matrix);
            WorldNormals[i]=i<normals.Length ? V.TransformNormal(Convert.Vec(normals[i]),normalMatrix) : V.Zero;
            if (!Geometry.Finite(World[i])) Valid=false;
        }
    }
    private bool ReadNativeCollisionInputs(MeshPoseCache poses)
    {
        if(Helper==null || !Helper._isInit || Helper._meshCalc==null) return false;
        var groups=Helper._nodeWeights;
        var output=Helper._newVert;
        if(groups==null || output==null || output.Length!=World.Length) return false;
        Array.Clear(World,0,World.Length);Array.Clear(WorldNormals,0,WorldNormals.Length);
        var sums=nativeWeightSums;Array.Clear(sums,0,sums.Length);
        int groupCount=groups.Length;
        for(int g=0;g<groupCount;g++)
        {
            var group=groups[g];
            if(group==null)return false;
            var bone=group.Transform;
            var items=group.Weights;
            if(bone==null || items==null)return false;
            M transform,normalTransform;
            if(poses!=null)
            {if(!poses.TryGet(bone,out transform,out normalTransform))return false;}
            else
            {
                transform=Convert.Matrix(bone.localToWorldMatrix);
                if(!M.Invert(transform,out var inv))return false;
                normalTransform=M.Transpose(inv);
            }
            int weightCount=items._size;
            // The interop List indexer invokes native IL2CPP code and boxes its
            // value result for every vertex. The runtime exposes the same live,
            // blittable backing array as a span. Read it once for this group;
            // never retain the span or cache pregnancy positions across frames.
            var nativeItems=items._items as Il2CppStructArray<SkinnedCollisionHelper.VertexWeight>;
            ReadOnlySpan<SkinnedCollisionHelper.VertexWeight> weights=nativeItems!=null?nativeItems.AsSpan():default;
            if(weightCount<0 || (nativeItems!=null && weightCount>weights.Length))return false;
            for(int k=0;k<weightCount;k++)
            {
                var w=nativeItems!=null?weights[k]:items[k];int i=w.Index;float weight=w.Weight;
                if((uint)i>=World.Length || !float.IsFinite(weight) || weight<0) return false;
                World[i]+=V.Transform(Convert.Vec(w.LocalPosition),transform)*weight;
                WorldNormals[i]+=V.TransformNormal(Convert.Vec(w.LocalNormal),normalTransform)*weight;
                sums[i]+=weight;
            }
            GC.KeepAlive(nativeItems);
        }
        for(int i=0;i<World.Length;i++) if(!Geometry.Finite(World[i]) || Math.Abs(sums[i]-1)>.01f) return false;
        Valid=true;return true;
    }
    internal bool Update(MeshPoseCache poses=null)
    {
        Valid=false;Tree.Enabled=false;
        // Hit renderers may be invisible by design. Their parent must exist, but renderer.enabled is not a validity test.
        if(Renderer==null)return false;
        var mesh=Renderer.sharedMesh;
        if(mesh==null || mesh.GetInstanceID()!=Source.GetInstanceID()) return false;
        BakeVertices(poses); if(Valid) Tree.Refit(WorldNormals);Tree.Enabled=Valid;return Valid;
    }
    public void Dispose() { if(Baked != null) UnityEngine.Object.Destroy(Baked); }
}

internal sealed class ActorData : IDisposable
{
    internal readonly Human Human;
    internal readonly long Fingerprint;
    internal readonly List<BoneNode> Nodes = new();
    internal readonly List<BodySurface> Surfaces = new();
    internal float Height = 1.6f;
    internal bool Ready;
    internal ActorData(Human human, long fingerprint, Plugin plugin)
    {
        Human=human; Fingerprint=fingerprint;
        var nodes=new Dictionary<int,BoneNode>();
        var setting=human.Body?.magicaSpringSetting;
        if(setting!=null)
            for(int type=0;type<8;type++)
            {
                var entries=setting.Get((HumanMagicaSpringSetting.Type)type,HumanMagicaSpringSetting.Side.All);
                if(entries==null) continue;
                foreach(var e in entries) if(e?.Spring!=null) AddCloth(e.Spring,type==0?Region.Breast:type==4?Region.Abdomen:Region.Other,nodes,plugin);
            }
        // Other spring roots are read ONLY to preserve skin-region classification
        // and breast self-exclusion weights. Only breasts become writable sources.
        Nodes.AddRange(nodes.Values.Where(n=>n.Region==Region.Breast).OrderBy(n=>n.Depth));
        var seen=new HashSet<int>();
        foreach(var obj in new[]{human.GameObject,human.Body?.objHitBody,human.Body?.objHitHead})
        {
            if(obj==null) continue;
            foreach(var helper in obj.GetComponentsInChildren<SkinnedCollisionHelper>(true))
            {
                var renderer=helper?._skinnedMeshRenderer;
                if(renderer==null || renderer.sharedMesh==null || !seen.Add(renderer.GetInstanceID())) continue;
                if(!renderer.name.StartsWith("o_hit_",StringComparison.OrdinalIgnoreCase)) continue;
                try { Surfaces.Add(new BodySurface(renderer,nodes,helper)); }
                catch(Exception e) { plugin.Log.LogWarning($"Skipped collision surface {renderer.name}: {e.GetType().Name}: {e.Message}"); }
            }
        }
        foreach(var node in Nodes)
            foreach(var surface in Surfaces)for(int v=0;v<surface.Owners.Length;v++)
                if(surface.Owners[v]==node.Id)node.Samples.Add(new BodySample(surface,v,surface.OwnerWeights[v]));
        UpdateHeight(); Ready=Nodes.Count>0;
        if(plugin.Diagnostics.Value && (Nodes.Count>0 || Surfaces.Count>0))
        {
            plugin.Log.LogInfo($"Actor bound: breastRoots={Nodes.Count}, breastSamplePoints={Nodes.Sum(n=>n.Samples.Count)}, fluidSurfaces={Surfaces.Count}, nativeCacheSurfaces={Surfaces.Count(s=>s.UsesNativeCache)}, height={Height:F3}");
            foreach(var node in Nodes.Where(n=>n.Region==Region.Breast))plugin.Log.LogInfo($"Breast binding: bone={node.Bone.name}, samples={node.Samples.Count}, cloth={node.Cloth.name}");
        }
    }
    private void AddCloth(MagicaCloth cloth,Region region,Dictionary<int,BoneNode> nodes,Plugin plugin)
    {
        // BoneSpring rootBones are physical sources even when proxy attributes
        // mark them fixed/no-collision. Do not use the BoneCloth vertex filter.
        var roots=cloth.SerializeData?.rootBones;
        if(roots!=null)foreach(var root in roots)
        {
            if(root==null || !root.IsChildOf(Human.Transform))continue;
            int id=root.GetInstanceID();if(nodes.ContainsKey(id))continue;
            nodes.Add(id,new BoneNode{Id=id,Bone=root,Cloth=cloth,Region=region,Depth=Convert.Depth(root)});
        }
    }
    internal void UpdateHeight()
    {
        float low=float.MaxValue, high=float.MinValue;
        // Measure bind-pose vertices in the character frame, not a crouched/lying animated AABB.
        M rootInverse=Convert.Matrix(Human.Transform.worldToLocalMatrix);
        foreach(var s in Surfaces)
        {
            M toRoot=Convert.Matrix(s.Renderer.transform.localToWorldMatrix)*rootInverse;
            Vector3[] vertices=s.Source.vertices;
            foreach(var v in vertices){var p=V.Transform(Convert.Vec(v),toRoot);low=Math.Min(low,p.Y);high=Math.Max(high,p.Y);}
        }
        float scale=Math.Abs(Human.Transform.lossyScale.y);high*=scale;low*=scale;
        if(high-low>.25f && high-low<30) Height=high-low;
    }
    internal bool Active => !Human.Disposed && !Human.IsReloading && Human.IsActiveAndEnabled && Human.HiPoly && Human.Transform!=null;
    public void Dispose(){foreach(var n in Nodes)n.Restore();foreach(var s in Surfaces)s.Dispose();}
}
