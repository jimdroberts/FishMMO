using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit factions panel. Renders each known faction as a row with icon, name, description,
	/// a coloured standing bar, and the numeric standing value. Standing colour reflects
	/// positive/neutral/negative reputation.
	/// </summary>
	public class UITKFactions : UITKCharacterControl
	{
		/// <summary>Name of the container that holds the generated faction rows.</summary>
		private const string LIST_NAME = "faction-list";

		/// <summary>Name of the header close button element.</summary>
		private const string CLOSE_BTN_NAME = "close-button";

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

			/* Resolved from the tree rather than cached: OnStarting re-runs on every reopen
			 * against a freshly cloned tree, so this is a new element each time and the
			 * handler cannot accumulate the way a subscription to a static event would. */
			Button closeButton = root.Q<Button>(CLOSE_BTN_NAME);
			if (closeButton != null)
			{
				closeButton.clicked += Hide;
			}

			list = root.Q(LIST_NAME);
			BindListChrome(
				list,
				root.Q<Label>("faction-count"),
				root.Q<Label>("faction-subtitle"),
				root.Q<Label>("faction-empty"),
				"faction",
				"factions");

			/* Unsubscribe first. OnStarting is re-run by ReinitializeIfTreeReplaced every time the
			 * visual tree is rebuilt — which is every reopen, because hiding the panel disables
			 * its UIDocument and re-enabling it clones the UXML afresh. A bare += here therefore
			 * stacked one more subscription per reopen. Removing a handler that is not subscribed
			 * is a no-op, so this is safe on the first pass. */
			IPlayerCharacter.OnStopLocalClient -= PlayerCharacter_OnStopLocalClient;
			IPlayerCharacter.OnStopLocalClient += PlayerCharacter_OnStopLocalClient;
		}

		/// <summary>
		/// Unsubscribes from events and clears all rows when the control is destroyed.
		/// </summary>
		public override void OnDestroying()
		{
			IPlayerCharacter.OnStopLocalClient -= PlayerCharacter_OnStopLocalClient;
			UnsubscribeFactions();

			ClearAll();
			base.OnDestroying();
		}

		/// <summary>
		/// Drops the faction subscription and the rows built against the previous tree.
		/// </summary>
		/// <remarks>
		/// The Pre half is what makes re-initialisation idempotent. <c>OnAfterStarting</c> runs
		/// <c>OnPreSetCharacter</c> then <c>OnPostSetCharacter</c> on every tree rebuild, and this
		/// panel's unsubscribe used to live in <see cref="OnPreUnsetCharacter"/> instead — a
		/// method that path never calls. The result was one extra subscription to a static event
		/// per reopen.
		/// <para>
		/// The rows go too: they belong to the tree that has just been replaced, and keeping them
		/// would leave the panel correctly wired and completely empty.
		/// </para>
		/// </remarks>
		public override void OnPreSetCharacter()
		{
			UnsubscribeFactions();
			ClearAll();
		}

		/// <summary>
		/// Subscribes to faction updates and rebuilds every row from the character's data.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			if (Character == null || !Character.TryGet(out IFactionController factionController))
			{
				return;
			}

			IFactionController.OnUpdateFaction += FactionController_OnUpdateFaction;

			/* Rebuilt from the controller — the model — rather than from whatever this panel was
			 * told while it was closed. The controller owns the standings as plain data; the rows
			 * are this panel's, and are disposable. */
			if (factionController.Factions == null)
			{
				return;
			}
			foreach (Faction faction in factionController.Factions.Values)
			{
				FactionController_OnUpdateFaction(Character, faction);
			}
		}

		/// <summary>
		/// Unsubscribes from faction update events and clears rows before the character is unset.
		/// </summary>
		public override void OnPreUnsetCharacter()
		{
			UnsubscribeFactions();
			ClearAll();
		}

		/// <summary>
		/// Removes the faction handler from the static event.
		/// </summary>
		/// <remarks>
		/// The event is static, so the unsubscribe must not be guarded by the character still
		/// being resolvable — the old code was, and silently skipped the unsubscribe whenever the
		/// character had already gone.
		/// </remarks>
		private void UnsubscribeFactions()
		{
			IFactionController.OnUpdateFaction -= FactionController_OnUpdateFaction;
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
		/// Creates or updates the row for a faction whose standing changed.
		/// </summary>
		/// <param name="character">The character whose faction changed.</param>
		/// <param name="faction">The faction data.</param>
		public void FactionController_OnUpdateFaction(ICharacter character, Faction faction)
		{
			/* This is a STATIC event: it fires for every character whose faction state changes,
			 * including every remote player whose FactionController reads its spawn payload as
			 * they come into observer range. Rows are keyed by faction template ID, so without
			 * this test walking past a stranger overwrote the local player's standings with
			 * theirs — a visible corruption of your own panel, and a disclosure of another
			 * player's exact reputation numbers through your own UI. */
			if (character == null || !ReferenceEquals(character, Character))
			{
				return;
			}
			if (faction == null || faction.Template == null || list == null)
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
		/// <returns>The normalized value, or 0 when the bounds carry no range.</returns>
		/// <remarks>
		/// <para>The degenerate bound is not hypothetical. <c>FactionTemplate.Minimum</c> and
		/// <c>Maximum</c> are authored on a ScriptableObject, and a template saved with them equal
		/// — or simply left at their defaults — made this a division by zero. In float arithmetic
		/// that does not throw: it produces NaN (for <c>x == min</c>) or an infinity, and
		/// <c>Mathf.Clamp01</c> passes NaN straight through, because NaN compares false against
		/// both bounds. The NaN then reached <c>Length.Percent</c> and poisoned the layout of the
		/// whole faction list, not just the one bar — a single mis-authored template blanked the
		/// panel.</para>
		/// <para>Zero rather than one for the degenerate case: an unauthored range is not evidence
		/// the player has maxed the faction, and an empty bar reads as "no information" where a
		/// full one would be an outright lie about their standing.</para>
		/// </remarks>
		private float Normalize(float x, float min, float max)
		{
			float range = max - min;
			if (range <= 0.0f || float.IsNaN(range) || float.IsInfinity(range))
			{
				return 0.0f;
			}

			float normalized = (x - min) / range;
			return float.IsNaN(normalized) ? 0.0f : normalized;
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
