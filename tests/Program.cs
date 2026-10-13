using ALCollisionPlus;
using System.Numerics;

int checks=0;
void Check(bool valid,string message){checks++;if(!valid)throw new Exception(message);}
void Near(Vector3 a,Vector3 b,string message,float tolerance=1e-5f)=>Check(Vector3.Distance(a,b)<tolerance,$"{message}: {a} != {b}");
var settings=new ContactSettings(.003126201f,.09859826f,1,.0013973799f,.2f,.30131006f,.003f,.103777304f,.77074236f,float.PositiveInfinity,8,8,.06f);
var contacts=new List<FrozenContact>();
for(int mask=0;mask<8;mask++)
{
    var switches=new Switches((mask&1)!=0,(mask&2)!=0,(mask&4)!=0);
    foreach(Region source in Enum.GetValues<Region>())foreach(Region target in Enum.GetValues<Region>())
    {
        bool expected=source==Region.Breast && (target==Region.Abdomen?switches.BreastAbdomen:switches.BreastBody);
        Check((switches.Pair(source,target)!=ContactKind.None)==expected,"independent region routing");
        Check(switches.Source(source)==(source==Region.Breast && mask!=0),"only breast can be written");
    }
}
SurfaceTree Plane(Region region,float y=0)
{
    var vertices=new[]{new Vector3(-1,y,-1),new Vector3(0,y,1),new Vector3(1,y,-1)};
    var indices=new[]{0,1,2};var tree=new SurfaceTree(vertices,indices,new[]{region});
    RegressionAdapter.Register(tree,indices);return tree;
}
ContactProbe[] Probes(float depth)=>new[]{new ContactProbe(new(0,-depth,0),1),new ContactProbe(new(.1f,.2f,0),1),new ContactProbe(new(-.1f,.2f,0),1)};
var abdomen=Plane(Region.Abdomen);var body=Plane(Region.Other);
ContactResult Solve(ContactKind kind,ContactProbe[] probes,ContactSettings? tuning=null,bool excluded=false)
    =>ContactProjection.Solve(probes,new[]{new ContactTarget(kind==ContactKind.BreastAbdomen?abdomen:body,new[]{excluded})},kind,tuning??settings,contacts,
        (p,c)=>new GroundContact(new(p.X,0,p.Z),Vector3.UnitY));
foreach(var kind in new[]{ContactKind.BreastAbdomen,ContactKind.BreastBody,ContactKind.BreastGround})
{
    Check(Solve(kind,Probes(-.00001f)).Offset==Vector3.Zero,$"{kind}: positive gap never triggers correction");
    Check(Solve(kind,Probes(.001f)).Offset==Vector3.Zero,$"{kind}: allowed compression stays soft");
    var hit=Solve(kind,Probes(.04f));
    Check(hit.Offset.Y>.038f && hit.Offset.X==0 && hit.Offset.Z==0,$"{kind}: genuine penetration produces outward correction");
    Near(Solve(kind,Probes(.04f),settings with{Strength=.2f}).Offset,hit.Offset*.2f,$"{kind}: strength applied once");
    Near(Solve(kind,Probes(.04f).Concat(Probes(.04f)).Reverse().ToArray()).Offset,hit.Offset,$"{kind}: density/order invariant");
    Check(Solve(kind,Probes(.04f),settings with{Strength=0}).Offset==Vector3.Zero,$"{kind}: strength zero");
    if(kind!=ContactKind.BreastGround)Check(Solve(kind,Probes(.04f),excluded:true).Offset==Vector3.Zero,$"{kind}: own skin excluded");
}
Check(Solve(ContactKind.BreastGround,new[]{new ContactProbe(new(0,-.03f,0),1)}).Offset==Vector3.Zero,"entire breast below an overhead platform must not be pulled toward it");
var ground=ContactProjection.Solve(Probes(.04f),Array.Empty<ContactTarget>(),ContactKind.BreastGround,settings,contacts,(p,c)=>new GroundContact(Vector3.Zero,Vector3.UnitX));
Check(ground.Offset==Vector3.Zero,"wall is not ground");
// Nearest eligible target must never let the new body toggle replace disabled abdomen.
var wrong=ContactProjection.Solve(Probes(.04f),new[]{new ContactTarget(abdomen,null)},ContactKind.BreastBody,settings,contacts);
Check(wrong.Offset==Vector3.Zero,"body solver excludes abdomen regardless of geometry");
var beyondEdge=Probes(.04f).Select(p=>p with{Position=p.Position+new Vector3(4,0,0)}).ToArray();
Check(Solve(ContactKind.BreastBody,beyondEdge,settings with{Capture=10}).Offset==Vector3.Zero,"no open-face extension at distant body edges");

// Compare against the actual 0.1.8 implementation, including enabled guard,
// low influence, search limits, partial coverage and curved contact normals.
var random=new Random(981);
int regressions=0,nonzero=0;
for(int scene=0;scene<100;scene++)
{
    var vertices=new List<Vector3>();var triangles=new List<int>();var regions=new List<Region>();
    for(int face=0;face<24;face++)
    {
        float angle=(face-12)*.09f;var rotation=Matrix4x4.CreateRotationZ(angle);
        var center=new Vector3((face-12)*.025f,random.NextSingle()*.02f,0);
        foreach(var v in new[]{new Vector3(-.04f,0,-.1f),new Vector3(0,0,.1f),new Vector3(.04f,0,-.1f)})
        {triangles.Add(vertices.Count);vertices.Add(Vector3.TransformNormal(v,rotation)+center);}
        regions.Add(face%4==0?Region.Other:Region.Abdomen);
    }
    var tree=new SurfaceTree(vertices.ToArray(),triangles.ToArray(),regions.ToArray());RegressionAdapter.Register(tree,triangles.ToArray());
    var targets=new[]{new ContactTarget(tree,null)};
    var probes=Enumerable.Range(0,80).Select(i=>new ContactProbe(new((random.NextSingle()-.5f)*.5f,(random.NextSingle()-.45f)*.18f,0),.15f+random.NextSingle()*.85f)).ToArray();
    foreach(float strength in new[]{.2f,1f})foreach(int passes in new[]{1,8,16})foreach(float guard in new[]{float.PositiveInfinity,0f,.007f})
    {
        var s=settings with{Strength=strength,Iterations=passes,ConflictTolerance=guard};
        var current=ContactProjection.Solve(probes,targets,ContactKind.BreastAbdomen,s,contacts).Offset;
        var old=RegressionAdapter.OldAbdomen(probes,targets,s);
        Near(current,old,"abdomen numerical compatibility",1e-6f);regressions++;if(current.Length()>1e-6f)nonzero++;
    }
}
Check(nonzero>100,"regression comparison must exercise actual corrections");

// Three types must not triple the displacement for one direction, and
// contradictory proposals must remain finite and cancel without attraction.
ContactResult Result(Vector3 p)=>new(p,1,1,1,p.Length());
Near(ContactProjection.Merge(new[]{Result(new(0,.04f,0)),Result(new(0,.04f,0)),Result(new(0,.04f,0))},.1f),new(0,.04f,0),"duplicate directions never multiply displacement");
Near(ContactProjection.Merge(new[]{Result(new(0,.04f,0)),Result(new(0,-.04f,0))},.1f),Vector3.Zero,"opposed proposals cancel");
var merged=ContactProjection.Merge(new[]{Result(new(0,.1f,0)),Result(new(.1f,0,0))},.1f);
Check(merged.Length()<=.100001f && merged.X>0 && merged.Y>0,"combined cap is shared, both valid directions retained");
foreach(int fps in new[]{30,60,144})
{
    var current=Vector3.Zero;
    for(int frame=0;frame<fps*10;frame++)
    {
        var target=frame<fps*3?new Vector3(0,.1f,0):frame<fps*6?new Vector3(.1f,0,0):Vector3.Zero;
        var next=Geometry.SmoothLimited(current,target,1-MathF.Exp(-8f/fps),.06f/fps,.09859826f);
        Check(Vector3.Distance(next,current)<=.06f/fps+1e-6f,"movement rate bound across contact changes");
        Check(next.Length()<=.098599f,"overall displacement bound");current=next;
    }
    Check(current.Length()<1e-5f,"smooth return after separation");
}
Console.WriteLine($"PASS geometry: {checks} checks, {regressions} old/new abdomen comparisons ({nonzero} nonzero), all 8 switch combinations and 3 motion frame rates.");
