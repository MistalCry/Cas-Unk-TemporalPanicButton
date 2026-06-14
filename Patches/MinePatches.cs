using HarmonyLib;
using TemporalPanicButton.Runtime;
using UnityEngine;

namespace TemporalPanicButton.Patches
{
    /// <summary>
    /// Mine support: trigger time stop on player contact and freeze the mine countdown during stop.
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

            Body player = PlayerCamera.main == null ? null : PlayerCamera.main.body;
            if (player == null)
                return;

            Vector2 minePosition = __instance.transform.position;
            Vector2 playerPosition = player.transform.position;
            if (Vector2.Distance(minePosition, playerPosition) >= 50f)
                return;

            // Prefix is enough here: the mine's own collision logic can still arm, but its
            // Update countdown is blocked while time is stopped.
            TimeStopController.TryTrigger(HazardKind.Mine, minePosition);
        }
    }

    [HarmonyPatch(typeof(MineScript), "Update")]
    internal static class MineScriptUpdatePatch
    {
        private static bool Prefix()
        {
            // Returning false prevents armed mines from counting down or exploding during time stop.
            return !TimeStopController.IsActive;
        }
    }
}
