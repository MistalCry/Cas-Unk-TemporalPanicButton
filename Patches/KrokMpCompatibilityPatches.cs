using System;
using System.Reflection;
using HarmonyLib;
using TemporalPanicButton.Runtime;
using UnityEngine;

namespace TemporalPanicButton.Patches
{
    /// <summary>
    /// Optional patches for KrokMP classes that are not referenced at compile time.
    /// These suppress multiplayer trap side effects when this mod has already frozen/cancelled vanilla trap logic.
    /// </summary>
    internal static class KrokMpCompatibilityPatches
    {
        private const string SoundCannonTrackerTypeName = "KrokoshaCasualtiesMP.KrokoshaSoundCannonNetworkTrackerComponent";

        private static bool installed;
        private static bool failed;

        public static void TryInstall(Harmony harmony)
        {
            if (installed || failed || harmony == null)
                return;

            if (AccessTools.TypeByName("KrokoshaCasualtiesMP.KrokoshaScavMultiplayer") == null)
                return;

            try
            {
                int patched = 0;
                // Every type name here is optional. PatchMethod returns 0 when a KrokMP build lacks it.
                patched += PatchMethod(harmony, SoundCannonTrackerTypeName, "Update", nameof(SoundCannonTrackerUpdatePrefix));
                patched += PatchMethod(harmony, "KrokoshaCasualtiesMP.JumpPadScript_OnCollisionEnter2D_MultiplayerPatch", "Postfix", nameof(SkipKrokMpTrapPatchDuringTimeStop));
                patched += PatchMethod(harmony, "KrokoshaCasualtiesMP.CoilScript_Shock_MultiplayerPatch", "Postfix", nameof(SkipKrokMpTrapPatchDuringTimeStop));
                patched += PatchMethod(harmony, "KrokoshaCasualtiesMP.BearTrap_OnCollisionEnter2D_MultiplayerPatch", "Postfix", nameof(SkipKrokMpTrapPatchDuringTimeStop));
                patched += PatchMethod(harmony, "KrokoshaCasualtiesMP.SpikeStabberScript_Stab_MultiplayerPatch", "Postfix", nameof(SkipKrokMpTrapPatchDuringTimeStop));
                patched += PatchMethod(harmony, "KrokoshaCasualtiesMP.SpikeStabberScript_CheckStab_MultiplayerPatch", "Postfix", nameof(SkipKrokMpTrapPatchDuringTimeStop));

                installed = patched > 0;
            }
            catch (Exception ex)
            {
                failed = true;
                Debug.LogWarning("Temporal Panic Button: KrokMP compatibility patch failed: " + ex);
            }
        }

        public static bool IsKrokMpSoundCannonTracker(MonoBehaviour behaviour)
        {
            return behaviour != null && behaviour.GetType().FullName == SoundCannonTrackerTypeName;
        }

        private static int PatchMethod(Harmony harmony, string typeName, string methodName, string prefixName)
        {
            Type type = AccessTools.TypeByName(typeName);
            MethodInfo original = type == null ? null : AccessTools.Method(type, methodName);
            MethodInfo prefix = AccessTools.Method(typeof(KrokMpCompatibilityPatches), prefixName);
            if (original == null || prefix == null)
                return 0;

            harmony.Patch(original, prefix: new HarmonyMethod(prefix));
            return 1;
        }

        private static bool SkipKrokMpTrapPatchDuringTimeStop()
        {
            // KrokMP postfixes often assume vanilla trap code already ran. If this mod skipped
            // vanilla trap logic during time stop, skip the matching network postfix too.
            return !TimeStopController.IsActive;
        }

        private static bool SoundCannonTrackerUpdatePrefix(MonoBehaviour __instance)
        {
            if (!TimeStopController.IsActive)
                return true;

            // KrokMP has its own sound cannon tracker; freeze that alongside the vanilla SoundCannon.
            TimeStopHazardAudio.PauseSoundCannonAudio();
            return false;
        }
    }
}
