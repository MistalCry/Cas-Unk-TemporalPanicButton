using HarmonyLib;
using TemporalPanicButton.Runtime;

namespace TemporalPanicButton.Patches
{
    /// <summary>
    /// Hooks visual update points used by the short time-stop pose and Observer placement effect.
    /// </summary>
    [HarmonyPatch(typeof(Body), "HandleVisuals")]
    internal static class BodyHandleVisualsPatch
    {
        private static void Prefix(Body __instance)
        {
            // Apply pose just before vanilla visuals run, then restore immediately after.
            TimeStopPoseVisual.PrepareAttackPose(__instance);
        }

        private static void Postfix(Body __instance)
        {
            TimeStopPoseVisual.FinishAttackPose(__instance);
        }
    }

    [HarmonyPatch(typeof(Observer), "Update")]
    internal static class ObserverUpdatePatch
    {
        private static void Postfix()
        {
            // Observer owns its transform in Update, so reposition it after vanilla logic.
            TimeStopPoseVisual.ApplyObserver();
        }
    }
}
