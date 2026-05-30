using System;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that applies a specified buff to a target character, potentially stacking it multiple times.
	/// </summary>
	[Serializable]
	public class ApplyBuffAction : BaseAction
	{
		/// <summary>
		/// The value provider that determines the number of stacks to apply.
		/// </summary>
		[Tooltip("The value provider that determines the number of buff stacks to apply.")]
		[SerializeReference, SubclassSelector]
		public IIntValueProvider StacksValue;

		/// <summary>
		/// The buff template to apply to the target.
		/// </summary>
		public BaseBuffTemplate BuffTemplate;

		/// <summary>
		/// Applies the specified buff to the target character, stacking it the computed number of times.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">The event data containing the target information.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (StacksValue == null)
			{
				Log.Warning("ApplyBuffAction", "StacksValue provider is null.");
				return;
			}

			if (!TryResolveTargetOrInitiator(initiator, eventData, out ICharacter target))
			{
				return;
			}

			if (!target.TryGet(out IBuffController buffController))
			{
				return;
			}

			int stacks = StacksValue.GetValue(initiator, eventData);

			// Only same-character replicate-domain TickEventData can go through Apply
			// directly. A caster's replicate tick is not necessarily in the target's
			// controller domain, so cross-character effects must route through the
			// target controller's authoritative mapper.
			TickEventData tickData = null;
			bool hasTickData = eventData != null && eventData.TryGet(out tickData);
			bool isPredictionPath = hasTickData && tickData.IsReplicateTick && tickData.IsForCharacter(target);
			uint authoritativeTick = hasTickData && !tickData.IsReplicateTick ? (uint)tickData.Tick : target.GetLocalTick();

			for (int i = 0; i < stacks; ++i)
			{
				if (isPredictionPath)
				{
					buffController.Apply(BuffTemplate, tickData.Tick);
				}
				else
				{
					buffController.ApplyAuthoritative(BuffTemplate, authoritativeTick);
				}
			}
		}
	}
}