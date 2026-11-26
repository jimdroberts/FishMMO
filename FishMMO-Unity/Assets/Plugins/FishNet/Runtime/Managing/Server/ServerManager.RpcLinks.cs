using FishNet.Object;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishNet.Managing.Server
{
    public sealed partial class ServerManager : MonoBehaviour
    {
        #region Internal
        /// <summary>
        /// Current RPCLinks.
        /// </summary>
        internal Dictionary<ushort, RpcLink> RpcLinks = new();
        /// <summary>
        /// RPCLink indexes which can be used.
        /// </summary>
        private Queue<ushort> _availableRpcLinkIndexes = new();
        /// <summary>
        /// FISHMMO EDIT: the behaviour holding each link index this server gave out, and the RPC type it stands for, so a
        /// diagnostic naming outbound messages can resolve a linked RPC in one lookup instead of reading every spawned
        /// behaviour's links by reflection (which took 16-48 ms a rebuild on a busy scene server).
        /// </summary>
        private Dictionary<ushort, (NetworkBehaviour Owner, PacketId RpcPacketId)> _rpcLinkOwners = new();
        #endregion

        /// <summary>
        /// FISHMMO EDIT: the behaviour and RPC type a link index this server gave out belongs to; false when it is not given out.
        /// </summary>
        public bool TryGetRpcLinkOwner(ushort linkIndex, out NetworkBehaviour owner, out PacketId rpcPacketId)
        {
            if (_rpcLinkOwners.TryGetValue(linkIndex, out (NetworkBehaviour Owner, PacketId RpcPacketId) entry))
            {
                owner = entry.Owner;
                rpcPacketId = entry.RpcPacketId;
                return true;
            }
            owner = null;
            rpcPacketId = default;
            return false;
        }

        /// <summary>FISHMMO EDIT: records who holds a link index just given out (see <see cref="TryGetRpcLinkOwner"/>).</summary>
        internal void NoteRpcLinkOwner(ushort linkIndex, NetworkBehaviour owner, PacketId rpcPacketId)
        {
            _rpcLinkOwners[linkIndex] = (owner, rpcPacketId);
        }

        /// <summary>
        /// Initializes RPC Links for NetworkBehaviours.
        /// </summary>
        private void InitializeRpcLinks()
        {
            ushort startingLink = NetworkManager.StartingRpcLinkIndex;
            for (ushort i = ushort.MaxValue; i >= startingLink; i--)
                _availableRpcLinkIndexes.Enqueue(i);
        }

        /// <summary>
        /// Sets the next RPC Link to use.
        /// </summary>
        /// <returns>True if a link was available and set.</returns>
        internal bool GetRpcLink(out ushort value)
        {
            if (_availableRpcLinkIndexes.Count > 0)
            {
                value = _availableRpcLinkIndexes.Dequeue();
                return true;
            }
            else
            {
                value = 0;
                return false;
            }
        }

        /// <summary>
        /// Sets data to RpcLinks for linkIndex.
        /// </summary>
        internal void SetRpcLink(ushort linkIndex, RpcLink data)
        {
            RpcLinks[linkIndex] = data;
        }

        /// <summary>
        /// Returns RPCLinks to availableRpcLinkIndexes.
        /// </summary>
        internal void StoreRpcLinks(Dictionary<uint, RpcLinkType> links)
        {
            foreach (RpcLinkType rlt in links.Values)
            {
                // FISHMMO EDIT: a returned link has no owner until it is given out again.
                _rpcLinkOwners.Remove(rlt.LinkPacketId);
                _availableRpcLinkIndexes.Enqueue(rlt.LinkPacketId);
            }
        }
    }
}