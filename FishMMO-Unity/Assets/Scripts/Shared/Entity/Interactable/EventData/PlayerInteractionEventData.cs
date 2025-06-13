using UnityEngine;

namespace FishMMO.Shared
{
	public class PlayerInteractionEventData : EventData
	{
		public GameObject Target { get; } // What was interacted with (e.g., the NPC GameObject)
		public string InteractionType;

		public PlayerInteractionEventData(IPlayerCharacter initiator, GameObject target, string interactionType) : base(initiator)
		{
			Target = target;
			InteractionType = interactionType;
		}

		public override string ToString() => $"PlayerInteractionEventData (Initiator: {Initiator?.Name}, Target: {Target?.name}, InteractionType: {InteractionType})";
	}
}