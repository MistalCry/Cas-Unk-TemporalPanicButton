using HarmonyLib;
using TemporalPanicButton.Runtime;
using UnityEngine;

namespace TemporalPanicButton.Patches
{
    /// <summary>
    /// Intercepts vanilla turret-style shots so trap gunfire can start time stop and resume later.
    /// </summary>
    [HarmonyPatch(typeof(TurretScript), nameof(TurretScript.Shoot))]
    internal static class TurretScriptShootPatch
    {
        private static bool Prefix(FireInfo info)
        {
            if (info == null)
                return true;

            if (TimeStopController.IsReplayingPendingShots)
                return true;

            if (TimeStopController.IsActive)
            {
                bool playerGunShot = !HazardPatchUtility.IsRealTurretShot(info);
                if (playerGunShot)
                    TimeStopBulletPreview.Add(info);

                // While time is stopped, do not let the vanilla raycast/damage happen now.
                // Store the shot so it resolves when the world resumes.
                TimeStopController.QueueShotDuringStop(info, playerGunShot, !playerGunShot);
                return false;
            }

            if (!HazardPatchUtility.IsRealTurretShot(info))
                return true;

            bool triggered = TimeStopController.TryTrigger(HazardKind.Turret, info.pos);
            if (!triggered)
                return true;

            // A trap shot that successfully starts time stop should be delayed, not fired twice.
            TimeStopController.QueueShotDuringStop(info);
            return false;
        }
    }

    [HarmonyPatch(typeof(TurretScript), "Update")]
    internal static class TurretScriptUpdatePatch
    {
        private static bool Prefix(TurretScript __instance)
        {
            if (__instance == null || TimeStopController.IsReplayingPendingShots)
                return true;

            if (!__instance.didBeep)
                return !TimeStopController.IsActive;

            Traverse turret = Traverse.Create(__instance);
            // TurretScript keeps the "about to shoot" state in private fields, so this patch
            // mirrors just enough of Update to catch the exact frame before Shoot would run.
            float beepTime = turret.Field("beepTime").GetValue<float>() + Time.deltaTime;
            bool willShootThisFrame = beepTime >= 0.5f && !turret.Field("didShoot").GetValue<bool>();

            if (!willShootThisFrame)
                return !TimeStopController.IsActive;

            Vector2 origin = __instance.barrel == null ? (Vector2)__instance.transform.position : (Vector2)__instance.barrel.position;
            if (!TimeStopController.IsActive && !TimeStopController.TryTrigger(HazardKind.Turret, origin))
                return true;

            FireInfo info = new FireInfo
            {
                pos = origin,
                dir = __instance.transform.right * __instance.transform.localScale.x,
                ignoreTrans = __instance.transform,
                playerDamageMultiplier = __instance.shotPowerMultiplier
            };

            TimeStopController.QueueShotDuringStop(info);
            // Preserve vanilla reload/cooldown semantics after suppressing the original Update body.
            turret.Field("didShoot").SetValue(true);
            turret.Field("<timeSinceFired>k__BackingField").SetValue(0f);
            turret.Field("beepTime").SetValue(beepTime);
            return false;
        }
    }

    [HarmonyPatch(typeof(GunmineScript), "OnWillRenderObject")]
    internal static class GunmineScriptOnWillRenderObjectPatch
    {
        private static bool Prefix(GunmineScript __instance)
        {
            if (__instance == null || TimeStopController.IsReplayingPendingShots)
                return true;

            Vector2 origin = __instance.transform.position;
            Vector2 direction = __instance.transform.up;
            RaycastHit2D hit = Physics2D.Raycast(origin, direction, 1f, LayerMask.GetMask("Body", "Limb"));

            if (!hit)
                return !TimeStopController.IsActive;

            if (__instance.cooldown > 0f)
                return !TimeStopController.IsActive;

            if (!TimeStopController.IsActive && !TimeStopController.TryTrigger(HazardKind.Turret, origin))
                return true;

            // GunmineScript shoots from OnWillRenderObject, so it needs its own queue path.
            FireInfo info = new FireInfo
            {
                pos = (Vector2)__instance.transform.position + (Vector2)__instance.transform.up * 0.25f,
                dir = __instance.transform.up,
                ignoreTrans = __instance.transform
            };

            TimeStopController.QueueShotDuringStop(info);
            Traverse.Create(__instance).Field("didReload").SetValue(false);
            __instance.cooldown = 6f;
            return false;
        }
    }
}
