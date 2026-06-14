using HarmonyLib;
using TemporalPanicButton.Runtime;

namespace TemporalPanicButton.Patches
{
    /// <summary>
    /// Temporarily normalizes medical minigame penalties while Steady Medical Hands is active.
    /// Prefix saves the penalizing value, vanilla code runs at full speed, postfix restores it.
    /// </summary>
    [HarmonyPatch(typeof(MinigameBase), nameof(MinigameBase.GetMousePos))]
    internal static class MinigameBaseGetMousePosPatch
    {
        private static void Prefix(MinigameBase __instance, out float __state)
        {
            __state = 0f;
            Body body = GetBoostedBody(__instance);
            if (body == null)
                return;

            // Pain affects cursor/hand stability in several minigames.
            __state = body.averagePain;
            body.averagePain = 0f;
        }

        private static void Postfix(MinigameBase __instance, float __state)
        {
            Body body = GetBoostedBody(__instance);
            if (body != null)
                body.averagePain = __state;
        }

        private static Body GetBoostedBody(MinigameBase minigame)
        {
            return TimeStopMedicalUi.ShouldBoostMedicalMinigames() && minigame != null ? minigame.body : null;
        }
    }

    [HarmonyPatch(typeof(MinigameBase), nameof(MinigameBase.UpdateHandPhysics))]
    internal static class MinigameBaseUpdateHandPhysicsPatch
    {
        private static void Prefix(MinigameBase __instance, out float __state)
        {
            __state = 0f;
            Body body = GetBoostedBody(__instance);
            if (body == null)
                return;

            // Low consciousness slows or destabilizes hand physics; make it normal only for this call.
            __state = body.consciousness;
            if (body.consciousness < 100f)
                body.consciousness = 100f;
        }

        private static void Postfix(MinigameBase __instance, float __state)
        {
            Body body = GetBoostedBody(__instance);
            if (body != null)
                body.consciousness = __state;
        }

        private static Body GetBoostedBody(MinigameBase minigame)
        {
            return TimeStopMedicalUi.ShouldBoostMedicalMinigames() && minigame != null ? minigame.body : null;
        }
    }

    [HarmonyPatch(typeof(BandageMinigame), nameof(BandageMinigame.PhysicsUpdate))]
    internal static class BandageMinigamePhysicsUpdatePatch
    {
        private static void Prefix(BandageMinigame __instance, out float __state)
        {
            __state = 0f;
            Limb limb = GetBoostedLimb(__instance);
            if (limb == null)
                return;

            // Injuries can reduce bandage speed through this limb multiplier.
            __state = limb.bandageMinigameSpeedMult;
            if (limb.bandageMinigameSpeedMult < 1f)
                limb.bandageMinigameSpeedMult = 1f;
        }

        private static void Postfix(BandageMinigame __instance, float __state)
        {
            Limb limb = GetBoostedLimb(__instance);
            if (limb != null)
                limb.bandageMinigameSpeedMult = __state;
        }

        private static Limb GetBoostedLimb(BandageMinigame minigame)
        {
            return TimeStopMedicalUi.ShouldBoostMedicalMinigames() && minigame != null ? minigame.limb : null;
        }
    }
}
