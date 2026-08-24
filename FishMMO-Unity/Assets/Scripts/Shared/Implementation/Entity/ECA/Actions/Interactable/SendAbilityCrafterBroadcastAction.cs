using System;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that opens the ability crafter UI for the interacting player.
	/// Broadcasts <see cref="AbilityCrafterBroadcast"/> to the owner connection.
	/// Server-only.
	/// </summary>
	[Serializable]
	public class SendAbilityCrafterBroadcastAction : BaseAction
	{
		/// <summary>
		/// Sends the ability-crafter-open broadcast.
		/// </summary>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			// Server-only. Runtime check, not #if UNITY_SERVER: that define is absent in the
			// editor, where the scene server also runs — see BaseAction.IsServer.
			if (!IsServer(initiator))
			{
				return;
			}

			if (initiator is not IPlayerCharacter player) return;
			if (!eventData.TryGet(out PlayerInteractionEventData data) || data.Interactable == null) return;

			initiator.NetworkObject.Broadcast(new AbilityCrafterBroadcast() { InteractableID = data.Interactable.ID });

			IAbilityCrafter abilityCrafter = data.Interactable as IAbilityCrafter;
			if (abilityCrafter?.AchievementTemplate != null &&
				player.TryGet(out IAchievementController achievementController))
			{
				achievementController.Increment(abilityCrafter.AchievementTemplate, 1);
			}
		}
	}
}