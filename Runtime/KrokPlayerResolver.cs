using System.Collections;
using System.Reflection;
using UnityEngine;

namespace TemporalPanicButton.Runtime
{
    /// <summary>
    /// 负责在 KrokMP clientId 和游戏 Body 实例之间互相查找。
    /// 它读取场景里的 NetPlayer / NetBody 状态；KrokMpBridge 只负责消息收发，所以两者分开维护。
    /// </summary>
    internal static class KrokPlayerResolver
    {
        private const string NetPlayerTypeName = "KrokoshaCasualtiesMP.NetPlayer";

        private static System.Type netPlayerType;
        private static System.Type netBodyType;
        private static FieldInfo netPlayerClientIdField;
        private static FieldInfo netPlayerBodyToPlayerDictField;
        private static FieldInfo netBodyPlayerField;
        private static PropertyInfo netBodyPlayerProperty;

        public static uint GetClientIdForBody(Body body)
        {
            if (body == null)
                return uint.MaxValue;

            if (body == PlayerCamera.main?.body)
                return KrokMpBridge.LocalClientId;

            // KrokMP 这里没有稳定的编译期 API，只能在联机程序集加载后反射查找并缓存字段。
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
                if (behaviour == null)
                    continue;

                if (behaviour.GetType().FullName == NetPlayerTypeName)
                {
                    try
                    {
                        return KrokMpBridge.ConvertKrokIdToUInt(netPlayerClientIdField.GetValue(behaviour));
                    }
                    catch
                    {
                        return uint.MaxValue;
                    }
                }

                if (behaviour.GetType().FullName == "KrokoshaCasualtiesMP.NetBody")
                {
                    uint clientId = GetClientIdFromNetBody(behaviour);
                    if (clientId != uint.MaxValue)
                        return clientId;
                }
            }

            return uint.MaxValue;
        }

        public static bool IsLocalRigidbody(Rigidbody2D rigidbody)
        {
            if (rigidbody == null)
                return false;

            if (!KrokMpBridge.IsNetworkRunning)
                return true;

            Body body = rigidbody.GetComponent<Body>();
            if (body == null)
            {
                Limb limb = rigidbody.GetComponent<Limb>();
                if (limb != null)
                    body = limb.body;
            }

            if (body == null)
                body = rigidbody.GetComponentInParent<Body>();

            return body != null && IsLocalBody(body);
        }

        public static bool IsLocalBody(Body body)
        {
            if (body == null)
                return false;

            if (!KrokMpBridge.IsNetworkRunning)
                return true;

            if (body == PlayerCamera.main?.body)
                return true;

            uint clientId = GetClientIdForBody(body);
            return clientId != uint.MaxValue && clientId == KrokMpBridge.LocalClientId;
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

                    uint mappedClientId = KrokMpBridge.ConvertKrokIdToUInt(netPlayerClientIdField.GetValue(netPlayer));
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

            if (netBodyType == null)
                netBodyType = FindNetBodyType();

            if (netBodyPlayerField == null)
                netBodyPlayerField = netBodyType?.GetField("<plr>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

            if (netBodyPlayerProperty == null)
                netBodyPlayerProperty = netBodyType?.GetProperty("plr", BindingFlags.Instance | BindingFlags.Public);
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

        private static System.Type FindNetBodyType()
        {
            if (netBodyType != null)
                return netBodyType;

            Assembly[] assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                System.Type type = assembly.GetType("KrokoshaCasualtiesMP.NetBody");
                if (type == null)
                    continue;

                netBodyType = type;
                return netBodyType;
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

                return KrokMpBridge.ConvertKrokIdToUInt(netPlayerClientIdField.GetValue(netPlayer));
            }
            catch
            {
                return uint.MaxValue;
            }
        }

        private static uint GetClientIdFromNetBody(object netBody)
        {
            if (netBody == null || netPlayerClientIdField == null)
                return uint.MaxValue;

            try
            {
                object netPlayer = netBodyPlayerField?.GetValue(netBody) ?? netBodyPlayerProperty?.GetValue(netBody, null);
                if (netPlayer == null)
                    return uint.MaxValue;

                return KrokMpBridge.ConvertKrokIdToUInt(netPlayerClientIdField.GetValue(netPlayer));
            }
            catch
            {
                return uint.MaxValue;
            }
        }
    }
}
