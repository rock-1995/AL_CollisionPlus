using System.Numerics;

namespace ALCollisionPlus.Legacy018;

internal enum Region { Other, Breast, Abdomen, Hair }
internal enum ContactKind { None, BodyBody, BodyGround, BodyHair, HairGround, BreastAbdomen }
internal readonly record struct Switches(bool BodyBody, bool BodyGround, bool BodyHair, bool HairGround, bool BreastAbdomen)
{
    public ContactKind Pair(Region a, Region b)
    {
        if (a == Region.Hair) return BodyHair && b != Region.Hair ? ContactKind.BodyHair : ContactKind.None;
        if (b == Region.Hair) return ContactKind.None; // Hair yields to the body; it does not lift the body.
        if ((a == Region.Breast && b == Region.Abdomen) || (a == Region.Abdomen && b == Region.Breast))
            return BreastAbdomen ? ContactKind.BreastAbdomen : ContactKind.None;
        return BodyBody ? ContactKind.BodyBody : ContactKind.None;
    }
    public bool Ground(Region region) => region == Region.Hair ? HairGround : BodyGround;
    public bool Source(Region region) => region == Region.Hair ? BodyHair || HairGround :
        BodyBody || BodyGround || (BreastAbdomen && (region == Region.Breast || region == Region.Abdomen));
    internal int TargetRegions(Region source)
    {
        int mask=0;
        for(int r=0;r<4;r++)if(Pair(source,(Region)r)!=ContactKind.None)mask|=1<<r;
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

    // Signed surface contact, bounded on both sides. Triangles are oriented by the baked mesh normals.
    public static Vector3 Contact(Vector3 p, Vector3 closest, Vector3 normal, float radius, float capture)
    {
        Vector3 d = p - closest;
        if (!Finite(d) || normal.LengthSquared() < .5f || d.LengthSquared() > capture * capture) return Vector3.Zero;
        float signed = Vector3.Dot(d, normal);
        if (signed >= radius || signed < -capture) return Vector3.Zero;
        // At a boundary, avoid treating the continuation of an open triangle as a solid half-space.
        float lateral = Math.Max(0, d.LengthSquared() - signed * signed);
        // Zero padding still needs a scale-relative roundoff allowance for
        // projections onto slanted faces; it must not extend an open edge.
        float edgeLimit = radius > 0 ? radius * radius * 4 : capture * capture * 1e-6f;
        if (lateral > edgeLimit) return Vector3.Zero;
        return normal * (radius - signed);
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
    internal readonly Vector3[] RegionMoments=new Vector3[4];
    internal readonly float[] RegionAreas=new float[4];
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

internal readonly record struct ContactProbe(Vector3 Position,float Influence,Vector3 Normal=default,Vector3 Anchor=default,bool HasAnchor=false);
internal readonly record struct ContactTarget(SurfaceTree Tree,bool[] Excluded,int Group=0,bool[] OrdinaryExcluded=null);
internal readonly record struct ProjectionSettings(float Radius,float OrdinaryLimit,float ChestLimit,float BodyStrength,float HairStrength,float ChestStrength,
    float ChestCompression=0,float ChestCoverage=.1f,float ChestNormalAlignment=.05f,float ChestTransition=0,float ChestCapture=0,float ChestMinInfluence=.5f,float ChestConflictTolerance=float.PositiveInfinity,int ChestIterations=8,
    float SurfaceRadius=-1,float GroundRadius=-1,float SurfaceCapture=-1,
    float BodyCompression=0,float BodyCoverage=.25f,float BodyMinInfluence=.75f,float BodyNormalOpposition=.2f);
internal readonly record struct FrozenContact(Vector3 Normal,float Distance,float Weight);
internal sealed class ProjectionWorkspace
{
    internal readonly List<FrozenContact> ChestContacts=new();
    internal readonly List<FrozenContact> BodyContacts=new();
    internal readonly List<FrozenContact> HairContacts=new();
}
internal readonly record struct ChestContactInfo(int Count,int ProbeCount,float Coverage,float MaxDepth);
internal readonly record struct ProjectionResult(Vector3 Ordinary,Vector3 Chest)
{
    internal Vector3 Total => Ordinary+Chest;
    internal ChestContactInfo ChestInfo {get;init;}=default;
}
internal static class ContactProjection
{
    internal static Vector3 Solve(IReadOnlyList<ContactProbe> probes,IReadOnlyList<ContactTarget> targets,Region region,Switches switches,ProjectionSettings settings,long[] counts,Func<Vector3,float,float,Vector3> ground=null)
        => SolveParts(probes,targets,region,switches,settings,counts,ground).Total;

    internal static ProjectionResult SolveParts(IReadOnlyList<ContactProbe> probes,IReadOnlyList<ContactTarget> targets,Region region,Switches switches,ProjectionSettings settings,long[] counts,Func<Vector3,float,float,Vector3> ground=null,ProjectionWorkspace workspace=null)
    {
        var ordinary=Vector3.Zero;
        var chest=SoftChest(probes,targets,region,switches,settings,counts,workspace,out var chestInfo);
        if(region!=Region.Hair)
            return new(SoftBody(probes,targets,region,switches,settings,counts,ground,workspace,chest),chest){ChestInfo=chestInfo};
        ordinary=SafeHairSurface(probes,targets,switches,settings,counts,workspace);
        float groundRadius=settings.GroundRadius>=0?settings.GroundRadius:settings.Radius;
        float groundCapture=settings.OrdinaryLimit*2+groundRadius;
        void Add(Vector3 correction,float influence,ContactKind kind)
        {
            ordinary=Geometry.Limit(ordinary+correction/Math.Max(.25f,influence),settings.OrdinaryLimit);
            counts[(int)kind]++;
        }
        bool groundQuery=ground!=null && switches.Ground(region);
        for(int pass=0;groundQuery && pass<2;pass++)
        {
          var passStart=ordinary;
          foreach(var probe in probes)
          {
            var p=probe.Position+(ordinary+chest)*probe.Influence;
            if(groundQuery)
            {
                var kind=region==Region.Hair?ContactKind.HairGround:ContactKind.BodyGround;
                var correction=ground(p,groundRadius,groundCapture)*(region==Region.Hair?settings.HairStrength:settings.BodyStrength);
                if(correction.LengthSquared()>1e-12f)Add(correction,probe.Influence,kind);
            }
          }
          // With identical input positions, another pass would repeat the same
          // read-only queries. Keep the second pass whenever the first moved us.
          if(ordinary==passStart)break;
        }
        return new(ordinary,chest){ChestInfo=chestInfo};
    }

    private static Vector3 SafeHairSurface(IReadOnlyList<ContactProbe> probes,IReadOnlyList<ContactTarget> targets,Switches switches,ProjectionSettings settings,long[] counts,ProjectionWorkspace workspace)
    {
        var fixedContacts=workspace?.HairContacts ?? new List<FrozenContact>();fixedContacts.Clear();
        if(!switches.BodyHair || settings.HairStrength<=0 || settings.OrdinaryLimit<=0)return Vector3.Zero;
        float radius=settings.SurfaceRadius>=0?settings.SurfaceRadius:settings.Radius;
        float capture=settings.SurfaceCapture>=0?Math.Max(radius,settings.SurfaceCapture):settings.OrdinaryLimit*2+radius;
        float totalWeight=0;
        foreach(var probe in probes)
        {
            if(!Geometry.Finite(probe.Position) || !float.IsFinite(probe.Influence) || probe.Influence<=0)continue;
            float influence=Math.Clamp(probe.Influence,0,1),weight=influence*influence;
            for(int start=0;start<targets.Count;)
            {
                int end=start+1;while(end<targets.Count && targets[end].Group==targets[start].Group)end++;
                int first=start;start=end;
                if(!Nearest(targets,first,end,probe.Position,capture,switches,Region.Hair,out var q,out var n,out _))continue;
                var correction=Geometry.Contact(probe.Position,q,n,radius,capture);
                float depth=correction.Length();if(depth<1e-8f)continue;
                if(Vector3.Dot(probe.Position-q,n)<0)
                {
                    // Negative distance on an open body piece alone is not
                    // penetration. Require a coherent entry from the original
                    // parent, which must be outside BOTH the candidate plane
                    // and its own nearest surface of this actor.
                    if(!probe.HasAnchor || !Geometry.Finite(probe.Anchor) || Vector3.Dot(probe.Anchor-q,n)<=radius)continue;
                    float anchorRange=capture+Vector3.Distance(probe.Anchor,probe.Position);
                    if(!Nearest(targets,first,end,probe.Anchor,anchorRange,switches,Region.Hair,out var aq,out var an,out _) ||
                       Vector3.Dot(probe.Anchor-aq,an)<=radius || Vector3.Dot(an,n)<.5f)continue;
                }
                fixedContacts.Add(new(n,Math.Min(settings.OrdinaryLimit,depth/Math.Max(.5f,influence)),weight));
                totalWeight+=weight;counts[(int)ContactKind.BodyHair]++;
            }
        }
        if(totalWeight<=1e-8f)return Vector3.Zero;
        var combined=Vector3.Zero;
        foreach(var contact in fixedContacts)combined+=contact.Normal*(contact.Distance*contact.Weight/totalWeight);
        foreach(var contact in fixedContacts)if(Vector3.Dot(combined,contact.Normal)<-1e-7f)return Vector3.Zero;
        return Geometry.Limit(combined,settings.OrdinaryLimit)*Math.Clamp(settings.HairStrength,0,1);
    }

    // A body's liquid-collision mesh consists of open, overlapping pieces. A
    // negative signed distance alone is NOT proof that another body entered it.
    // Require a facing surface on the source and its bulk outside the contact
    // plane, then average a frozen patch once; never walk the entire spring root
    // through successive vertices or magnify a weak seam's skin weight.
    private static Vector3 SoftBody(IReadOnlyList<ContactProbe> probes,IReadOnlyList<ContactTarget> targets,Region region,Switches switches,ProjectionSettings settings,long[] counts,Func<Vector3,float,float,Vector3> ground,ProjectionWorkspace workspace,Vector3 chest)
    {
        var fixedContacts=workspace?.BodyContacts ?? new List<FrozenContact>();fixedContacts.Clear();
        if((!switches.BodyBody && !switches.BodyGround) || settings.BodyStrength<=0 || settings.OrdinaryLimit<=0)return Vector3.Zero;
        float minInfluence=Math.Clamp(settings.BodyMinInfluence,.5f,1),allWeight=0;
        var center=Vector3.Zero;
        foreach(var probe in probes)
        {
            if(!Geometry.Finite(probe.Position) || !float.IsFinite(probe.Influence) || probe.Influence<minInfluence)continue;
            float influence=Math.Clamp(probe.Influence,0,1),weight=influence*influence;
            center+=(probe.Position+chest*influence)*weight;allWeight+=weight;
        }
        if(allWeight<=1e-8f)return Vector3.Zero;center/=allWeight;
        float radius=settings.SurfaceRadius>=0?settings.SurfaceRadius:settings.Radius;
        float capture=settings.SurfaceCapture>=0?Math.Max(radius,settings.SurfaceCapture):settings.OrdinaryLimit*2+radius;
        float groundRadius=settings.GroundRadius>=0?settings.GroundRadius:settings.Radius;
        float groundCapture=Math.Min(settings.OrdinaryLimit*2+groundRadius,capture+groundRadius);
        float allowance=Math.Max(0,settings.BodyCompression),contactWeight=0;
        var bodyOnly=switches with{BreastAbdomen=false};
        foreach(var probe in probes)
        {
            if(!Geometry.Finite(probe.Position) || !float.IsFinite(probe.Influence) || probe.Influence<minInfluence || !Geometry.Finite(probe.Normal) || probe.Normal.LengthSquared()<1e-10f)continue;
            float influence=Math.Clamp(probe.Influence,0,1),weight=influence*influence;
            var sourceNormal=Vector3.Normalize(probe.Normal);
            var p=probe.Position+chest*influence;
            // Reject inward folds on the source as well as same-facing seams.
            if(Vector3.Dot(p-center,sourceNormal)<=0)continue;
            void Add(Vector3 correction,float padding,ContactKind kind)
            {
                float rawDepth=correction.Length();
                if(!float.IsFinite(rawDepth) || rawDepth<=allowance || rawDepth<1e-8f)return;
                var n=correction/rawDepth;
                if(Vector3.Dot(sourceNormal,n)>-Math.Clamp(settings.BodyNormalOpposition,0,1))return;
                // Signed distance of the SOURCE BULK to this plane must be
                // positive. Entirely buried/wrong-sided patches are ambiguous.
                if(Vector3.Dot(center-p,n)+padding-rawDepth<=padding)return;
                float depth=rawDepth-allowance;
                float onset=allowance>0?Math.Min(1,depth/allowance):1;
                fixedContacts.Add(new(n,Math.Min(settings.OrdinaryLimit,depth/influence)*onset,weight));
                contactWeight+=weight;counts[(int)kind]++;
            }
            for(int start=0;switches.BodyBody && start<targets.Count;)
            {
                int end=start+1;while(end<targets.Count && targets[end].Group==targets[start].Group)end++;
                bool found=Nearest(targets,start,end,p,capture,bodyOnly,region,out var q,out var normal,out _,ordinary:true);start=end;
                if(found)Add(Geometry.Contact(p,q,normal,radius,capture),radius,ContactKind.BodyBody);
            }
            if(switches.BodyGround && ground!=null)Add(ground(p,groundRadius,groundCapture),groundRadius,ContactKind.BodyGround);
        }
        if(contactWeight<=1e-8f)return Vector3.Zero;
        var combined=Vector3.Zero;
        foreach(var contact in fixedContacts)combined+=contact.Normal*(contact.Distance*contact.Weight);
        combined/=Math.Max(contactWeight,allWeight*Math.Clamp(settings.BodyCoverage,0,1));
        // Contradictory patches must not push INTO any accepted contact plane.
        foreach(var contact in fixedContacts)if(Vector3.Dot(combined,contact.Normal)<-1e-7f)return Vector3.Zero;
        float strength=Math.Clamp(settings.BodyStrength,0,1);
        return Geometry.Limit(combined,settings.OrdinaryLimit)*strength;
    }

    // A body spring translates a whole soft region. Sequentially projecting every
    // vertex makes low strength converge toward full ejection and lets a folded
    // face walk the root from one triangle to the next. Measure the UNCORRECTED
    // pose once and combine its contact patch before applying strength exactly once.
    private static Vector3 SoftChest(IReadOnlyList<ContactProbe> probes,IReadOnlyList<ContactTarget> targets,Region region,Switches switches,ProjectionSettings settings,long[] counts,ProjectionWorkspace workspace,out ChestContactInfo info)
    {
        info=default;
        workspace?.ChestContacts.Clear();
        if(!switches.BreastAbdomen || (region!=Region.Breast && region!=Region.Abdomen) || settings.ChestLimit<=0 || settings.ChestStrength<=0)return Vector3.Zero;
        var fixedContacts=workspace?.ChestContacts ?? new List<FrozenContact>();
        float allWeight=0;var center=Vector3.Zero;
        foreach(var p in probes)
        {
            if(!Geometry.Finite(p.Position) || !float.IsFinite(p.Influence) || p.Influence<=0)continue;
            float w=Math.Clamp(p.Influence,0,1);w*=w;
            center+=p.Position*w;allWeight+=w;
        }
        if(allWeight<=1e-8f)return Vector3.Zero;center/=allWeight;
        float allowance=Math.Max(0,settings.ChestCompression);
        float capture=settings.ChestCapture>0?settings.ChestCapture:settings.ChestLimit+allowance+settings.Radius;
        var chestOnly=new Switches(false,false,false,false,true);
        float contactWeight=0,maxDepth=0;
        for(int start=0;start<targets.Count;)
        {
            int end=start+1;while(end<targets.Count && targets[end].Group==targets[start].Group)end++;
            int targetRegion=(int)(region==Region.Breast?Region.Abdomen:Region.Breast);
            var targetMoment=Vector3.Zero;float targetArea=0;
            for(int i=start;i<end;i++)if(targets[i].Tree.Enabled)
            {
                targetMoment+=targets[i].Tree.RegionMoments[targetRegion];
                targetArea+=targets[i].Tree.RegionAreas[targetRegion];
            }
            var outward=targetArea>1e-8f?center-targetMoment/targetArea:Vector3.Zero;
            float outwardLength=outward.Length();
            if(outwardLength<1e-6f){start=end;continue;}
            outward/=outwardLength;
            foreach(var probe in probes)
            {
                if(!Geometry.Finite(probe.Position) || !float.IsFinite(probe.Influence) || probe.Influence<=0)continue;
                float influence=Math.Clamp(probe.Influence,0,1),weight=influence*influence;
                bool found=Nearest(targets,start,end,probe.Position,capture,chestOnly,region,out var q,out var normal,out _);
                if(!found)continue;
                // Follow faces that push away from the target region's bulk. This
                // rejects inward/back faces without requiring the source center
                // to already lie outside every locally intersecting triangle.
                if(Vector3.Dot(outward,normal)<settings.ChestNormalAlignment)continue;
                var d=probe.Position-q;float signed=Vector3.Dot(d,normal);
                float depth=-signed-allowance;
                if(depth<=0 || Vector3.Dot(d,d)-signed*signed>4*settings.Radius*settings.Radius)continue;
                float distance=Math.Min(settings.ChestLimit,depth/Math.Max(settings.ChestMinInfluence,influence));
                // Quadratic onset gives zero slope at the compression boundary.
                float onset=settings.ChestTransition>0?Math.Min(1,depth/settings.ChestTransition):1;
                fixedContacts.Add(new(normal,distance*onset,weight));contactWeight+=weight;
                maxDepth=Math.Max(maxDepth,depth);
                counts[(int)ContactKind.BreastAbdomen]++;
            }
            start=end;
        }
        // A few intersecting seam vertices cannot translate the entire breast.
        // Duplicating/reordering all samples leaves the result unchanged.
        float denominator=Math.Max(contactWeight,allWeight*Math.Clamp(settings.ChestCoverage,0,1));
        info=new(fixedContacts.Count,probes.Count,Math.Min(1,contactWeight/allWeight),maxDepth);
        if(contactWeight<=1e-8f || denominator<=1e-8f)return Vector3.Zero;
        // Relax only these frozen contact planes. A single mean of normal vectors
        // under-corrects a curved patch: sideways components cancel while overlap
        // remains. Each synchronous pass measures the remaining plane residual;
        // it never finds a new face or accumulates displacement across frames.
        var combined=Vector3.Zero;
        for(int pass=0;pass<Math.Clamp(settings.ChestIterations,1,16);pass++)
        {
            var residual=Vector3.Zero;
            foreach(var c in fixedContacts)
                residual+=c.Normal*(Math.Max(0,c.Distance-Vector3.Dot(combined,c.Normal))*c.Weight);
            combined=Geometry.Limit(combined+residual/contactWeight,settings.ChestLimit);
        }
        // Coverage and strength each apply once AFTER relaxation, so additional
        // passes cannot turn a lone seam vertex or low strength into full ejection.
        combined*=contactWeight/denominator*Math.Clamp(settings.ChestStrength,0,1);
        if(combined.LengthSquared()>1e-12f && float.IsFinite(settings.ChestConflictTolerance))
        {
            // Intersecting triangles may impose contradictory directions. Allow
            // a small configurable compression change, otherwise back off this
            // frame's whole proposal rather than walking the root to another face.
            float allowed=WorstOverlap(probes,targets,region,settings,capture,Vector3.Zero)+Math.Max(0,settings.ChestConflictTolerance);
            int tries=0;
            while(WorstOverlap(probes,targets,region,settings,capture,combined)>allowed+1e-6f)
            {
                if(++tries>6){combined=Vector3.Zero;break;}
                combined*=.5f;
            }
        }
        return combined;
    }

    internal static float WorstOverlap(IReadOnlyList<ContactProbe> probes,IReadOnlyList<ContactTarget> targets,Region region,ProjectionSettings settings,float capture,Vector3 offset)
    {
        float worst=0;var only=new Switches(false,false,false,false,true);
        foreach(var probe in probes)
        {
            if(!Geometry.Finite(probe.Position) || !float.IsFinite(probe.Influence) || probe.Influence<=0)continue;
            var p=probe.Position+offset*Math.Clamp(probe.Influence,0,1);
            for(int start=0;start<targets.Count;)
            {
                int end=start+1;while(end<targets.Count && targets[end].Group==targets[start].Group)end++;
                bool found=Nearest(targets,start,end,p,capture,only,region,out var q,out var normal,out _);start=end;
                if(!found)continue;
                var d=p-q;float signed=Vector3.Dot(d,normal);
                if(Vector3.Dot(d,d)-signed*signed<=4*settings.Radius*settings.Radius)
                    worst=Math.Max(worst,-signed-Math.Max(0,settings.ChestCompression));
            }
        }
        return worst;
    }
    internal static bool Nearest(IReadOnlyList<ContactTarget> targets,int start,int end,Vector3 p,float capture,Switches switches,Region region,out Vector3 closest,out Vector3 normal,out ContactKind kind,bool ordinary=false)
    {
        float best=capture*capture;closest=normal=default;kind=ContactKind.None;
        for(int i=start;i<end;i++)
        {
            var t=targets[i];
            if(!t.Tree.Enabled || !t.Tree.Nearest(p,MathF.Sqrt(best),ordinary?t.OrdinaryExcluded??t.Excluded:t.Excluded,switches,region,out var q,out var n,out var k))continue;
            float d=Vector3.DistanceSquared(p,q);if(d>=best)continue;
            best=d;closest=q;normal=n;kind=k;
        }
        return kind!=ContactKind.None;
    }
}
