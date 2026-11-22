using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using MagicaCloth2;
using UnityEngine;

namespace ALCollisionPlus;

[BepInPlugin(Id, "AL Collision Plus", Version)]
[BepInDependency("local.al.pregnancy.preview", BepInDependency.DependencyFlags.SoftDependency)]
[BepInProcess("AmanatsuLocation.exe")]
public sealed class Plugin : BasePlugin
{
    public const string Id = "local.al.collisionplus", Version = "0.2.0";
    internal static Plugin Instance;
    private Harmony harmony;
    private CollisionDriver driver;
    internal NativeCollisionRuntime Runtime;
    internal ConfigEntry<bool> Enabled, BreastAbdomen, BreastBody, BreastGround, Diagnostics, ShowStatusOverlay;
    internal ConfigEntry<int> MaxCharacters, GroundMask;
    internal ConfigEntry<float> RadiusRatio, LimitRatio, GroundNormal;

    public override void Load()
    {
        Instance = this;
        const string switches = "01 Switches / \u78b0\u649e\u5f00\u5173";
        Enabled = Config.Bind(switches, "Enabled", true, "Enable native collisions. Disabling restores the original breast physics settings and removes colliders added by this plugin.");
        BreastAbdomen = Config.Bind(switches, "BreastAbdomen", true, "Breast–abdomen contact. Feeds spheres that follow the belly shape into the game's breast spring solver.");
        BreastBody = Config.Bind(switches, "BreastBody", true, "Breast contact with other body parts. Uses the character's existing native colliders.");
        BreastGround = Config.Bind(switches, "BreastGround", true, "Breast–ground contact. Finds support surfaces near the breasts and lets native plane colliders handle the response.");
        RadiusRatio = Config.Bind("02 Native Physics / \u539f\u751f\u7269\u7406", "BreastRadiusHeightRatio", .012f,
            new ConfigDescription("Native breast contact radius as a fraction of character height. This applies to the native sphere colliders.", new AcceptableValueRange<float>(.001f, .05f)));
        LimitRatio = Config.Bind("02 Native Physics / \u539f\u751f\u7269\u7406", "CollisionLimitHeightRatio", .06f,
            new ConfigDescription("Native BoneSpring sphere collision displacement limit as a fraction of character height. Capsules and planes still use the game's spring limits.", new AcceptableValueRange<float>(.001f, .15f)));
        MaxCharacters = Config.Bind("03 Performance / \u6027\u80fd", "MaxCharacters", 4,
            new ConfigDescription("Maximum number of loaded high-detail characters to attach colliders to.", new AcceptableValueRange<int>(1, 12)));
        GroundMask = Config.Bind("04 Advanced / \u9ad8\u7ea7", "GroundLayerMask", -5, "Unity layer mask for ground queries. Excludes characters and triggers.");
        GroundNormal = Config.Bind("02C Breast Ground / \u80f8\u4e0e\u5730\u9762", "BreastGroundMinNormalY", .35f,
            new ConfigDescription("Minimum upward component of a support surface normal. Uses the existing configuration value.", new AcceptableValueRange<float>(.05f, 1)));
        Diagnostics = Config.Bind("04 Advanced / \u9ad8\u7ea7", "Diagnostics", false, "Log native collider counts and preparation times for in-game diagnostics.");
        ShowStatusOverlay = Config.Bind("04 Advanced / \u9ad8\u7ea7", "ShowStatusOverlay", false, "Show native collision attachment status.");
        try
        {
        Runtime = new NativeCollisionRuntime(this);
        harmony = new Harmony(Id);
        NativeSpringBinding.Install(harmony, message => Log.LogInfo("[Native spring] " + message));
        // The previous solve completes its jobs before returning. This common boundary
        // works for both BeforeLateUpdate and AfterLateUpdate game configurations.
        harmony.Patch(AccessTools.Method(typeof(ClothManager), nameof(ClothManager.ClothUpdate)),
            prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeNativeSolve)) { priority = Priority.First });
        driver = AddComponent<CollisionDriver>();
        Log.LogInfo($"AL Collision Plus {Version}: native BoneSpring collision enabled; no managed triangle solver or bone offsets. Pregnancy fluid collision remains native.");
        }
        catch
        {
            Unload();
            throw;
        }
    }

    private static void BeforeNativeSolve() => Instance?.Runtime?.Step();
    public override bool Unload()
    {
        Runtime?.Dispose();
        harmony?.UnpatchSelf();
        if (driver != null) UnityEngine.Object.Destroy(driver);
        NativeSpringBinding.ClearState();
        Instance = null;
        return true;
    }
}

public sealed class CollisionDriver : MonoBehaviour
{
    public CollisionDriver(IntPtr pointer) : base(pointer) { }
    public void Update() => Plugin.Instance?.Runtime?.PollDisabled();
    public void OnGUI()
    {
        var plugin = Plugin.Instance;
        if (plugin == null || !plugin.ShowStatusOverlay.Value || plugin.Runtime == null) return;
        GUI.Box(new Rect(12, 12, 740, 54), plugin.Runtime.Status);
    }
}
