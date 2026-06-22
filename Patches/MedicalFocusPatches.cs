using HarmonyLib;
using TemporalPanicButton.Runtime;

namespace TemporalPanicButton.Patches
{
    /// <summary>
    /// 在 `Steady Medical Hands` 开启时，临时抹平医疗小游戏里的惩罚值。
    /// 做法是先保存原值，再让原版代码按“正常状态”运行，最后把现场恢复回去。
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

            // 疼痛会影响多个医疗小游戏里的鼠标/手部稳定性。
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

            // 低意识会拖慢或扰乱手部物理，这里只在本次调用中临时修正。
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

            // 受伤会通过这个倍率拖慢绷带处理速度。
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
