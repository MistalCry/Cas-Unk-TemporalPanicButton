using System.Globalization;
using UnityEngine;

namespace TemporalPanicButton.Runtime
{
    /// <summary>
    /// KrokMP 时停消息的稳定文本格式。
    /// 格式：version|casterClientId|duration|originX|originY|hazardKind|stopId|originInstanceId。
    /// </summary>
    internal static class KrokMpPayload
    {
        private const string PayloadVersion = "1";

        public static string Build(uint casterClientId, float duration, Vector2 origin, HazardKind hazard, uint stopId, string originInstanceId)
        {
            return string.Join("|", new[]
            {
                PayloadVersion,
                casterClientId.ToString(CultureInfo.InvariantCulture),
                Mathf.Clamp(duration, 0.1f, 120f).ToString("R", CultureInfo.InvariantCulture),
                origin.x.ToString("R", CultureInfo.InvariantCulture),
                origin.y.ToString("R", CultureInfo.InvariantCulture),
                ((int)hazard).ToString(CultureInfo.InvariantCulture),
                stopId.ToString(CultureInfo.InvariantCulture),
                originInstanceId ?? string.Empty
            });
        }

        public static string ReplaceCaster(string payload, uint clientId)
        {
            // 主机统一修正 casterId，既简化客机发包，也避免客机伪造其他玩家身份。
            string[] parts = payload.Split('|');
            if (parts.Length < 7)
                return payload;

            parts[1] = clientId.ToString(CultureInfo.InvariantCulture);
            return string.Join("|", parts);
        }

        public static bool TryParse(string payload, out MultiplayerTimeStopEvent stopEvent)
        {
            stopEvent = default(MultiplayerTimeStopEvent);
            if (string.IsNullOrEmpty(payload))
                return false;

            string[] parts = payload.Split('|');
            if (parts.Length < 7 || parts[0] != PayloadVersion)
                return false;

            if (!uint.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint casterClientId) ||
                !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float duration) ||
                !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
                !int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int hazardValue) ||
                !uint.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint stopId))
                return false;

            string originInstanceId = parts.Length >= 8 ? parts[7] : string.Empty;
            stopEvent = new MultiplayerTimeStopEvent(casterClientId, Mathf.Clamp(duration, 0.1f, 120f), new Vector2(x, y), (HazardKind)hazardValue, stopId, originInstanceId);
            return true;
        }
    }
}
