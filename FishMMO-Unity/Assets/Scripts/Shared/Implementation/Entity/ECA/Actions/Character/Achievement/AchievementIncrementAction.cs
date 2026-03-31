using System;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that increments achievement progress for a character.
	/// Server-only execution.
	/// </summary>
	[Serializable]
	public class AchievementIncrementAction : BaseAction
	{
		/// <summary>
		/// The achievement template to increment.
		/// </summary>
		[Tooltip("The achievement template to increment.")]
		public AchievementTemplate AchievementTemplate;

		/// <summary>
		/// The value provider that determines the amount to increment by.
		/// </summary>
		[Tooltip("The value provider that determines the amount to increment the achievement by.")]
		[SerializeReference, SubclassSelector]
		public IIntValueProvider AmountValue;

		/// <inheritdoc />
		public override void Execute(ICharacter initiator, EventData eventData)
		{
#if UNITY_SERVER
			if (AchievementTemplate == null || initiator == null)
			{
				return;
			}

			if (AmountValue == null)
			{
				Log.Warning("AchievementIncrementAction", "AmountValue provider is null.");
				return;
			}

			if (!initiator.TryGet(out IAchievementController achievementController))
			{
				return;
			}

			int value = AmountValue.GetValue(initiator, eventData);
			if (value < 1)
			{
				return;
			}
			achievementController.Increment(AchievementTemplate, (uint)value);
#endif
		}
	}
}