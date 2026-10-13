using System.Diagnostics;
using System.Text.Json;
using ALCollisionPlus;

internal static class PerformanceChecks
{
    internal static void Verify(ActorData actor,Action<bool,string> check)
    {
        long vertices=0;
        foreach(var surface in actor.Surfaces)
        {
            foreach(var group in surface.Helper._nodeWeights){group.Weights.UseSpan=false;group.Weights.IndexerReads=0;}
            check(surface.Update(),"fallback native list remains supported");
            var positions=surface.World.ToArray();var normals=surface.WorldNormals.ToArray();
            vertices+=surface.Helper._nodeWeights.Sum(g=>g.Weights.IndexerReads);
            foreach(var group in surface.Helper._nodeWeights){group.Weights.UseSpan=true;group.Weights.IndexerReads=0;}
            check(surface.Update(),"live native array span accepted");
            check(positions.SequenceEqual(surface.World) && normals.SequenceEqual(surface.WorldNormals),"native span preserves exact geometry");
            check(surface.Helper._nodeWeights.All(g=>g.Weights.IndexerReads==0),"fast path never invokes per-vertex list indexer");
        }
        var list=actor.Surfaces[0].Helper._nodeWeights[0].Weights;
        int size=list._size;list._size=size+1;
        check(!actor.Surfaces[0].Update(),"corrupt native size rejected before span access");
        list._size=size;check(actor.Surfaces[0].Update(),"valid live array recovers");
        Console.WriteLine($"PERF evidence: {vertices} per-vertex list calls -> 0 across {actor.Surfaces.Count} surfaces; same live positions and normals.");
    }
    internal static void Benchmark(ActorData source,Plugin plugin,string output)
    {
        plugin.Diagnostics.Value=false;var reports=new List<object>();
        foreach(int count in new[]{1,2,4})
        {
            var actors=Enumerable.Range(0,count).Select(i=>new ActorData(source.Human,i,plugin)).ToArray();
            foreach(var actor in actors)foreach(var node in actor.Nodes)
                for(int group=0;group<actors.Length;group++)foreach(var surface in actors[group].Surfaces)
                    node.Targets.Add(new(surface.Tree,ReferenceEquals(actor,actors[group])?surface.Exclusions.GetValueOrDefault(node.Id):null,group));
            foreach(var mode in new[]{new Switches(true,false,false),new Switches(true,true,true)})
            {
                var frame=new FrameWork();
                void Pass()
                {
                    frame.Prepare(actors,mode);frame.UpdateSurfaces();
                    foreach(var pair in frame.Nodes)
                    {
                        var node=pair.Node;node.Probes.Clear();
                        foreach(var sample in node.Samples)if(sample.Surface.Valid)node.Probes.Add(new(sample.Surface.World[sample.Vertex],sample.Weight));
                        for(int kind=1;kind<=3;kind++)if(mode.Enabled((ContactKind)kind))
                            ContactProjection.Solve(node.Probes,node.Targets,(ContactKind)kind,plugin.Settings((ContactKind)kind,pair.Actor.Height),node.Projection.Contacts);
                    }
                }
                for(int warm=0;warm<150;warm++)Pass();
                var samples=new double[5];long bytes=0;
                for(int batch=0;batch<5;batch++)
                {
                    long allocation=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();
                    for(int i=0;i<200;i++)Pass();
                    samples[batch]=(Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency/200;
                    bytes+=GC.GetAllocatedBytesForCurrentThread()-allocation;
                }
                Array.Sort(samples);var report=new{Actors=count,Mode=mode.BreastBody?"all":"abdomen",MedianMs=samples[2],MinMs=samples[0],MaxMs=samples[4],BytesPerFrame=bytes/1000,Meshes=frame.MeshUpdates,MatrixReads=frame.Poses.Reads};
                reports.Add(report);Console.WriteLine(JsonSerializer.Serialize(report));
            }
            foreach(var actor in actors)actor.Dispose();
        }
        File.WriteAllText(output,JsonSerializer.Serialize(new{Scope="Managed geometry only; Unity/IL2CPP calls are stubbed. Overlapping duplicate snapshot actors are a stress case, not an FPS measurement.",Samples=reports},new JsonSerializerOptions{WriteIndented=true}));
    }
}
