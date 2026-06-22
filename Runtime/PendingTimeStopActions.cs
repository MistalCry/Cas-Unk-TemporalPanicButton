using System.Collections.Generic;
using UnityEngine;

namespace TemporalPanicButton.Runtime
{
    /// <summary>
    /// 保存时停期间“已经发生但还不能结算”的物理动作。
    /// 目前负责延迟射击、炮台子弹，以及投掷物在时停结束后的释放速度。
    /// </summary>
    internal sealed class PendingTimeStopActions
    {
        private readonly List<PendingShot> pendingShots = new List<PendingShot>();
        private readonly List<PendingThrow> pendingThrows = new List<PendingThrow>();

        public bool IsReplayingShots { get; private set; }
        public bool HasPendingActions => pendingShots.Count > 0 || pendingThrows.Count > 0;

        public bool QueueShot(FireInfo info)
        {
            return QueueShot(info, false, true);
        }

        public bool QueueShot(FireInfo info, bool replayOnClients, bool playDelayedSound)
        {
            if (info == null)
                return false;

            // FireInfo 可能被调用方复用或改写，入队时复制一份，避免时停结束时读到变过的参数。
            pendingShots.Add(new PendingShot(CloneFireInfo(info), replayOnClients, playDelayedSound));
            return true;
        }

        public void QueueThrownItem(Item item, Vector2 velocity, float angularVelocity)
        {
            if (item == null || item.rb == null)
                return;

            PendingThrow pendingThrow = new PendingThrow(item, velocity, angularVelocity);
            // 同一个物品在时停中可能多次被同步或重新瞄准，保留最后一次速度，恢复时才符合玩家最终出手方向。
            for (int i = 0; i < pendingThrows.Count; i++)
            {
                if (pendingThrows[i].Item != item)
                    continue;

                pendingThrows[i] = pendingThrow;
                return;
            }

            pendingThrows.Add(pendingThrow);
        }

        public bool MaintainQueuedThrow(Item item)
        {
            if (item == null || item.rb == null)
                return false;

            for (int i = 0; i < pendingThrows.Count; i++)
            {
                if (pendingThrows[i].Item != item)
                    continue;

                HoldRigidbody(item.rb);
                return true;
            }

            return false;
        }

        public bool UpdateQueuedThrowVelocityIfMoving(Item item, Vector2 velocity, float angularVelocity)
        {
            if (item == null || item.rb == null || !IsMoving(velocity, angularVelocity))
                return false;

            for (int i = 0; i < pendingThrows.Count; i++)
            {
                if (pendingThrows[i].Item != item)
                    continue;

                pendingThrows[i] = new PendingThrow(item, velocity, angularVelocity);
                return true;
            }

            return false;
        }

        public void ReleaseQueuedThrows()
        {
            for (int i = 0; i < pendingThrows.Count; i++)
            {
                PendingThrow pendingThrow = pendingThrows[i];
                Item item = pendingThrow.Item;
                if (item == null || item.rb == null)
                    continue;

                Rigidbody2D rb = item.rb;
                rb.bodyType = RigidbodyType2D.Dynamic;
                rb.velocity = pendingThrow.Velocity;
                rb.angularVelocity = pendingThrow.AngularVelocity;
                rb.WakeUp();
                KrokMpBridge.TryServerSyncItem(item, true);
            }
        }

        public void ReplayQueuedShots()
        {
            if (pendingShots.Count == 0)
                return;

            IsReplayingShots = true;
            try
            {
                for (int i = 0; i < pendingShots.Count; i++)
                {
                    PendingShot shot = pendingShots[i];
                    if (!ShouldReplayPendingShot(shot))
                        continue;

                    if (shot.PlayDelayedSound)
                        PlayDelayedShotSound(shot.Info);

                    TurretScript.Shoot(shot.Info);
                }
            }
            finally
            {
                IsReplayingShots = false;
            }
        }

        public void Clear()
        {
            pendingThrows.Clear();
            pendingShots.Clear();
            IsReplayingShots = false;
        }

        private static bool ShouldReplayPendingShot(PendingShot shot)
        {
            if (!KrokMpBridge.IsNetworkRunning)
                return true;

            // 联机中世界伤害由主机回放；客机只允许回放明确属于本地玩家的视觉预览射击。
            return KrokMpBridge.IsServer || shot.ReplayOnClients;
        }

        private static void PlayDelayedShotSound(FireInfo info)
        {
            if (info == null)
                return;

            Sound.Play("rifleshot", info.pos, true, false, null, 1f, 1f, false, false);
        }

        private static bool IsMoving(Vector2 velocity, float angularVelocity)
        {
            return velocity.sqrMagnitude > 0.0001f || Mathf.Abs(angularVelocity) > 0.0001f;
        }

        private static void HoldRigidbody(Rigidbody2D rb)
        {
            if (rb == null || rb.bodyType == RigidbodyType2D.Static)
                return;

            rb.velocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.bodyType = RigidbodyType2D.Static;
        }

        private static FireInfo CloneFireInfo(FireInfo info)
        {
            return new FireInfo
            {
                pos = info.pos,
                dir = info.dir,
                ignoreTrans = info.ignoreTrans,
                ignoreBody = info.ignoreBody,
                doTinnitus = info.doTinnitus,
                structureDamage = info.structureDamage,
                animalDamage = info.animalDamage,
                forceHead = info.forceHead,
                playerDamageMultiplier = info.playerDamageMultiplier
            };
        }

        private readonly struct PendingShot
        {
            public readonly FireInfo Info;
            public readonly bool ReplayOnClients;
            public readonly bool PlayDelayedSound;

            public PendingShot(FireInfo info, bool replayOnClients, bool playDelayedSound)
            {
                Info = info;
                ReplayOnClients = replayOnClients;
                PlayDelayedSound = playDelayedSound;
            }
        }

        private readonly struct PendingThrow
        {
            public readonly Item Item;
            public readonly Vector2 Velocity;
            public readonly float AngularVelocity;

            public PendingThrow(Item item, Vector2 velocity, float angularVelocity)
            {
                Item = item;
                Velocity = velocity;
                AngularVelocity = angularVelocity;
            }
        }
    }
}
