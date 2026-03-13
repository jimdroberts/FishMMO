using System;
using UnityEngine;
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
		/// The amount to increment by.
		/// </summary>
		[Tooltip("The amount to increment the achievement by.")]
		[Min(1)]
		public uint Amount = 1;

		/// <inheritdoc />
		public override void Execute(ICharacter initiator, EventData eventData)
		{
#if UNITY_SERVER
			if (AchievementTemplate == null || initiator == null)
			{
				return;
			}

			if (!initiator.TryGet(out IAchievementController achievementController))
			{
				return;
			}

			achievementController.Increment(AchievementTemplate, Amount);
#endif
		}
	}
}