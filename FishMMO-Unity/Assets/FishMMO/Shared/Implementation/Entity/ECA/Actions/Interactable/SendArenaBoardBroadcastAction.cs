using System;
using System.Collections.Generic;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that opens the arena board panel for the interacting player.
	/// </summary>
	/// <remarks>
	/// The arena counterpart of <see cref="SendDungeonFinderBroadcastAction"/>: sends the board's
	/// identity and the template IDs of the arenas it offers, and nothing else. The panel resolves
	/// the rest from the template cache.
	/// </remarks>
	[Serializable]
	public class SendArenaBoardBroadcastAction : BaseAction
	{
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			// Server-only. Runtime check, not #if UNITY_SERVER: that define is absent in the
			// editor, where the scene server also runs — see BaseAction.IsServer.
			if (!IsServer(initiator))
			{
				return;
			}

			if (!eventData.TryGet(out PlayerInteractionEventData data) || data.Interactable == null) return;

			int[] templateIDs = Array.Empty<int>();
			if (data.Interactable is IArenaBoard board)
			{
				IReadOnlyList<int> ids = board.ArenaTemplateIDs;
				templateIDs = new int[ids.Count];
				for (int i = 0; i < ids.Count; ++i)
				{
					templateIDs[i] = ids[i];
				}
			}

			SendToOwner(initiator, new ArenaBoardBroadcast()
			{
				InteractableID = data.Interactable.ID,
				ArenaTemplateIDs = templateIDs,
			});
		}
	}
}
