using MagicaCloth2;
using UnityEngine;

namespace ALCollisionPlus;

// Two short native capsules across each palm approximate a rounded plate.
// Fixed estimates in hand-local units: no mesh reads or per-frame fitting.
// The game's hand scale bones already carry height and HandW adjustments.
internal sealed class NativePalmColliders : IDisposable
{
    internal readonly List<ColliderComponent> Colliders = new(4);

    internal NativePalmColliders(GameObject skeleton)
    {
        try
        {
            foreach (var bone in skeleton.GetComponentsInChildren<Transform>(true))
            {
                string name = bone.name;
                if (name != "cf_s_hand_L" && name != "cf_s_hand_R") continue;
                float direction = name.EndsWith("_L", StringComparison.Ordinal) ? -1 : 1;
                for (int i = 0; i < 2; i++)
                {
                    var go = new GameObject("ALCollisionPlus_Palm" + (direction < 0 ? "L" : "R") + i);
                    go.transform.SetParent(bone, false);
                    go.transform.localPosition = new Vector3(direction * (i == 0 ? .12f : .35f), -.025f, -.02f);
                    var capsule = go.AddComponent<MagicaCapsuleCollider>();
                    Colliders.Add(capsule);
                    capsule.direction = (MagicaCapsuleCollider.Direction)2;
                    capsule.alignedOnCenter = true;
                    capsule.reverseDirection = false;
                    capsule.radiusSeparation = false;
                    capsule.center = Vector3.zero;
                    capsule.SetSize(.12f, .12f, .5f);
                    capsule.UpdateParameters();
                }
            }
        }
        catch { Dispose(); throw; }
    }

    public void Dispose()
    {
        foreach (var collider in Colliders)
            if (collider != null) { collider.enabled = false; UnityEngine.Object.Destroy(collider.gameObject); }
        Colliders.Clear();
    }
}
