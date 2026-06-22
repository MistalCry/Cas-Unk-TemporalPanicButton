using HarmonyLib;
using System.Reflection;
using TemporalPanicButton.Runtime;
using UnityEngine;

namespace TemporalPanicButton.Patches
{
    /// <summary>
    /// 兜底限制 Body 自身的更新入口。
    /// WorldFreezeService 会冻结刚体和大部分行为，但联机输入、KrokMP 回放或游戏自身流程仍可能再次进入 Body 的更新方法。
    /// 这里让 Body 也遵循“只有拥有当前时停权限的玩家可以行动”的同一套规则。
    /// </summary>
    internal static class BodyFreezePatches
    {
        private static readonly string[] BodyMethodNames =
        {
            "Update",
            "PhysicsUpdate",
            "HandleInput",
            "HandlePhysics",
            "HandleAttacks"
        };

        private static bool installed;

        public static void TryInstall(Harmony harmony)
        {
            if (installed || harmony == null)
                return;

            MethodInfo prefix = AccessTools.Method(typeof(BodyFreezePatches), nameof(BodyTimeStopPrefix));
            if (prefix == null)
                return;

            int patched = 0;
            for (int i = 0; i < BodyMethodNames.Length; i++)
                patched += PatchBodyMethod(harmony, BodyMethodNames[i], prefix);

            installed = patched > 0;
        }

        private static int PatchBodyMethod(Harmony harmony, string methodName, MethodInfo prefix)
        {
            MethodInfo original = AccessTools.Method(typeof(Body), methodName);
            if (original == null)
                return 0;

            try
            {
                harmony.Patch(original, prefix: new HarmonyMethod(prefix));
                return 1;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("Temporal Panic Button: failed to patch Body." + methodName + ": " + ex.GetType().Name);
                return 0;
            }
        }

        private static bool BodyTimeStopPrefix(Body __instance)
        {
            return TimeStopController.IsBodyEmpowered(__instance);
        }
    }

    [HarmonyPatch(typeof(Body), "Update")]
    internal static class BodyUpdateVitalsProtectionPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Body __instance)
        {
            TimeStopController.ProtectBodyVitalsDuringStop(__instance);
        }
    }
}
