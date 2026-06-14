using HarmonyLib;
using System.Collections.Generic;

namespace TemporalPanicButton.Patches
{
    /// <summary>
    /// Adds this mod's options when the game builds its normal settings list.
    /// This avoids forcing Settings initialization during plugin startup, which can break other mods' keybinds.
    /// </summary>
    [HarmonyPatch(typeof(Settings), nameof(Settings.DefaultSettings))]
    internal static class SettingsPatch
    {
        private static void Postfix(List<Setting> __result)
        {
            ModSettings.AddMissingSettings(__result);
        }
    }
}
