using UnityEngine;

namespace TemporalPanicButton.Runtime
{
    /// <summary>
    /// 时停 HUD。
    /// 使用 IMGUI 是为了不依赖场景 UI 预制体，在这个 Unity 游戏里能稳定显示剩余时间、冷却和联机警告。
    /// </summary>
    internal sealed class TimeStopTimerDisplay : MonoBehaviour
    {
        private static TimeStopTimerDisplay instance;
        private Texture2D backgroundTexture;
        private GUIStyle boxStyle;
        private GUIStyle labelStyle;
        private GUIStyle subLabelStyle;

        public static void Install(MonoBehaviour owner)
        {
            if (instance != null || owner == null)
                return;

            instance = owner.gameObject.AddComponent<TimeStopTimerDisplay>();
        }

        public static void Uninstall()
        {
            if (instance == null)
                return;

            instance.DestroyResources();
            Destroy(instance);
            instance = null;
        }

        private void OnGUI()
        {
            if (!ModSettings.Enabled)
                return;

            if (!TimeStopController.IsPlayableContext)
                return;

            if (KrokMpBridge.ShouldShowHostMissingWarning)
            {
                DrawHostMissingWarning();
                return;
            }

            if (TimeStopController.TryGetActiveDisplay(out bool isLocalStop, out float remaining, out float total))
            {
                // 联机时需要区分“我拥有时停权限”和“别人正在时停，我被冻住”。
                DrawStatus(isLocalStop ? "YOUR TIME STOP" : "OTHER TIME STOP", remaining, total, isLocalStop ? new Color(0.35f, 0.9f, 1f, 1f) : new Color(0.65f, 0.75f, 1f, 1f));
                return;
            }

            if (TimeStopController.CooldownRemainingSeconds > 0f)
            {
                DrawStatus("COOLDOWN", TimeStopController.CooldownRemainingSeconds, TimeStopController.CooldownTotalSeconds, new Color(1f, 0.75f, 0.35f, 1f));
                return;
            }

            if (!ModSettings.TimeStopUnlocked)
            {
                DrawLocked();
                return;
            }

            DrawReady();
        }

        private void DrawStatus(string title, float remaining, float total, Color accent)
        {
            EnsureStyles();

            float safeWidth = Mathf.Min(Screen.width - 24f, 460f);
            if (safeWidth < 220f)
                safeWidth = Screen.width - 12f;

            Rect rect = new Rect((Screen.width - safeWidth) * 0.5f, 8f, safeWidth, 44f);
            Rect fillRect = new Rect(rect.x + 4f, rect.y + rect.height - 8f, Mathf.Max(0f, rect.width - 8f) * GetProgress(remaining, total), 4f);
            string text = title + "  " + Mathf.Max(0f, remaining).ToString("0.0") + "s";

            Color oldColor = GUI.color;
            GUI.color = Color.white;
            GUI.Box(rect, GUIContent.none, boxStyle);
            GUI.color = accent;
            GUI.DrawTexture(fillRect, Texture2D.whiteTexture);
            GUI.color = accent;
            GUI.Label(new Rect(rect.x, rect.y + 1f, rect.width, 28f), text, labelStyle);
            GUI.color = new Color(1f, 1f, 1f, 0.8f);
            GUI.Label(new Rect(rect.x, rect.y + 24f, rect.width, 16f), GetSubLabel(), subLabelStyle);
            GUI.color = oldColor;
        }

        private void DrawLocked()
        {
            EnsureStyles();

            float width = Mathf.Min(Screen.width - 24f, 360f);
            if (width < 220f)
                width = Screen.width - 12f;

            Rect rect = new Rect((Screen.width - width) * 0.5f, 8f, width, 38f);
            Color oldColor = GUI.color;
            GUI.color = Color.white;
            GUI.Box(rect, GUIContent.none, boxStyle);
            GUI.color = new Color(0.75f, 0.82f, 0.95f, 1f);
            GUI.Label(new Rect(rect.x, rect.y + 1f, rect.width, 22f), "TIME STOP LOCKED  INT " + ModSettings.IntelligenceLevel + "/7", labelStyle);
            GUI.color = new Color(1f, 1f, 1f, 0.75f);
            GUI.Label(new Rect(rect.x, rect.y + 23f, rect.width, 14f), GetSubLabel(), subLabelStyle);
            GUI.color = oldColor;
        }

        private void DrawReady()
        {
            EnsureStyles();

            float width = Mathf.Min(Screen.width - 24f, 460f);
            if (width < 260f)
                width = Screen.width - 12f;

            Rect rect = new Rect((Screen.width - width) * 0.5f, 8f, width, 44f);
            Color oldColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.9f);
            GUI.Box(rect, GUIContent.none, boxStyle);
            GUI.color = new Color(0.45f, 1f, 0.65f, 1f);
            GUI.Label(new Rect(rect.x, rect.y + 1f, rect.width, 24f), "TIME STOP READY", labelStyle);
            GUI.color = new Color(1f, 1f, 1f, 0.75f);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 24f, rect.width - 20f, 16f), GetReadySubLabel(), subLabelStyle);
            GUI.color = oldColor;
        }

        private void DrawHostMissingWarning()
        {
            EnsureStyles();

            float width = Mathf.Min(Screen.width - 24f, 620f);
            if (width < 260f)
                width = Screen.width - 12f;

            Rect rect = new Rect((Screen.width - width) * 0.5f, 8f, width, 62f);
            Color oldColor = GUI.color;
            GUI.color = Color.white;
            GUI.Box(rect, GUIContent.none, boxStyle);
            GUI.color = new Color(1f, 0.45f, 0.45f, 1f);
            GUI.Label(new Rect(rect.x, rect.y + 2f, rect.width, 24f), "MOD HOST WARNING", labelStyle);
            GUI.color = new Color(1f, 1f, 1f, 0.9f);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 27f, rect.width - 20f, 30f), "当前主机未加本模组，模组会失效，状态延迟等严重问题", subLabelStyle);
            GUI.color = oldColor;
        }

        private static string GetSubLabel()
        {
            return ModSettings.ProgressionSummary() + " | MP " + KrokMpBridge.Status;
        }

        private static string GetReadySubLabel()
        {
            return ModSettings.ProgressionSummary();
        }

        private static float GetProgress(float remaining, float total)
        {
            if (total <= 0.01f)
                return 0f;

            return Mathf.Clamp01(remaining / total);
        }

        private void EnsureStyles()
        {
            if (labelStyle != null)
                return;

            backgroundTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            backgroundTexture.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.68f));
            backgroundTexture.Apply();

            boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.normal.background = backgroundTexture;

            labelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18,
                fontStyle = FontStyle.Bold
            };

            subLabelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 9,
                fontStyle = FontStyle.Normal,
                wordWrap = true
            };
        }

        private void DestroyResources()
        {
            if (backgroundTexture == null)
                return;

            Destroy(backgroundTexture);
            backgroundTexture = null;
        }
    }
}
