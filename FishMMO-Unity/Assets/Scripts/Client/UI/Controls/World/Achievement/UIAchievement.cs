using UnityEngine;
using FishMMO.Shared;
using System.Collections.Generic;
using LiteNetLib;

namespace FishMMO.Client
{
	public class UIAchievements : UICharacterControl
	{
		public AchievementCategory CurrentCategory;

		public RectTransform AchievementCategoryParent;
		public UIAchievementCategory AchievementCategoryButtonPrefab;

        public RectTransform AchievementDescriptionParent;
		public UIAchievementDescription AchievementDescriptionPrefab;

		private List<UIAchievementCategory> CategoryButtons;
		private Dictionary<AchievementCategory, Dictionary<int, UIAchievementDescription>> Categories;


		public override void OnStarting()
		{
			OnSetCharacter += CharacterControl_OnSetCharacter;
			IPlayerCharacter.OnStopLocalClient += (c) => ClearAll();
		}

		public override void OnDestroying()
		{
			IPlayerCharacter.OnStopLocalClient -= (c) => ClearAll();
			OnSetCharacter -= CharacterControl_OnSetCharacter;

			ClearAll();
		}

		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			if (Character.TryGet(out IAchievementController achievementController))
			{
				achievementController.OnAddAchievement += AchievementController_OnAddAchievement;
				achievementController.OnUpdateAchievement += AchievementController_OnUpdateAchievement;
			}
		}

		public override void OnPreUnsetCharacter()
		{
			if (Character.TryGet(out IAchievementController achievementController))
			{
				achievementController.OnAddAchievement -= AchievementController_OnAddAchievement;
				achievementController.OnUpdateAchievement -= AchievementController_OnUpdateAchievement;
			}
		}

		public override void OnQuitToLogin()
		{
			ClearAll();
		}

		private void CharacterControl_OnSetCharacter(IPlayerCharacter character)
		{
			if (character.TryGet(out IAchievementController achievementController))
			{
				if (achievementController.Achievements == null)
				{
					return;
				}
				foreach (Achievement achievement in achievementController.Achievements.Values)
				{
					AchievementController_OnAddAchievement(achievement);
				}
			}
		}

		public void AchievementController_OnAddAchievement(Achievement achievement)
		{
			if (achievement == null)
			{
				return;
			}

			InstantiateAchievement(achievement);
		}

		public void AchievementController_OnUpdateAchievement(Achievement achievement)
		{
			if (achievement == null)
			{
				return;
			}

			if (Categories.TryGetValue(achievement.Template.Category, out Dictionary<int, UIAchievementDescription> achievements) &&
				achievements.TryGetValue(achievement.Template.ID, out UIAchievementDescription description))
			{
				if (description.Progress != null &&
					achievement.Template.Tiers != null)
				{
					description.Progress.value = achievement.CurrentMaxValue / achievement.CurrentValue;
				}
			}
			else
			{
				InstantiateAchievement(achievement);
			}
		}

		private void InstantiateAchievement(Achievement achievement)
		{
			// Instantiate the Category Button
			if (!Categories.TryGetValue(achievement.Template.Category, out Dictionary<int, UIAchievementDescription> achievements))
			{
				Categories.Add(achievement.Template.Category, achievements = new Dictionary<int, UIAchievementDescription>());

				UIAchievementCategory categoryButton = Instantiate(AchievementCategoryButtonPrefab, AchievementCategoryParent);
				if (categoryButton.Label != null)
				{
					categoryButton.Label.text = achievement.Template.Category.ToString();
				}
				if (categoryButton.Button != null)
				{
					categoryButton.Button.onClick.AddListener(() => Category_OnClick(achievement.Template.Category));
				}
				CategoryButtons.Add(categoryButton);
			}

			// Instantiate the Achievement
			if (!achievements.TryGetValue(achievement.Template.ID, out UIAchievementDescription description))
			{
				description = Instantiate(AchievementDescriptionPrefab, AchievementDescriptionParent);
				if (description.Label != null)
				{
					description.Label.text = achievement.Template.Description;
				}
				if (description.Image != null)
				{
					description.Image.sprite = achievement.Template.Icon;
				}
				if (description.Progress != null &&
					achievement.Template.Tiers != null)
				{
					description.Progress.value = achievement.CurrentMaxValue / achievement.CurrentValue;
				}
				achievements.Add(achievement.Template.ID, description);
			}
		}

		public void ClearAll()
		{
			foreach (KeyValuePair<AchievementCategory, Dictionary<int, UIAchievementDescription>> pair in Categories)
			{
				foreach (UIAchievementDescription description in pair.Value.Values)
				{
					Destroy(description.gameObject);
				}
			}

			foreach (UIAchievementCategory category in CategoryButtons)
			{
				category.Button.onClick.RemoveAllListeners();
				Destroy(category.gameObject);
			}
		}

		public void Category_OnClick(AchievementCategory type)
		{
			CurrentCategory = type;
			foreach (KeyValuePair<AchievementCategory, Dictionary<int, UIAchievementDescription>> pair in Categories)
			{
				foreach (UIAchievementDescription description in pair.Value.Values)
				{
					description.gameObject.SetActive(pair.Key == CurrentCategory);
				}
			}
		}
	}
}