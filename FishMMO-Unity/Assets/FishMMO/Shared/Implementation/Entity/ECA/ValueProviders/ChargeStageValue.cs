using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// A value chosen by how far a staged charge was held (<see cref="AbilityTemplate.ChargeStages"/>): one entry per stage.
	/// </summary>
	/// <remarks>
	/// The stage is read from the ability object the event belongs to (<see cref="AbilityObject.ChargeStage"/>), or from the
	/// ability itself on a spawn chain (<see cref="Ability.ChargeStage"/>). Anything without a stage reads as stage 1, so the
	/// provider is safe on an ordinary ability too.
	/// </remarks>
	[Serializable]
	public sealed class ChargeStageValue : IIntValueProvider
	{
		/// <summary>The value at each stage, from stage 1.</summary>
		[Tooltip("The value at each charge stage, from stage 1. A stage past the end uses the last entry.")]
		public int[] PerStage = { 1 };

		/// <inheritdoc/>
		public int GetValue(ICharacter initiator, EventData eventData)
		{
			if (PerStage == null || PerStage.Length == 0)
			{
				return 0;
			}
			return PerStage[IndexOf(StageOf(eventData), PerStage.Length)];
		}

		/// <inheritdoc/>
		public string Describe()
		{
			return PerStage == null || PerStage.Length == 0 ? null : string.Join(" / ", PerStage) + " by charge";
		}

		/// <summary>The charge stage an event was cast at, from 1 (1 when it carries none).</summary>
		public static byte StageOf(EventData eventData)
		{
			if (AbilityObject.TryResolveFrom(eventData, out AbilityObject abilityObject) && abilityObject.ChargeStage > 0)
			{
				return abilityObject.ChargeStage;
			}
			if (eventData != null && eventData.TryGet(out AbilitySpawnEventData spawn) && spawn.Ability != null && spawn.Ability.ChargeStage > 0)
			{
				return spawn.Ability.ChargeStage;
			}
			return 1;
		}

		/// <summary>The array index for <paramref name="stage"/> in a table of <paramref name="length"/> entries.</summary>
		public static int IndexOf(byte stage, int length)
		{
			return Mathf.Clamp(stage - 1, 0, Mathf.Max(0, length - 1));
		}
	}
}
