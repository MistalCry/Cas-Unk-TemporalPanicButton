using HarmonyLib;
using TemporalPanicButton.Runtime;
using UnityEngine;

namespace TemporalPanicButton.Patches
{
    /// <summary>
    /// 地雷相关补丁。
    /// 负责在玩家踩雷时触发时停，并在时停期间阻止地雷继续倒计时或爆炸。
    /// </summary>
    [HarmonyPatch(typeof(MineScript), "OnCollisionEnter2D")]
    internal static class MineScriptOnCollisionEnter2DPatch
    {
        private static void Prefix(MineScript __instance, Collision2D collision)
        {
            if (__instance == null || collision == null)
                return;

            Rigidbody2D hitBody = collision.collider == null ? null : collision.collider.attachedRigidbody;
            if (hitBody == null || hitBody.isKinematic)
                return;

            // 联机时，只有本机真正控制的身体才有资格把这次踩雷变成本机时停。
            if (!KrokPlayerResolver.IsLocalRigidbody(hitBody))
                return;

            Body player = PlayerCamera.main == null ? null : PlayerCamera.main.body;
            if (player == null)
                return;

            Vector2 minePosition = __instance.transform.position;
            Vector2 playerPosition = player.transform.position;
            if (Vector2.Distance(minePosition, playerPosition) >= 50f)
                return;

            // 这里用 Prefix 就够了：地雷本身仍可保持上膛状态，但时停期间不再走倒计时。
            TimeStopController.TryTrigger(HazardKind.Mine, minePosition);
        }
    }

    [HarmonyPatch(typeof(MineScript), "Update")]
    internal static class MineScriptUpdatePatch
    {
        private static bool Prefix()
        {
            // 返回 false 可以直接拦住原版 Update，让已上膛地雷在时停期间不继续推进。
            return !TimeStopController.IsActive;
        }
    }
}
