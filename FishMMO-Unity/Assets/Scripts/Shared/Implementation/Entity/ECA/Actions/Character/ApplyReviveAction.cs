using System;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that resurrects a dead target character using a configurable value provider.
	/// Unlike <see cref="ApplyHealAction"/>, this works on dead characters (CurrentValue == 0).
	/// Used by resurrect/resurrection ability templates.
	/// </summary>
	[Serializable]
	public class ApplyReviveAction : BaseAction
	{
		/// <summary>
		/// The value provider that determines the amount of health to restore on resurrect.
		/// </summary>
		[Tooltip("The value provider that determines the amount of health to restore on resurrect.")]
		[SerializeReference, SubclassSelector]
		public IIntValueProvider ReviveValue;

		/// <summary>
		/// Resurrects the target character using the computed value.
		/// </summary>
		/// <param name="initiator">The character casting the resurrect.</param>
		/// <param name="eventData">The event data containing the target information.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (ReviveValue == null)
			{
				Log.Warning("ApplyReviveAction", "ReviveValue provider is null.");
				return;
			}

			if (!TryResolveTargetOrInitiator(initiator, eventData, out ICharacter target))
			{
				return;
			}

			if (target.TryGet(out ICharacterDamageController defenderDamageController))
			{
				int amount = ReviveValue.GetValue(initiator, eventData);
				defenderDamageController.Revive(initiator, amount);

				// Send resurrect offer broadcast to the dead player so their
				// death dialog shows the "Accept Resurrect" button.
				if (target is IPlayerCharacter playerCharacter &&
					playerCharacter.Owner != null &&
					playerCharacter.Owner.IsValid)
				{
					playerCharacter.NetworkObject.NetworkManager.ServerManager.Broadcast(
						playerCharacter.Owner,
						new ResurrectOfferBroadcast { ResurrectorID = initiator.ID },
						true,
						FishNet.Transporting.Channel.Reliable);
				}
			}
		}
	}
}
