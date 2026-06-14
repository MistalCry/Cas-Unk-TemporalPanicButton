using System.Collections;
using System.Reflection;
using UnityEngine;

namespace TemporalPanicButton.Runtime
{
    /// <summary>
    /// Resolves KrokMP client ids to Body instances and back.
    /// Kept separate from KrokMpBridge because this reads NetPlayer scene/runtime state,
    /// while the bridge only sends and receives messages.
    /// </summary>
    internal static class KrokPlayerResolver
    {
        private const string NetPlayerTypeName = "KrokoshaCasualtiesMP.NetPlayer";

        private static System.Type netPlayerType;
        private static FieldInfo netPlayerClientIdField;
        private static FieldInfo netPlayerBodyToPlayerDictField;

        public static uint GetClientIdForBody(Body body)
        {
            if (body == null)
                return uint.MaxValue;

            if (body == PlayerCamera.main?.body)
                return KrokMpBridge.LocalClientId;

            // KrokMP does not expose a stable compile-time API here, so we resolve the
            // mapping reflectively and cache the fields after the multiplayer assembly loads.
            EnsureFields();
            if (netPlayerClientIdField == null)
                return uint.MaxValue;

            uint mappedClientId = GetClientIdFromKrokPlayerMap(body);
            if (mappedClientId != uint.MaxValue)
                return mappedClientId;

            MonoBehaviour[] behaviours = body.GetComponentsInParent<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || behaviour.GetType().FullName != NetPlayerTypeName)
                    continue;

                try
                {
                    return (uint)netPlayerClientIdField.GetValue(behaviour);
                }
                catch
                {
                    return uint.MaxValue;
                }
            }

            return uint.MaxValue;
        }

        public static Body GetBodyForClientId(uint clientId)
        {
            if (clientId == KrokMpBridge.LocalClientId)
                return PlayerCamera.main == null ? null : PlayerCamera.main.body;

            EnsureFields();
            if (netPlayerBodyToPlayerDictField == null || netPlayerClientIdField == null)
                return null;

            try
            {
                IDictionary dict = netPlayerBodyToPlayerDictField.GetValue(null) as IDictionary;
                if (dict == null)
                    return null;

                foreach (DictionaryEntry entry in dict)
                {
                    object netPlayer = entry.Value;
                    if (netPlayer == null)
                        continue;

                    uint mappedClientId = (uint)netPlayerClientIdField.GetValue(netPlayer);
                    if (mappedClientId == clientId)
                        return entry.Key as Body;
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        private static void EnsureFields()
        {
            if (netPlayerClientIdField == null)
                netPlayerClientIdField = FindNetPlayerClientIdField();

            if (netPlayerBodyToPlayerDictField == null)
                netPlayerBodyToPlayerDictField = FindNetPlayerBodyToPlayerDictField();
        }

        private static FieldInfo FindNetPlayerClientIdField()
        {
            System.Type type = FindNetPlayerType();
            FieldInfo field = type?.GetField("<clientId>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null)
                return field;

            MonoBehaviour[] behaviours = Object.FindObjectsOfType<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || behaviour.GetType().FullName != NetPlayerTypeName)
                    continue;

                return behaviour.GetType().GetField("<clientId>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            }

            return null;
        }

        private static FieldInfo FindNetPlayerBodyToPlayerDictField()
        {
            System.Type type = FindNetPlayerType();
            FieldInfo field = type?.GetField("BodyToPlayerDict", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
                return field;

            MonoBehaviour[] behaviours = Object.FindObjectsOfType<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || behaviour.GetType().FullName != NetPlayerTypeName)
                    continue;

                return behaviour.GetType().GetField("BodyToPlayerDict", BindingFlags.Static | BindingFlags.Public);
            }

            return null;
        }

        private static System.Type FindNetPlayerType()
        {
            if (netPlayerType != null)
                return netPlayerType;

            Assembly[] assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                System.Type type = assembly.GetType(NetPlayerTypeName);
                if (type == null)
                    continue;

                netPlayerType = type;
                return netPlayerType;
            }

            return null;
        }

        private static uint GetClientIdFromKrokPlayerMap(Body body)
        {
            if (netPlayerBodyToPlayerDictField == null || netPlayerClientIdField == null)
                return uint.MaxValue;

            try
            {
                IDictionary dict = netPlayerBodyToPlayerDictField.GetValue(null) as IDictionary;
                if (dict == null || !dict.Contains(body))
                    return uint.MaxValue;

                object netPlayer = dict[body];
                if (netPlayer == null)
                    return uint.MaxValue;

                return (uint)netPlayerClientIdField.GetValue(netPlayer);
            }
            catch
            {
                return uint.MaxValue;
            }
        }
    }
}
