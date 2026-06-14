using System.Collections.Generic;
using UnityEngine;

namespace TemporalPanicButton.Runtime
{
    /// <summary>
    /// Stores physical actions that should look frozen during time stop and resolve on resume.
    /// Currently covers delayed gun/turret shots and thrown item release velocity.
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

            // Clone FireInfo because some callers reuse or mutate the object after Shoot returns.
            pendingShots.Add(new PendingShot(CloneFireInfo(info), replayOnClients, playDelayedSound));
            return true;
        }

        public void QueueThrownItem(Item item, Vector2 velocity, float angularVelocity)
        {
            if (item == null || item.rb == null)
                return;

            PendingThrow pendingThrow = new PendingThrow(item, velocity, angularVelocity);
            // A held item can be updated more than once while frozen; keep the last throw
            // vector so release uses the player's final aim and force.
            for (int i = 0; i < pendingThrows.Count; i++)
            {
                if (pendingThrows[i].Item != item)
                    continue;

                pendingThrows[i] = pendingThrow;
                return;
            }

            pendingThrows.Add(pendingThrow);
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

            // In multiplayer the host owns world-side replay, while explicit client-side
            // previews are still allowed for local player gun shots.
            return KrokMpBridge.IsServer || shot.ReplayOnClients;
        }

        private static void PlayDelayedShotSound(FireInfo info)
        {
            if (info == null)
                return;

            Sound.Play("rifleshot", info.pos, true, false, null, 1f, 1f, false, false);
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
