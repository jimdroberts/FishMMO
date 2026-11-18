using System.Collections.Generic;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;

namespace FishMMO.Shared
{
	/// <summary>
	/// Sends a broadcast to everyone observing a character except its owner.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Every observer-facing message in this project has the same shape: the owner already knows,
	/// because it either predicted the change or received a reliable acknowledgement addressed to it,
	/// so sending it the observer copy costs bytes and is then discarded on arrival.
	/// </para>
	/// <para>
	/// <b>Why this exists rather than a direct <c>BroadcastExcept</c> call.</b>
	/// <c>ServerManager.BroadcastExcept(HashSet, NetworkConnection, ...)</c> calls
	/// <c>connections.Remove(excludedConnection)</c> on the set it is handed. Passing
	/// <c>NetworkObject.Observers</c> to it therefore does not exclude the owner for one message — it
	/// permanently removes the owner from that object's observer set, and the owner stops receiving
	/// every later observer message for its own character. The copy below is not an optimisation to
	/// be removed; it is the thing that makes the call safe.
	/// </para>
	/// <para>
	/// The scratch set is static and reused, which is safe because FishNet's server work is
	/// single-threaded and the set is fully consumed inside the call.
	/// </para>
	/// </remarks>
	public static class ObserverBroadcastScope
	{
		/// <summary>Scratch recipient set. See the type remarks for why the copy is mandatory.</summary>
		private static readonly HashSet<NetworkConnection> recipients = new HashSet<NetworkConnection>();

		/// <summary>
		/// Copies <paramref name="observers"/> into <paramref name="into"/>, dropping
		/// <paramref name="excluded"/> and any null entries.
		/// </summary>
		/// <remarks>
		/// Exposed for tests, which assert the owner is absent and that the source set is untouched.
		/// </remarks>
		/// <returns>The number of recipients collected.</returns>
		public static int CollectRecipients(IEnumerable<NetworkConnection> observers, NetworkConnection excluded, HashSet<NetworkConnection> into)
		{
			into.Clear();
			if (observers == null)
			{
				return 0;
			}
			foreach (NetworkConnection conn in observers)
			{
				if (conn == null || ReferenceEquals(conn, excluded))
				{
					continue;
				}
				into.Add(conn);
			}
			return into.Count;
		}

		/// <summary>
		/// Broadcasts <paramref name="message"/> to <paramref name="networkObject"/>'s observers,
		/// excluding its owner.
		/// </summary>
		/// <remarks>
		/// Silently does nothing when the object is not spawned or the server is not running: a
		/// caller that fires during startup or teardown has no observers to reach anyway, and the
		/// spawn payload is what carries state to anyone who arrives later.
		/// </remarks>
		/// <returns>True when the message was handed to at least one connection.</returns>
		public static bool BroadcastToObserversExceptOwner<T>(NetworkObject networkObject, T message, Channel channel = Channel.Reliable) where T : struct, IBroadcast
		{
			if (networkObject == null || !networkObject.IsSpawned)
			{
				return false;
			}

			NetworkManager nm = networkObject.NetworkManager;
			if (nm == null || !nm.IsServerStarted)
			{
				return false;
			}

			if (CollectRecipients(networkObject.Observers, networkObject.Owner, recipients) < 1)
			{
				return false;
			}

			nm.ServerManager.Broadcast(recipients, message, true, channel);
			recipients.Clear();
			return true;
		}
	}
}
