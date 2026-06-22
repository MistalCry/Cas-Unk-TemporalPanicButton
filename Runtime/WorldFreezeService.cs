using System;
using System.Collections.Generic;
using TemporalPanicButton.Patches;
using UnityEngine;

namespace TemporalPanicButton.Runtime
{
    /// <summary>
    /// 冻结世界里的刚体和游戏行为，同时保留被授权玩家与必要 UI。
    /// 哪些刚体/行为应该继续运行由调用方提供谓词决定。
    /// </summary>
    internal sealed class WorldFreezeService
    {
        private readonly Func<Rigidbody2D, bool> shouldKeepRigidbodyActive;
        private readonly Func<MonoBehaviour, bool> shouldKeepBehaviourActive;
        private readonly List<FrozenBody> frozenBodies = new List<FrozenBody>();
        private readonly List<FrozenBehaviour> frozenBehaviours = new List<FrozenBehaviour>();
        private readonly HashSet<Rigidbody2D> frozenSet = new HashSet<Rigidbody2D>();
        private readonly HashSet<MonoBehaviour> frozenBehaviourSet = new HashSet<MonoBehaviour>();

        public WorldFreezeService(
            Func<Rigidbody2D, bool> shouldKeepRigidbodyActive,
            Func<MonoBehaviour, bool> shouldKeepBehaviourActive)
        {
            this.shouldKeepRigidbodyActive = shouldKeepRigidbodyActive;
            this.shouldKeepBehaviourActive = shouldKeepBehaviourActive;
        }

        public void Freeze()
        {
            Restore();

            // 时停期间仍保持 Time.timeScale = 1，让发动者、UI 和医疗小游戏正常运行。
            // 世界对象则通过刚体和 MonoBehaviour 显式冻结。
            Rigidbody2D[] bodies = UnityEngine.Object.FindObjectsOfType<Rigidbody2D>();
            foreach (Rigidbody2D body in bodies)
            {
                if (body == null ||
                    body.bodyType == RigidbodyType2D.Static ||
                    shouldKeepRigidbodyActive(body) ||
                    frozenSet.Contains(body))
                    continue;

                frozenBodies.Add(new FrozenBody(body));
                frozenSet.Add(body);
                FreezeNow(body);
            }

            MonoBehaviour[] behaviours = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>();
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (!ShouldFreezeBehaviour(behaviour))
                    continue;

                frozenBehaviours.Add(new FrozenBehaviour(behaviour));
                frozenBehaviourSet.Add(behaviour);
                behaviour.enabled = false;
            }
        }

        public void Restore()
        {
            for (int i = 0; i < frozenBodies.Count; i++)
                frozenBodies[i].Restore();

            for (int i = 0; i < frozenBehaviours.Count; i++)
                frozenBehaviours[i].Restore();

            frozenBodies.Clear();
            frozenBehaviours.Clear();
            frozenSet.Clear();
            frozenBehaviourSet.Clear();
        }

        public void Maintain()
        {
            for (int i = 0; i < frozenBodies.Count; i++)
                frozenBodies[i].Maintain();
        }

        public bool MaintainIfFrozen(Rigidbody2D body)
        {
            if (body == null || !frozenSet.Contains(body))
                return false;

            FreezeNow(body);
            return true;
        }

        private static void FreezeNow(Rigidbody2D body)
        {
            if (body == null)
                return;

            if (body.bodyType == RigidbodyType2D.Static)
                return;

            body.velocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.bodyType = RigidbodyType2D.Static;
        }

        private bool ShouldFreezeBehaviour(MonoBehaviour behaviour)
        {
            if (behaviour == null ||
                !behaviour.enabled ||
                frozenBehaviourSet.Contains(behaviour) ||
                IsTemporalPanicButtonBehaviour(behaviour) ||
                IsCoreBehaviour(behaviour) ||
                IsTreatmentUiBehaviour(behaviour) ||
                IsConsoleBehaviour(behaviour) ||
                shouldKeepBehaviourActive(behaviour))
                return false;

            bool isGameAssemblyBehaviour = IsGameAssemblyBehaviour(behaviour);
            bool isKrokMpFreezableBehaviour = KrokMpCompatibilityPatches.IsKrokMpSoundCannonTracker(behaviour);
            return isGameAssemblyBehaviour || isKrokMpFreezableBehaviour;
        }

        private static bool IsTemporalPanicButtonBehaviour(MonoBehaviour behaviour)
        {
            Type type = behaviour.GetType();
            return type.Namespace != null && type.Namespace.StartsWith("TemporalPanicButton");
        }

        private static bool IsGameAssemblyBehaviour(MonoBehaviour behaviour)
        {
            Type type = behaviour.GetType();
            return type.Assembly.GetName().Name == "Assembly-CSharp";
        }

        private static bool IsCoreBehaviour(MonoBehaviour behaviour)
        {
            return behaviour is PlayerCamera ||
                   behaviour is PauseHandler ||
                   behaviour is SettingsMenu ||
                   behaviour is WorldGeneration ||
                   behaviour is GlobalDark ||
                   behaviour is Observer;
        }

        private static bool IsTreatmentUiBehaviour(MonoBehaviour behaviour)
        {
            // 医疗 UI 必须继续跑，发动者才能在时停里给自己处理伤口。
            return behaviour is MinigameBase ||
                   behaviour is WoundView ||
                   behaviour is WoundViewLimb ||
                   behaviour is InvButton ||
                   behaviour is InventorySlot ||
                   behaviour is OnLimbWound ||
                   behaviour is UITooltip ||
                   behaviour is UIHoverDescription ||
                   behaviour is LiquidTransfer ||
                   behaviour is WaterContainerItem ||
                   behaviour is LiquidAffect;
        }

        private static bool IsConsoleBehaviour(MonoBehaviour behaviour)
        {
            Type type = behaviour.GetType();
            string typeName = type.Name;
            return typeName == "ConsoleScript" ||
                   typeName == "ConsoleSettings";
        }

        private readonly struct FrozenBody
        {
            private readonly Rigidbody2D body;
            private readonly RigidbodyType2D bodyType;
            private readonly Vector2 velocity;
            private readonly float angularVelocity;

            public FrozenBody(Rigidbody2D body)
            {
                this.body = body;
                bodyType = body.bodyType;
                velocity = body.velocity;
                angularVelocity = body.angularVelocity;
            }

            public void Restore()
            {
                if (body == null)
                    return;

                body.bodyType = bodyType;
                body.velocity = velocity;
                body.angularVelocity = angularVelocity;
            }

            public void Maintain()
            {
                if (body == null)
                    return;

                FreezeNow(body);
            }
        }

        private readonly struct FrozenBehaviour
        {
            private readonly MonoBehaviour behaviour;

            public FrozenBehaviour(MonoBehaviour behaviour)
            {
                this.behaviour = behaviour;
            }

            public void Restore()
            {
                if (behaviour == null)
                    return;

                behaviour.enabled = true;
            }
        }
    }
}
