using System;
using TemporalPanicButton.Runtime;
using UnityEngine;

namespace TemporalPanicButton.Patches
{
    /// <summary>
    /// 陷阱补丁共用工具。
    /// 这里集中处理“当前能不能让陷阱继续跑”“炮台射线打中了谁”等判断，避免每个补丁重复写一份。
    /// </summary>
    internal static class HazardPatchUtility
    {
        public static bool AllowTrapLogic()
        {
            return !TimeStopController.IsActive;
        }

        public static bool IsRealTurretShot(FireInfo info)
        {
            if (info == null || info.ignoreTrans == null)
                return false;

            // 玩家枪械和炮台都会进入 TurretScript.Shoot。
            // ignoreTrans 所在父级能区分这次射击到底是不是炮台/枪雷发出的。
            return info.ignoreTrans.GetComponentInParent<TurretScript>() != null;
        }

        public static bool IsAimingAtLocalPlayer(FireInfo info)
        {
            return TryGetAimedBody(info, out Body body) && KrokPlayerResolver.IsLocalBody(body);
        }

        public static bool TryGetAimedPlayerClientId(FireInfo info, out uint clientId)
        {
            clientId = uint.MaxValue;
            if (!TryGetAimedBody(info, out Body body))
                return false;

            clientId = KrokPlayerResolver.GetClientIdForBody(body);
            return clientId != uint.MaxValue;
        }

        public static bool TryGetAimedBody(FireInfo info, out Body body)
        {
            body = null;
            if (info == null || info.dir.sqrMagnitude <= 0.0001f)
                return false;

            RaycastHit2D hit = Physics2D.Raycast(info.pos, info.dir.normalized, Mathf.Infinity, LayerMask.GetMask("Body", "Limb"));
            body = GetBodyFromHit(hit);
            return body != null && KrokPlayerResolver.GetClientIdForBody(body) != uint.MaxValue;
        }

        public static Body GetBodyFromHit(RaycastHit2D hit)
        {
            if (!hit || hit.collider == null)
                return null;

            Body body = hit.collider.GetComponent<Body>();
            if (body != null)
                return body;

            Limb limb = hit.collider.GetComponent<Limb>();
            if (limb != null)
                return limb.body;

            Rigidbody2D rigidbody = hit.collider.attachedRigidbody;
            if (rigidbody == null)
                return null;

            body = rigidbody.GetComponent<Body>();
            if (body != null)
                return body;

            limb = rigidbody.GetComponent<Limb>();
            return limb == null ? null : limb.body;
        }

        public static Exception FilterCancelledTrapException(Exception exception)
        {
            if (exception == null)
                return null;

            if (!TimeStopController.IsActive || !IsKrokMpException(exception))
                return exception;

            // 本模组取消原版陷阱逻辑后，KrokMP 的 postfix 有时仍会继续执行并报错。
            // 只在时停期间吞掉 KrokMP 侧的连带异常，其他异常仍然保留，方便发现真实问题。
            return null;
        }

        private static bool IsKrokMpException(Exception exception)
        {
            string stack = exception.StackTrace;
            if (!string.IsNullOrEmpty(stack) && stack.IndexOf("KrokoshaCasualtiesMP", StringComparison.Ordinal) >= 0)
                return true;

            return exception.TargetSite != null &&
                   exception.TargetSite.DeclaringType != null &&
                   exception.TargetSite.DeclaringType.FullName != null &&
                   exception.TargetSite.DeclaringType.FullName.StartsWith("KrokoshaCasualtiesMP.", StringComparison.Ordinal);
        }
    }
}
