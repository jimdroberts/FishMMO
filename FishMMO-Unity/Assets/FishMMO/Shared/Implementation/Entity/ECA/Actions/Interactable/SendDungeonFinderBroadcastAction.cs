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

			/* The template ID rides along with the open.
			 *
			 * Without it the panel opens knowing only which scene object it belongs to, and would
			 * have to ask the server what dungeon that is before it could draw its own header or
			 * its difficulty tabs — a round trip in front of a window the player has already
			 * opened. The entrance knows; sending it costs an int. */
			int templateID = 0;
			if (data.Interactable is IDungeonEntrance dungeonEntrance)
			{
				templateID = dungeonEntrance.DungeonTemplateID;
			}

			SendToOwner(initiator, new DungeonFinderBroadcast()
			{
				InteractableID = data.Interactable.ID,
				DungeonTemplateID = templateID,
			});
		}
	}
}