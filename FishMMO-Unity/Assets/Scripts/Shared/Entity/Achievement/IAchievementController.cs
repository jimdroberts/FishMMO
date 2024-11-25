using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public interface IAchievementController : ICharacterBehaviour
	{
		static Action<ICharacter, AchievementTemplate, AchievementTier> OnCompleteAchievement;
		static Action<Achievement> OnUpdateAchievement;

		Dictionary<int, Achievement> Achievements { get; }
		void SetAchievement(int templateID, byte tier, uint value);
		bool TryGetAchievement(int templateID, out Achievement achievement);
		void Increment(AchievementTemplate template, uint amount);
	}
}