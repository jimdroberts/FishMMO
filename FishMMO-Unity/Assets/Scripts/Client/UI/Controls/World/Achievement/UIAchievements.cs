using UnityEngine;
using FishMMO.Shared;
using System.Collections.Generic;

namespace FishMMO.Client
{
	public class UIAchievements : UICharacterControl
	{
		public AchievementCategory CurrentCategory;

		public RectTransform AchievementCategoryParent;
		public UIAchievementCategory AchievementCategoryButtonPrefab;

        public RectTransform AchievementDescriptionParent;
		public UIAchievementDescription AchievementDescriptionPrefab;

		private List<UIAchievementCategory> CategoryButtons = new List<UIAchievementCategory>();
		private Dictionary<AchievementCategory, Dictionary<int, UIAchievementDescription>> Categories = new Dictionary<AchievementCategory, Dictionary<int, UIAchievementDescription>>();


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
				IAchievementController.OnUpdateAchievement += AchievementController_OnUpdateAchievement;
			}
		}

		public override void OnPreUnsetCharacter()
		{
			if (Character.TryGet(out IAchievementController achievementController))
			{
				IAchievementController.OnUpdateAchievement -= AchievementController_OnUpdateAchievement;
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
					AchievementController_OnUpdateAchievement(achievement);
				}
			}
		}

		public void AchievementController_OnUpdateAchievement(Achievement achievement)
		{
			if (achievement == null)
			{
				return;
			}

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
				categoryButton.gameObject.SetActive(true);
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
				description.gameObject.SetActive(true);
				achievements.Add(achievement.Template.ID, description);
			}

			if (description.Progress != null)
			{
				description.Progress.value = achievement.CurrentMaxValue / achievement.CurrentValue;
			}
			if (description.Value != null)
			{
				string maxValue = (achievement.CurrentMaxValue > 1) ? $"/ {achievement.CurrentMaxValue}" : "";
				description.Value.text = $"{achievement.CurrentValue} {maxValue}";
			}
		}

		public void ClearAll()
		{
			if (Categories == null)
			{
				return;
			}
			foreach (KeyValuePair<AchievementCategory, Dictionary<int, UIAchievementDescription>> pair in Categories)
			{
				foreach (UIAchievementDescription description in pair.Value.Values)
				{
					Destroy(description.gameObject);
				}
			}
			if (CategoryButtons == null)
			{
				return;
			}
			foreach (UIAchievementCategory category in CategoryButtons)
			{
				if (category == null)
				{
					continue;
				}
				if (category.Button != null)
				{
					category.Button.onClick.RemoveAllListeners();
				}
				Destroy(category.gameObject);
			}
			Categories.Clear();
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