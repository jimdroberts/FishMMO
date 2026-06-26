using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit factions panel. Replaces the legacy UGUI <see cref="UIFactions"/> / <see cref="UIFactionDescription"/>:
	/// renders each known faction as a row with icon, name, description, a coloured standing bar, and the
	/// numeric standing value. Standing colour reflects positive/neutral/negative reputation.
	/// </summary>
	public class UITKFactions : UITKCharacterControl
	{
		/// <summary>Name of the container that holds the generated faction rows.</summary>
		private const string LIST_NAME = "faction-list";

		/// <summary>USS class applied to each generated faction row.</summary>
		private const string ROW_CLASS = "faction-row";

		/// <summary>USS class applied to a row's icon element.</summary>
		private const string ROW_ICON_CLASS = "faction-row__icon";

		/// <summary>USS class applied to a row's name label.</summary>
		private const string ROW_NAME_CLASS = "faction-row__name";

		/// <summary>USS class applied to a row's description label.</summary>
		private const string ROW_DESC_CLASS = "faction-row__desc";

		/// <summary>USS class applied to a row's standing bar track.</summary>
		private const string ROW_BAR_CLASS = "faction-row__bar";

		/// <summary>USS class applied to a row's standing bar fill.</summary>
		private const string ROW_FILL_CLASS = "faction-row__fill";

		/// <summary>USS class applied to a row's standing value label.</summary>
		private const string ROW_VALUE_CLASS = "faction-row__value";

		/// <summary>Standing colour used when reputation is positive.</summary>
		private static readonly Color PositiveColor = new Color(0.0f, 1.0f, 0.0f, 1.0f);

		/// <summary>Standing colour used when reputation is negative.</summary>
		private static readonly Color NegativeColor = new Color(1.0f, 0.0f, 0.0f, 1.0f);

		/// <summary>Standing colour used when reputation is neutral.</summary>
		private static readonly Color NeutralColor = new Color(0.529f, 0.808f, 0.980f, 1.0f);

		/// <summary>
		/// Visual elements backing a single faction row.
		/// </summary>
		private sealed class FactionRow
		{
			/// <summary>Root container for the row.</summary>
			public VisualElement Root;
			/// <summary>Faction icon element.</summary>
			public VisualElement Icon;
			/// <summary>Faction name label.</summary>
			public Label Name;
			/// <summary>Faction description label.</summary>
			public Label Description;
			/// <summary>Standing bar fill element.</summary>
			public VisualElement Fill;
			/// <summary>Standing value label.</summary>
			public Label Value;
		}

		/// <summary>All created faction rows keyed by faction template ID.</summary>
		private readonly Dictionary<int, FactionRow> factions = new Dictionary<int, FactionRow>();
		/// <summary>The container element that holds the generated faction rows.</summary>
		private VisualElement list;

		/// <summary>
		/// Queries the faction list and subscribes to character/client lifecycle events.
		/// </summary>
		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			list = root.Q(LIST_NAME);

			OnSetCharacter += CharacterControl_OnSetCharacter;
			IPlayerCharacter.OnStopLocalClient += PlayerCharacter_OnStopLocalClient;
		}

		/// <summary>
		/// Unsubscribes from events and clears all rows when the control is destroyed.
		/// </summary>
		public override void OnDestroying()
		{
			IPlayerCharacter.OnStopLocalClient -= PlayerCharacter_OnStopLocalClient;
			OnSetCharacter -= CharacterControl_OnSetCharacter;

			ClearAll();
		}

		/// <summary>
		/// Subscribes to faction update events after the character is set.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			if (Character.TryGet(out IFactionController factionController))
			{
				IFactionController.OnUpdateFaction += FactionController_OnUpdateFaction;
			}
		}

		/// <summary>
		/// Unsubscribes from faction update events and clears rows before the character is unset.
		/// </summary>
		public override void OnPreUnsetCharacter()
		{
			if (Character.TryGet(out IFactionController factionController))
			{
				IFactionController.OnUpdateFaction -= FactionController_OnUpdateFaction;
			}
			ClearAll();
		}

		/// <summary>
		/// Clears all faction rows when quitting to login.
		/// </summary>
		public override void OnQuitToLogin()
		{
			ClearAll();
		}

		/// <summary>
		/// Handles the local client stopping by clearing all rows.
		/// </summary>
		/// <param name="character">The local player character.</param>
		private void PlayerCharacter_OnStopLocalClient(IPlayerCharacter character)
		{
			ClearAll();
		}

		/// <summary>
		/// Populates faction rows for all of the character's known factions.
		/// </summary>
		/// <param name="character">The player character.</param>
		private void CharacterControl_OnSetCharacter(IPlayerCharacter character)
		{
			if (character.TryGet(out IFactionController factionController))
			{
				if (factionController.Factions == null)
				{
					return;
				}
				foreach (Faction faction in factionController.Factions.Values)
				{
					FactionController_OnUpdateFaction(character, faction);
				}
			}
		}

		/// <summary>
		/// Creates or updates the row for a faction whose standing changed.
		/// </summary>
		/// <param name="character">The character whose faction changed.</param>
		/// <param name="faction">The faction data.</param>
		public void FactionController_OnUpdateFaction(ICharacter character, Faction faction)
		{
			if (faction == null || list == null)
			{
				return;
			}

			Color color;
			if (faction.Value > 0)
			{
				color = PositiveColor;
			}
			else if (faction.Value < 0)
			{
				color = NegativeColor;
			}
			else
			{
				color = NeutralColor;
			}

			if (!factions.TryGetValue(faction.Template.ID, out FactionRow row))
			{
				row = CreateRow();

				row.Name.text = faction.Template.Name;
				row.Description.text = faction.Template.Description;
				if (faction.Template.Icon != null)
				{
					row.Icon.style.backgroundImage = new StyleBackground(faction.Template.Icon);
				}

				factions.Add(faction.Template.ID, row);
			}

			row.Name.style.color = color;

			float progress = Normalize(faction.Value, FactionTemplate.Minimum, FactionTemplate.Maximum);
			row.Fill.style.width = Length.Percent(Mathf.Clamp01(progress) * 100.0f);
			row.Fill.style.backgroundColor = color;

			row.Value.text = faction.Value.ToString();
		}

		/// <summary>
		/// Builds a new faction row and appends it to the list.
		/// </summary>
		/// <returns>The created row.</returns>
		private FactionRow CreateRow()
		{
			VisualElement rowRoot = new VisualElement();
			rowRoot.AddToClassList(ROW_CLASS);

			VisualElement icon = new VisualElement();
			icon.AddToClassList(ROW_ICON_CLASS);
			rowRoot.Add(icon);

			VisualElement column = new VisualElement();
			column.AddToClassList("faction-row__column");

			Label name = new Label();
			name.AddToClassList(ROW_NAME_CLASS);
			column.Add(name);

			Label description = new Label();
			description.AddToClassList(ROW_DESC_CLASS);
			column.Add(description);

			VisualElement bar = new VisualElement();
			bar.AddToClassList("fish-bar");
			bar.AddToClassList(ROW_BAR_CLASS);
			VisualElement fill = new VisualElement();
			fill.AddToClassList(ROW_FILL_CLASS);
			bar.Add(fill);

			Label value = new Label();
			value.AddToClassList(ROW_VALUE_CLASS);
			bar.Add(value);

			column.Add(bar);
			rowRoot.Add(column);

			list.Add(rowRoot);

			return new FactionRow
			{
				Root = rowRoot,
				Icon = icon,
				Name = name,
				Description = description,
				Fill = fill,
				Value = value,
			};
		}

		/// <summary>
		/// Normalizes a value into the 0-1 range for the given bounds.
		/// </summary>
		/// <param name="x">The value to normalize.</param>
		/// <param name="min">The minimum bound.</param>
		/// <param name="max">The maximum bound.</param>
		/// <returns>The normalized value.</returns>
		private float Normalize(float x, float min, float max)
		{
			return (x - min) / (max - min);
		}

		/// <summary>
		/// Removes all faction rows.
		/// </summary>
		public void ClearAll()
		{
			foreach (FactionRow row in factions.Values)
			{
				row.Root?.RemoveFromHierarchy();
			}
			factions.Clear();
		}
	}
}
