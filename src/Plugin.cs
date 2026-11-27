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
    public const string Id = "local.al.collisionplus", Version = "0.3.7";
    internal static Plugin Instance;
    private Harmony harmony;
    private CollisionDriver driver;
    internal NativeCollisionRuntime Runtime;
    internal ConfigEntry<bool> Enabled, BreastAbdomen, BreastBody, BreastGround, BreastHair, Diagnostics, ShowStatusOverlay;
    internal ConfigEntry<int> MaxCharacters, GroundMask;
    internal ConfigEntry<float> RadiusScale, LimitRatio, GroundNormal;

    public override void Load()
    {
        Instance = this;
        const string switches = "01 Switches / \u78b0\u649e\u5f00\u5173";
        Enabled = Config.Bind(switches, "Enabled", true, "Enable native collisions. Disabling restores the original breast physics settings and removes colliders added by this plugin.");
        BreastAbdomen = Config.Bind(switches, "BreastAbdomen", true, "Breast–abdomen contact. Feeds spheres that follow the belly shape into the game's breast spring solver.");
        BreastBody = Config.Bind(switches, "BreastBody", true, "Breast contact with other body parts. Uses native spheres and capsules and their entry direction for arms, palms, legs, and other characters' breasts. Two short capsules per palm follow the hand size.");
        BreastGround = Config.Bind(switches, "BreastGround", true, "Breast–ground contact. Finds support surfaces near the breasts and lets native plane colliders handle the response.");
        BreastHair = Config.Bind(switches, "BreastHair", true, "Breasts can push their own character's hair and other characters' hair. Hair does not push the breasts.");
        RadiusScale = Config.Bind("02 Native Physics / \u539f\u751f\u7269\u7406", "BreastRadiusScale", 1f,
            NumericSetting.Description("Breast collision sphere multiplier. Values at or below 0 disable added collision. A value of 1 fits one main sphere per breast to the body mesh, with its center allowed to differ from the native bone point. The fit is measured on initialization, a body mesh change, or a native settings refresh.", 0, 2));
        LimitRatio = Config.Bind("02 Native Physics / \u539f\u751f\u7269\u7406", "CollisionLimitHeightRatio", .06f,
            NumericSetting.Description("Combined contact displacement limit as a fraction of character height, applied to spheres, capsules, and ground. Values at or below 0 remove this limit. Native free motion is unaffected.", .001f, .15f));
        MaxCharacters = Config.Bind("03 Performance / \u6027\u80fd", "MaxCharacters", 4,
            NumericSetting.Description("Maximum number of loaded high-detail characters to attach colliders to. Values at or below 0 disable added collision.", 1, 12));
        GroundMask = Config.Bind("04 Advanced / \u9ad8\u7ea7", "GroundLayerMask", -5, "Unity layer mask for ground queries. Excludes characters and triggers.");
        GroundNormal = Config.Bind("02C Breast Ground / \u80f8\u4e0e\u5730\u9762", "BreastGroundMinNormalY", .35f,
            NumericSetting.Description("Minimum upward component of a support surface normal. Uses the existing configuration value.", .05f, 1));
        Diagnostics = Config.Bind("04 Advanced / \u9ad8\u7ea7", "Diagnostics", false, "Log switches, character binding, breast sphere preparation, arm selection, contacts, native updates, and measured displacement every five seconds to BepInEx/LogOutput.log. Failed steps include their reason.");
        ShowStatusOverlay = Config.Bind("04 Advanced / \u9ad8\u7ea7", "ShowStatusOverlay", false, "Show native collision attachment status.");
        try
        {
        Runtime = new NativeCollisionRuntime(this);
        harmony = new Harmony(Id);
        NativeSpringBinding.Install(harmony, message => Log.LogInfo("[Native spring] " + message));
        NativeBodyContacts.Install(harmony);
        // Sample after MMD has written this frame's pose, but before Magica
        // captures transforms. The previous solve has completed its native jobs.
        harmony.Patch(AccessTools.Method(typeof(ClothManager), nameof(ClothManager.ClothUpdate)),
            prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeNativeSolve)) { priority = Priority.Last, after = new[] { "local.al.mmd" } },
            postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterNativeSolve)));
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
    private static void AfterNativeSolve() => Instance?.Runtime?.CaptureBreastContacts();
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
