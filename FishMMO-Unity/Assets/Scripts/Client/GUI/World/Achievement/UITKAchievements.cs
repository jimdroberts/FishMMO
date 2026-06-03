using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit achievements panel. Replaces the legacy UGUI <see cref="UIAchievements"/> /
	/// <see cref="UIAchievementCategory"/> / <see cref="UIAchievementDescription"/>: renders achievement
	/// category buttons and, for the selected category, each achievement's icon, description, progress bar,
	/// and value.
	/// </summary>
	public class UITKAchievements : UITKCharacterControl
	{
		/// <summary>Name of the category button container.</summary>
		private const string CATEGORY_LIST_NAME = "achievement-categories";

		/// <summary>Name of the achievement description container.</summary>
		private const string DESC_LIST_NAME = "achievement-descriptions";

		/// <summary>USS class applied to each category button.</summary>
		private const string CATEGORY_BUTTON_CLASS = "achievement-category";

		/// <summary>USS class applied to each achievement row.</summary>
		private const string DESC_CLASS = "achievement-desc";

		/// <summary>USS class applied to an achievement row's icon element.</summary>
		private const string DESC_ICON_CLASS = "achievement-desc__icon";

		/// <summary>USS class applied to an achievement row's label.</summary>
		private const string DESC_LABEL_CLASS = "achievement-desc__label";

		/// <summary>USS class applied to an achievement row's progress bar track.</summary>
		private const string DESC_BAR_CLASS = "achievement-desc__bar";

		/// <summary>USS class applied to an achievement row's progress fill.</summary>
		private const string DESC_FILL_CLASS = "achievement-desc__fill";

		/// <summary>USS class applied to an achievement row's value label.</summary>
		private const string DESC_VALUE_CLASS = "achievement-desc__value";

		/// <summary>
		/// Visual elements backing a single achievement row.
		/// </summary>
		private sealed class DescriptionRow
		{
			/// <summary>Root container for the row.</summary>
			public VisualElement Root;
			/// <summary>Achievement icon element.</summary>
			public VisualElement Icon;
			/// <summary>Achievement description label.</summary>
			public Label Label;
			/// <summary>Progress bar fill element.</summary>
			public VisualElement Fill;
			/// <summary>Progress value label.</summary>
			public Label Value;
		}

		/// <summary>The currently selected achievement category.</summary>
		private AchievementCategory currentCategory;

		private readonly Dictionary<AchievementCategory, Button> categoryButtons = new Dictionary<AchievementCategory, Button>();
		private readonly Dictionary<AchievementCategory, Dictionary<int, DescriptionRow>> categories = new Dictionary<AchievementCategory, Dictionary<int, DescriptionRow>>();

		private VisualElement categoryList;
		private VisualElement descriptionList;

		/// <summary>
		/// Queries containers and subscribes to character/client lifecycle events.
		/// </summary>
		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			categoryList = root.Q(CATEGORY_LIST_NAME);
			descriptionList = root.Q(DESC_LIST_NAME);

			OnSetCharacter += CharacterControl_OnSetCharacter;
			IPlayerCharacter.OnStopLocalClient += PlayerCharacter_OnStopLocalClient;
		}

		/// <summary>
		/// Unsubscribes from events and clears UI when the control is destroyed.
		/// </summary>
		public override void OnDestroying()
		{
			IPlayerCharacter.OnStopLocalClient -= PlayerCharacter_OnStopLocalClient;
			OnSetCharacter -= CharacterControl_OnSetCharacter;

			ClearAll();
		}

		/// <summary>
		/// Subscribes to achievement update events after the character is set.
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
		/// Unsubscribes from achievement update events before the character is unset.
		/// </summary>
		public override void OnPreUnsetCharacter()
		{
			if (Character.TryGet(out IAchievementController achievementController))
			{
				IAchievementController.OnUpdateAchievement -= AchievementController_OnUpdateAchievement;
			}
		}

		/// <summary>
		/// Clears all achievement UI when quitting to login.
		/// </summary>
		public override void OnQuitToLogin()
		{
			ClearAll();
		}

		/// <summary>
		/// Handles the local client stopping by clearing all UI.
		/// </summary>
		/// <param name="character">The local player character.</param>
		private void PlayerCharacter_OnStopLocalClient(IPlayerCharacter character)
		{
			ClearAll();
		}

		/// <summary>
		/// Populates achievement UI for all of the character's achievements.
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
		/// Creates or updates the row (and category button) for an achievement.
		/// </summary>
		/// <param name="character">The character associated with the achievement.</param>
		/// <param name="achievement">The achievement to update.</param>
		public void AchievementController_OnUpdateAchievement(ICharacter character, Achievement achievement)
		{
			if (achievement == null || categoryList == null || descriptionList == null)
			{
				return;
			}

			AchievementCategory category = achievement.Template.Category;

			if (!categories.TryGetValue(category, out Dictionary<int, DescriptionRow> achievements))
			{
				categories.Add(category, achievements = new Dictionary<int, DescriptionRow>());

				Button categoryButton = new Button(() => Category_OnClick(category))
				{
					text = category.ToString(),
				};
				categoryButton.AddToClassList("fish-tab");
				categoryButton.AddToClassList(CATEGORY_BUTTON_CLASS);
				categoryList.Add(categoryButton);
				categoryButtons.Add(category, categoryButton);
			}

			if (!achievements.TryGetValue(achievement.Template.ID, out DescriptionRow row))
			{
				row = CreateRow();
				row.Label.text = achievement.Template.Description;
				if (achievement.Template.Icon != null)
				{
					row.Icon.style.backgroundImage = new StyleBackground(achievement.Template.Icon);
				}
				achievements.Add(achievement.Template.ID, row);
			}

			uint nextTierValue = achievement.NextTierValue;

			float progress = nextTierValue > 0 && achievement.CurrentValue > 0
				? (float)nextTierValue / achievement.CurrentValue
				: 1.0f;
			row.Fill.style.width = Length.Percent(Mathf.Clamp01(progress) * 100.0f);

			string maxValue = nextTierValue > 1 ? $" / {nextTierValue}" : "";
			row.Value.text = $"{achievement.CurrentValue}{maxValue}";
		}

		/// <summary>
		/// Selects a category and shows only its achievement rows.
		/// </summary>
		/// <param name="type">The category to display.</param>
		public void Category_OnClick(AchievementCategory type)
		{
			currentCategory = type;

			foreach (KeyValuePair<AchievementCategory, Dictionary<int, DescriptionRow>> pair in categories)
			{
				bool visible = pair.Key == currentCategory;
				foreach (DescriptionRow row in pair.Value.Values)
				{
					row.Root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
				}
			}

			foreach (KeyValuePair<AchievementCategory, Button> pair in categoryButtons)
			{
				pair.Value.EnableInClassList("fish-tab--active", pair.Key == currentCategory);
			}
		}

		/// <summary>
		/// Builds a new achievement row and appends it to the description list.
		/// </summary>
		/// <returns>The created row.</returns>
		private DescriptionRow CreateRow()
		{
			VisualElement rowRoot = new VisualElement();
			rowRoot.AddToClassList(DESC_CLASS);

			VisualElement icon = new VisualElement();
			icon.AddToClassList(DESC_ICON_CLASS);
			rowRoot.Add(icon);

			VisualElement column = new VisualElement();
			column.AddToClassList("achievement-desc__column");

			Label label = new Label();
			label.AddToClassList(DESC_LABEL_CLASS);
			column.Add(label);

			VisualElement bar = new VisualElement();
			bar.AddToClassList("fish-bar");
			bar.AddToClassList(DESC_BAR_CLASS);
			VisualElement fill = new VisualElement();
			fill.AddToClassList("fish-bar__fill--xp");
			fill.AddToClassList(DESC_FILL_CLASS);
			bar.Add(fill);

			Label value = new Label();
			value.AddToClassList(DESC_VALUE_CLASS);
			bar.Add(value);

			column.Add(bar);
			rowRoot.Add(column);

			descriptionList.Add(rowRoot);

			return new DescriptionRow
			{
				Root = rowRoot,
				Icon = icon,
				Label = label,
				Fill = fill,
				Value = value,
			};
		}

		/// <summary>
		/// Removes all category buttons and achievement rows.
		/// </summary>
		public void ClearAll()
		{
			foreach (KeyValuePair<AchievementCategory, Dictionary<int, DescriptionRow>> pair in categories)
			{
				foreach (DescriptionRow row in pair.Value.Values)
				{
					row.Root?.RemoveFromHierarchy();
				}
			}
			categories.Clear();

			foreach (Button button in categoryButtons.Values)
			{
				button.RemoveFromHierarchy();
			}
			categoryButtons.Clear();
		}
	}
}
