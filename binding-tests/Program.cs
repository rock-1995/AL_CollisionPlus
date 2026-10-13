using System.Text.Json;
using ALCollisionPlus;
using Character;
using UnityEngine;
using V=System.Numerics.Vector3;
using M=System.Numerics.Matrix4x4;

int checks=0;
void Check(bool v,string reason){checks++;if(!v)throw new Exception(reason);}
float[] Floats(JsonElement v)=>v.EnumerateArray().Select(x=>x.GetSingle()).ToArray();
Vector3 Vec(JsonElement v){var a=Floats(v);return new(a[0],a[1],a[2]);}
M Matrix(JsonElement v){var a=Floats(v);return new(a[0],a[1],a[2],a[3],a[4],a[5],a[6],a[7],a[8],a[9],a[10],a[11],a[12],a[13],a[14],a[15]);}
if(args[0] is "--export-config" or "--export-release-config"){ConfigAudit.Export(args[1],args[2],args[0]=="--export-release-config");return;}
var document=JsonDocument.Parse(File.ReadAllText(args[0]));var capture=document.RootElement;
var root=new Transform{name="Character"};var objectRoot=new GameObject();
var byPath=new Dictionary<string,Transform>();var byName=new Dictionary<string,Transform>();
Transform Bone(string path)
{
    // Snapshot paths are depth-limited; normalize their common rig suffix.
    int pivot=path.IndexOf("cf_j_root/",StringComparison.Ordinal);if(pivot>=0)path=path[pivot..];
    if(byPath.TryGetValue(path,out var t))return t;
    int slash=path.LastIndexOf('/');t=new Transform{name=slash<0?path:path[(slash+1)..],parent=slash<0?root:Bone(path[..slash])};
    byPath[path]=t;byName[t.name]=t;return t;
}
foreach(var mesh in capture.GetProperty("meshes").EnumerateArray())foreach(var p in mesh.GetProperty("nativeBonePaths").EnumerateArray())if(p.GetString() is string path && path!="")Bone(path);
var rootPaths=capture.GetProperty("meshes")[0].GetProperty("breastPhysics").GetProperty("rootPaths").EnumerateArray().Select(p=>p.GetString()).ToArray();
var body=new HumanBody{objHitBody=objectRoot,magicaSpringSetting=new(){Entries=rootPaths.Select(p=>new HumanMagicaSpringSetting.Entry{Spring=new(){name="BustSpring",SerializeData=new(){rootBones=new(){Bone(p)}}}}).ToArray()}};
var human=new Human{Body=body,GameObject=objectRoot,Transform=root};
var expected=new Dictionary<string,V[]>();
foreach(var h in capture.GetProperty("collision").GetProperty("helpers").EnumerateArray())
{
    var name=h.GetProperty("name").GetString();if(!name.StartsWith("o_hit_"))continue;
    var bones=h.GetProperty("boneNames").EnumerateArray().Select(n=>byName.TryGetValue(n.GetString(),out var t)?t:Bone(n.GetString())).ToArray();
    var boneMatrices=h.GetProperty("boneToWorldRowMajor").EnumerateArray().ToArray();for(int i=0;i<bones.Length;i++)bones[i].Matrix=Matrix(boneMatrices[i]);
    var weights=h.GetProperty("weights").EnumerateArray().Select(w=>{var a=Floats(w);return new BoneWeight{boneIndex0=(int)a[0],weight0=a[1],boneIndex1=(int)a[2],weight1=a[3],boneIndex2=(int)a[4],weight2=a[5],boneIndex3=(int)a[6],weight3=a[7]};}).ToArray();
    var mesh=new Mesh{name=name,vertices=h.GetProperty("vertices").EnumerateArray().Select(Vec).ToArray(),triangles=h.GetProperty("triangles").EnumerateArray().Select(x=>x.GetInt32()).ToArray(),boneWeights=weights};
    var renderer=new SkinnedMeshRenderer{name=name,sharedMesh=mesh,bones=bones,transform=new Transform{Matrix=Matrix(h.GetProperty("rendererToWorldRowMajor")),parent=root}};
    var calculated=h.GetProperty("calculatedVertices").EnumerateArray().Select(Vec).ToArray();
    // The saved native output is represented as a carrier-local cache, matching pregnancy's dynamic-cache contract.
    var helper=new SkinnedCollisionHelper{_isInit=true,_meshCalc=new Mesh(),_skinnedMeshRenderer=renderer,_newVert=calculated,
        _nodeWeights=new[]{new SkinnedCollisionHelper.WeightList{Transform=renderer.transform,Weights=calculated.Select((v,i)=>new SkinnedCollisionHelper.VertexWeight{Index=i,Weight=1,LocalPosition=v}).ToList()}}};
    renderer.Helper=helper;objectRoot.Components.Add(helper);
    expected[name]=calculated.Select(v=>V.Transform(ALCollisionPlus.Convert.Vec(v),renderer.transform.Matrix)).ToArray();
}
var plugin=new Plugin();plugin.Load();
using var actor=new ActorData(human,1,plugin);
if(args.Contains("--benchmark")){PerformanceChecks.Benchmark(actor,plugin,args[^1]);return;}
var breasts=actor.Nodes.Where(n=>n.Region==Region.Breast).ToArray();
Check(breasts.Length==2,"Native BoneSpring roots must bind even with fixed/no-collision proxy attributes");
Check(breasts.All(b=>b.Samples.Count>0),"Each physical breast root must own real surface samples");
Check(actor.Surfaces.Count>=14 && actor.Surfaces.All(s=>s.UsesNativeCache),"Real native collider cache discovery");
foreach(var s in actor.Surfaces)for(int i=0;i<s.World.Length;i++)Check(V.Distance(s.World[i],expected[s.Renderer.name][i])<1e-4,"native cache world-space reconstruction");
var belly=actor.Surfaces.Single(s=>s.Renderer.name=="o_hit_hara_f");
V nativeBefore=belly.World[0];var w0=belly.Helper._nodeWeights[0].Weights[0];w0.LocalPosition.y+=.123f;belly.Helper._nodeWeights[0].Weights[0]=w0;belly.Update();
Check(V.Distance(belly.World[0],nativeBefore)>.05f,"Live cache content changes must update even when cache/mesh identities stay the same");
w0.LocalPosition.y-=.123f;belly.Helper._nodeWeights[0].Weights[0]=w0;belly.Update();
var invalid=w0;invalid.Index=999999;belly.Helper._nodeWeights[0].Weights[0]=invalid;
Check(!belly.Update()&&!belly.Tree.Enabled,"invalid initialized native cache must fail closed, never flatten pregnancy with a renderer fallback");
belly.Helper._nodeWeights[0].Weights[0]=w0;Check(belly.Update(),"native-cache recovery after valid input restored");
Check(belly.Renderer.sharedMesh.vertices[0].y==belly.Source.vertices[0].y,"native cache reader must not mutate source meshes");
foreach(var surface in actor.Surfaces)RegressionAdapter.Register(surface.Tree,surface.Triangles);
int comparisons=0,contactCases=0;
foreach(var node in breasts)
{
    var targets=actor.Surfaces.Select(s=>new ContactTarget(s.Tree,s.Exclusions.GetValueOrDefault(node.Id))).ToArray();
    var center=node.Samples.Select(s=>s.Surface.World[s.Vertex]).Aggregate(V.Zero,(a,b)=>a+b)/node.Samples.Count;
    var bellyCenter=belly.World.Aggregate(V.Zero,(a,b)=>a+b)/belly.World.Length;var toward=V.Normalize(bellyCenter-center);
    foreach(float shift in new[]{0f,.25f,.5f,1f})foreach(float strength in new[]{.20524018f,1f})foreach(bool guard in new[]{false,true})
    {
        var probes=node.Samples.Select(s=>new ContactProbe(s.Surface.World[s.Vertex]+toward*shift,s.Weight)).ToArray();
        var settings=plugin.Settings(ContactKind.BreastAbdomen,actor.Height) with{Strength=strength,ConflictTolerance=guard?actor.Height*.007117904f:float.PositiveInfinity};
        var actual=ContactProjection.Solve(probes,targets,ContactKind.BreastAbdomen,settings,new());
        var expectedOffset=RegressionAdapter.OldAbdomen(probes,targets,settings);
        Check(V.Distance(actual.Offset,expectedOffset)<1e-6f,"captured pregnant mesh preserves established abdomen output");
        Check(actual.Offset.Length()<=settings.Limit*settings.Strength+1e-5f,"captured correction bounded");
        comparisons++;if(actual.Offset.Length()>1e-6f)contactCases++;
    }
}
Check(contactCases>0,"snapshot regression exercises active pregnant abdomen contacts");
Console.WriteLine($"Snapshot: {comparisons} identical old/new abdomen outputs; {contactCases} nonzero; {breasts.Length} breast roots, {breasts.Sum(n=>n.Samples.Count)} probes.");

// Actual Plugin.Load and ContactConfig are compiled here, with host APIs stubbed.
Check(plugin.Config.Entries.Count==47,"only requested active configuration keys are bound");
Check(plugin.Config.Entries.Keys.All(d=>!d.Key.Contains("Hair") && d.Key!="BodyBody" && d.Key!="BodyGround"),"no obsolete settings");
var aConfig=plugin.Settings(ContactKind.BreastAbdomen,1);
Check(aConfig.Limit==.09859826f && aConfig.Compression==.0013973799f && aConfig.Coverage==.2f && aConfig.Response==8 && aConfig.MaxSpeed==.06f,"established defaults baked into actual binding source");
Check(plugin.Switches==new Switches(true,true,true),"three features enabled by default");
Check(!plugin.Diagnostics.Value && !plugin.ShowStatusOverlay.Value && !plugin.TrackStatistics,"release defaults have no diagnostics or overlay");
Check(Application.AuditSubscriptions==0,"release has no before-render diagnostic callback");
foreach(var setting in new[]{plugin.Diagnostics,plugin.ShowStatusOverlay})
    Check(setting.Description.Tags.Any(tag=>tag.GetType().GetField("Browsable").GetValue(tag).Equals(false)),"debug settings hidden from configuration UI");
var bodyConfig=plugin.Settings(ContactKind.BreastBody,1);
Check(bodyConfig==aConfig,"body uses same soft-contact defaults as abdomen");

// Source pruning and per-pass mesh caches still use production FrameWork.
var frame=new FrameWork();
for(int mask=0;mask<8;mask++)
{
    var switches=new Switches((mask&1)!=0,(mask&2)!=0,(mask&4)!=0);
    frame.Prepare(new[]{actor},switches);frame.UpdateSurfaces();
    Check(frame.Nodes.Count==(mask==0?0:2),"only breast roots enter solve list");
    Check(frame.Nodes.All(n=>n.Node.Region==Region.Breast),"no writable body or hair sources");
    if(mask==0)Check(frame.MeshUpdates==0 && frame.ClothChecks==0,"all-off skips mesh and cloth reads");
    if(mask==1)Check(frame.MeshUpdates<actor.Surfaces.Count,"abdomen-only still prunes unrelated targets");
    if(mask==4)Check(frame.MeshUpdates==actor.Surfaces.Count(s=>breasts.Any(b=>b.Samples.Any(p=>p.Surface==s))),"ground-only skins source meshes without body targets");
}
foreach(var s in actor.Surfaces)s.Update();

// Master/category switch restores owned poses on Update, including paused physics.
var runtime=plugin.Runtime;
var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
var runtimeActors=(Dictionary<IntPtr,ActorData>)typeof(CollisionRuntime).GetField("actors",flags).GetValue(runtime);
runtimeActors.Add(human.Pointer,actor);runtime.PollControls();var bn=breasts[0];
for(int i=0;i<8;i++)
{
    bn.SavedLocal=new(1,2,3);bn.AppliedLocal=new(2,2,3);bn.Bone.localPosition=new(2,2,3);bn.Applied=true;bn.OffsetLocal=new(.1f,0,0);bn.MotionKinds=7;
    plugin.Enabled.Value=false;Check(!runtime.PollControls(),"master off immediate");
    Check(!bn.Applied && bn.OffsetLocal==V.Zero && bn.MotionKinds==0 && bn.Bone.localPosition.x==1,"owned pose and pending correction reset");
    Check(runtime.StatusText.Contains("OFF") && runtime.StatusText.Contains("Chest target: 0.000%"),"overlay cleared");
    plugin.Enabled.Value=true;Check(runtime.PollControls(),"master on");
}
bn.SavedLocal=new(1,2,3);bn.AppliedLocal=new(2,2,3);bn.Bone.localPosition=new(8,2,3);bn.Applied=true;
plugin.Enabled.Value=false;runtime.PollControls();Check(bn.Bone.localPosition.x==8,"do not undo another plugin pose");
plugin.Enabled.Value=true;runtime.PollControls();
foreach(var entry in new[]{plugin.BreastAbdomen,plugin.BreastBody,plugin.BreastGround})
{
    bn.SavedLocal=new(1,2,3);bn.AppliedLocal=new(2,2,3);bn.Bone.localPosition=new(2,2,3);bn.Applied=true;bn.OffsetLocal=new(.1f,0,0);
    entry.Value=false;runtime.PollControls();Check(!bn.Applied && bn.OffsetLocal==V.Zero && bn.Bone.localPosition.x==1,"each individual toggle clears pending offsets");
}
Check(!runtime.PollControls(),"all three off");
Check(runtime.StatusText.Contains("Abdomen: False [0]") && runtime.StatusText.Contains("Body: False [0]") && runtime.StatusText.Contains("Ground: False [0]"),"three switches independently visible");
RuntimeChecks.Run(Check);
PerformanceChecks.Verify(actor,Check);
Console.WriteLine($"PASS binding/config/native-cache/replay/runtime: {checks} checks. Snapshot version {capture.GetProperty("version").GetString()}; no game launched or operated.");
