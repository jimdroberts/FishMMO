using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;

namespace FishNet.Observing
{
    /* FISHMMO EDIT: per-observer send filtering for unreliable ObserversRpc traffic.
     *
     * FishNet decides WHO can see an object (observer conditions) but not HOW OFTEN each observer
     * hears from it: every ObserversRpc — NetworkTransform updates included — goes to every
     * observer every time, and NetworkTransform's interval is a single value per object,
     * changed through a buffered RPC to all observers. A per-connection level of detail
     * therefore needs a hook here. The filter is consulted only for UNRELIABLE, NON-BUFFERED
     * RPCs: a reliable send (a NetworkTransform settle, a teleport) and a buffered one (an
     * interval change) must reach everyone, and skipping an unreliable update is
     * indistinguishable to the receiver from ordinary packet loss, which every such sender
     * already tolerates. */

    /// <summary>
    /// Decides, per observer and per send, whether an unreliable ObserversRpc from a
    /// NetworkObject should reach that observer. Assign to <see cref="NetworkObject.ObserverSendFilter"/>.
    /// </summary>
    public interface IObserverSendFilter
    {
        /// <summary>
        /// Returns true to send this RPC to <paramref name="connection"/>, false to skip it.
        /// </summary>
        /// <param name="networkObject">Object sending the RPC.</param>
        /// <param name="connection">Observer the RPC would be sent to.</param>
        /// <param name="channel">Channel the RPC is being sent on. Only Unreliable is ever filtered.</param>
        bool ShouldSend(NetworkObject networkObject, NetworkConnection connection, Channel channel);
    }
}
