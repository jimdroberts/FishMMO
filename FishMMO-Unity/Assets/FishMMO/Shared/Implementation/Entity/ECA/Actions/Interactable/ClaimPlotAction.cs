using System;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that asks the server to claim the plot the player is interacting with.
	/// Server-only.
	/// </summary>
	/// <remarks>
	/// Deliberately thin. Claiming needs the database — read the row, take the money, take the plot —
	/// and none of that can happen inside a synchronous ECA action, nor can shared code reach the
	/// server behaviour that owns the connection. So this raises a request and returns.
	///
	/// <para>Nothing is validated here, not even affordability. Every check that matters has to be
	/// made where the claim is actually written, or it is a check made against state that may have
	/// changed by the time it is used: another player on another scene server can take the plot in
	/// the gap. Testing anything here would only produce a friendlier lie.</para>
	/// </remarks>
	[Serializable]
	public class ClaimPlotAction : BaseAction
	{
		/// <summary>
		/// Raises a claim request for the interacted foundation.
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

			if (data.Interactable is not IPlotFoundation foundation)
			{
				return;
			}

			PlotFoundation.Registry.RequestClaim(player, foundation);
		}
	}
}
