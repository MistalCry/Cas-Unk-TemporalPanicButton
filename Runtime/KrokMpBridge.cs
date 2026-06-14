using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace TemporalPanicButton.Runtime
{
    /// <summary>
    /// Optional runtime bridge to KrokMP.
    /// Everything is discovered through reflection so the plugin remains usable without KrokMP installed.
    /// </summary>
    internal static class KrokMpBridge
    {
        // Private message ids used only by this mod. Request is client -> host, announce is host -> clients.
        private const ushort RequestMessageId = 0x7A50;
        private const ushort AnnounceMessageId = 0x7A51;

        private static Type netType;
        private static Type netPlayerType;
        private static Type multiplayerType;
        private static Type readerType;
        private static Type writerType;
        private static Type deliveryMethodType;
        private static Type netExtensionsType;
        private static Type receiverDelegateType;
        private static MethodInfo clientSendStringMethod;
        private static MethodInfo serverRelayStringMethod;
        private static MethodInfo serverSendOneStringMethod;
        private static MethodInfo readerGetStringMethod;
        private static MethodInfo readerGetKrokStringMethod;
        private static MethodInfo writerPutKrokStringMethod;
        private static MethodInfo netCreateWriterMethod;
        private static MethodInfo netClientSendMethod;
        private static MethodInfo netServerSendToClientsMethod;
        private static PropertyInfo allClientIdsProperty;
        private static PropertyInfo netRunningProperty;
        private static PropertyInfo netIsServerProperty;
        private static FieldInfo localPlayerField;
        private static FieldInfo clientIdBackingField;
        private static FieldInfo serverHandlersField;
        private static FieldInfo clientHandlersField;
        private static bool initialized;
        private static bool available;
        private static float nextInitAttemptTime;
        private static uint localSequence;
        private static string status = "not initialized";
        private static string lastError;
        private static uint sentRequests;
        private static uint receivedRequests;
        private static uint sentAnnouncements;
        private static uint receivedAnnouncements;

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
            string payload = KrokMpPayload.Build(GetLocalClientId(), duration, origin, hazard, localSequence);
            // Apply immediately on the local machine so the caster does not wait for network echo.
            TimeStopController.ReceiveMultiplayerTimeStop(payload);
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
                // KrokMP is optional, so the mod discovers it at runtime instead of taking
                // a hard assembly reference that would break singleplayer installs.
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
                netExtensionsType = assembly.GetType("KrokoshaCasualtiesMP.MyLiteNetLibExtensions");
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

                // KrokMP has gone through a few API shapes. Cache both high-level helpers and
                // lower-level Net writer/send methods, then choose the best available path later.
                netRunningProperty = netType.GetProperty("running", BindingFlags.Static | BindingFlags.Public);
                netIsServerProperty = netType.GetProperty("is_server", BindingFlags.Static | BindingFlags.Public);
                localPlayerField = netPlayerType.GetField("LOCAL_PLAYER", BindingFlags.Static | BindingFlags.Public);
                clientIdBackingField = netPlayerType.GetField("<clientId>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
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

                serverSendOneStringMethod = FindMethod(
                    multiplayerType,
                    "Server_SendSimpleMessageToOneClient",
                    typeof(ushort).MakeByRefType(),
                    typeof(uint),
                    typeof(string),
                    typeof(bool));

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

                // Register both sides. On a host, the local server receiver handles client requests;
                // every connected client listens for host announcements.
                RegisterReceiver(true, RequestMessageId);
                RegisterReceiver(false, AnnounceMessageId);
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
                // KrokMP can rebuild handler dictionaries while entering/leaving a session.
                // Re-register if our handler disappeared.
                if (!IsMessageRegistered(true, RequestMessageId))
                    RegisterReceiver(true, RequestMessageId);

                if (!IsMessageRegistered(false, AnnounceMessageId))
                    RegisterReceiver(false, AnnounceMessageId);
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
            string role = GetBoolProperty(netIsServerProperty) ? "host" : "client";
            string state = running ? "ready" : "idle";
            string result = string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1} id {2} rq {3}/{4} an {5}/{6}",
                state,
                role,
                GetLocalClientId(),
                sentRequests,
                receivedRequests,
                sentAnnouncements,
                receivedAnnouncements);

            // This short status is intentionally HUD-friendly and doubles as a multiplayer debug line.
            if (!string.IsNullOrEmpty(lastError))
                result += " " + lastError;

            return result;
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
            MethodInfo[] methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public);
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

        private static MethodInfo FindServerSendToClientsMethod()
        {
            MethodInfo[] methods = netType.GetMethods(BindingFlags.Static | BindingFlags.Public);
            Type readOnlyListType = typeof(IReadOnlyList<uint>);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method.Name != "Server_SendToClients")
                    continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 3)
                    continue;

                if (parameters[0].ParameterType != deliveryMethodType.MakeByRefType() ||
                    parameters[1].ParameterType != writerType.MakeByRefType() ||
                    parameters[2].ParameterType != readOnlyListType.MakeByRefType())
                    continue;

                return method;
            }

            return null;
        }

        private static void RegisterReceiver(bool server, ushort messageId)
        {
            if (IsMessageRegistered(server, messageId))
                throw new InvalidOperationException("KrokMP message id already registered: " + messageId.ToString("X4", CultureInfo.InvariantCulture));

            string registerMethodName = server ? "RegisterServerReciever" : "RegisterClientReciever";
            MethodInfo register = netType.GetMethod(registerMethodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (register == null)
                throw new MissingMethodException(netType.FullName, registerMethodName);

            Delegate receiver = CreateReceiverDelegate(server);
            register.Invoke(null, new object[] { messageId, receiver });
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

        private static Delegate CreateReceiverDelegate(bool server)
        {
            MethodInfo target = typeof(KrokMpBridge).GetMethod(
                server ? nameof(OnServerRequestBoxed) : nameof(OnClientAnnouncementBoxed),
                BindingFlags.Static | BindingFlags.NonPublic);

            // KrokMP's receiver delegate uses ref NetDataReader from LiteNetLib.
            // The dynamic bridge lets this mod subscribe without directly compiling against LiteNetLib.
            DynamicMethod bridge = new DynamicMethod(
                server ? "TemporalPanicButtonKrokServerReceiver" : "TemporalPanicButtonKrokClientReceiver",
                typeof(void),
                new[] { typeof(uint), readerType.MakeByRefType() },
                typeof(KrokMpBridge),
                true);

            ILGenerator il = bridge.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldind_Ref);
            il.Emit(OpCodes.Call, target);
            il.Emit(OpCodes.Ret);

            return bridge.CreateDelegate(receiverDelegateType);
        }

        private static void SendRequest(string payload)
        {
            try
            {
                if (netCreateWriterMethod != null && writerPutKrokStringMethod != null && netClientSendMethod != null)
                {
                    // Prefer the raw writer path because it matches KrokMP's internal message system.
                    object writer = CreatePayloadWriter(RequestMessageId, payload);
                    object delivery = CreateReliableDelivery();
                    object[] args = { delivery, writer };
                    netClientSendMethod.Invoke(null, args);
                    sentRequests++;
                    lastError = null;
                    return;
                }

                // Older KrokMP builds expose a simpler string helper; keep it as a fallback.
                object[] fallbackArgs = { RequestMessageId, payload, true };
                clientSendStringMethod.Invoke(null, fallbackArgs);
                sentRequests++;
                lastError = null;
            }
            catch (Exception ex)
            {
                lastError = "send-rq " + GetExceptionName(ex);
                Debug.LogWarning("Temporal Panic Button: KrokMP request send failed: " + ex);
            }
        }

        private static void RelayAnnouncement(string payload)
        {
            try
            {
                object clientIds = allClientIdsProperty?.GetValue(null, null);
                if (clientIds != null && netServerSendToClientsMethod != null)
                {
                    // Direct send is preferred because the host can include every current client id.
                    object writer = CreatePayloadWriter(AnnounceMessageId, payload);
                    object delivery = CreateReliableDelivery();
                    object[] args = { delivery, writer, clientIds };
                    netServerSendToClientsMethod.Invoke(null, args);
                    sentAnnouncements++;
                    lastError = null;
                    return;
                }
            }
            catch (Exception ex)
            {
                lastError = "send-an " + GetExceptionName(ex);
                Debug.LogWarning("Temporal Panic Button: KrokMP direct relay failed: " + ex);
            }

            bool sentToAnyClient = false;
            try
            {
                // Fall back through progressively older KrokMP helper APIs.
                System.Collections.IEnumerable clientIds = allClientIdsProperty?.GetValue(null, null) as System.Collections.IEnumerable;
                if (clientIds != null && serverSendOneStringMethod != null)
                {
                    foreach (object clientIdObject in clientIds)
                    {
                        uint clientId = Convert.ToUInt32(clientIdObject, CultureInfo.InvariantCulture);
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
                }
            }
            catch (Exception ex)
            {
                lastError = "send-an " + GetExceptionName(ex);
                Debug.LogWarning("Temporal Panic Button: KrokMP relay fallback failed: " + ex);
            }
        }

        private static void OnServerRequestBoxed(uint clientId, object reader)
        {
            receivedRequests++;
            string payload = ReadString(reader);
            if (string.IsNullOrEmpty(payload))
            {
                lastError = "empty-rq";
                return;
            }

            lastError = null;
            // The host is authoritative for caster id. Clients may send 0 before their local
            // player id is fully known, so replace it with the sender id observed by KrokMP.
            payload = KrokMpPayload.ReplaceCaster(payload, clientId);
            TimeStopController.ReceiveMultiplayerTimeStop(payload);
            RelayAnnouncement(payload);
        }

        private static void OnClientAnnouncementBoxed(uint clientId, object reader)
        {
            receivedAnnouncements++;
            string payload = ReadString(reader);
            if (!string.IsNullOrEmpty(payload))
            {
                lastError = null;
                TimeStopController.ReceiveMultiplayerTimeStop(payload);
            }
            else
            {
                lastError = "empty-an";
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

        private static object CreatePayloadWriter(ushort messageId, string payload)
        {
            object writer = netCreateWriterMethod.Invoke(null, new object[] { messageId });
            object[] putArgs = { writer, payload, true };
            writerPutKrokStringMethod.Invoke(null, putArgs);
            return writer;
        }

        private static object CreateReliableDelivery()
        {
            // LiteNetLib.DeliveryMethod.ReliableOrdered is enum value 0 in the KrokMP build used here.
            return Enum.ToObject(deliveryMethodType, 0);
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
            if (localPlayerField == null || clientIdBackingField == null)
                return 0;

            try
            {
                object localPlayer = localPlayerField.GetValue(null);
                if (localPlayer == null)
                    return 0;

                return (uint)clientIdBackingField.GetValue(localPlayer);
            }
            catch
            {
                return 0;
            }
        }
    }

    /// <summary>
    /// Parsed KrokMP time-stop announcement used by TimeStopController.
    /// </summary>
    internal readonly struct MultiplayerTimeStopEvent
    {
        public readonly uint CasterClientId;
        public readonly float Duration;
        public readonly Vector2 Origin;
        public readonly HazardKind Hazard;
        public readonly uint StopId;

        public MultiplayerTimeStopEvent(uint casterClientId, float duration, Vector2 origin, HazardKind hazard, uint stopId)
        {
            CasterClientId = casterClientId;
            Duration = duration;
            Origin = origin;
            Hazard = hazard;
            StopId = stopId;
        }
    }
}
