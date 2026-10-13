using System.Globalization;

namespace BepInEx
{
    public class BepInPlugin:Attribute{public BepInPlugin(string id,string name,string version){}}
    public class BepInDependency:Attribute{public enum DependencyFlags{SoftDependency}public BepInDependency(string id,DependencyFlags flags){}}
    public class BepInProcess:Attribute{public BepInProcess(string name){}}
}
namespace BepInEx.Configuration
{
    public record ConfigDefinition(string Section,string Key);
    public class ConfigDescription{public string Description;public object Range;public object[] Tags;public ConfigDescription(string text,object range=null,params object[] tags){Description=text;Range=range;Tags=tags;}}
    public class AcceptableValueRange<T>{public T Min,Max;public AcceptableValueRange(T min,T max){Min=min;Max=max;}}
    public class ConfigEntryBase
    {
        public ConfigDefinition Definition;public ConfigDescription Description;public Type SettingType;
        public object BoxedValue,DefaultValue;
    }
    public class ConfigEntry<T>:ConfigEntryBase{public T Value{get=>(T)BoxedValue;set=>BoxedValue=value;}}
    public class SettingChangedEventArgs:EventArgs{public ConfigEntryBase ChangedSetting;}
    public class ConfigFile
    {
        public event EventHandler<SettingChangedEventArgs> SettingChanged;
        public void Raise(ConfigEntryBase entry)=>SettingChanged?.Invoke(this,new(){ChangedSetting=entry});
        public readonly Dictionary<ConfigDefinition,ConfigEntryBase> Entries=new();
        public ConfigEntry<T> Bind<T>(string section,string key,T value,string text)=>Bind(section,key,value,new ConfigDescription(text));
        public ConfigEntry<T> Bind<T>(string section,string key,T value,ConfigDescription text)
        {
            var def=new ConfigDefinition(section,key);var entry=new ConfigEntry<T>{Definition=def,BoxedValue=value,DefaultValue=value,Description=text,SettingType=typeof(T)};
            Entries.Add(def,entry);return entry;
        }
    }
}
namespace BepInEx.Unity.IL2CPP
{
    public class TestLog
    {
        public readonly List<string> Messages=new();
        public void LogInfo(string text){Messages.Add(text);Console.WriteLine(text);}
        public void LogWarning(string text)=>throw new Exception(text);
        public void LogError(string text)=>throw new Exception(text);
    }
    public class BasePlugin
    {
        public readonly BepInEx.Configuration.ConfigFile Config=new();public readonly TestLog Log=new();
        public virtual void Load(){}public virtual bool Unload()=>true;
        protected T AddComponent<T>() => (T)Activator.CreateInstance(typeof(T),IntPtr.Zero);
    }
}
namespace HarmonyLib
{
    public class Harmony{public Harmony(string id){}public void Patch(object method,HarmonyMethod prefix=null,HarmonyMethod postfix=null){}public void UnpatchSelf(){}}
    public class HarmonyMethod{public HarmonyMethod(Type t,string name){}}
}
namespace MagicaCloth2
{
    public class ClothManager{public static void OnEarlyClothUpdate(){}public static void ClothUpdate(){}public static void OnAfterLateUpdate(){}}
}
namespace UnityEngine
{
    public class MonoBehaviour:Object{public MonoBehaviour(IntPtr p){}}
    public readonly record struct Rect(int X,int Y,int Width,int Height);
    public static class GUI{public static int Boxes;public static void Box(Rect rect,string text){Boxes++;}}
    public static class Application{public static int AuditSubscriptions;public static void add_onBeforeRender(Events.UnityAction action){AuditSubscriptions++;}public static void remove_onBeforeRender(Events.UnityAction action){AuditSubscriptions--;}}
}
namespace UnityEngine.Events
{
    public class UnityAction{public static implicit operator UnityAction(Action action)=>new();}
}

internal static class ConfigAudit
{
    internal static void Export(string oldPath,string destination,bool quiet=false)
    {
        var plugin=new ALCollisionPlus.Plugin();plugin.Load();
        var old=new Dictionary<BepInEx.Configuration.ConfigDefinition,string>();string section="";
        foreach(var line in File.ReadAllLines(oldPath))
        {
            string text=line.Trim();if(text.StartsWith("[")){section=text[1..^1];continue;}
            int equals=text.IndexOf('=');if(equals<0 || text.StartsWith('#'))continue;
            old.Add(new(section,text[..equals].Trim()),text[(equals+1)..].Trim());
        }
        int preserved=0;
        var lines=new List<string>{$"## AL Collision Plus {ALCollisionPlus.Plugin.Version}","## 三个胸部碰撞类型；胸腹保留当前值；新两项默认开启。"};
        foreach(var group in plugin.Config.Entries.Values.GroupBy(e=>e.Definition.Section))
        {
            lines.Add("");lines.Add($"[{group.Key}]");
            foreach(var entry in group)
            {
                if(old.TryGetValue(entry.Definition,out var value))
                {
                    entry.BoxedValue=System.Convert.ChangeType(value,entry.SettingType,CultureInfo.InvariantCulture);preserved++;
                }
                if(quiet && entry.Definition.Key is "Diagnostics" or "ShowStatusOverlay")entry.BoxedValue=false;
                lines.Add("");lines.Add("## "+entry.Description.Description);
                lines.Add("# Setting type: "+entry.SettingType.Name);
                lines.Add("# Default value: "+System.Convert.ToString(entry.DefaultValue,CultureInfo.InvariantCulture));
                lines.Add(entry.Definition.Key+" = "+System.Convert.ToString(entry.BoxedValue,CultureInfo.InvariantCulture).ToLowerInvariant());
            }
        }
        if(preserved!=20 && preserved!=47)throw new Exception($"Unexpected preserved key count {preserved}");
        if(plugin.Config.Entries.Count!=47)throw new Exception($"Unexpected new key count {plugin.Config.Entries.Count}");
        File.WriteAllLines(destination,lines,new System.Text.UTF8Encoding(false));
        Console.WriteLine($"Config export: {preserved} original keys preserved, {plugin.Config.Entries.Count} active keys; old body/hair keys removed.");
    }
}
