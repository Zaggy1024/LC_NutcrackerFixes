using System;
using System.Reflection;

using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace NutcrackerFixes.Patches
{
    public static class Reflection
    {
        public static readonly MethodInfo m_Debug_Log = typeof(Debug).GetMethod(nameof(Debug.Log), new Type[] { typeof(object) });

        public static readonly MethodInfo m_NetworkBehaviour_get_IsOwner = typeof(NetworkBehaviour).GetMethod("get_IsOwner", new Type[0]);

        public static readonly MethodInfo m_NetworkBehaviour_beginSendServerRpc = AccessTools.Method(typeof(NetworkBehaviour), "__beginSendServerRpc", new Type[] { typeof(uint), typeof(ServerRpcParams), typeof(RpcDelivery) });
        public static readonly MethodInfo m_NetworkBehaviour_endSendServerRpc = AccessTools.Method(typeof(NetworkBehaviour), "__endSendServerRpc", new Type[] { typeof(FastBufferWriter).MakeByRefType(), typeof(uint), typeof(ServerRpcParams), typeof(RpcDelivery) });

        public static readonly MethodInfo m_NetworkBehaviour_beginSendClientRpc = AccessTools.Method(typeof(NetworkBehaviour), "__beginSendClientRpc", new Type[] { typeof(uint), typeof(ClientRpcParams), typeof(RpcDelivery) });
        public static readonly MethodInfo m_NetworkBehaviour_endSendClientRpc = AccessTools.Method(typeof(NetworkBehaviour), "__endSendClientRpc", new Type[] { typeof(FastBufferWriter).MakeByRefType(), typeof(uint), typeof(ClientRpcParams), typeof(RpcDelivery) });

        public static FastBufferWriter BeginSendServerRPC(NetworkBehaviour behaviour, uint rpcID, ServerRpcParams rpcParams, RpcDelivery delivery)
        {
            return (FastBufferWriter)m_NetworkBehaviour_beginSendServerRpc.Invoke(behaviour, new object[] { rpcID, rpcParams, delivery });
        }

        public static void EndSendServerRPC(NetworkBehaviour behaviour, ref FastBufferWriter buffer, uint rpcID, ServerRpcParams rpcParams, RpcDelivery delivery)
        {
            var arguments = new object[] { buffer, rpcID, rpcParams, delivery };
            m_NetworkBehaviour_endSendServerRpc.Invoke(behaviour, arguments);
            buffer = (FastBufferWriter)arguments[0];
        }

        public static FastBufferWriter BeginSendClientRPC(NetworkBehaviour behaviour, uint rpcID, ClientRpcParams rpcParams, RpcDelivery delivery)
        {
            return (FastBufferWriter)m_NetworkBehaviour_beginSendClientRpc.Invoke(behaviour, new object[] { rpcID, rpcParams, delivery });
        }

        public static void EndSendClientRPC(NetworkBehaviour behaviour, ref FastBufferWriter buffer, uint rpcID, ClientRpcParams rpcParams, RpcDelivery delivery)
        {
            var arguments = new object[] { buffer, rpcID, rpcParams, delivery };
            m_NetworkBehaviour_endSendClientRpc.Invoke(behaviour, arguments);
            buffer = (FastBufferWriter)arguments[0];
        }
    }
}
