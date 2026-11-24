using System;
using System.Collections.Generic;

namespace FishNet.Managing.Object
{
    /// <summary>
    /// FISHMMO EDIT: what the server's spawn messages cost, by part: header, behaviour payloads (and each behaviour type's
    /// share), RPC link table, SyncTypes. A diagnostic, off unless Enabled; local only, not proposed upstream.
    /// </summary>
    public static class SpawnWireStats
    {
        /// <summary>True while counting.</summary>
        public static bool Enabled;

        public static long Count, HeaderBytes, PayloadBytes, RpcLinkBytes, SyncTypeBytes;

        /// <summary>Payload bytes by behaviour type since the last Reset.</summary>
        public static readonly Dictionary<Type, long> PayloadBytesByBehaviour = new();

        internal static void Add(int header, int payload, int rpcLinks, int syncTypes)
        {
            Count++;
            HeaderBytes += header;
            PayloadBytes += payload;
            RpcLinkBytes += rpcLinks;
            SyncTypeBytes += syncTypes;
        }

        internal static void AddPayload(Type behaviour, int bytes)
        {
            PayloadBytesByBehaviour.TryGetValue(behaviour, out long total);
            PayloadBytesByBehaviour[behaviour] = total + bytes;
        }

        /// <summary>Clears every counter.</summary>
        public static void Reset()
        {
            Count = HeaderBytes = PayloadBytes = RpcLinkBytes = SyncTypeBytes = 0;
            PayloadBytesByBehaviour.Clear();
        }
    }
}
