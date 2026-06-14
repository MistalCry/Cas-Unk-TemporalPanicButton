using HarmonyLib;

namespace TemporalPanicButton.Patches
{
    /// <summary>
    /// Supplies English fallback labels/descriptions for settings when the game has no locale entry.
    /// </summary>
    [HarmonyPatch(typeof(Locale), nameof(Locale.GetOther))]
    internal static class LocalePatch
    {
        private static void Postfix(string str, ref string __result)
        {
            // Locale.GetOther returns the key itself when it cannot find a translation.
            if (__result != str)
                return;

            if (ModSettings.TryGetLocaleFallback(str, out string fallback))
                __result = fallback;
        }
    }
}
