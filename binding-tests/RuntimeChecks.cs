using ALCollisionPlus;
using Character;
using UnityEngine;
using V=System.Numerics.Vector3;

internal static class RuntimeChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var root=new Transform{name="root"};var breast=new Transform{name="breast",parent=root};
        var belly=new Transform{name="belly",parent=root,localPosition=new(2,3,4)};
        var thigh=new Transform{name="thigh",parent=root,localPosition=new(5,6,7)};
        var hair=new Transform{name="hair",parent=root,localPosition=new(8,9,10)};
        var setting=new HumanMagicaSpringSetting{Entries=new[]{new HumanMagicaSpringSetting.Entry{Spring=new(){SerializeData=new(){rootBones=new(){breast}}}}}};
        setting.Additional[HumanMagicaSpringSetting.Type.Belly]=new[]{new HumanMagicaSpringSetting.Entry{Spring=new(){SerializeData=new(){rootBones=new(){belly}}}}};
        setting.Additional[HumanMagicaSpringSetting.Type.Leg]=new[]{new HumanMagicaSpringSetting.Entry{Spring=new(){SerializeData=new(){rootBones=new(){thigh}}}}};
        var objects=new GameObject();
        SkinnedCollisionHelper Surface(string name,Transform bone,Vector3[] points)
        {
            var mesh=new Mesh{vertices=points,triangles=new[]{0,1,2},boneWeights=points.Select(_=>new BoneWeight{weight0=1}).ToArray()};
            var renderer=new SkinnedMeshRenderer{name=name,bones=new[]{bone},sharedMesh=mesh,transform=new(){parent=root}};
            var helper=new SkinnedCollisionHelper{_isInit=true,_meshCalc=mesh,_skinnedMeshRenderer=renderer,_newVert=points,
                _nodeWeights=new[]{new SkinnedCollisionHelper.WeightList{Transform=root,Weights=points.Select((p,i)=>new SkinnedCollisionHelper.VertexWeight{Index=i,Weight=1,LocalPosition=p}).ToList()}}};
            renderer.Helper=helper;objects.Components.Add(helper);return helper;
        }
        var source=Surface("o_hit_mune",breast,new[]{new Vector3(-.1f,.2f,0),new Vector3(0,-.04f,0),new Vector3(.1f,.2f,0)});
        Surface("o_hit_hara",belly,new[]{new Vector3(-1,0,-1),new Vector3(0,0,1),new Vector3(1,0,-1)});
        Surface("o_hit_leg",thigh,new[]{new Vector3(-1,0,-1),new Vector3(0,0,1),new Vector3(1,0,-1)});
        var human=new Human{Transform=root,GameObject=objects,Body=new(){objHitBody=objects,magicaSpringSetting=setting},Hair=new(){Hairs=new[]{new HairData{Obj=new GameObject()}}}};
        var saved=Human._list;Human._list=new(){human};
        var plugin=new Plugin();plugin.Load();plugin.Diagnostics.Value=true;
        var runtime=plugin.Runtime;var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        var floor=new Collider();
        RaycastHit Hit(Collider c,float y=0)=>new(){collider=c,point=new(0,y,0),normal=new(0,1,0)};
        void Set(int bits)
        {
            plugin.Enabled.Value=false;runtime.PollControls();
            plugin.BreastAbdomen.Value=(bits&1)!=0;plugin.BreastBody.Value=(bits&2)!=0;plugin.BreastGround.Value=(bits&4)!=0;
            plugin.Enabled.Value=true;runtime.PollControls();breast.position=new();
        }
        int[] Counts()=>(int[])typeof(CollisionRuntime).GetField("currentContacts",flags).GetValue(runtime);
        void Step(){runtime.Restore();Time.frameCount++;Time.unscaledTime+=.016f;Time.unscaledDeltaTime=1f/60;runtime.Step();}
        Time.unscaledTime=200;float reference=0;
        for(int bits=0;bits<8;bits++)
        {
            Set(bits);Physics.Hits=new[]{Hit(floor)};Step();
            if(bits==0){check(breast.position.y==0,"no categories, no write");continue;}
            if(bits==1)reference=breast.position.y;
            check(reference>0 && Math.Abs(breast.position.y-reference)<1e-6f,"same soft response for each category and duplicate contacts");
            for(int i=0;i<3;i++)check((Counts()[i]>0)==((bits&(1<<i))!=0),"actual runtime routes independent switches");
            check(belly.position.x==2 && belly.position.y==3 && thigh.position.x==5 && thigh.position.y==6 && hair.position.y==9,"non-breast transforms never written");
        }
        var actors=(Dictionary<IntPtr,ActorData>)typeof(CollisionRuntime).GetField("actors",flags).GetValue(runtime);
        check(actors[human.Pointer].Nodes.Count==1 && actors[human.Pointer].Nodes[0].Bone==breast,"body spring roots used only for region labels, never active sources");
        var owned=actors[human.Pointer].Surfaces.Single(s=>s.Renderer.name=="o_hit_mune");
        check(owned.Exclusions[breast.GetInstanceID()].All(v=>v),"own weighted surface excluded from new breast-body query");
        var originals=source._nodeWeights[0].Weights.ToArray();
        for(int i=0;i<source._nodeWeights[0].Weights.Count;i++)
        {var p=source._nodeWeights[0].Weights[i];p.LocalPosition.y+=.05f;source._nodeWeights[0].Weights[i]=p;}
        Set(7);Physics.Hits=new[]{Hit(floor)};Step();
        check(breast.position.y==0 && Counts().All(n=>n==0),"actual runtime does not push before touching, for all three kinds");
        source._nodeWeights[0].Weights=originals.ToList();
        var person=new Collider{transform=new(){parent=root}};var trigger=new Collider{isTrigger=true};
        foreach(var hits in new[]{new[]{Hit(person)},new[]{Hit(trigger)},new[]{Hit(floor,2)},
            new[]{new RaycastHit{collider=floor,point=new(0,0,0),normal=new(1,0,0)}},Enumerable.Repeat(Hit(floor),64).ToArray()})
        {
            Set(4);Physics.Hits=hits;Step();check(breast.position.y==0,"ground rejects characters, triggers, distant surfaces, walls and saturated hit buffer");
        }
        Set(4);Physics.Hits=new[]{Hit(trigger),Hit(person),Hit(floor)};Step();check(breast.position.y>0,"valid floor still works among rejected hits");
        Set(4);Physics.Hits=Array.Empty<RaycastHit>();int rays=Physics.RayQueries,bounds=Physics.BoundsQueries;Step();
        check(Physics.RayQueries==rays && Physics.BoundsQueries==bounds+1,"one empty ground bound skips all individual rays");
        Set(4);Physics.Hits=new[]{Hit(person),Hit(trigger)};rays=Physics.RayQueries;Step();
        check(Physics.RayQueries==rays,"character-only ground bounds skip rays");
        Set(4);Physics.Hits=Enumerable.Repeat(Hit(person),64).ToArray();rays=Physics.RayQueries;Step();
        check(Physics.RayQueries>rays,"saturated bounds fall back to rays, never assume empty");
        Set(1);Physics.Hits=Array.Empty<RaycastHit>();Step();
        var node=actors[human.Pointer].Nodes[0];
        var probes=node.Samples.Select(s=>new ContactProbe(s.Surface.World[s.Vertex],s.Weight)).ToArray();
        foreach(var surface in actors[human.Pointer].Surfaces)RegressionAdapter.Register(surface.Tree,surface.Triangles);
        var settings=plugin.Settings(ContactKind.BreastAbdomen,actors[human.Pointer].Height);
        var oldTarget=RegressionAdapter.OldAbdomen(probes,node.Targets.ToArray(),settings);
        var oldStep=Geometry.SmoothLimited(V.Zero,oldTarget,1-MathF.Exp(-settings.Response/60),settings.MaxSpeed/60,settings.Limit*settings.Strength);
        check(V.Distance(ALCollisionPlus.Convert.Vec(breast.position),oldStep)<1e-6f,"abdomen-only runtime smoothing matches previous behavior");
        // Removing all contacts must unwind the displacement with the same bound.
        for(int i=0;i<source._nodeWeights[0].Weights.Count;i++)
        {var p=source._nodeWeights[0].Weights[i];p.LocalPosition.y+=.05f;source._nodeWeights[0].Weights[i]=p;}
        float previous=breast.position.y;
        for(int i=0;i<180;i++)
        {
            Step();float now=breast.position.y;
            check(Math.Abs(now-previous)<=settings.MaxSpeed/60+1e-6f,"production response remains bounded during release");previous=now;
        }
        check(Math.Abs(previous)<1e-5f,"production displacement returns to zero");
        source._nodeWeights[0].Weights=originals.ToList();
        plugin.UpdateDiagnostics();check(Application.AuditSubscriptions==1,"explicit diagnostic opt-in attaches callback");
        plugin.Diagnostics.Value=false;plugin.ShowStatusOverlay.Value=false;plugin.UpdateDiagnostics();
        check(Application.AuditSubscriptions==0,"diagnostics off detaches callback");
        long beforeFrames=(int)typeof(CollisionRuntime).GetField("timedFrames",flags).GetValue(runtime);
        int logCount=plugin.Log.Messages.Count,boxes=GUI.Boxes;
        Set(1);Step();runtime.ObserveBeforeRender();new CollisionDriver(IntPtr.Zero).OnGUI();
        check(V.Distance(ALCollisionPlus.Convert.Vec(breast.position),oldStep)<1e-6f,"quiet release produces identical collision motion");
        check((int)typeof(CollisionRuntime).GetField("timedFrames",flags).GetValue(runtime)==beforeFrames,"quiet release skips performance timer accumulation");
        check(plugin.Log.Messages.Count==logCount && GUI.Boxes==boxes,"quiet release writes no debug log and draws no overlay");
        plugin.Unload();Physics.Hits=Array.Empty<RaycastHit>();Human._list=saved;
    }
}
