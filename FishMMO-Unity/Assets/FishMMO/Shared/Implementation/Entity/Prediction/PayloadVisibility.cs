using FishNet.Connection;
using FishNet.Object;

namespace FishMMO.Shared
{
	/// <summary>
	/// Answers "is this spawn payload being written for the character's owner?" for the
	/// controllers that filter what non-owners receive.
	/// </summary>
	/// <remarks>
	/// FishNet builds the spawn message per receiving connection (<c>ServerObjects.Observers</c>
	/// calls <c>WriteSpawn(nob, writer, conn)</c> inside the per-connection rebuild), so a
	/// behaviour's <c>WritePayload</c> may legitimately vary by receiver. <c>conn.IsValid</c> is
	/// tested first because an unowned NPC's <c>Owner</c> is FishNet's shared EmptyConnection and
	/// a predicted-spawn write passes that same instance as the receiver — without the validity
	/// test those two compare equal and hand the owner-only set to a path that is not the owner.
	/// A null NetworkObject (an unspawned controller, which only tests construct) answers false,
	/// the filtered direction.
	/// </remarks>
	public static class PayloadVisibility
	{
		/// <summary>True when <paramref name="conn"/> owns <paramref name="behaviour"/>'s object.</summary>
		public static bool IsOwner(NetworkBehaviour behaviour, NetworkConnection conn)
		{
			if (behaviour == null || conn == null || !conn.IsValid)
			{
				return false;
			}
			NetworkObject nob = behaviour.NetworkObject;
			return nob != null && nob.Owner == conn;
		}
	}
}
