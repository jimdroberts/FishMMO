using System;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action on a plot's deed post: asks the server to describe the plot to the player who
	/// walked up to it. Server-only.
	/// </summary>
	/// <remarks>
	/// Replaces <see cref="ClaimPlotAction"/> on generated plots. Interacting used to buy the land
	/// outright, so pressing the interact key near a post spent money with no price shown and
	/// nothing to confirm. Now the server answers with what the plot is — price, owner, rent, and
	/// what this player may do — and the purchase is a separate request sent from the client's
	/// plot panel, carrying the price the panel showed.
	/// </remarks>
	[Serializable]
	public class OpenPlotPanelAction : BaseAction
	{
		/// <summary>
		/// Raises a panel request for the interacted foundation.
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
			if (data.Interactable is not IPlotFoundation foundation) return;

			PlotFoundation.Registry.RequestPanel(player, foundation);
		}
	}
}
