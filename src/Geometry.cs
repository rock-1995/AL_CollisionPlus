using System.Numerics;

namespace ALCollisionPlus;

internal enum Region { Other, Breast, Abdomen }
internal enum ContactKind { None, BreastAbdomen, BreastBody, BreastGround }
internal readonly record struct Switches(bool BreastAbdomen, bool BreastBody, bool BreastGround)
{
    public ContactKind Pair(Region source, Region target) => source != Region.Breast ? ContactKind.None :
        target == Region.Abdomen ? (BreastAbdomen ? ContactKind.BreastAbdomen : ContactKind.None) :
        BreastBody ? ContactKind.BreastBody : ContactKind.None;
    public bool Source(Region source) => source == Region.Breast && Any;
    public bool Any => BreastAbdomen || BreastBody || BreastGround;
    internal bool Enabled(ContactKind kind) => kind switch
    { ContactKind.BreastAbdomen => BreastAbdomen, ContactKind.BreastBody => BreastBody, ContactKind.BreastGround => BreastGround, _ => false };
    internal static Switches Only(ContactKind kind) => new(kind==ContactKind.BreastAbdomen,kind==ContactKind.BreastBody,kind==ContactKind.BreastGround);
    internal int TargetRegions(Region source)
    {
        int mask=0;
        for(int r=0;r<3;r++)if(Pair(source,(Region)r)!=ContactKind.None)mask|=1<<r;
        return mask;
    }
}

internal static class Geometry
{
    public static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
    public static Vector3 Limit(Vector3 p, float max)
    {
        if (!Finite(p) || max <= 0) return Vector3.Zero;
        float length = p.Length();
        return length > max ? p * (max / length) : p;
    }
    public static Vector3 SmoothLimited(Vector3 current,Vector3 target,float alpha,float maxStep,float maxOffset)
    {
        current=Limit(current,maxOffset);target=Limit(target,maxOffset);
        return Limit(current+Limit((target-current)*Math.Clamp(alpha,0,1),maxStep),maxOffset);
    }
    public static Vector3 Closest(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return a;
        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return b;
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));
        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return c;
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));
        float va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
        float sum = va + vb + vc;
        if (Math.Abs(sum) < 1e-20f) return a;
        return a + ab * (vb / sum) + ac * (vc / sum);
    }

}

internal sealed class SurfaceTree
{
    public bool Enabled = true;
    private struct Branch { public Vector3 Min, Max; public int Start, Count, Left, Right; }
    private readonly int[] triangles, order;
    private readonly Branch[] branches;
    private int used;
    public Vector3[] Vertices;
    public Vector3[] Normals;
    public Region[] Regions;
    internal readonly Vector3[] RegionMoments=new Vector3[3];
    internal readonly float[] RegionAreas=new float[3];
    public int TriangleCount => triangles.Length / 3;
    public SurfaceTree(Vector3[] vertices, int[] indices, Region[] regions)
    {
        Vertices = vertices; triangles = indices; Regions = regions;
        Normals = new Vector3[TriangleCount]; order = Enumerable.Range(0, TriangleCount).ToArray();
        branches = new Branch[Math.Max(1, TriangleCount * 2)];
        if (TriangleCount > 0) { Build(0, TriangleCount); Refit(); }
    }
    private Vector3 Center(int t) => (Vertices[triangles[t * 3]] + Vertices[triangles[t * 3 + 1]] + Vertices[triangles[t * 3 + 2]]) / 3;
    private int Build(int start, int count)
    {
        int index = used++; branches[index].Start = start; branches[index].Count = count;
        if (count > 8)
        {
            Vector3 min = new(float.MaxValue), max = new(float.MinValue);
            for (int i = start; i < start + count; i++) { var c = Center(order[i]); min = Vector3.Min(min, c); max = Vector3.Max(max, c); }
            Vector3 size = max - min; int axis = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;
            float Axis(Vector3 v) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
            Array.Sort(order, start, count, Comparer<int>.Create((a,b) => Axis(Center(a)).CompareTo(Axis(Center(b)))));
            branches[index].Left = Build(start, count / 2); branches[index].Right = Build(start + count / 2, count - count / 2);
            branches[index].Count = 0;
        }
        return index;
    }
    public void Refit(Vector3[] vertexNormals = null)
    {
        Array.Clear(RegionMoments,0,RegionMoments.Length);Array.Clear(RegionAreas,0,RegionAreas.Length);
        for (int t = 0; t < TriangleCount; t++)
        {
            int a = triangles[t * 3], b = triangles[t * 3 + 1], c = triangles[t * 3 + 2];
            var n = Vector3.Cross(Vertices[b] - Vertices[a], Vertices[c] - Vertices[a]);
            float l = n.Length(); n = l > 1e-10f ? n / l : Vector3.Zero;
            if(l>1e-10f && float.IsFinite(l))
            {
                int region=(int)Regions[t];
                RegionMoments[region]+=(Vertices[a]+Vertices[b]+Vertices[c])*(l/3);
                RegionAreas[region]+=l;
            }
            if (vertexNormals != null && Vector3.Dot(n, vertexNormals[a] + vertexNormals[b] + vertexNormals[c]) < 0) n = -n;
            Normals[t] = n;
        }
        if (TriangleCount > 0) RefitBranch(0);
    }
    private void RefitBranch(int index)
    {
        ref var b = ref branches[index];
        if (b.Count == 0)
        { RefitBranch(b.Left); RefitBranch(b.Right); b.Min = Vector3.Min(branches[b.Left].Min, branches[b.Right].Min); b.Max = Vector3.Max(branches[b.Left].Max, branches[b.Right].Max); }
        else
        {
            b.Min = new(float.MaxValue); b.Max = new(float.MinValue);
            for (int j = b.Start; j < b.Start + b.Count; j++) for (int k = 0; k < 3; k++)
            { var p = Vertices[triangles[order[j] * 3 + k]]; b.Min = Vector3.Min(b.Min, p); b.Max = Vector3.Max(b.Max, p); }
        }
    }
    public bool Nearest(Vector3 point, float range, bool[] excluded, Switches switches, Region source, out Vector3 closest, out Vector3 normal, out ContactKind kind)
    {
        float best = range * range; int hit = -1; Vector3 q = default;
        if (TriangleCount > 0) Search(0, point, excluded, switches, source, ref best, ref hit, ref q);
        closest = q; normal = hit < 0 ? default : Normals[hit]; kind = hit < 0 ? ContactKind.None : switches.Pair(source, Regions[hit]);
        return hit >= 0;
    }
    private void Search(int index, Vector3 p, bool[] excluded, Switches switches, Region source, ref float best, ref int hit, ref Vector3 closest)
    {
        ref var b = ref branches[index];
        if (Vector3.DistanceSquared(p, Vector3.Clamp(p, b.Min, b.Max)) > best) return;
        if (b.Count == 0) { Search(b.Left,p,excluded,switches,source,ref best,ref hit,ref closest); Search(b.Right,p,excluded,switches,source,ref best,ref hit,ref closest); return; }
        for (int j = b.Start; j < b.Start + b.Count; j++)
        {
            int t = order[j];
            if ((excluded != null && excluded[t]) || switches.Pair(source, Regions[t]) == ContactKind.None || Normals[t].LengthSquared() < .5f) continue;
            var q = Geometry.Closest(p, Vertices[triangles[t*3]], Vertices[triangles[t*3+1]], Vertices[triangles[t*3+2]]);
            float ds = Vector3.DistanceSquared(p,q);
            if (ds < best) { best = ds; closest = q; hit = t; }
        }
    }
}

internal readonly record struct ContactProbe(Vector3 Position,float Influence);
internal readonly record struct ContactTarget(SurfaceTree Tree,bool[] Excluded,int Group=0);
internal readonly record struct ContactSettings(float Radius,float Limit,float Strength,
    float Compression,float Coverage,float NormalAlignment,float Transition,float Capture,
    float MinInfluence,float ConflictTolerance,int Iterations,float Response,float MaxSpeed,float GroundNormalY=.35f);
internal readonly record struct FrozenContact(Vector3 Normal,float Distance,float Weight);
internal readonly record struct GroundContact(Vector3 Point,Vector3 Normal);
internal readonly record struct ContactResult(Vector3 Offset,int Count,int ProbeCount,float Coverage,float MaxDepth);
internal sealed class ProjectionWorkspace
{
    internal readonly List<FrozenContact> Contacts=new();
    internal readonly ContactResult[] Results=new ContactResult[3];
    internal readonly ContactSettings[] Settings=new ContactSettings[3];
}

internal static class ContactProjection
{
    // The breast-abdomen path preserves the 0.1.8 frozen-plane solver, including
    // its region centroid, weighting, onset, iteration order and conflict guard.
    // New body contacts use the centroid of the nearest body piece: the center
    // of an entire actor is not the center of a hand or a thigh near the breast.
    internal static ContactResult Solve(IReadOnlyList<ContactProbe> probes,IReadOnlyList<ContactTarget> targets,
        ContactKind kind,ContactSettings settings,List<FrozenContact> contacts,
        Func<Vector3,float,GroundContact?> ground=null)
    {
        contacts.Clear();
        if(settings.Limit<=0 || settings.Strength<=0 || kind==ContactKind.None)return default;
        float allWeight=0;var center=Vector3.Zero;
        foreach(var p in probes)
        {
            if(!Valid(p))continue;
            float w=Math.Clamp(p.Influence,0,1);w*=w;
            center+=p.Position*w;allWeight+=w;
        }
        if(allWeight<=1e-8f)return default;center/=allWeight;
        float allowance=Math.Max(0,settings.Compression);
        float capture=settings.Capture>0?settings.Capture:settings.Limit+allowance+settings.Radius;
        float contactWeight=0,maxDepth=0;
        void Add(ContactProbe probe,Vector3 q,Vector3 normal,bool checkEdge)
        {
            var d=probe.Position-q;float signed=Vector3.Dot(d,normal);
            float depth=-signed-allowance;
            if(!float.IsFinite(depth) || depth<=0 || (checkEdge && Vector3.Dot(d,d)-signed*signed>4*settings.Radius*settings.Radius))return;
            float influence=Math.Clamp(probe.Influence,0,1),weight=influence*influence;
            float distance=Math.Min(settings.Limit,depth/Math.Max(settings.MinInfluence,influence));
            float onset=settings.Transition>0?Math.Min(1,depth/settings.Transition):1;
            contacts.Add(new(normal,distance*onset,weight));contactWeight+=weight;
            maxDepth=Math.Max(maxDepth,depth);
        }
        if(kind==ContactKind.BreastGround)
        {
            if(ground!=null)foreach(var probe in probes)
            {
                if(!Valid(probe))continue;
                var hit=ground(probe.Position,capture);if(!hit.HasValue)continue;
                var plane=hit.Value;
                if(!Geometry.Finite(plane.Point) || !Geometry.Finite(plane.Normal) || plane.Normal.Y<settings.GroundNormalY)continue;
                // An overhead platform is not a support underneath this breast.
                // No positive contact padding: a visible gap cannot trigger lift.
                if(Vector3.Dot(center-plane.Point,plane.Normal)<=0)continue;
                Add(probe,plane.Point,plane.Normal,false);
            }
        }
        else for(int start=0;start<targets.Count;)
        {
            int end=start+1;while(end<targets.Count && targets[end].Group==targets[start].Group)end++;
            var targetMoment=Vector3.Zero;float targetArea=0;
            if(kind==ContactKind.BreastAbdomen)
                for(int i=start;i<end;i++)if(targets[i].Tree.Enabled)
                {
                    targetMoment+=targets[i].Tree.RegionMoments[(int)Region.Abdomen];
                    targetArea+=targets[i].Tree.RegionAreas[(int)Region.Abdomen];
                }
            var outward=targetArea>1e-8f?center-targetMoment/targetArea:Vector3.Zero;
            float outwardLength=outward.Length();
            if(kind==ContactKind.BreastAbdomen)
            {
                if(outwardLength<1e-6f){start=end;continue;}
                outward/=outwardLength;
            }
            foreach(var probe in probes)
            {
                if(!Valid(probe))continue;
                if(!Nearest(targets,start,end,probe.Position,capture,kind,out var q,out var normal,out var tree))continue;
                if(kind==ContactKind.BreastBody)
                {
                    targetArea=tree.RegionAreas[(int)Region.Other]+tree.RegionAreas[(int)Region.Breast];
                    targetMoment=tree.RegionMoments[(int)Region.Other]+tree.RegionMoments[(int)Region.Breast];
                    outward=targetArea>1e-8f?center-targetMoment/targetArea:Vector3.Zero;
                    outwardLength=outward.Length();if(outwardLength<1e-6f)continue;
                    outward/=outwardLength;
                }
                if(Vector3.Dot(outward,normal)<settings.NormalAlignment)continue;
                Add(probe,q,normal,true);
            }
            start=end;
        }
        float denominator=Math.Max(contactWeight,allWeight*Math.Clamp(settings.Coverage,0,1));
        var result=new ContactResult(Vector3.Zero,contacts.Count,probes.Count,Math.Min(1,contactWeight/allWeight),maxDepth);
        if(contactWeight<=1e-8f || denominator<=1e-8f)return result;
        var combined=Vector3.Zero;
        for(int pass=0;pass<Math.Clamp(settings.Iterations,1,16);pass++)
        {
            var residual=Vector3.Zero;
            foreach(var c in contacts)
                residual+=c.Normal*(Math.Max(0,c.Distance-Vector3.Dot(combined,c.Normal))*c.Weight);
            combined=Geometry.Limit(combined+residual/contactWeight,settings.Limit);
        }
        combined*=contactWeight/denominator*Math.Clamp(settings.Strength,0,1);
        if(kind!=ContactKind.BreastGround && combined.LengthSquared()>1e-12f && float.IsFinite(settings.ConflictTolerance))
        {
            float allowed=WorstOverlap(probes,targets,kind,settings,capture,Vector3.Zero)+Math.Max(0,settings.ConflictTolerance);
            int tries=0;
            while(WorstOverlap(probes,targets,kind,settings,capture,combined)>allowed+1e-6f)
            {
                if(++tries>6){combined=Vector3.Zero;break;}
                combined*=.5f;
            }
        }
        return result with{Offset=combined};
    }
    private static bool Valid(ContactProbe p) => Geometry.Finite(p.Position) && float.IsFinite(p.Influence) && p.Influence>0;
    internal static float WorstOverlap(IReadOnlyList<ContactProbe> probes,IReadOnlyList<ContactTarget> targets,ContactKind kind,ContactSettings settings,float capture,Vector3 offset)
    {
        float worst=0;
        foreach(var probe in probes)
        {
            if(!Valid(probe))continue;
            var p=probe.Position+offset*Math.Clamp(probe.Influence,0,1);
            for(int start=0;start<targets.Count;)
            {
                int end=start+1;while(end<targets.Count && targets[end].Group==targets[start].Group)end++;
                bool found=Nearest(targets,start,end,p,capture,kind,out var q,out var normal,out _);start=end;
                if(!found)continue;
                var d=p-q;float signed=Vector3.Dot(d,normal);
                if(Vector3.Dot(d,d)-signed*signed<=4*settings.Radius*settings.Radius)
                    worst=Math.Max(worst,-signed-Math.Max(0,settings.Compression));
            }
        }
        return worst;
    }
    internal static bool Nearest(IReadOnlyList<ContactTarget> targets,int start,int end,Vector3 p,float capture,ContactKind kind,out Vector3 closest,out Vector3 normal,out SurfaceTree tree)
    {
        float best=capture*capture;closest=normal=default;tree=null;
        var only=Switches.Only(kind);
        for(int i=start;i<end;i++)
        {
            var t=targets[i];
            if(!t.Tree.Enabled || !t.Tree.Nearest(p,MathF.Sqrt(best),t.Excluded,only,Region.Breast,out var q,out var n,out _))continue;
            float d=Vector3.DistanceSquared(p,q);if(d>=best)continue;
            best=d;closest=q;normal=n;tree=t.Tree;
        }
        return tree!=null;
    }
    // Merge the three already-softened proposals as directional constraints.
    // Adding vectors would double identical contacts at an abdomen/body seam.
    // One active proposal is returned verbatim, preserving the established path.
    internal static Vector3 Merge(ContactResult[] results,float limit)
    {
        int count=0;var single=Vector3.Zero;
        foreach(var result in results)if(result.Offset.LengthSquared()>1e-12f){count++;single=result.Offset;}
        if(count==0)return Vector3.Zero;
        if(count==1)return Geometry.Limit(single,limit);
        var combined=Vector3.Zero;
        for(int pass=0;pass<8;pass++)
        {
            var residual=Vector3.Zero;
            foreach(var result in results)
            {
                float length=result.Offset.Length();if(length<=1e-6f)continue;
                var n=result.Offset/length;
                residual+=n*Math.Max(0,length-Vector3.Dot(combined,n));
            }
            combined=Geometry.Limit(combined+residual/count,limit);
        }
        return combined;
    }
}
