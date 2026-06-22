using HarmonyLib;
using TemporalPanicButton.Runtime;
using UnityEngine;

namespace TemporalPanicButton.Patches
{
    /// <summary>
    /// 玩家动作补丁。
    /// 这些动作在时停中不能简单冻结，需要记录原本的物理结果，并在时停结束后再释放。
    /// </summary>
    [HarmonyPatch(typeof(Body), nameof(Body.ThrowItem))]
    internal static class BodyThrowItemPatch
    {
        private static void Prefix(Body __instance, float force, ref ThrowItemState __state)
        {
            __state = default;
            if (__instance == null || !TimeStopController.IsActive || !TimeStopController.IsBodyEmpowered(__instance))
                return;

            Item item = __instance.GetItem(__instance.handSlot);
            if (item == null)
                return;

            float clampedForce = Mathf.Clamp01(force);
            // 在原版 ThrowItem 把物品脱手/移动前，先算出它原本应该获得的投掷速度。
            __state = new ThrowItemState(__instance, item, __instance.ThrowVelocity(item, clampedForce));
        }

        private static void Postfix(ThrowItemState __state)
        {
            if (__state.Body == null || __state.Item == null || __state.Item.rb == null || !TimeStopController.IsActive || !TimeStopController.IsBodyEmpowered(__state.Body))
                return;

            Rigidbody2D rb = __state.Item.rb;
            TimeStopController.QueueThrownItemDuringStop(__state.Item, __state.Velocity, rb.angularVelocity);
            // 物品已经脱手，但世界仍在时停；把它固定在空中，等待控制器统一释放。
            if (rb.bodyType != RigidbodyType2D.Static)
            {
                rb.velocity = Vector2.zero;
                rb.angularVelocity = 0f;
                rb.bodyType = RigidbodyType2D.Static;
            }

            KrokMpBridge.TryServerSyncItem(__state.Item, true);
        }

        private readonly struct ThrowItemState
        {
            public readonly Body Body;
            public readonly Item Item;
            public readonly Vector2 Velocity;

            public ThrowItemState(Body body, Item item, Vector2 velocity)
            {
                Body = body;
                Item = item;
                Velocity = velocity;
            }
        }
    }

    [HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.SetTimeScale))]
    internal static class PlayerCameraSetTimeScalePatch
    {
        private static bool Prefix()
        {
            // PlayerCamera 会尝试改 timeScale；时停期间统一由 TimeStopController 接管。
            return !TimeStopController.IsActive;
        }
    }
}
