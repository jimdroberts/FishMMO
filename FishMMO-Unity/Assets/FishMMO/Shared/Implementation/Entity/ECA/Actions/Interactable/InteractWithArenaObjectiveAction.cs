using System;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that hands an arena objective interaction to the server's match coordinator.
	/// </summary>
	/// <remarks>
	/// Raises <see cref="IArenaObjective.OnServerInteracted"/> and nothing else. Whether the
	/// interaction takes a flag, captures one, or advances a control point is decided by the match
	/// the player is in, on the server that hosts it.
	/// </remarks>
	[Serializable]
	public class InteractWithArenaObjectiveAction : BaseAction
	{
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (!IsServer(initiator))
			{
				return;
			}

			if (!eventData.TryGet(out PlayerInteractionEventData data) ||
				!(data.Interactable is IArenaObjective objective) ||
				!(initiator is IPlayerCharacter player))
			{
				return;
			}

			IArenaObjective.OnServerInteracted?.Invoke(player, objective);
		}
	}
}
