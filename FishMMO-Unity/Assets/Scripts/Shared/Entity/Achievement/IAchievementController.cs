using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	public interface IAchievementController : ICharacterBehaviour
	{
		Dictionary<int, Achievement> Achievements { get; }
		void SetAchievement(int templateID, byte tier, uint value);
		bool TryGetAchievement(int templateID, out Achievement achievement);
		void Increment(AchievementTemplate template, uint amount);

#if !UNITY_SERVER
		event Func<string, Vector3, Color, float, float, bool, IReference> OnCompleteAchievement;
#endif

#if UNITY_SERVER
		void HandleRewards(AchievementTier tier);
#endif
	}
}