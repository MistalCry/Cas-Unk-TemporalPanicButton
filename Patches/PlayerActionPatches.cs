using HarmonyLib;
using TemporalPanicButton.Runtime;
using UnityEngine;

namespace TemporalPanicButton.Patches
{
    /// <summary>
    /// Player action patches that need special behavior while the rest of the world is frozen.
    /// </summary>
    [HarmonyPatch(typeof(Body), nameof(Body.ThrowItem))]
    internal static class BodyThrowItemPatch
    {
        private static void Prefix(Body __instance, float force, ref ThrowItemState __state)
        {
            __state = default;
            if (__instance == null || !TimeStopController.IsActive)
                return;

            Item item = __instance.GetItem(__instance.handSlot);
            if (item == null)
                return;

            float clampedForce = Mathf.Clamp01(force);
            // Capture vanilla throw velocity before the item is detached/moved by Body.ThrowItem.
            __state = new ThrowItemState(item, __instance.ThrowVelocity(item, clampedForce));
        }

        private static void Postfix(ThrowItemState __state)
        {
            if (__state.Item == null || __state.Item.rb == null || !TimeStopController.IsActive)
                return;

            Rigidbody2D rb = __state.Item.rb;
            TimeStopController.QueueThrownItemDuringStop(__state.Item, __state.Velocity, rb.angularVelocity);
            // Make the thrown object visibly hang in the frozen world until the controller releases it.
            rb.velocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.bodyType = RigidbodyType2D.Static;
        }

        private readonly struct ThrowItemState
        {
            public readonly Item Item;
            public readonly Vector2 Velocity;

            public ThrowItemState(Item item, Vector2 velocity)
            {
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
            // PlayerCamera may try to apply slow motion or restore timescale. The controller owns it.
            return !TimeStopController.IsActive;
        }
    }
}
