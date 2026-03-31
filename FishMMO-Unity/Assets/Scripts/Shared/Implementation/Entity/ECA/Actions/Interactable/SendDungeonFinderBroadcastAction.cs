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
		public override void Execute(ICharacter initiator, EventData eventData)
		{
#if UNITY_SERVER
			if (!eventData.TryGet(out PlayerInteractionEventData data) || data.Interactable == null) return;

			initiator.NetworkObject.Broadcast(new DungeonFinderBroadcast()
			{
				InteractableID = data.Interactable.ID,
			});
#endif
		}
	}
}