using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that applies a buff to a character in a region.
	/// Suppressed during prediction reconciliation.
	/// </summary>
	[Serializable]
	public class ApplyRegionBuffAction : BaseAction
	{
		/// <summary>
		/// The buff template to apply.
		/// </summary>
		[Tooltip("The buff template to apply to the character.")]
		public BaseBuffTemplate Buff;

		/// <inheritdoc />
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (initiator == null || Buff == null)
			{
				return;
			}

			if (eventData != null &&
				eventData.TryGet(out RegionEventData regionData) &&
				regionData.IsReconciling)
			{
				return;
			}

			if (!initiator.TryGet(out IBuffController buffController))
			{
				return;
			}

			// Use region event's reconciling info: prefer TickEventData if present, otherwise use the character's local tick.
			if (eventData != null && eventData.TryGet(out TickEventData tickData))
			{
				buffController.Apply(Buff, tickData.Tick);
			}
			else
			{
				uint tick = initiator.GetLocalTick();
				buffController.Apply(Buff, tick);
			}
		}
	}
}