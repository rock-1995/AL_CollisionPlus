using UnityEngine;

namespace ALCollisionPlus;

public sealed class CollisionDriver : MonoBehaviour
{
    public CollisionDriver(IntPtr pointer) : base(pointer) { }
    public void Update()
    {
        // The master switch must clear offsets even when cloth updates are paused.
        Plugin.Instance?.Runtime?.PollControls();
        Plugin.Instance?.UpdateDiagnostics();
    }
    public void OnGUI()
    {
        var plugin=Plugin.Instance;
        if(plugin==null || !plugin.ShowStatusOverlay.Value || plugin.Runtime==null)return;
        GUI.Box(new Rect(12,12,740,72),plugin.Runtime.StatusText);
    }
}
