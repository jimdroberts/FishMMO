using FishMMO.Shared.Core;
using FishNet.Connection;
using FishNet.Object;

namespace FishMMO.Shared
{
	/// <summary>
	/// A companion (a pet) is always visible to its owner: no distance, budget or crowd takes it off the owner's client.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A player's pet is always visible to them: flying away with /gm fly carried the owner out of
	/// the pet's DistanceCondition range, and the budget's own-pet pin is bounded by the engagement ceiling, so the pet was
	/// despawned on the owner's client. The owner commands it and reads it on the pet panel wherever it is.
	/// </para>
	/// <para>
	/// Asked by <see cref="ClassifiedDistanceCondition"/> and <see cref="ObserverBudgetCondition"/> before their own rule.
	/// A condition is instantiated per object, so each caches its object's <see cref="NPC"/> once; the owner and its
	/// connection are read live (a pet changes hands only by being re-summoned, but an NPC object is pooled).
	/// </para>
	/// </remarks>
	public struct CompanionVisibility
	{
		private NPC npc;
		private bool resolved;

		/// <summary>Whether <paramref name="connection"/> owns the character whose companion <paramref name="networkObject"/> is.</summary>
		public bool OwnerSees(NetworkObject networkObject, NetworkConnection connection)
		{
			if (networkObject == null || connection == null)
			{
				return false;
			}
			if (!resolved)
			{
				npc = networkObject.GetComponent<NPC>();
				resolved = true;
			}
			return IsOwner(npc != null ? npc.CompanionOwner : null, connection);
		}

		/// <summary>The rule itself, as a pure function: the companion's owner is this connection's character.</summary>
		public static bool IsOwner(ICharacter companionOwner, NetworkConnection connection)
		{
			if (companionOwner == null || connection == null || companionOwner.GameObject == null)
			{
				return false;
			}
			NetworkConnection ownerConnection = companionOwner.Owner;
			return ownerConnection != null && ownerConnection.IsValid && ReferenceEquals(ownerConnection, connection);
		}
	}
}
