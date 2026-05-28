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

			if (target.TryGet(out IBuffController buffController))
			{
				int stacks = StacksValue.GetValue(initiator, eventData);
				for (int i = 0; i < stacks; ++i)
				{
					// If the triggering EventData carries a deterministic tick (prediction path), use it.
					if (eventData != null && eventData.TryGet(out TickEventData tickData))
					{
						buffController.Apply(BuffTemplate, tickData.Tick);
					}
					else
					{
						uint tick = target.GetLocalTick();
						buffController.Apply(BuffTemplate, tick);
					}
				}
			}
		}
	}
}