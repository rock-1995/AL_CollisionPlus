using System.Numerics;

namespace ALCollisionPlus;

// Exact nearest-triangle queries for the denser body surface. Topology is built
// once; only bounds are refitted when new skinned positions are supplied.
internal sealed class BreastSurfaceIndex
{
    private struct Node
    {
        internal Vector3 Low, High;
        internal int Left, Right, Start, Count;
    }

    private readonly int[] triangles, order;
    private readonly Node[] nodes;
    private Vector3[] vertices;

    internal BreastSurfaceIndex(Vector3[] rest, int[] triangles)
    {
        this.triangles = triangles;
        order = Enumerable.Range(0, triangles.Length / 3).ToArray();
        var centers = new Vector3[order.Length];
        for (int i = 0; i < centers.Length; i++)
            centers[i] = (rest[triangles[i*3]] + rest[triangles[i*3+1]] + rest[triangles[i*3+2]]) / 3;
        var built = new List<Node>();
        int Build(int start, int count)
        {
            int index = built.Count;
            built.Add(new Node { Start = start, Count = count });
            if (count <= 8) return index;
            var low = new Vector3(float.PositiveInfinity); var high = new Vector3(float.NegativeInfinity);
            for (int i = start; i < start + count; i++)
            { low = Vector3.Min(low, centers[order[i]]); high = Vector3.Max(high, centers[order[i]]); }
            var size = high - low;
            int axis = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;
            float Axis(Vector3 p) => axis == 0 ? p.X : axis == 1 ? p.Y : p.Z;
            Array.Sort(order, start, count, Comparer<int>.Create((a,b) => Axis(centers[a]).CompareTo(Axis(centers[b]))));
            int half = count / 2, left = Build(start, half), right = Build(start + half, count - half);
            built[index] = new Node { Left = left, Right = right };
            return index;
        }
        if (order.Length > 0) Build(0, order.Length);
        nodes = built.ToArray();
    }

    internal void Refit(Vector3[] world)
    {
        vertices = world;
        for (int i = nodes.Length - 1; i >= 0; i--)
        {
            ref var node = ref nodes[i];
            if (node.Count == 0)
            {
                node.Low = Vector3.Min(nodes[node.Left].Low, nodes[node.Right].Low);
                node.High = Vector3.Max(nodes[node.Left].High, nodes[node.Right].High);
                continue;
            }
            var low = new Vector3(float.PositiveInfinity); var high = new Vector3(float.NegativeInfinity);
            for (int j = node.Start; j < node.Start + node.Count; j++)
                for (int corner = 0; corner < 3; corner++)
                {
                    var p = world[triangles[order[j]*3+corner]];
                    low = Vector3.Min(low, p); high = Vector3.Max(high, p);
                }
            node.Low = low; node.High = high;
        }
    }

    internal float Clearance(Vector3 center)
        => Query(center, -1);

    // When a candidate cannot beat the current fitted radius, its exact
    // distance is unnecessary. A triangle within that radius proves this.
    internal float ClearanceAbove(Vector3 center, float threshold)
        => Query(center, threshold >= 0 ? threshold * threshold : -1);

    private float Query(Vector3 center, float cutoff)
    {
        if (nodes.Length == 0) return 0;
        float squared = float.PositiveInfinity, signed = 0;
        int nearest = int.MaxValue;
        Search(0, center, cutoff, ref squared, ref signed, ref nearest);
        return float.IsFinite(squared) ? MathF.Sqrt(squared) * (signed <= 0 ? 1 : -1) : 0;
    }

    private static float DistanceSquared(Vector3 p, in Node node)
        => Vector3.Max(Vector3.Max(node.Low - p, Vector3.Zero), p - node.High).LengthSquared();

    private bool Search(int index, Vector3 p, float cutoff, ref float squared, ref float signed, ref int nearest)
    {
        ref var node = ref nodes[index];
        if (DistanceSquared(p, node) > squared) return false;
        if (node.Count == 0)
        {
            int first = node.Left, second = node.Right;
            if (DistanceSquared(p, nodes[first]) > DistanceSquared(p, nodes[second])) (first, second) = (second, first);
            return Search(first, p, cutoff, ref squared, ref signed, ref nearest) ||
                Search(second, p, cutoff, ref squared, ref signed, ref nearest);
        }
        for (int i = node.Start; i < node.Start + node.Count; i++)
        {
            int triangle = order[i];
            var a = vertices[triangles[triangle*3]];
            var b = vertices[triangles[triangle*3+1]];
            var c = vertices[triangles[triangle*3+2]];
            var delta = p - BreastSurfaceFit.Closest(p, a, b, c);
            float distance = delta.LengthSquared();
            // Tie-breaking matches a scan in original triangle order, including
            // the signed result on shared edges and duplicate triangles.
            if (distance < squared || (distance == squared && triangle < nearest))
            {
                squared = distance; nearest = triangle;
                signed = Vector3.Dot(delta, Vector3.Cross(b - a, c - a));
                if (squared <= cutoff) return true;
            }
        }
        return false;
    }
}
