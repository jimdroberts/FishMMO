using UnityEngine;
using FishMMO.Shared;
using System.Collections.Generic;

namespace FishMMO.Client
{
	public class UIAchievements : UICharacterControl
	{
		/// <summary>
		/// The currently selected achievement category.
		/// </summary>
		public AchievementCategory CurrentCategory;

		/// <summary>
		/// The parent RectTransform for achievement category buttons.
		/// </summary>
		public RectTransform AchievementCategoryParent;
		/// <summary>
		/// The prefab used to instantiate achievement category buttons.
		/// </summary>
		public UIAchievementCategory AchievementCategoryButtonPrefab;

		/// <summary>
		/// The parent RectTransform for achievement description UI elements.
		/// </summary>
		public RectTransform AchievementDescriptionParent;
		/// <summary>
		/// The prefab used to instantiate achievement description UI elements.
		/// </summary>
		public UIAchievementDescription AchievementDescriptionPrefab;

		/// <summary>
		/// List of all achievement category buttons currently displayed.
		/// </summary>
		private List<UIAchievementCategory> CategoryButtons = new List<UIAchievementCategory>();
		/// <summary>
		/// Dictionary mapping achievement categories to their achievement descriptions.
		/// </summary>
		private Dictionary<AchievementCategory, Dictionary<int, UIAchievementDescription>> Categories = new Dictionary<AchievementCategory, Dictionary<int, UIAchievementDescription>>();

		/// <summary>
		/// Called when the UI is starting. Subscribes to character and local client events.
		/// </summary>
		public override void OnStarting()
		{
			OnSetCharacter += CharacterControl_OnSetCharacter;
			IPlayerCharacter.OnStopLocalClient += (c) => ClearAll();
		}

		/// <summary>
		/// Called when the UI is being destroyed. Unsubscribes from events and clears all achievement UI.
		/// </summary>
		public override void OnDestroying()
		{
			IPlayerCharacter.OnStopLocalClient -= (c) => ClearAll();
			OnSetCharacter -= CharacterControl_OnSetCharacter;

			ClearAll();
		}

		/// <summary>
		/// Called after the character is set. Subscribes to achievement update events.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			if (Character.TryGet(out IAchievementController achievementController))
			{
				IAchievementController.OnUpdateAchievement += AchievementController_OnUpdateAchievement;
			}
		}

		/// <summary>
		/// Called before the character is unset. Unsubscribes from achievement update events.
		/// </summary>
		public override void OnPreUnsetCharacter()
		{
			if (Character.TryGet(out IAchievementController achievementController))
			{
				IAchievementController.OnUpdateAchievement -= AchievementController_OnUpdateAchievement;
			}
		}

		/// <summary>
		/// Called when quitting to login. Clears all achievement UI.
		/// </summary>
		public override void OnQuitToLogin()
		{
			ClearAll();
		}

		/// <summary>
		/// Event handler for when the character is set. Instantiates achievement UI for all achievements.
		/// </summary>
		/// <param name="character">The player character.</param>
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
					AchievementController_OnUpdateAchievement(character, achievement);
				}
			}
		}

		/// <summary>
		/// Event handler for when an achievement is updated. Instantiates or updates achievement UI elements.
		/// </summary>
		/// <param name="character">The character associated with the achievement.</param>
		/// <param name="achievement">The achievement to update.</param>
		public void AchievementController_OnUpdateAchievement(ICharacter character, Achievement achievement)
		{
			if (achievement == null)
			{
				return;
			}

			// Instantiate the Category Button
			// If the category does not exist, create a new category button and dictionary for achievements.
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
			// If the achievement does not exist in the category, create a new achievement description UI.
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

			// Update progress and value display for the achievement.
			uint nextTierValue = achievement.NextTierValue;

			if (description.Progress != null)
			{
				description.Progress.value = nextTierValue > 0 && achievement.CurrentValue > 0 ? nextTierValue / achievement.CurrentValue : 1.0f;
			}
			if (description.Value != null)
			{
				string maxValue = (nextTierValue > 1) ? $" / {nextTierValue}" : "";
				description.Value.text = $"{achievement.CurrentValue}{maxValue}";
			}
		}

		/// <summary>
		/// Clears all achievement UI elements and listeners.
		/// </summary>
		public void ClearAll()
		{
			// Destroy all achievement description UI elements and clear the dictionary.
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
			// Destroy all category buttons and remove their listeners.
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

		/// <summary>
		/// Event handler for when an achievement category is clicked. Updates the visible achievements.
		/// </summary>
		/// <param name="type">The achievement category to display.</param>
		public void Category_OnClick(AchievementCategory type)
		{
			// Set the current category and update which achievements are visible.
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