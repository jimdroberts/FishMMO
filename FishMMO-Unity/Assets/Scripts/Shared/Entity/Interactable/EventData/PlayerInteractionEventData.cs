using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Event data for player interactions, such as talking to NPCs or interacting with objects.
	/// Contains information about the initiator, target, and type of interaction.
	/// </summary>
	public class PlayerInteractionEventData : EventData
	{
		/// <summary>
		/// The GameObject that was interacted with (e.g., the NPC or object).
		/// </summary>
		public GameObject Target { get; }

		/// <summary>
		/// The type of interaction performed (e.g., "Talk", "Trade", "Attack").
		/// </summary>
		public string InteractionType;

		/// <summary>
		/// Constructs a new PlayerInteractionEventData with the initiator, target, and interaction type.
		/// </summary>
		/// <param name="initiator">The player character who initiated the interaction.</param>
		/// <param name="target">The GameObject that was interacted with.</param>
		/// <param name="interactionType">The type of interaction performed.</param>
		public PlayerInteractionEventData(IPlayerCharacter initiator, GameObject target, string interactionType) : base(initiator)
		{
			Target = target;
			InteractionType = interactionType;
		}

		/// <summary>
		/// Returns a string representation of the event data for debugging/logging.
		/// </summary>
		public override string ToString() => $"PlayerInteractionEventData (Initiator: {Initiator?.Name}, Target: {Target?.name}, InteractionType: {InteractionType})";
	}
}