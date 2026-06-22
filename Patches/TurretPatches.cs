using HarmonyLib;
using TemporalPanicButton.Runtime;
using UnityEngine;

namespace TemporalPanicButton.Patches
{
    /// <summary>
    /// 拦截原版炮台 / 枪雷射击。
    /// 这样陷阱开火时可以先触发时停，再把这次射击延后到世界恢复后结算。
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

                // 时停期间不让原版射线和伤害现在就发生，先把这一发记下来，恢复后再结算。
                TimeStopController.QueueShotDuringStop(info, playerGunShot, !playerGunShot);
                return false;
            }

            if (!HazardPatchUtility.IsRealTurretShot(info))
                return true;

            if (!HazardPatchUtility.TryGetAimedPlayerClientId(info, out uint aimedClientId))
                return true;

            bool triggered = aimedClientId == KrokMpBridge.LocalClientId
                ? TimeStopController.TryTrigger(
                    HazardKind.Turret,
                    info.pos,
                    () => Sound.Play("rifleshot", info.pos, true, false, null, 1f, 1f, false, false))
                : TimeStopController.TryTriggerForMultiplayerClient(aimedClientId, HazardKind.Turret, info.pos);
            if (!triggered)
                return true;

            // 成功触发时停的那发炮台子弹不能再走一次原版开火流程，要改成延后结算。
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

            Traverse turret = Traverse.Create(__instance);
            Vector2 origin = __instance.barrel == null ? (Vector2)__instance.transform.position : (Vector2)__instance.barrel.position;
            Vector2 direction = __instance.transform.right * __instance.transform.localScale.x;

            if (!__instance.didBeep)
                return !TimeStopController.IsActive;

            // TurretScript 把“即将开火”的状态藏在私有字段里，这里只复刻必要部分，
            // 目的是抓住 Shoot 真正执行前的那一帧。
            float beepTime = turret.Field("beepTime").GetValue<float>() + Time.deltaTime;
            bool willShootThisFrame = beepTime >= 0.5f && !turret.Field("didShoot").GetValue<bool>();

            if (!willShootThisFrame)
                return !TimeStopController.IsActive;

            RaycastHit2D hit = Physics2D.Raycast(origin, direction, Mathf.Infinity, LayerMask.GetMask("Body", "Limb"));
            Body targetBody = HazardPatchUtility.GetBodyFromHit(hit);
            uint targetClientId = KrokPlayerResolver.GetClientIdForBody(targetBody);
            if (targetClientId == uint.MaxValue)
                return true;

            if (!TimeStopController.IsActive)
            {
                bool triggered = targetClientId == KrokMpBridge.LocalClientId
                    ? TimeStopController.TryTrigger(
                        HazardKind.Turret,
                        origin,
                        () => Sound.Play("rifleshot", origin, true, false, null, 1f, 1f, false, false))
                    : TimeStopController.TryTriggerForMultiplayerClient(targetClientId, HazardKind.Turret, origin);
                if (!triggered)
                    return true;
            }
            else
            {
                Sound.Play("rifleshot", origin, true, false, null, 1f, 1f, false, false);
            }

            QueueTurretShot(__instance, turret, origin, direction, beepTime);
            return false;
        }

        private static void QueueTurretShot(TurretScript turretScript, Traverse turret, Vector2 origin, Vector2 direction, float beepTime)
        {
            FireInfo info = new FireInfo
            {
                pos = origin,
                dir = direction,
                ignoreTrans = turretScript.transform,
                playerDamageMultiplier = turretScript.shotPowerMultiplier
            };

            TimeStopController.QueueShotDuringStop(info);
            // 原版 Update 被拦后，仍要把装填 / 冷却相关字段补回去，保持节奏不乱。
            turret.Field("didBeep").SetValue(true);
            turret.Field("didShoot").SetValue(true);
            turret.Field("<timeSinceFired>k__BackingField").SetValue(0f);
            turret.Field("beepTime").SetValue(Mathf.Max(0.5f, beepTime));
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

            Body targetBody = HazardPatchUtility.GetBodyFromHit(hit);
            uint targetClientId = KrokPlayerResolver.GetClientIdForBody(targetBody);
            if (targetClientId == uint.MaxValue)
                return true;

            if (__instance.cooldown > 0f)
                return !TimeStopController.IsActive;

            if (!TimeStopController.IsActive)
            {
                bool triggered = targetClientId == KrokMpBridge.LocalClientId
                    ? TimeStopController.TryTrigger(
                        HazardKind.Turret,
                        origin,
                        () => Sound.Play("rifleshot", origin, true, false, null, 1f, 1f, false, false))
                    : TimeStopController.TryTriggerForMultiplayerClient(targetClientId, HazardKind.Turret, origin);
                if (!triggered)
                    return true;
            }

            // GunmineScript 的开火点在 OnWillRenderObject，不走普通 Update，所以要单独排队。
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
