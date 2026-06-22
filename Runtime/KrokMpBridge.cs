using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace TemporalPanicButton.Runtime
{
    /// <summary>
    /// KrokMP 运行时桥接层。
    /// 所有 KrokMP 类型和方法都通过反射发现，确保没有安装 KrokMP 的单人环境也能正常启动本模组。
    /// </summary>
    internal static class KrokMpBridge
    {
        // 本模组私有消息号：Request 为客机发给主机，Announce 为主机广播给客机。
        private const ushort RequestMessageId = 0x7A50;
        private const ushort AnnounceMessageId = 0x7A51;
        private const ushort FuseCutRequestMessageId = 0x7A52;
        private const ushort FuseCutAnnounceMessageId = 0x7A53;
        private const ushort PresenceRequestMessageId = 0x7A54;
        private const ushort PresenceAnnounceMessageId = 0x7A55;
        private const string FuseCutPayloadVersion = "fuse1";
        private const string PresencePayloadVersion = "mod1";
        private const string PresencePayloadVersion2 = "mod2";
        private const float PresencePingInterval = 2f;
        private const float HostPresenceWarningDelay = 6f;

        private static Type netType;
        private static Type netPlayerType;
        private static Type multiplayerType;
        private static Type itemSyncType;
        private static Type netObjectRegistryType;
        private static Type syncInfoType;
        private static Type readerType;
        private static Type writerType;
        private static Type deliveryMethodType;
        private static Type netExtensionsType;
        private static Type receiverDelegateType;
        private static Type netIdType;
        private static MethodInfo clientSendStringMethod;
        private static MethodInfo serverRelayStringMethod;
        private static MethodInfo serverSendOneStringMethod;
        private static MethodInfo readerGetStringMethod;
        private static MethodInfo readerGetKrokStringMethod;
        private static MethodInfo writerPutKrokStringMethod;
        private static MethodInfo netPlayerGetClientIdFromBodyMethod;
        private static MethodInfo itemSyncTryGetSyncInfoMethod;
        private static MethodInfo netObjectRegistryTryGetSyncInfoByIdMethod;
        private static MethodInfo netObjectRegistryTryGetSyncInfoByObjectMethod;
        private static MethodInfo netObjectRegistryTryGetSyncInfoOrRegisterMethod;
        private static MethodInfo netObjectRegistryServerObjectSyncSingleMethod;
        private static MethodInfo netCreateWriterMethod;
        private static MethodInfo netClientSendMethod;
        private static MethodInfo netServerSendToClientsMethod;
        private static PropertyInfo allClientIdsProperty;
        private static PropertyInfo netRunningProperty;
        private static PropertyInfo netIsServerProperty;
        private static PropertyInfo netPlayerClientIdProperty;
        private static PropertyInfo syncInfoItemProperty;
        private static FieldInfo localPlayerField;
        private static FieldInfo clientIdBackingField;
        private static FieldInfo syncInfoSyncIdField;
        private static FieldInfo netIdValueField;
        private static FieldInfo serverHandlersField;
        private static FieldInfo clientHandlersField;
        private static ConstructorInfo netIdConstructor;
        private static bool initialized;
        private static bool available;
        private static float nextInitAttemptTime;
        private static readonly string LocalInstanceId = Guid.NewGuid().ToString("N");
        private static uint localSequence;
        private static string status = "not initialized";
        private static string lastError;
        private static uint sentRequests;
        private static uint receivedRequests;
        private static uint sentAnnouncements;
        private static uint receivedAnnouncements;
        private static string lastRequestStatus;
        private static string lastAnnouncementStatus;
        private static float nextPresencePingTime;
        private static bool lastNetworkRunning;
        private static float networkRunningSince;
        private static bool hostPresenceKnown;
        private static bool hostIntelligenceProgression;
        private static readonly HashSet<uint> knownModdedClientIds = new HashSet<uint>();

        public static string Status
        {
            get
            {
                EnsureInitialized();
                return BuildStatus();
            }
        }

        public static bool IsAvailable
        {
            get
            {
                EnsureInitialized();
                EnsureRegistered();
                return available && IsNetworkRunning;
            }
        }

        public static bool IsNetworkRunning
        {
            get
            {
                EnsureInitialized();
                return GetBoolProperty(netRunningProperty);
            }
        }

        public static bool IsServer
        {
            get
            {
                EnsureInitialized();
                return available && GetBoolProperty(netIsServerProperty);
            }
        }

        public static bool ShouldShowHostMissingWarning
        {
            get
            {
                EnsureInitialized();
                EnsureRegistered();
                return available &&
                       IsNetworkRunning &&
                       !IsServer &&
                       !hostPresenceKnown &&
                       networkRunningSince > 0f &&
                       Time.realtimeSinceStartup - networkRunningSince >= HostPresenceWarningDelay;
            }
        }

        public static bool IsMultiplayerFeatureUsable
        {
            get
            {
                EnsureInitialized();
                EnsureRegistered();
                return available && IsNetworkRunning && (IsServer || hostPresenceKnown);
            }
        }

        public static bool TryGetHostIntelligenceProgression(out bool enabled)
        {
            enabled = false;
            EnsureInitialized();
            EnsureRegistered();
            if (!available || !IsNetworkRunning)
                return false;

            if (IsServer)
            {
                enabled = ModSettings.LocalIntelligenceProgression;
                return true;
            }

            if (!hostPresenceKnown)
                return false;

            enabled = hostIntelligenceProgression;
            return true;
        }

        public static uint LocalClientId
        {
            get
            {
                EnsureInitialized();
                return GetLocalClientId();
            }
        }

        public static void Warmup()
        {
            EnsureInitialized();
            EnsureRegistered();
            TickPresenceAnnouncements();
        }

        public static bool HasPresenceForClient(uint clientId)
        {
            EnsureInitialized();
            EnsureRegistered();
            if (!available || !IsNetworkRunning || clientId == uint.MaxValue)
                return false;

            if (clientId == GetLocalClientId())
                return true;

            return knownModdedClientIds.Contains(clientId);
        }

        public static bool TryAnnounceLocalTrigger(HazardKind hazard, Vector2 origin, float duration)
        {
            EnsureInitialized();
            EnsureRegistered();
            if (!available || !IsNetworkRunning)
            {
                if (available)
                    status = "ready, network not running";

                return false;
            }

            localSequence++;
            string payload = KrokMpPayload.Build(GetLocalClientId(), duration, origin, hazard, localSequence, LocalInstanceId);
            // 发动者本机立即应用时停，不等待网络回声，减少按键后的延迟感。
            bool appliedLocally = TimeStopController.ReceiveMultiplayerTimeStop(payload);
            lastAnnouncementStatus = "local " + (appliedLocally ? "ok " : "drop ") + DescribePayload(payload);
            if (IsServer)
            {
                RelayAnnouncement(payload);
                return true;
            }

            SendRequest(payload);
            return true;
        }

        private static void EnsureInitialized()
        {
            if (initialized)
                return;

            if (Time.realtimeSinceStartup < nextInitAttemptTime)
                return;

            nextInitAttemptTime = Time.realtimeSinceStartup + 1f;
            initialized = true;
            try
            {
                // KrokMP 是可选依赖，不能写死程序集引用；否则没装 KrokMP 的单人玩家会直接加载失败。
                Assembly assembly = FindKrokMpAssembly();
                if (assembly == null)
                {
                    status = "KrokMP assembly not loaded";
                    initialized = false;
                    return;
                }

                netType = assembly.GetType("KrokoshaCasualtiesMP.Net");
                netPlayerType = assembly.GetType("KrokoshaCasualtiesMP.NetPlayer");
                multiplayerType = assembly.GetType("KrokoshaCasualtiesMP.KrokoshaScavMultiplayer");
                itemSyncType = assembly.GetType("KrokoshaCasualtiesMP.ItemSync");
                netObjectRegistryType = assembly.GetType("KrokoshaCasualtiesMP.NetObjectRegistry");
                syncInfoType = assembly.GetType("KrokoshaCasualtiesMP.SyncInfo");
                netExtensionsType = assembly.GetType("KrokoshaCasualtiesMP.MyLiteNetLibExtensions");
                netIdType = assembly.GetType("KrokoshaCasualtiesMP.knetid");
                readerType = FindType("LiteNetLib.Utils.NetDataReader");
                writerType = FindType("LiteNetLib.Utils.NetDataWriter");
                deliveryMethodType = FindType("LiteNetLib.DeliveryMethod");
                receiverDelegateType = assembly.GetType("KrokoshaCasualtiesMP.KrokoshaScavMultiplayer+KrokoshaHandleNamedMessageDelegate");

                if (netType == null || netPlayerType == null || multiplayerType == null || netExtensionsType == null || readerType == null || writerType == null || deliveryMethodType == null || receiverDelegateType == null)
                {
                    status = "KrokMP types missing";
                    initialized = false;
                    return;
                }

                // KrokMP 不同版本暴露过不同 API。
                // 这里同时缓存高级字符串 helper 和底层 Net writer/send，发送时再选择可用路径。
                netRunningProperty = netType.GetProperty("running", BindingFlags.Static | BindingFlags.Public);
                netIsServerProperty = netType.GetProperty("is_server", BindingFlags.Static | BindingFlags.Public);
                localPlayerField = netPlayerType.GetField("LOCAL_PLAYER", BindingFlags.Static | BindingFlags.Public);
                netPlayerClientIdProperty = netPlayerType.GetProperty("clientId", BindingFlags.Instance | BindingFlags.Public);
                clientIdBackingField = netPlayerType.GetField("<clientId>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
                if (netIdType == null)
                    netIdType = netPlayerClientIdProperty?.PropertyType ?? clientIdBackingField?.FieldType;

                CacheNetIdAccessors();
                netPlayerGetClientIdFromBodyMethod = netPlayerType.GetMethod(
                    "GetClientIdFromBody",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(Body) },
                    null);
                serverHandlersField = netType.GetField("SERVER_MESSAGE_HANDLERS", BindingFlags.Static | BindingFlags.NonPublic);
                clientHandlersField = netType.GetField("CLIENT_MESSAGE_HANDLERS", BindingFlags.Static | BindingFlags.NonPublic);
                readerGetStringMethod = readerType.GetMethod("GetString", Type.EmptyTypes);
                readerGetKrokStringMethod = FindMethod(
                    netExtensionsType,
                    "Get",
                    readerType,
                    typeof(string).MakeByRefType(),
                    typeof(bool));

                writerPutKrokStringMethod = FindMethod(
                    netExtensionsType,
                    "Put",
                    writerType,
                    typeof(string),
                    typeof(bool));

                netCreateWriterMethod = FindMethod(netType, "CreateWriter", typeof(ushort));
                netClientSendMethod = FindMethod(
                    netType,
                    "Client_Send",
                    deliveryMethodType.MakeByRefType(),
                    writerType.MakeByRefType());
                netServerSendToClientsMethod = FindServerSendToClientsMethod();

                if (itemSyncType != null && netObjectRegistryType != null && syncInfoType != null)
                {
                    itemSyncTryGetSyncInfoMethod = FindMethod(itemSyncType, "TryGetSyncInfo", typeof(Item), syncInfoType.MakeByRefType());
                    netObjectRegistryTryGetSyncInfoByIdMethod = FindMethodWithNetId(netObjectRegistryType, "TryGetSyncInfo", syncInfoType.MakeByRefType());
                    netObjectRegistryTryGetSyncInfoByObjectMethod = FindMethod(netObjectRegistryType, "TryGetSyncInfo", typeof(GameObject), syncInfoType.MakeByRefType());
                    netObjectRegistryTryGetSyncInfoOrRegisterMethod = FindMethod(netObjectRegistryType, "TryGetSyncInfoOrRegister", typeof(GameObject), syncInfoType.MakeByRefType());
                    netObjectRegistryServerObjectSyncSingleMethod = FindServerObjectSyncSingleMethod();
                    syncInfoItemProperty = syncInfoType.GetProperty("item", BindingFlags.Instance | BindingFlags.Public);
                    syncInfoSyncIdField = syncInfoType.GetField("syncid", BindingFlags.Instance | BindingFlags.Public) ??
                                          syncInfoType.GetField("syncId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }

                clientSendStringMethod = FindMethod(
                    multiplayerType,
                    "Client_SendSimpleMessageToServer",
                    typeof(ushort).MakeByRefType(),
                    typeof(string),
                    typeof(bool));

                serverRelayStringMethod = FindMethod(
                    multiplayerType,
                    "Server_SendRelayMessageToClients",
                    typeof(ushort).MakeByRefType(),
                    typeof(string).MakeByRefType(),
                    typeof(bool),
                    typeof(bool));

                serverSendOneStringMethod = FindServerSendOneStringMethod();

                Type serverMainType = assembly.GetType("KrokoshaCasualtiesMP.ServerMain");
                allClientIdsProperty = serverMainType?.GetProperty("AllClientIds", BindingFlags.Static | BindingFlags.Public);

                if (readerGetKrokStringMethod == null || writerPutKrokStringMethod == null ||
                    netCreateWriterMethod == null || netClientSendMethod == null ||
                    (netServerSendToClientsMethod == null && serverRelayStringMethod == null) ||
                    (clientSendStringMethod == null && netClientSendMethod == null) ||
                    readerGetStringMethod == null)
                {
                    status = "KrokMP methods missing";
                    initialized = false;
                    return;
                }

                // 主机侧接收客机请求，所有客户端接收主机广播；同一台主机也可能同时拥有本地 client。
                RegisterReceiver(true, RequestMessageId);
                RegisterReceiver(false, AnnounceMessageId);
                RegisterReceiver(true, FuseCutRequestMessageId);
                RegisterReceiver(false, FuseCutAnnounceMessageId);
                RegisterReceiver(true, PresenceRequestMessageId);
                RegisterReceiver(false, PresenceAnnounceMessageId);
                available = true;
                status = "ready";
                lastError = null;
            }
            catch (Exception ex)
            {
                status = "unavailable: " + ex.GetType().Name + ": " + ex.Message;
                lastError = ex.GetType().Name;
                Debug.LogWarning("Temporal Panic Button: KrokMP bridge unavailable: " + ex);
                available = false;
            }
        }

        private static void EnsureRegistered()
        {
            if (!initialized || !available)
                return;

            try
            {
                // KrokMP 进出房间时可能重建消息 handler 字典；如果本模组 handler 丢失，就补注册。
                if (!IsMessageRegistered(true, RequestMessageId))
                    RegisterReceiver(true, RequestMessageId);

                if (!IsMessageRegistered(false, AnnounceMessageId))
                    RegisterReceiver(false, AnnounceMessageId);

                if (!IsMessageRegistered(true, FuseCutRequestMessageId))
                    RegisterReceiver(true, FuseCutRequestMessageId);

                if (!IsMessageRegistered(false, FuseCutAnnounceMessageId))
                    RegisterReceiver(false, FuseCutAnnounceMessageId);

                if (!IsMessageRegistered(true, PresenceRequestMessageId))
                    RegisterReceiver(true, PresenceRequestMessageId);

                if (!IsMessageRegistered(false, PresenceAnnounceMessageId))
                    RegisterReceiver(false, PresenceAnnounceMessageId);
            }
            catch (Exception ex)
            {
                status = "registration lost: " + ex.GetType().Name + ": " + ex.Message;
                lastError = ex.GetType().Name;
                available = false;
            }
        }

        private static Assembly FindKrokMpAssembly()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                if (assembly.GetName().Name == "KrokoshaCasualtiesMP")
                    return assembly;
            }

            return null;
        }

        private static string BuildStatus()
        {
            if (!available)
                return status;

            bool running = GetBoolProperty(netRunningProperty);
            if (!running)
                return "idle";

            if (GetBoolProperty(netIsServerProperty))
                return "host";

            if (hostPresenceKnown)
                return "client";

            return ShouldShowHostMissingWarning ? "host mod missing" : "connecting";
        }

        private static Type FindType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(fullName);
                if (type != null)
                    return type;
            }

            return null;
        }

        private static MethodInfo FindMethod(Type type, string name, params Type[] parameterTypes)
        {
            if (type == null)
                return null;

            MethodInfo[] methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method.Name != name)
                    continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != parameterTypes.Length)
                    continue;

                bool match = true;
                for (int j = 0; j < parameters.Length; j++)
                {
                    if (parameters[j].ParameterType != parameterTypes[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                    return method;
            }

            return null;
        }

        private static MethodInfo FindMethodWithNetId(Type type, string name, Type secondParameterType)
        {
            if (type == null)
                return null;

            MethodInfo[] methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method.Name != name)
                    continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 2)
                    continue;

                if (IsNetIdParameter(parameters[0].ParameterType) &&
                    parameters[1].ParameterType == secondParameterType)
                    return method;
            }

            return null;
        }

        private static MethodInfo FindServerSendToClientsMethod()
        {
            MethodInfo[] methods = netType.GetMethods(BindingFlags.Static | BindingFlags.Public);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method.Name != "Server_SendToClients")
                    continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 3)
                    continue;

                if (parameters[0].ParameterType != deliveryMethodType.MakeByRefType() ||
                    parameters[1].ParameterType != writerType.MakeByRefType())
                    continue;

                Type clientIdsType = parameters[2].ParameterType;
                if (!clientIdsType.IsByRef)
                    continue;

                if (IsNetIdCollectionType(clientIdsType.GetElementType()))
                    return method;
            }

            return null;
        }

        private static MethodInfo FindServerSendOneStringMethod()
        {
            MethodInfo[] methods = multiplayerType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method.Name != "Server_SendSimpleMessageToOneClient")
                    continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 4)
                    continue;

                if (parameters[0].ParameterType == typeof(ushort).MakeByRefType() &&
                    IsNetIdParameter(parameters[1].ParameterType) &&
                    parameters[2].ParameterType == typeof(string) &&
                    parameters[3].ParameterType == typeof(bool))
                    return method;
            }

            return null;
        }

        private static MethodInfo FindServerObjectSyncSingleMethod()
        {
            MethodInfo withReliable = FindMethod(netObjectRegistryType, "Server_ObjectSyncSingle", typeof(GameObject), typeof(bool));
            if (withReliable != null)
                return withReliable;

            return FindMethod(netObjectRegistryType, "Server_ObjectSyncSingle", typeof(GameObject));
        }

        private static void RegisterReceiver(bool server, ushort messageId)
        {
            if (IsMessageRegistered(server, messageId))
                return;

            MethodInfo register = FindRegisterReceiverMethod(server);
            if (register == null)
                throw new MissingMethodException(netType.FullName, server ? "RegisterServerReceiver" : "RegisterClientReceiver");

            Delegate receiver = CreateReceiverDelegate(server, messageId);
            register.Invoke(null, new object[] { messageId, receiver });
        }

        private static MethodInfo FindRegisterReceiverMethod(bool server)
        {
            string[] names = server
                ? new[] { "RegisterServerReceiver", "RegisterServerReciever" }
                : new[] { "RegisterClientReceiver", "RegisterClientReciever" };

            for (int i = 0; i < names.Length; i++)
            {
                MethodInfo method = netType.GetMethod(names[i], BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (method == null)
                    continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 2 &&
                    parameters[0].ParameterType == typeof(ushort) &&
                    parameters[1].ParameterType.IsAssignableFrom(receiverDelegateType))
                    return method;
            }

            return null;
        }

        private static bool IsMessageRegistered(bool server, ushort messageId)
        {
            FieldInfo field = server ? serverHandlersField : clientHandlersField;
            if (field == null)
                return false;

            try
            {
                System.Collections.IDictionary dict = field.GetValue(null) as System.Collections.IDictionary;
                return dict != null && dict.Contains(messageId);
            }
            catch
            {
                return false;
            }
        }

        private static Delegate CreateReceiverDelegate(bool server, ushort messageId)
        {
            string methodName;
            if (server)
            {
                if (messageId == FuseCutRequestMessageId)
                    methodName = nameof(OnServerFuseCutRequestBoxed);
                else if (messageId == PresenceRequestMessageId)
                    methodName = nameof(OnServerPresenceRequestBoxed);
                else
                    methodName = nameof(OnServerRequestBoxed);
            }
            else
            {
                if (messageId == FuseCutAnnounceMessageId)
                    methodName = nameof(OnClientFuseCutAnnouncementBoxed);
                else if (messageId == PresenceAnnounceMessageId)
                    methodName = nameof(OnClientPresenceAnnouncementBoxed);
                else
                    methodName = nameof(OnClientAnnouncementBoxed);
            }

            MethodInfo target = typeof(KrokMpBridge).GetMethod(
                methodName,
                BindingFlags.Static | BindingFlags.NonPublic);

            // KrokMP 的接收委托参数是 ref NetDataReader。
            // 用 DynamicMethod 桥接 object 读取，避免项目直接依赖 LiteNetLib 类型。
            DynamicMethod bridge = new DynamicMethod(
                server ? "TemporalPanicButtonKrokServerReceiver" : "TemporalPanicButtonKrokClientReceiver",
                typeof(void),
                new[] { receiverDelegateType.GetMethod("Invoke").GetParameters()[0].ParameterType, readerType.MakeByRefType() },
                typeof(KrokMpBridge),
                true);

            ILGenerator il = bridge.GetILGenerator();
            EmitClientIdToUInt(il, receiverDelegateType.GetMethod("Invoke").GetParameters()[0].ParameterType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldind_Ref);
            il.Emit(OpCodes.Call, target);
            il.Emit(OpCodes.Ret);

            return bridge.CreateDelegate(receiverDelegateType);
        }

        private static void CacheNetIdAccessors()
        {
            netIdValueField = null;
            netIdConstructor = null;
            if (netIdType == null || netIdType == typeof(uint) || netIdType == typeof(ushort))
                return;

            netIdValueField = netIdType.GetField("id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            netIdConstructor = netIdType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(ushort) },
                null);
        }

        private static bool IsNetIdParameter(Type type)
        {
            Type actualType = type != null && type.IsByRef ? type.GetElementType() : type;
            if (actualType == null)
                return false;

            if (actualType == typeof(uint) || actualType == typeof(ushort) || actualType == typeof(int))
                return true;

            if (netIdType != null && actualType == netIdType)
                return true;

            return actualType.FullName == "KrokoshaCasualtiesMP.knetid";
        }

        private static bool IsNetIdCollectionType(Type type)
        {
            if (type == null)
                return false;

            if (type.IsArray)
                return IsNetIdParameter(type.GetElementType());

            if (!type.IsGenericType)
                return false;

            Type genericType = type.GetGenericTypeDefinition();
            if (genericType != typeof(IReadOnlyList<>) &&
                genericType != typeof(IEnumerable<>) &&
                genericType != typeof(IList<>) &&
                genericType != typeof(List<>))
                return false;

            return IsNetIdParameter(type.GetGenericArguments()[0]);
        }

        private static void EmitClientIdToUInt(ILGenerator il, Type sourceType)
        {
            if (sourceType == typeof(uint))
            {
                il.Emit(OpCodes.Ldarg_0);
                return;
            }

            if (sourceType == typeof(ushort) || sourceType == typeof(byte))
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Conv_U4);
                return;
            }

            if (netIdValueField != null && sourceType == netIdValueField.DeclaringType && netIdValueField.FieldType == typeof(ushort))
            {
                il.Emit(OpCodes.Ldarga_S, (byte)0);
                il.Emit(OpCodes.Ldfld, netIdValueField);
                il.Emit(OpCodes.Conv_U4);
                return;
            }

            il.Emit(OpCodes.Ldarg_0);
            if (sourceType.IsValueType)
                il.Emit(OpCodes.Box, sourceType);

            il.Emit(OpCodes.Call, typeof(KrokMpBridge).GetMethod(nameof(ConvertKrokIdToUInt), BindingFlags.Static | BindingFlags.NonPublic));
        }

        internal static uint ConvertKrokIdToUInt(object value)
        {
            if (value == null)
                return uint.MaxValue;

            if (value is uint uintValue)
                return uintValue;

            if (value is ushort ushortValue)
                return ushortValue;

            if (value is byte byteValue)
                return byteValue;

            if (value is int intValue)
                return intValue < 0 ? uint.MaxValue : (uint)intValue;

            Type valueType = value.GetType();
            FieldInfo idField = valueType.GetField("id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (idField != null)
                return ConvertKrokIdToUInt(idField.GetValue(value));

            if (uint.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint parsed))
                return parsed;

            return uint.MaxValue;
        }

        private static object CreateClientIdArgument(uint clientId, Type parameterType)
        {
            Type targetType = parameterType.IsByRef ? parameterType.GetElementType() : parameterType;
            if (targetType == typeof(uint))
                return clientId;

            if (targetType == typeof(ushort))
                return clientId > ushort.MaxValue ? ushort.MaxValue : (ushort)clientId;

            if (targetType == typeof(int))
                return clientId > int.MaxValue ? int.MaxValue : (int)clientId;

            if (!IsNetIdParameter(targetType))
                return clientId;

            ushort shortId = clientId > ushort.MaxValue ? ushort.MaxValue : (ushort)clientId;
            if (netIdConstructor != null && netIdConstructor.DeclaringType == targetType)
                return netIdConstructor.Invoke(new object[] { shortId });

            object boxed = Activator.CreateInstance(targetType);
            FieldInfo idField = targetType.GetField("id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (idField != null)
                idField.SetValue(boxed, shortId);

            return boxed;
        }

        private static object CreateClientIdArgument(object clientId, Type parameterType)
        {
            return CreateClientIdArgument(ConvertKrokIdToUInt(clientId), parameterType);
        }

        private static void SendRequest(string payload)
        {
            try
            {
                if (netCreateWriterMethod != null && writerPutKrokStringMethod != null && netClientSendMethod != null)
                {
                    // 优先走底层 writer，这条路径最贴近 KrokMP 自己的消息系统。
                    object writer = CreatePayloadWriter(RequestMessageId, payload);
                    object delivery = CreateReliableDelivery();
                    object[] args = { delivery, writer };
                    netClientSendMethod.Invoke(null, args);
                    sentRequests++;
                    lastError = null;
                    lastRequestStatus = "sent " + DescribePayload(payload);
                    return;
                }

                // 旧版 KrokMP 可能只有简单字符串 helper，保留作为兼容兜底。
                object[] fallbackArgs = { RequestMessageId, payload, true };
                clientSendStringMethod.Invoke(null, fallbackArgs);
                sentRequests++;
                lastError = null;
                lastRequestStatus = "fallback " + DescribePayload(payload);
            }
            catch (Exception ex)
            {
                lastError = "send-rq " + GetExceptionName(ex);
                lastRequestStatus = "fail " + DescribePayload(payload);
                Debug.LogWarning("Temporal Panic Button: KrokMP request send failed: " + ex);
            }
        }

        private static void SendFuseCutRequest(string payload)
        {
            try
            {
                object writer = CreatePayloadWriter(FuseCutRequestMessageId, payload);
                object delivery = CreateReliableDelivery();
                object[] args = { delivery, writer };
                netClientSendMethod.Invoke(null, args);
                lastError = null;
            }
            catch (Exception ex)
            {
                lastError = "send-fuse " + GetExceptionName(ex);
                Debug.LogWarning("Temporal Panic Button: KrokMP fuse cut request failed: " + ex);
            }
        }

        private static void SendPresenceRequest(string payload)
        {
            try
            {
                object writer = CreatePayloadWriter(PresenceRequestMessageId, payload);
                object delivery = CreateReliableDelivery();
                object[] args = { delivery, writer };
                netClientSendMethod.Invoke(null, args);
                lastError = null;
            }
            catch (Exception ex)
            {
                lastError = "send-pres " + GetExceptionName(ex);
                Debug.LogWarning("Temporal Panic Button: KrokMP presence send failed: " + ex);
            }
        }

        private static void RelayAnnouncement(string payload)
        {
            try
            {
                object clientIds = allClientIdsProperty?.GetValue(null, null);
                if (clientIds != null && netServerSendToClientsMethod != null)
                {
                    // 主机优先直接给当前 client 列表发送，避免 relay helper 在部分版本里漏目标。
                    object writer = CreatePayloadWriter(AnnounceMessageId, payload);
                    object delivery = CreateReliableDelivery();
                    object[] args = { delivery, writer, clientIds };
                    netServerSendToClientsMethod.Invoke(null, args);
                    sentAnnouncements++;
                    lastError = null;
                    lastAnnouncementStatus = "broadcast " + DescribePayload(payload);
                    return;
                }
            }
            catch (Exception ex)
            {
                lastError = "send-an " + GetExceptionName(ex);
                lastAnnouncementStatus = "direct-fail " + DescribePayload(payload);
                Debug.LogWarning("Temporal Panic Button: KrokMP direct relay failed: " + ex);
            }

            bool sentToAnyClient = false;
            try
            {
                // 如果直接发送不可用，再逐级回退到旧版 KrokMP helper。
                System.Collections.IEnumerable clientIds = allClientIdsProperty?.GetValue(null, null) as System.Collections.IEnumerable;
                if (clientIds != null && serverSendOneStringMethod != null)
                {
                    Type clientIdParameterType = serverSendOneStringMethod.GetParameters()[1].ParameterType;
                    foreach (object clientIdObject in clientIds)
                    {
                        object clientId = CreateClientIdArgument(clientIdObject, clientIdParameterType);
                        object[] oneArgs = { AnnounceMessageId, clientId, payload, true };
                        serverSendOneStringMethod.Invoke(null, oneArgs);
                        sentToAnyClient = true;
                    }
                }

                if (!sentToAnyClient && serverRelayStringMethod != null)
                {
                    object[] args = { AnnounceMessageId, payload, true, true };
                    serverRelayStringMethod.Invoke(null, args);
                    sentToAnyClient = true;
                }

                if (sentToAnyClient)
                {
                    sentAnnouncements++;
                    lastError = null;
                    lastAnnouncementStatus = "relay " + DescribePayload(payload);
                }
            }
            catch (Exception ex)
            {
                lastError = "send-an " + GetExceptionName(ex);
                lastAnnouncementStatus = "relay-fail " + DescribePayload(payload);
                Debug.LogWarning("Temporal Panic Button: KrokMP relay fallback failed: " + ex);
            }
        }

        private static void RelayFuseCutAnnouncement(string payload)
        {
            try
            {
                object clientIds = allClientIdsProperty?.GetValue(null, null);
                if (clientIds != null && netServerSendToClientsMethod != null)
                {
                    object writer = CreatePayloadWriter(FuseCutAnnounceMessageId, payload);
                    object delivery = CreateReliableDelivery();
                    object[] args = { delivery, writer, clientIds };
                    netServerSendToClientsMethod.Invoke(null, args);
                    lastError = null;
                    return;
                }
            }
            catch (Exception ex)
            {
                lastError = "send-fuse-an " + GetExceptionName(ex);
                Debug.LogWarning("Temporal Panic Button: KrokMP fuse cut announcement failed: " + ex);
            }
        }

        private static void RelayPresenceAnnouncement(string payload)
        {
            try
            {
                object clientIds = allClientIdsProperty?.GetValue(null, null);
                if (clientIds != null && netServerSendToClientsMethod != null)
                {
                    object writer = CreatePayloadWriter(PresenceAnnounceMessageId, payload);
                    object delivery = CreateReliableDelivery();
                    object[] args = { delivery, writer, clientIds };
                    netServerSendToClientsMethod.Invoke(null, args);
                    lastError = null;
                    return;
                }
            }
            catch (Exception ex)
            {
                lastError = "send-pres-an " + GetExceptionName(ex);
                Debug.LogWarning("Temporal Panic Button: KrokMP presence announcement failed: " + ex);
            }

            bool sentToAnyClient = false;
            try
            {
                System.Collections.IEnumerable clientIds = allClientIdsProperty?.GetValue(null, null) as System.Collections.IEnumerable;
                if (clientIds != null && serverSendOneStringMethod != null)
                {
                    Type clientIdParameterType = serverSendOneStringMethod.GetParameters()[1].ParameterType;
                    foreach (object clientIdObject in clientIds)
                    {
                        object clientId = CreateClientIdArgument(clientIdObject, clientIdParameterType);
                        object[] oneArgs = { PresenceAnnounceMessageId, clientId, payload, true };
                        serverSendOneStringMethod.Invoke(null, oneArgs);
                        sentToAnyClient = true;
                    }
                }

                if (!sentToAnyClient && serverRelayStringMethod != null)
                {
                    object[] args = { PresenceAnnounceMessageId, payload, true, true };
                    serverRelayStringMethod.Invoke(null, args);
                    sentToAnyClient = true;
                }

                if (sentToAnyClient)
                    lastError = null;
            }
            catch (Exception ex)
            {
                lastError = "send-pres-an " + GetExceptionName(ex);
                Debug.LogWarning("Temporal Panic Button: KrokMP presence relay fallback failed: " + ex);
            }
        }

        private static void OnServerRequestBoxed(uint clientId, object reader)
        {
            receivedRequests++;
            string payload = ReadString(reader);
            if (string.IsNullOrEmpty(payload))
            {
                lastError = "empty-rq";
                lastRequestStatus = "empty";
                return;
            }

            if (!KrokMpPayload.TryParse(payload, out MultiplayerTimeStopEvent _))
            {
                lastError = "bad-rq";
                lastRequestStatus = "bad";
                return;
            }

            RememberPresence(clientId);
            // casterId 以主机看到的发送者为准。
            // 客机刚入局时可能还不知道自己的 id，会发出 0；这里统一替换成 KrokMP 提供的 clientId。
            payload = KrokMpPayload.ReplaceCaster(payload, clientId);
            bool applied = TimeStopController.ReceiveMultiplayerTimeStop(payload);
            lastError = null;
            lastRequestStatus = (applied ? "recv-ok " : "recv-drop ") + DescribePayload(payload);
            RelayAnnouncement(payload);
        }

        private static void OnClientAnnouncementBoxed(uint clientId, object reader)
        {
            receivedAnnouncements++;
            string payload = ReadString(reader);
            if (!string.IsNullOrEmpty(payload))
            {
                bool applied = TimeStopController.ReceiveMultiplayerTimeStop(payload);
                lastError = null;
                lastAnnouncementStatus = (applied ? "recv-ok " : "recv-drop ") + DescribePayload(payload);
            }
            else
            {
                lastError = "empty-an";
                lastAnnouncementStatus = "empty";
            }
        }

        private static void OnServerFuseCutRequestBoxed(uint clientId, object reader)
        {
            string payload = ReadString(reader);
            if (!TryParseFuseCutPayload(payload, out uint syncId))
            {
                lastError = "bad-fuse";
                return;
            }

            RememberPresence(clientId);
            if (DynamiteFuseCutFeature.ApplyNetworkFuseCut(syncId))
                RelayFuseCutAnnouncement(payload);
        }

        private static void OnClientFuseCutAnnouncementBoxed(uint clientId, object reader)
        {
            string payload = ReadString(reader);
            if (!TryParseFuseCutPayload(payload, out uint syncId))
            {
                lastError = "bad-fuse-an";
                return;
            }

            DynamiteFuseCutFeature.ApplyNetworkFuseCut(syncId);
        }

        private static void OnServerPresenceRequestBoxed(uint clientId, object reader)
        {
            string payload = ReadString(reader);
            if (!TryParsePresencePayload(payload, out uint announcedClientId, out bool _, out bool announcedIntelligenceProgression))
            {
                lastError = "bad-pres";
                return;
            }

            uint resolvedClientId = clientId == uint.MaxValue ? announcedClientId : clientId;
            RememberPresence(resolvedClientId);
            RelayPresenceAnnouncement(BuildPresencePayload(resolvedClientId, false, announcedIntelligenceProgression));
            RelayPresenceAnnouncement(BuildPresencePayload(GetLocalClientId(), true, ModSettings.LocalIntelligenceProgression));
        }

        private static void OnClientPresenceAnnouncementBoxed(uint clientId, object reader)
        {
            string payload = ReadString(reader);
            if (!TryParsePresencePayload(payload, out uint moddedClientId, out bool isHost, out bool intelligenceProgression))
            {
                lastError = "bad-pres-an";
                return;
            }

            RememberPresence(moddedClientId);
            if (isHost)
            {
                hostPresenceKnown = true;
                hostIntelligenceProgression = intelligenceProgression;
            }
        }

        private static string ReadString(object reader)
        {
            try
            {
                if (readerGetKrokStringMethod != null)
                {
                    object[] args = { reader, null, true };
                    readerGetKrokStringMethod.Invoke(null, args);
                    return args[1] as string;
                }

                return readerGetStringMethod?.Invoke(reader, null) as string;
            }
            catch (Exception ex)
            {
                lastError = "read " + GetExceptionName(ex);
                Debug.LogWarning("Temporal Panic Button: KrokMP payload read failed: " + ex);
                return null;
            }
        }

        private static void TickPresenceAnnouncements()
        {
            if (!available)
                return;

            bool running = IsNetworkRunning;
            if (!running)
            {
                if (lastNetworkRunning)
                {
                    knownModdedClientIds.Clear();
                    hostPresenceKnown = false;
                    hostIntelligenceProgression = false;
                }

                lastNetworkRunning = false;
                nextPresencePingTime = 0f;
                networkRunningSince = 0f;
                return;
            }

            if (!lastNetworkRunning)
            {
                knownModdedClientIds.Clear();
                hostPresenceKnown = false;
                hostIntelligenceProgression = false;
                networkRunningSince = Time.realtimeSinceStartup;
            }

            uint localClientId = GetLocalClientId();
            RememberPresence(localClientId);
            if (IsServer)
            {
                hostPresenceKnown = true;
                hostIntelligenceProgression = ModSettings.LocalIntelligenceProgression;
            }

            if (lastNetworkRunning && Time.realtimeSinceStartup < nextPresencePingTime)
                return;

            lastNetworkRunning = true;
            nextPresencePingTime = Time.realtimeSinceStartup + PresencePingInterval;
            string payload = BuildPresencePayload(localClientId, IsServer, ModSettings.LocalIntelligenceProgression);
            if (IsServer)
                RelayPresenceAnnouncement(payload);
            else
                SendPresenceRequest(payload);
        }

        private static void RememberPresence(uint clientId)
        {
            if (clientId != uint.MaxValue)
                knownModdedClientIds.Add(clientId);
        }

        private static string BuildPresencePayload(uint clientId, bool isHost, bool intelligenceProgression)
        {
            return string.Join("|", new[]
            {
                PresencePayloadVersion2,
                clientId.ToString(CultureInfo.InvariantCulture),
                isHost ? "host" : "client",
                intelligenceProgression ? "1" : "0"
            });
        }

        private static bool TryParsePresencePayload(string payload, out uint clientId)
        {
            return TryParsePresencePayload(payload, out clientId, out bool _, out bool _);
        }

        private static bool TryParsePresencePayload(string payload, out uint clientId, out bool isHost, out bool intelligenceProgression)
        {
            clientId = uint.MaxValue;
            isHost = false;
            intelligenceProgression = false;
            if (string.IsNullOrEmpty(payload))
                return false;

            string[] parts = payload.Split('|');
            if (parts.Length == 2 && parts[0] == PresencePayloadVersion)
                return uint.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out clientId);

            if (parts.Length < 4 || parts[0] != PresencePayloadVersion2)
                return false;

            if (!uint.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out clientId))
                return false;

            isHost = parts[2] == "host";
            intelligenceProgression = parts[3] == "1";
            return true;
        }

        public static bool TryAnnounceDynamiteFuseCut(Item item)
        {
            EnsureInitialized();
            EnsureRegistered();
            if (!available || !IsNetworkRunning || item == null)
                return false;

            if (!TryGetItemSyncId(item, out uint syncId))
                return false;

            string payload = BuildFuseCutPayload(syncId);
            if (IsServer)
            {
                DynamiteFuseCutFeature.ApplyNetworkFuseCut(syncId);
                RelayFuseCutAnnouncement(payload);
                return true;
            }

            SendFuseCutRequest(payload);
            return true;
        }

        public static bool TryAnnounceTriggerForClient(uint casterClientId, HazardKind hazard, Vector2 origin, float duration)
        {
            EnsureInitialized();
            EnsureRegistered();
            if (!available || !IsNetworkRunning || !IsServer || casterClientId == uint.MaxValue)
                return false;

            if (!HasPresenceForClient(casterClientId))
            {
                lastAnnouncementStatus = "host-target no-mod " + casterClientId.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            localSequence++;
            string payload = KrokMpPayload.Build(casterClientId, duration, origin, hazard, localSequence, "host:" + LocalInstanceId);
            bool appliedLocally = TimeStopController.ReceiveMultiplayerTimeStop(payload);
            lastAnnouncementStatus = "host-target " + (appliedLocally ? "ok " : "drop ") + DescribePayload(payload);
            RelayAnnouncement(payload);
            return true;
        }

        public static bool TryGetItemSyncId(Item item, out uint syncId)
        {
            syncId = 0;
            EnsureInitialized();
            if (!available || item == null || syncInfoType == null)
                return false;

            object syncInfo = null;
            if (itemSyncTryGetSyncInfoMethod != null)
            {
                object[] args = { item, null };
                if (InvokeBool(itemSyncTryGetSyncInfoMethod, args))
                    syncInfo = args[1];
            }

            if (syncInfo == null && netObjectRegistryTryGetSyncInfoOrRegisterMethod != null && item.gameObject != null)
            {
                object[] args = { item.gameObject, null };
                if (InvokeBool(netObjectRegistryTryGetSyncInfoOrRegisterMethod, args))
                    syncInfo = args[1];
            }

            if (syncInfo == null && netObjectRegistryTryGetSyncInfoByObjectMethod != null && item.gameObject != null)
            {
                object[] args = { item.gameObject, null };
                if (InvokeBool(netObjectRegistryTryGetSyncInfoByObjectMethod, args))
                    syncInfo = args[1];
            }

            if (syncInfo == null && itemSyncTryGetSyncInfoMethod != null)
            {
                object[] args = { item, null };
                if (InvokeBool(itemSyncTryGetSyncInfoMethod, args))
                    syncInfo = args[1];
            }

            return TryGetSyncId(syncInfo, out syncId);
        }

        public static bool TryGetItemBySyncId(uint syncId, out Item item)
        {
            item = null;
            EnsureInitialized();
            if (!available || syncInfoType == null || netObjectRegistryTryGetSyncInfoByIdMethod == null)
                return false;

            Type syncIdParameterType = netObjectRegistryTryGetSyncInfoByIdMethod.GetParameters()[0].ParameterType;
            object[] args = { CreateClientIdArgument(syncId, syncIdParameterType), null };
            if (!InvokeBool(netObjectRegistryTryGetSyncInfoByIdMethod, args))
                return false;

            item = GetItemFromSyncInfo(args[1]);
            return item != null;
        }

        public static bool TryServerSyncItem(Item item, bool reliable)
        {
            EnsureInitialized();
            if (!available || !IsServer || item == null || item.gameObject == null || netObjectRegistryServerObjectSyncSingleMethod == null)
                return false;

            try
            {
                ParameterInfo[] parameters = netObjectRegistryServerObjectSyncSingleMethod.GetParameters();
                object[] args = parameters.Length == 2
                    ? new object[] { item.gameObject, reliable }
                    : new object[] { item.gameObject };
                netObjectRegistryServerObjectSyncSingleMethod.Invoke(null, args);
                return true;
            }
            catch (Exception ex)
            {
                lastError = "sync-item " + GetExceptionName(ex);
                return false;
            }
        }

        private static object CreatePayloadWriter(ushort messageId, string payload)
        {
            object writer = netCreateWriterMethod.Invoke(null, new object[] { messageId });
            object[] putArgs = { writer, payload, true };
            writerPutKrokStringMethod.Invoke(null, putArgs);
            return writer;
        }

        private static object CreateReliableDelivery()
        {
            // 当前 KrokMP 使用的 LiteNetLib 中 ReliableOrdered 的枚举值为 0。
            return Enum.ToObject(deliveryMethodType, 0);
        }

        private static string BuildFuseCutPayload(uint syncId)
        {
            return FuseCutPayloadVersion + "|" + syncId.ToString(CultureInfo.InvariantCulture);
        }

        private static bool TryParseFuseCutPayload(string payload, out uint syncId)
        {
            syncId = 0;
            if (string.IsNullOrEmpty(payload))
                return false;

            string[] parts = payload.Split('|');
            return parts.Length == 2 &&
                   parts[0] == FuseCutPayloadVersion &&
                   uint.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out syncId);
        }

        private static bool InvokeBool(MethodInfo method, object[] args)
        {
            try
            {
                object result = method.Invoke(null, args);
                return result is bool value && value;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetSyncId(object syncInfo, out uint syncId)
        {
            syncId = 0;
            if (syncInfo == null || syncInfoSyncIdField == null)
                return false;

            try
            {
                syncId = ConvertKrokIdToUInt(syncInfoSyncIdField.GetValue(syncInfo));
                return syncId != 0 && syncId != uint.MaxValue;
            }
            catch
            {
                return false;
            }
        }

        private static Item GetItemFromSyncInfo(object syncInfo)
        {
            if (syncInfo == null || syncInfoItemProperty == null)
                return null;

            try
            {
                return syncInfoItemProperty.GetValue(syncInfo, null) as Item;
            }
            catch
            {
                return null;
            }
        }

        private static string GetExceptionName(Exception ex)
        {
            TargetInvocationException invocationException = ex as TargetInvocationException;
            if (invocationException?.InnerException != null)
                return invocationException.InnerException.GetType().Name;

            return ex.GetType().Name;
        }

        public static bool TryParsePayload(string payload, out MultiplayerTimeStopEvent stopEvent)
        {
            return KrokMpPayload.TryParse(payload, out stopEvent);
        }

        public static bool IsLocalOrigin(string originInstanceId)
        {
            return !string.IsNullOrEmpty(originInstanceId) && originInstanceId == LocalInstanceId;
        }

        private static string DescribePayload(string payload)
        {
            if (string.IsNullOrEmpty(payload))
                return "len=0";

            string[] parts = payload.Split('|');
            if (parts.Length < 8)
                return "len=" + payload.Length.ToString(CultureInfo.InvariantCulture);

            return string.Format(
                CultureInfo.InvariantCulture,
                "stop={0} caster={1} dur={2}",
                parts[6],
                parts[1],
                parts[2]);
        }

        private static bool GetBoolProperty(PropertyInfo property)
        {
            if (property == null)
                return false;

            try
            {
                return (bool)property.GetValue(null, null);
            }
            catch
            {
                return false;
            }
        }

        private static uint GetLocalClientId()
        {
            try
            {
                object localPlayer = localPlayerField == null ? null : localPlayerField.GetValue(null);
                Body localBody = PlayerCamera.main == null ? null : PlayerCamera.main.body;

                if (localPlayer != null && clientIdBackingField != null)
                {
                    uint clientId = ConvertKrokIdToUInt(clientIdBackingField.GetValue(localPlayer));
                    if (clientId != uint.MaxValue)
                        return clientId;
                }

                if (localPlayer != null && netPlayerClientIdProperty != null)
                {
                    uint clientId = ConvertKrokIdToUInt(netPlayerClientIdProperty.GetValue(localPlayer, null));
                    if (clientId != uint.MaxValue)
                        return clientId;
                }

                if (localPlayer != null && localBody != null && netPlayerGetClientIdFromBodyMethod != null)
                {
                    object result = netPlayerGetClientIdFromBodyMethod.Invoke(localPlayer, new object[] { localBody });
                    uint resolvedClientId = ConvertKrokIdToUInt(result);
                    if (resolvedClientId != uint.MaxValue)
                        return resolvedClientId;
                }
            }
            catch
            {
            }

            return 0;
        }
    }

    /// <summary>
    /// 已解析的 KrokMP 时停广播。
    /// TimeStopController 只消费这个结构，不直接关心原始字符串格式。
    /// </summary>
    internal readonly struct MultiplayerTimeStopEvent
    {
        public readonly uint CasterClientId;
        public readonly float Duration;
        public readonly Vector2 Origin;
        public readonly HazardKind Hazard;
        public readonly uint StopId;
        public readonly string OriginInstanceId;

        public MultiplayerTimeStopEvent(uint casterClientId, float duration, Vector2 origin, HazardKind hazard, uint stopId, string originInstanceId)
        {
            CasterClientId = casterClientId;
            Duration = duration;
            Origin = origin;
            Hazard = hazard;
            StopId = stopId;
            OriginInstanceId = originInstanceId;
        }
    }
}
