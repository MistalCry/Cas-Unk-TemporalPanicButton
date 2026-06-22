using System.Collections.Generic;
using UnityEngine;

namespace TemporalPanicButton.Runtime
{
    /// <summary>
    /// 显示时停中开枪产生的短暂冻结弹道。
    /// 它只负责视觉表现；真正的命中和伤害仍由 PendingTimeStopActions 延后回放。
    /// </summary>
    internal sealed class TimeStopBulletPreview : MonoBehaviour
    {
        private const float InitialSpeed = 34f;
        private const float Deceleration = 44f;
        private const float MaxLength = 7.5f;
        private const float LineWidth = 0.055f;

        private static TimeStopBulletPreview instance;
        private readonly List<FrozenShotLine> lines = new List<FrozenShotLine>();
        private Material lineMaterial;

        public static void Add(FireInfo info)
        {
            if (info == null || !TimeStopController.IsActive)
                return;

            EnsureInstance();
            instance.AddInternal(info);
        }

        public static void Clear()
        {
            if (instance == null)
                return;

            instance.ClearInternal();
        }

        private static void EnsureInstance()
        {
            if (instance != null)
                return;

            GameObject host = new GameObject("Temporal Panic Button Bullet Preview");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<TimeStopBulletPreview>();
        }

        private void AddInternal(FireInfo info)
        {
            Vector2 direction = info.dir.sqrMagnitude > 0.0001f ? info.dir.normalized : Vector2.right;
            GameObject obj = new GameObject("Frozen Time Stop Shot");
            obj.transform.SetParent(transform, false);

            LineRenderer line = obj.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = LineWidth;
            line.endWidth = LineWidth * 0.35f;
            line.numCapVertices = 2;
            line.sortingOrder = short.MaxValue;
            line.material = GetLineMaterial();
            line.startColor = new Color(1f, 0.92f, 0.18f, 0.95f);
            line.endColor = new Color(1f, 0.55f, 0.05f, 0.55f);
            line.SetPosition(0, info.pos);
            line.SetPosition(1, info.pos);

            lines.Add(new FrozenShotLine(line, info.pos, direction));
        }

        private void Update()
        {
            if (lines.Count == 0)
                return;

            float deltaTime = Time.unscaledDeltaTime;
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                FrozenShotLine line = lines[i];
                if (line.Renderer == null)
                {
                    lines.RemoveAt(i);
                    continue;
                }

                line.Step(deltaTime);
                lines[i] = line;
            }
        }

        private Material GetLineMaterial()
        {
            if (lineMaterial != null)
                return lineMaterial;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            lineMaterial = new Material(shader);
            return lineMaterial;
        }

        private void ClearInternal()
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Renderer != null)
                    Destroy(lines[i].Renderer.gameObject);
            }

            lines.Clear();
        }

        private void OnDestroy()
        {
            ClearInternal();
            if (lineMaterial != null)
                Destroy(lineMaterial);

            if (instance == this)
                instance = null;
        }

        private struct FrozenShotLine
        {
            public readonly LineRenderer Renderer;
            private readonly Vector2 start;
            private readonly Vector2 direction;
            private float length;
            private float speed;

            public FrozenShotLine(LineRenderer renderer, Vector2 start, Vector2 direction)
            {
                Renderer = renderer;
                this.start = start;
                this.direction = direction;
                length = 0f;
                speed = InitialSpeed;
            }

            public void Step(float deltaTime)
            {
                // 让弹道先短暂前进再减速停住，形成“子弹被时停卡在空中”的感觉。
                if (speed > 0f)
                {
                    length = Mathf.Min(MaxLength, length + speed * deltaTime);
                    speed = Mathf.Max(0f, speed - Deceleration * deltaTime);
                }

                Vector2 end = start + direction * length;
                Renderer.SetPosition(0, start);
                Renderer.SetPosition(1, end);
            }
        }
    }
}
