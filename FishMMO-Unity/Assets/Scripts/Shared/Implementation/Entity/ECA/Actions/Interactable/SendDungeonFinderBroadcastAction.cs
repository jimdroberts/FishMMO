using System;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that broadcasts a <see cref="DungeonFinderBroadcast"/> to the player,
	/// opening the dungeon finder interface on the client. Server-only.
	/// </summary>
	[Serializable]
	public class SendDungeonFinderBroadcastAction : BaseAction
	{
		/// <summary>
		/// Sends the dungeon finder broadcast to the player, opening the dungeon finder interface on the client.
		/// Server-only.
		/// </summary>
		/// <param name="initiator">The character opening the dungeon finder.</param>
		/// <param name="eventData">The event data containing the interaction context.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			// Server-only. Runtime check, not #if UNITY_SERVER: that define is absent in the
			// editor, where the scene server also runs — see BaseAction.IsServer.
			if (!IsServer(initiator))
			{
				return;
			}

			if (!eventData.TryGet(out PlayerInteractionEventData data) || data.Interactable == null) return;

			initiator.NetworkObject.Broadcast(new DungeonFinderBroadcast()
			{
				InteractableID = data.Interactable.ID,
			});
		}
	}
}