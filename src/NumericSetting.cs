using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;

namespace ALCollisionPlus;

// Recognized by the installed ConfigurationManager through its tag convention.
internal sealed class ConfigurationManagerAttributes
{
    public Action<ConfigEntryBase> CustomDrawer;
}

internal sealed class NumericSetting
{
    private readonly float minimum, maximum;
    private string text;
    private object shown;
    private NumericSetting(float minimum, float maximum) { this.minimum = minimum; this.maximum = maximum; }

    internal static ConfigDescription Description(string description, float minimum, float maximum)
    {
        var editor = new NumericSetting(minimum, maximum);
        return new ConfigDescription(description + " The slider limits mouse input. Typed values can exceed this range.", null,
            new ConfigurationManagerAttributes { CustomDrawer = editor.Draw });
    }

    internal static bool TryParse(string text, Type type, out object value)
    {
        value = null;
        if (type == typeof(int) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer))
        { value = integer; return true; }
        if (type == typeof(float) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float number) && float.IsFinite(number))
        { value = number; return true; }
        return false;
    }

    private void Draw(ConfigEntryBase entry)
    {
        // IMGUI also runs these controls for Layout/Repaint and when a window
        // is reopened. Those events may update control state, never config.
        var input = Event.current?.rawType ?? EventType.Ignore;
        bool sliderInput = input == EventType.MouseDown || input == EventType.MouseDrag ||
            input == EventType.KeyDown || input == EventType.ScrollWheel;
        bool textInput = input == EventType.KeyDown || input == EventType.ExecuteCommand;
        var current = entry.BoxedValue;
        if (!Equals(current, shown)) { text = Convert.ToString(current, CultureInfo.InvariantCulture); shown = current; }
        // Clamp only the displayed handle. A repaint must never write its
        // clamped position back over an out-of-slider-range typed value.
        float handle = Math.Clamp(Convert.ToSingle(current, CultureInfo.InvariantCulture), minimum, maximum);
        bool previousChanged = GUI.changed; GUI.changed = false;
        float next = GUILayout.HorizontalSlider(handle, minimum, maximum, GUILayout.ExpandWidth(true));
        bool sliderChanged = GUI.changed; GUI.changed |= previousChanged;
        if (sliderInput && sliderChanged && !Mathf.Approximately(next, handle))
        {
            entry.BoxedValue = entry.SettingType == typeof(int) ? (object)(int)MathF.Round(next) : next;
            shown = entry.BoxedValue; text = Convert.ToString(shown, CultureInfo.InvariantCulture);
        }
        string edited = GUILayout.TextField(text ?? "", GUILayout.Width(90));
        if (!textInput || edited == text) return;
        text = edited;
        if (TryParse(edited, entry.SettingType, out var parsed)) { entry.BoxedValue = parsed; shown = entry.BoxedValue; }
    }
}
