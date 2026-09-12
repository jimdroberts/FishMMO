using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit factions panel. Renders the character's known factions as rows grouped by
	/// alliance level — allies first, neutrals in the middle, enemies last — each row carrying an
	/// icon, name, a coloured standing bar and the numeric standing value. Clicking a row expands
	/// it to reveal that faction's description; one row is open at a time.
	/// </summary>
	public class UITKFactions : UITKCharacterControl
	{
		/// <summary>Name of the container that holds the section elements.</summary>
		private const string LIST_NAME = "faction-list";

		/// <summary>Name of the header close button element.</summary>
		private const string CLOSE_BTN_NAME = "close-button";

		/// <summary>USS class applied to each generated faction row.</summary>
		private const string ROW_CLASS = "faction-row";

		/// <summary>USS class applied to a row's icon element.</summary>
		private const string ROW_ICON_CLASS = "faction-row__icon";

		/// <summary>USS class applied to the row that holds a row's caret and name.</summary>
		private const string ROW_NAMELINE_CLASS = "faction-row__nameline";

		/// <summary>USS class applied to a row's expansion caret.</summary>
		private const string ROW_CARET_CLASS = "faction-row__caret";

		/// <summary>USS class applied to a row's name label.</summary>
		private const string ROW_NAME_CLASS = "faction-row__name";

		/// <summary>USS class applied to a row's description label.</summary>
		private const string ROW_DESC_CLASS = "faction-row__desc";

		/// <summary>USS class that hides a row's description while the row is folded shut.</summary>
		private const string ROW_DESC_COLLAPSED_CLASS = "faction-row__desc--collapsed";

		/// <summary>USS class applied to a row's standing bar track.</summary>
		private const string ROW_BAR_CLASS = "faction-row__bar";

		/// <summary>USS class applied to a row's standing bar fill.</summary>
		private const string ROW_FILL_CLASS = "faction-row__fill";

		/// <summary>USS class applied to a row's standing value label.</summary>
		private const string ROW_VALUE_CLASS = "faction-row__value";

		/// <summary>USS class that hides a section holding no rows.</summary>
		private const string SECTION_EMPTY_CLASS = "faction-section--empty";

		/// <summary>Caret glyph shown on a row that is open.</summary>
		private const string CARET_EXPANDED = "▼";

		/// <summary>Caret glyph shown on a row that is shut.</summary>
		private const string CARET_COLLAPSED = "▶";

		/// <summary>
		/// The alliance levels, in the order their sections are displayed.
		/// </summary>
		/// <remarks>
		/// Allies first, neutrals in the middle, enemies last — which is also the order
		/// <see cref="FactionAllianceLevel"/> declares its own members in, so the enum's numeric
		/// value is used directly as a section index. The two must stay in step; the names are
		/// derived from the enum rather than written out here, so only the ORDER is asserted by
		/// this array.
		/// </remarks>
		private static readonly FactionAllianceLevel[] SectionLevels =
		{
			FactionAllianceLevel.Ally,
			FactionAllianceLevel.Neutral,
			FactionAllianceLevel.Enemy,
		};

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
			/// <summary>Template ID of the faction this row renders.</summary>
			public int TemplateID;
			/// <summary>The section this row currently sits in.</summary>
			public FactionAllianceLevel Level;
			/// <summary>Root container for the row.</summary>
			public VisualElement Root;
			/// <summary>Faction icon element.</summary>
			public VisualElement Icon;
			/// <summary>Expansion caret.</summary>
			public Label Caret;
			/// <summary>Faction name label.</summary>
			public Label Name;
			/// <summary>Faction description label, shown only while the row is expanded.</summary>
			public Label Description;
			/// <summary>Standing bar fill element.</summary>
			public VisualElement Fill;
			/// <summary>Standing value label.</summary>
			public Label Value;
		}

		/// <summary>All created faction rows keyed by faction template ID.</summary>
		private readonly Dictionary<int, FactionRow> factions = new Dictionary<int, FactionRow>();

		/// <summary>Section containers, indexed by <see cref="FactionAllianceLevel"/>.</summary>
		private readonly VisualElement[] sectionRoots = new VisualElement[SectionLevels.Length];

		/// <summary>Row containers inside the sections, indexed by <see cref="FactionAllianceLevel"/>.</summary>
		private readonly VisualElement[] sectionRows = new VisualElement[SectionLevels.Length];

		/// <summary>The container element that holds the generated faction sections.</summary>
		private VisualElement list;

		/// <summary>
		/// The one row currently expanded, or null when every row is folded shut.
		/// </summary>
		/// <remarks>
		/// Held as a row REFERENCE rather than as a template ID sentinel. Template IDs are
		/// <c>(typeName + assetName)</c> run through a 32-bit string hash, so they occupy the
		/// whole signed range — including any value a sentinel would have to reserve, which is
		/// why <c>FactionController</c> says as much in its payload comments. A reference cannot
		/// collide with an ID at all, and it survives the row being re-parented between sections
		/// because re-parenting does not replace the object.
		/// </remarks>
		private FactionRow expandedRow;

		/// <summary>
		/// Queries the faction sections and subscribes to character/client lifecycle events.
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

			list = ResolveSections(root);
			BindListChrome(
				list,
				root.Q<Label>("faction-count"),
				root.Q<Label>("faction-subtitle"),
				root.Q<Label>("faction-empty"),
				"faction",
				"factions",
				/* The rows are one level down, inside the section containers, so the badge has to
				 * be told how to count them — its default is the container's direct child count,
				 * which here would be the number of SECTIONS. */
				CountRows);

			/* Unsubscribe first. OnStarting is re-run by ReinitializeIfTreeReplaced every time the
			 * visual tree is rebuilt — which is every reopen, because hiding the panel disables
			 * its UIDocument and re-enabling it clones the UXML afresh. A bare += here therefore
			 * stacked one more subscription per reopen. Removing a handler that is not subscribed
			 * is a no-op, so this is safe on the first pass. */
			IPlayerCharacter.OnStopLocalClient -= PlayerCharacter_OnStopLocalClient;
			IPlayerCharacter.OnStopLocalClient += PlayerCharacter_OnStopLocalClient;
		}

		/// <summary>
		/// Resolves the section containers declared by the UXML.
		/// </summary>
		/// <param name="root">The panel's visual tree root.</param>
		/// <returns>The list container, or null when the tree does not declare every section.</returns>
		/// <remarks>
		/// All-or-nothing by design. A tree missing one section would have nowhere to file that
		/// level's rows, so the panel reports itself unbuildable instead of rendering a lie. A
		/// null list is already the state both <see cref="UITKControl.BindListChrome"/> and
		/// <see cref="ApplyFaction"/> treat as "do nothing", so a broken tree leaves the panel
		/// inert rather than throwing from a render path.
		/// </remarks>
		private VisualElement ResolveSections(VisualElement root)
		{
			VisualElement listElement = root.Q(LIST_NAME);
			if (listElement == null)
			{
				Log.Error("UITKFactions", $"List container '{LIST_NAME}' is missing from the UXML.");
				return null;
			}

			for (int i = 0; i < SectionLevels.Length; ++i)
			{
				string level = SectionLevels[i].ToString().ToLowerInvariant();
				string sectionName = $"faction-section-{level}";
				string rowsName = $"faction-section-rows-{level}";

				VisualElement sectionRoot = root.Q(sectionName);
				VisualElement rows = root.Q(rowsName);
				if (sectionRoot == null || rows == null)
				{
					Log.Error("UITKFactions",
						$"Section elements '{sectionName}' and '{rowsName}' are both required by the " +
						$"{SectionLevels[i]} section. The faction list will not be rendered.");
					return null;
				}

				sectionRoots[i] = sectionRoot;
				sectionRows[i] = rows;
			}

			return listElement;
		}

		/// <summary>
		/// Counts the faction rows across the three sections.
		/// </summary>
		/// <param name="unused">The bound list container. Unused; the rows live one level down.</param>
		/// <remarks>
		/// The sum is invariant under a row moving between sections, which matters: a move changes
		/// the distribution but not the total, so the container's geometry does not change and no
		/// <c>GeometryChangedEvent</c> fires. A count that was read off the tree's shape rather
		/// than summed would go stale exactly then.
		/// </remarks>
		private int CountRows(VisualElement unused)
		{
			int total = 0;
			for (int i = 0; i < sectionRows.Length; ++i)
			{
				if (sectionRows[i] != null)
				{
					total += sectionRows[i].childCount;
				}
			}
			return total;
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
			 * are this panel's, and are disposable.
			 *
			 * Routed through the guarded public handler rather than straight to ApplyFaction, so
			 * the rebuild keeps exercising the same identity test every live update does and there
			 * is no second, unguarded way into the render path. Order within a section does not
			 * depend on this loop: rows are filed by name, so any iteration order settles the same. */
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

			ApplyFaction(faction);
		}

		/// <summary>
		/// Creates or updates one faction's row and files it under its alliance section.
		/// </summary>
		/// <param name="faction">The faction to render.</param>
		/// <remarks>
		/// Split out from the handler above and deliberately taking no character: the identity
		/// test there answers "whose standing is this", which is a question about the event, not
		/// about rendering one row. Rendering a row needs only the row's own data, which is also
		/// what makes this path drivable without a networked character in a test.
		/// </remarks>
		private void ApplyFaction(Faction faction)
		{
			if (faction == null || faction.Template == null || list == null)
			{
				return;
			}

			/* One decision, used for both the section and the colour. The standing's sign already
			 * answers "which alliance level is this", and FactionController.GetAllianceLevelForStanding
			 * is the shared statement of that rule — the same partition the controller's own Allied,
			 * Neutral and Hostile tables are built from. */
			FactionAllianceLevel level = FactionController.GetAllianceLevelForStanding(faction.Value);
			Color color = StandingColor(level);

			if (!factions.TryGetValue(faction.Template.ID, out FactionRow row))
			{
				row = CreateRow(faction.Template.ID);

				row.Name.text = faction.Template.Name;
				row.Description.text = faction.Template.Description;
				if (faction.Template.Icon != null)
				{
					row.Icon.style.backgroundImage = new StyleBackground(faction.Template.Icon);
				}

				factions.Add(faction.Template.ID, row);
				PlaceRow(row, level);
			}
			else if (row.Level != level)
			{
				/* The standing crossed zero, so this row belongs in a different section now. */
				PlaceRow(row, level);
			}

			row.Name.style.color = color;

			float progress = Normalize(faction.Value, FactionTemplate.Minimum, FactionTemplate.Maximum);
			row.Fill.style.width = Length.Percent(Mathf.Clamp01(progress) * 100.0f);
			row.Fill.style.backgroundColor = color;

			row.Value.text = faction.Value.ToString();
		}

		/// <summary>
		/// The standing colour that represents an alliance level.
		/// </summary>
		/// <param name="level">The alliance level to colour.</param>
		private static Color StandingColor(FactionAllianceLevel level)
		{
			switch (level)
			{
				case FactionAllianceLevel.Ally: return PositiveColor;
				case FactionAllianceLevel.Enemy: return NegativeColor;
				default: return NeutralColor;
			}
		}

		/// <summary>
		/// Moves a row into the section for <paramref name="level"/> and files it in name order.
		/// </summary>
		/// <param name="row">The row to place.</param>
		/// <param name="level">The section it belongs in.</param>
		/// <remarks>
		/// Inserted at its alphabetical position rather than re-sorting the whole section: a
		/// re-sort would reshuffle rows the player is reading while standings stream in. Rows
		/// only reach here when they are created or when their level actually changed, so an
		/// ordinary reputation tick never re-parents one.
		/// </remarks>
		private void PlaceRow(FactionRow row, FactionAllianceLevel level)
		{
			int index = (int)level;
			if (index < 0 || index >= sectionRows.Length)
			{
				return;
			}

			VisualElement section = sectionRows[index];
			if (section == null)
			{
				return;
			}

			int previousIndex = (int)row.Level;

			row.Root.RemoveFromHierarchy();
			section.Insert(InsertionIndex(section, row.Name.text), row.Root);
			row.Level = level;

			/* Both, always. The section the row left is one shorter and may have just emptied,
			 * and refreshing the one it joined is idempotent — so there is no case to single out. */
			RefreshSectionVisibility(previousIndex);
			RefreshSectionVisibility(index);

			/* Re-applied after the move so a row that migrates keeps the expansion it had: the
			 * row object is the same one, so expandedRow still refers to it. */
			ApplyExpansion(row);
		}

		/// <summary>
		/// The index within a section at which a row named <paramref name="name"/> belongs.
		/// </summary>
		/// <param name="section">The section's row container.</param>
		/// <param name="name">The name of the row being placed.</param>
		/// <returns>The child index to insert at.</returns>
		/// <remarks>
		/// Comparison is explicitly ordinal-ignoring-case rather than culture-sensitive, so the
		/// panel reads the same on every machine. Siblings are identified by their name label,
		/// which is written once when a row is created and never rewritten — the caret carries a
		/// class of its own precisely so it cannot be mistaken for one.
		/// </remarks>
		private static int InsertionIndex(VisualElement section, string name)
		{
			int count = section.childCount;
			for (int i = 0; i < count; ++i)
			{
				Label sibling = section[i]?.Q<Label>(className: ROW_NAME_CLASS);
				if (sibling != null &&
					string.Compare(sibling.text, name, StringComparison.OrdinalIgnoreCase) > 0)
				{
					return i;
				}
			}
			return count;
		}

		/// <summary>
		/// Shows or hides one section according to whether it holds any rows.
		/// </summary>
		/// <param name="index">The section index to refresh.</param>
		/// <remarks>
		/// Hidden rather than removed from the hierarchy, so the UXML-declared element stays
		/// resolvable and no section ever has to be rebuilt. The invariant worth keeping is that
		/// a section is hidden exactly when its rows container is empty — that is what lets the
		/// header count be a sum over those containers.
		/// </remarks>
		private void RefreshSectionVisibility(int index)
		{
			if (index < 0 || index >= sectionRows.Length)
			{
				return;
			}

			VisualElement sectionRoot = sectionRoots[index];
			VisualElement rows = sectionRows[index];
			if (sectionRoot == null || rows == null)
			{
				return;
			}

			sectionRoot.EnableInClassList(SECTION_EMPTY_CLASS, rows.childCount == 0);
		}

		/// <summary>
		/// Recomputes the visibility of every section.
		/// </summary>
		private void RefreshAllSectionVisibility()
		{
			for (int i = 0; i < sectionRows.Length; ++i)
			{
				RefreshSectionVisibility(i);
			}
		}

		/// <summary>
		/// Folds one row's description open or shut to match <see cref="expandedRow"/>.
		/// </summary>
		/// <param name="row">The row to fold.</param>
		private void ApplyExpansion(FactionRow row)
		{
			if (row == null)
			{
				return;
			}

			bool expanded = ReferenceEquals(row, expandedRow);
			row.Description.EnableInClassList(ROW_DESC_COLLAPSED_CLASS, !expanded);
			row.Caret.text = expanded ? CARET_EXPANDED : CARET_COLLAPSED;
		}

		/// <summary>
		/// Re-folds every row in place, without rebuilding any of them.
		/// </summary>
		/// <remarks>
		/// In place rather than through a rebuild, for the reason the guild rank list gives: a
		/// rebuild triggered by a click is a rebuild that happens while the player's pointer is
		/// still down on the thing they clicked.
		/// </remarks>
		private void RefreshExpansion()
		{
			foreach (FactionRow row in factions.Values)
			{
				ApplyExpansion(row);
			}
		}

		/// <summary>
		/// Opens one faction's row and shuts every other, or shuts it when it is already open.
		/// </summary>
		/// <param name="templateID">Template ID of the faction whose row was clicked.</param>
		/// <remarks>
		/// One at a time because the point of the expansion is to stop the panel reading as a wall
		/// of text; several open rows is most of the way back to that. Split out from the click
		/// handler so the behaviour can be driven directly — the handler's own contribution is the
		/// button test, which is pinned separately.
		/// </remarks>
		private void ToggleExpansion(int templateID)
		{
			if (!factions.TryGetValue(templateID, out FactionRow row))
			{
				return;
			}

			expandedRow = ReferenceEquals(expandedRow, row) ? null : row;
			RefreshExpansion();
		}

		/// <summary>
		/// Builds a new faction row. It is not parented here — <see cref="PlaceRow"/> places it.
		/// </summary>
		/// <param name="templateID">Template ID of the faction the row renders.</param>
		/// <returns>The created row.</returns>
		private FactionRow CreateRow(int templateID)
		{
			VisualElement rowRoot = new VisualElement();
			rowRoot.AddToClassList(ROW_CLASS);

			VisualElement icon = new VisualElement();
			icon.AddToClassList(ROW_ICON_CLASS);
			rowRoot.Add(icon);

			VisualElement column = new VisualElement();
			column.AddToClassList("faction-row__column");

			/* The caret and the name get a row of their own. Adding the caret straight to the
			 * column would put it on a line above the name, because the column is a flex COLUMN —
			 * the caret only sits beside the name if something lays the two out in a row. */
			VisualElement nameLine = new VisualElement();
			nameLine.AddToClassList(ROW_NAMELINE_CLASS);

			Label caret = new Label();
			caret.AddToClassList(ROW_CARET_CLASS);
			nameLine.Add(caret);

			Label name = new Label();
			name.AddToClassList(ROW_NAME_CLASS);
			nameLine.Add(name);

			column.Add(nameLine);

			Label description = new Label();
			description.AddToClassList(ROW_DESC_CLASS);
			// Faction descriptions are authored assets rather than player input, but a stray
			// angle bracket should render as text rather than be parsed as markup.
			description.enableRichText = false;
			description.AddToClassList(ROW_DESC_COLLAPSED_CLASS);
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

			/* Registered on the row itself, unlike the guild rank list which has to register on
			 * its header to protect the toggles it holds. Nothing inside a faction row reacts to
			 * a pointer, so the whole row can be the target — and the row is a much easier thing
			 * to hit than a 14px caret. The left-button test keeps a right-click or a middle-click
			 * from folding the row as a side effect of whatever the player meant by it. */
			rowRoot.RegisterCallback<PointerDownEvent>(evt =>
			{
				if (evt.button != 0)
				{
					return;
				}

				ToggleExpansion(templateID);
			});

			return new FactionRow
			{
				TemplateID = templateID,
				Root = rowRoot,
				Icon = icon,
				Caret = caret,
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
		/// Removes all faction rows and hides every section.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The sections are re-hidden here rather than only where rows are placed. This is reached
		/// from <see cref="OnQuitToLogin"/> and from the local client stopping, neither of which
		/// rebuilds afterwards — leaving visibility to the next placement would strand three
		/// headings over the empty placeholder on the way back to the login screen.
		/// </para>
		/// <para>
		/// Clearing the containers as well as the rows keeps the two in step even if a row were
		/// ever orphaned by a placement mistake, and it is safe to call twice in a row and with a
		/// torn-down tree: <see cref="OnDestroying"/> reaches it directly and again through the
		/// base class's character teardown.
		/// </para>
		/// </remarks>
		public void ClearAll()
		{
			foreach (FactionRow row in factions.Values)
			{
				row.Root?.RemoveFromHierarchy();
			}
			factions.Clear();

			for (int i = 0; i < sectionRows.Length; ++i)
			{
				sectionRows[i]?.Clear();
			}

			expandedRow = null;
			RefreshAllSectionVisibility();
		}
	}
}
