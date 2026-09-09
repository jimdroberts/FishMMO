using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit abilities panel: what the character can cast, and what they can craft with.
	/// </summary>
	/// <remarks>
	/// <para><b>Two tabs, because there are two kinds of thing.</b> An ability on the Abilities tab
	/// is finished — it can be dragged onto a hotkey and used. Everything on the Knowledge tab is a
	/// part: a base ability is the core of a craft, an effect is something a base ability is
	/// configured with, and neither does anything until they are combined at an Ability Crafter.
	/// The panel used to show all three as identical icon squares, so a template read as an ability
	/// that simply refused to work (issue #247).</para>
	///
	/// <para><b>Rows, not bare icons.</b> Each entry carries its name and the numbers that
	/// distinguish it, and selecting one renders its full description in the pane beside the list —
	/// the same <see cref="TooltipContent"/> the cursor tooltip draws, through the same
	/// <see cref="UITKTooltipView"/>. An icon grid could not show any of that, so two abilities
	/// that differed only in cooldown and range looked the same until the player hovered each in
	/// turn.</para>
	///
	/// <para><b>Only the Abilities tab drags.</b> A click that cannot pick an entry up writes the
	/// reason to the status line under the list, where the tab's standing hint otherwise sits.</para>
	/// </remarks>
	public class UITKAbilities : UITKCharacterControl
	{
		/// <summary>Name of the abilities tab button.</summary>
		private const string ABILITY_TAB_NAME = "ability-tab-abilities";

		/// <summary>Name of the knowledge tab button.</summary>
		private const string KNOWLEDGE_TAB_NAME = "ability-tab-knowledge";

		/// <summary>Name of the header close button element.</summary>
		private const string CLOSE_BTN_NAME = "close-button";

		/// <summary>Name of the entry container.</summary>
		private const string LIST_NAME = "ability-list";

		/// <summary>Name of the empty-list label.</summary>
		private const string EMPTY_NAME = "ability-empty";

		/// <summary>Name of the header subtitle.</summary>
		private const string SUBTITLE_NAME = "ability-subtitle";

		/// <summary>Name of the header count badge.</summary>
		private const string COUNT_NAME = "ability-count";

		/// <summary>Name of the search field.</summary>
		private const string SEARCH_NAME = "ability-search";

		/// <summary>Names of the knowledge filter chips.</summary>
		private const string FILTER_ALL_NAME = "ability-filter-all";
		private const string FILTER_BASES_NAME = "ability-filter-bases";
		private const string FILTER_EFFECTS_NAME = "ability-filter-effects";

		/// <summary>Name of the details pane's content container.</summary>
		private const string DETAILS_NAME = "ability-details-content";

		/// <summary>Name of the details pane's placeholder.</summary>
		private const string DETAILS_EMPTY_NAME = "ability-details-empty";

		/// <summary>Name of the status line under the list.</summary>
		private const string STATUS_LABEL_NAME = "ability-status";

		/// <summary>USS class applied to each generated entry row.</summary>
		private const string ENTRY_CLASS = "ability-entry";

		/// <summary>USS class marking a row that holds knowledge rather than a usable ability.</summary>
		private const string ENTRY_KNOWLEDGE_CLASS = "ability-entry--knowledge";

		/// <summary>Standing hints, one per tab.</summary>
		private const string ABILITY_TAB_HINT = "Click an ability to pick it up, then click a hotkey slot.";
		private const string KNOWLEDGE_TAB_HINT = "Knowledge is crafted into abilities at an Ability Crafter.";

		/// <summary>What the status line says when a piece of knowledge is clicked.</summary>
		private const string TEMPLATE_REFUSAL = "That is a base ability, not a usable one yet. Craft it at an Ability Crafter.";
		private const string EFFECT_REFUSAL = "That is an effect. Add it to a base ability at an Ability Crafter.";

		/// <summary>Empty-list wording per view.</summary>
		private const string ABILITY_EMPTY = "No abilities learned. Craft one at an Ability Crafter.";
		private const string KNOWLEDGE_EMPTY = "No knowledge learned. Merchants sell base abilities and effects.";
		private const string SEARCH_EMPTY = "Nothing matches that search.";

		/// <summary>Guidance rows appended to a knowledge entry's description.</summary>
		private const string TEMPLATE_HINT = "Base ability. Take it to an Ability Crafter to build a usable ability from it.";
		private const string EFFECT_HINT = "Ability effect. Add it to a base ability at an Ability Crafter.";

		/// <summary>Name of the shared drag object overlay panel.</summary>
		private const string DRAG_OBJECT_NAME = "UIDragObject";

		/// <summary>
		/// One row: what it holds, what it says, and where it may go.
		/// </summary>
		private sealed class AbilityEntry
		{
			/// <summary>Root row element.</summary>
			public VisualElement Root;
			/// <summary>Icon sprite, used when picking up onto the drag object.</summary>
			public Sprite IconSprite;
			/// <summary>Ability instance ID, or template/event ID for knowledge.</summary>
			public long ReferenceID;
			/// <summary>Reference type for the drag object. <c>None</c> for knowledge.</summary>
			public ReferenceButtonType Type;
			/// <summary>Which tab the entry belongs to.</summary>
			public AbilityTabType Tab;
			/// <summary>Which knowledge filter it satisfies.</summary>
			public KnowledgeFilterType Kind;
			/// <summary>Lower-cased name, matched against the search text.</summary>
			public string SearchKey;
			/// <summary>The description, built once when the row is created.</summary>
			public TooltipContent Content;
		}

		/// <summary>Every row, in both tabs.</summary>
		private readonly List<AbilityEntry> entries = new List<AbilityEntry>();

		/// <summary>Tab buttons.</summary>
		private Button abilityTab;
		private Button knowledgeTab;

		/// <summary>Knowledge filter chips.</summary>
		private Button filterAll;
		private Button filterBases;
		private Button filterEffects;

		/// <summary>The row container.</summary>
		private VisualElement listRoot;

		/// <summary>The placeholder shown when the visible list is empty.</summary>
		private Label emptyLabel;

		/// <summary>Header subtitle and count badge.</summary>
		private Label subtitleLabel;
		private Label countLabel;

		/// <summary>The search field.</summary>
		private TextField searchField;

		/// <summary>The details pane and its placeholder.</summary>
		private VisualElement detailsRoot;
		private Label detailsEmpty;

		/// <summary>Status line under the list.</summary>
		private Label statusLabel;

		/// <summary>The visible tab.</summary>
		private AbilityTabType currentTab = AbilityTabType.Ability;

		/// <summary>The knowledge tab's filter.</summary>
		private KnowledgeFilterType currentFilter = KnowledgeFilterType.All;

		/// <summary>The search text, kept across tree rebuilds.</summary>
		private string searchText = string.Empty;

		/// <summary>The selected entry's reference, so a rebuild can restore the details pane.</summary>
		private long selectedReferenceID;

		/// <summary>The selected entry's tab, because ids are only unique within one.</summary>
		private AbilityTabType selectedTab;

		/// <summary>
		/// Queries elements, wires the tabs, filters and search, and subscribes to lifecycle events.
		/// </summary>
		public override void OnStarting()
		{
			/* Every row in this list is a VisualElement from the tree that was just discarded.
			 * Keeping them would leave the panel rendering nothing while still believing it is
			 * full, and every later add would append to a dead container. */
			entries.Clear();

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

			abilityTab = root.Q<Button>(ABILITY_TAB_NAME);
			knowledgeTab = root.Q<Button>(KNOWLEDGE_TAB_NAME);
			filterAll = root.Q<Button>(FILTER_ALL_NAME);
			filterBases = root.Q<Button>(FILTER_BASES_NAME);
			filterEffects = root.Q<Button>(FILTER_EFFECTS_NAME);
			listRoot = root.Q(LIST_NAME);
			emptyLabel = root.Q<Label>(EMPTY_NAME);
			subtitleLabel = root.Q<Label>(SUBTITLE_NAME);
			countLabel = root.Q<Label>(COUNT_NAME);
			searchField = root.Q<TextField>(SEARCH_NAME);
			detailsRoot = root.Q(DETAILS_NAME);
			detailsEmpty = root.Q<Label>(DETAILS_EMPTY_NAME);
			statusLabel = root.Q<Label>(STATUS_LABEL_NAME);

			if (abilityTab != null) abilityTab.clicked += () => SwitchTab(AbilityTabType.Ability);
			if (knowledgeTab != null) knowledgeTab.clicked += () => SwitchTab(AbilityTabType.Knowledge);
			if (filterAll != null) filterAll.clicked += () => SwitchFilter(KnowledgeFilterType.All);
			if (filterBases != null) filterBases.clicked += () => SwitchFilter(KnowledgeFilterType.BaseAbilities);
			if (filterEffects != null) filterEffects.clicked += () => SwitchFilter(KnowledgeFilterType.Effects);

			if (searchField != null)
			{
				/* SetValueWithoutNotify, because writing .value inside a rebuild queues a
				 * ChangeEvent that arrives after this method returns and re-enters the filter
				 * with a stale tree. The text is restored here so a hide/show does not silently
				 * drop a search the player had typed. */
				searchField.SetValueWithoutNotify(searchText);
				searchField.RegisterValueChangedCallback(OnSearchChanged);
			}

			/* Static event, and OnStarting re-runs on every tree rebuild — remove before add so the
			 * pair is idempotent instead of leaking one handler per rebuild. */
			IPlayerCharacter.OnStopLocalClient -= PlayerCharacter_OnStopLocalClient;
			IPlayerCharacter.OnStopLocalClient += PlayerCharacter_OnStopLocalClient;

			SwitchTab(currentTab);
		}

		/// <summary>
		/// Unsubscribes from events and clears all rows when the control is destroyed.
		/// </summary>
		public override void OnDestroying()
		{
			IPlayerCharacter.OnStopLocalClient -= PlayerCharacter_OnStopLocalClient;

			UnsubscribeAbilityController();
			ClearEntries();

			base.OnDestroying();
		}

		/// <summary>
		/// Subscribes to ability controller events after the character is set.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			if (Character.TryGet(out IAbilityController abilityController))
			{
				abilityController.OnCanManipulate += CanManipulateAbility;
				abilityController.OnAddAbility += AddAbility;
				abilityController.OnAddKnownAbility += AddKnownAbility;
				abilityController.OnAddKnownAbilityEvent += AddKnownAbilityEvent;
				abilityController.OnRemoveAbility += RemoveAbility;

				RebuildFromCharacter(abilityController);
			}
		}

		/// <summary>
		/// Drops the outgoing character's subscriptions before a new character is applied.
		/// </summary>
		/// <remarks>
		/// <c>UITKCharacterControl.OnAfterStarting</c> calls Pre then Post on every tree rebuild so
		/// the two cancel out. With Pre un-overridden the cancelling half did nothing and each
		/// rebuild stacked another copy of all four handlers.
		/// </remarks>
		public override void OnPreSetCharacter()
		{
			UnsubscribeAbilityController();
		}

		/// <summary>Drops every ability-controller subscription held on the current character.</summary>
		private void UnsubscribeAbilityController()
		{
			if (Character != null &&
				Character.TryGet(out IAbilityController abilityController))
			{
				abilityController.OnCanManipulate -= CanManipulateAbility;
				abilityController.OnAddAbility -= AddAbility;
				abilityController.OnAddKnownAbility -= AddKnownAbility;
				abilityController.OnAddKnownAbilityEvent -= AddKnownAbilityEvent;
				abilityController.OnRemoveAbility -= RemoveAbility;
			}
		}

		/// <summary>
		/// Repopulates both tabs from the character's current knowledge.
		/// </summary>
		/// <remarks>
		/// The panel is otherwise fed exclusively by the <c>OnAdd*</c> events, which the controller
		/// raises once, on <c>OnStartCharacter</c>. Anything that rebuilds the visual tree after
		/// that point — the first hide/show, a theme reload — left the panel permanently empty,
		/// because the events that would have filled it had already fired.
		/// </remarks>
		private void RebuildFromCharacter(IAbilityController abilityController)
		{
			ClearEntries();

			foreach (Ability ability in abilityController.KnownAbilities.Values)
			{
				AddAbility(ability);
			}

			foreach (int templateID in abilityController.KnownBaseAbilities)
			{
				AddKnownAbility(BaseAbilityTemplate.Get<BaseAbilityTemplate>(templateID));
			}

			foreach (int eventID in abilityController.KnownAbilityEvents)
			{
				AddKnownAbilityEvent(AbilityEvent.Get<AbilityEvent>(eventID));
			}

			ApplyFilter();
		}

		/// <summary>Clears rows when the character is unset.</summary>
		public override void OnPreUnsetCharacter()
		{
			UnsubscribeAbilityController();
			ClearEntries();
		}

		/// <summary>Clears rows when quitting to login.</summary>
		public override void OnQuitToLogin()
		{
			ClearEntries();
		}

		/// <summary>Whether ability manipulation is allowed. Blocked while mouse mode is active.</summary>
		private bool CanManipulateAbility()
		{
			return !PlayerInputController.MouseMode;
		}

		/// <summary>Clears rows when the local client stops.</summary>
		private void PlayerCharacter_OnStopLocalClient(IPlayerCharacter character)
		{
			ClearEntries();
		}

		/// <summary>
		/// Adds a row for a usable ability.
		/// </summary>
		/// <param name="ability">The ability to add.</param>
		public void AddAbility(Ability ability)
		{
			if (ability?.Template == null)
			{
				return;
			}

			AbilitySummary summary = AbilitySummary.FromAbility(ability);
			TooltipContent content = new TooltipContent { Icon = ability.Template.Icon };
			summary.BuildTooltip(content);

			CreateEntry(new AbilityEntry
			{
				ReferenceID = ability.ID,
				Type = ReferenceButtonType.Ability,
				Tab = AbilityTabType.Ability,
				Kind = KnowledgeFilterType.All,
				IconSprite = ability.Template.Icon,
				SearchKey = ability.Name?.ToLowerInvariant(),
				Content = content,
			}, ability.Name, AbilityMeta(summary), null);
		}

		/// <summary>
		/// Adds a row for a base ability the character knows.
		/// </summary>
		/// <param name="template">The base ability template to add.</param>
		public void AddKnownAbility(BaseAbilityTemplate template)
		{
			if (template == null)
			{
				return;
			}

			TooltipContent content = template.BuildContent();
			content.AddHint(TEMPLATE_HINT);

			string meta;
			if (template is AbilityTemplate abilityTemplate)
			{
				AbilitySummary summary = AbilitySummary.Compose(abilityTemplate);
				meta = AbilityMeta(summary);
			}
			else
			{
				meta = "Ability modifier";
			}

			CreateEntry(new AbilityEntry
			{
				ReferenceID = template.ID,
				Type = ReferenceButtonType.None,
				Tab = AbilityTabType.Knowledge,
				Kind = KnowledgeFilterType.BaseAbilities,
				IconSprite = template.Icon,
				SearchKey = template.Name?.ToLowerInvariant(),
				Content = content,
			}, template.Name, meta, "base");
		}

		/// <summary>
		/// Adds a row for an effect the character knows.
		/// </summary>
		/// <param name="abilityEvent">The ability event to add.</param>
		public void AddKnownAbilityEvent(AbilityEvent abilityEvent)
		{
			if (abilityEvent == null)
			{
				return;
			}

			TooltipContent content = abilityEvent.BuildContent();
			content.AddHint(EFFECT_HINT);

			CreateEntry(new AbilityEntry
			{
				ReferenceID = abilityEvent.ID,
				Type = ReferenceButtonType.None,
				Tab = AbilityTabType.Knowledge,
				Kind = KnowledgeFilterType.Effects,
				IconSprite = abilityEvent.Icon,
				SearchKey = abilityEvent.Name?.ToLowerInvariant(),
				Content = content,
			}, abilityEvent.Name, EffectMeta(abilityEvent), "effect");
		}

		/// <summary>
		/// Removes the row for an ability the character no longer knows.
		/// </summary>
		/// <param name="referenceID">The removed ability's reference ID.</param>
		public void RemoveAbility(long referenceID)
		{
			for (int i = entries.Count - 1; i >= 0; --i)
			{
				if (entries[i].Tab != AbilityTabType.Ability || entries[i].ReferenceID != referenceID)
				{
					continue;
				}

				entries[i].Root?.RemoveFromHierarchy();
				entries.RemoveAt(i);
			}

			if (selectedTab == AbilityTabType.Ability && selectedReferenceID == referenceID)
			{
				ClearSelection();
			}
			ApplyFilter();
		}

		/// <summary>
		/// The one-line summary under an ability's name.
		/// </summary>
		/// <remarks>
		/// Deliberately short. The row is one line wide and clips what does not fit, and a clipped
		/// resource cost reading "7 Ma" is worse than no resource cost at all — the details pane
		/// beside the list carries the full picture. Four facts is what fits.
		/// </remarks>
		private static string AbilityMeta(AbilitySummary summary)
		{
			List<string> parts = new List<string>();
			if (summary.Type != AbilityType.None)
			{
				parts.Add(summary.Type.ToString());
			}
			if (summary.Stats.Cooldown > 0.0f) parts.Add($"{summary.Stats.Cooldown:0.##}s cd");
			if (summary.Stats.Range > 0.0f) parts.Add($"{summary.Stats.Range:0.#}m");
			if (summary.Stats.ActivationTime > 0.0f) parts.Add($"{summary.Stats.ActivationTime:0.##}s cast");
			return string.Join("  ·  ", parts.GetRange(0, Mathf.Min(parts.Count, 3)));
		}

		/// <summary>The one-line summary under an effect's name: when it runs and what it changes.</summary>
		private static string EffectMeta(AbilityEvent abilityEvent)
		{
			List<string> parts = new List<string> { AbilitySummary.EventCategory(abilityEvent) };
			AppendModifier(parts, abilityEvent.Cooldown, "s cd");
			AppendModifier(parts, abilityEvent.ActivationTime, "s cast");
			AppendModifier(parts, abilityEvent.Speed, "m/s");
			AppendModifier(parts, abilityEvent.LifeTime, "s life");
			return string.Join("  ·  ", parts);
		}

		/// <summary>Appends a signed modifier, or nothing when the effect does not change that stat.</summary>
		private static void AppendModifier(List<string> parts, float value, string suffix)
		{
			if (Mathf.Abs(value) < 0.0005f)
			{
				return;
			}
			parts.Add(value > 0.0f ? $"+{value:0.##}{suffix}" : $"{value:0.##}{suffix}");
		}

		/// <summary>
		/// Builds a row and adds it to the list.
		/// </summary>
		/// <param name="entry">The entry state.</param>
		/// <param name="name">The display name.</param>
		/// <param name="meta">The line of stats under the name.</param>
		/// <param name="badge">The corner badge naming what the row holds, or null for an ability.</param>
		private void CreateEntry(AbilityEntry entry, string name, string meta, string badge)
		{
			if (listRoot == null)
			{
				return;
			}

			VisualElement row = new VisualElement();
			row.AddToClassList("fish-row");
			row.AddToClassList(ENTRY_CLASS);
			if (entry.Tab == AbilityTabType.Knowledge)
			{
				row.AddToClassList(ENTRY_KNOWLEDGE_CLASS);
			}

			VisualElement icon = new VisualElement();
			icon.AddToClassList("ability-entry__icon");
			if (entry.IconSprite != null)
			{
				icon.style.backgroundImage = new StyleBackground(entry.IconSprite);
			}
			icon.pickingMode = PickingMode.Ignore;
			row.Add(icon);

			VisualElement text = new VisualElement();
			text.AddToClassList("ability-entry__text");
			text.pickingMode = PickingMode.Ignore;

			Label nameLabel = new Label(name);
			nameLabel.AddToClassList("ability-entry__name");
			nameLabel.pickingMode = PickingMode.Ignore;
			text.Add(nameLabel);

			if (!string.IsNullOrEmpty(meta))
			{
				Label metaLabel = new Label(meta);
				metaLabel.AddToClassList("ability-entry__meta");
				metaLabel.pickingMode = PickingMode.Ignore;
				text.Add(metaLabel);
			}
			row.Add(text);

			if (!string.IsNullOrEmpty(badge))
			{
				Label badgeLabel = new Label(badge);
				badgeLabel.AddToClassList("ability-entry__badge");
				badgeLabel.AddToClassList(entry.Kind == KnowledgeFilterType.Effects
					? "ability-entry__badge--effect"
					: "ability-entry__badge--craft");
				badgeLabel.pickingMode = PickingMode.Ignore;
				row.Add(badgeLabel);
			}

			entry.Root = row;

			/* Hover previews into the details pane rather than opening the cursor tooltip. The
			 * pane is right there and is bigger; a floating tooltip over the top of it would be
			 * the same information twice, one copy covering the other. */
			row.RegisterCallback<PointerEnterEvent>(_ => ShowDetails(entry));
			row.RegisterCallback<PointerLeaveEvent>(_ => RestoreSelectedDetails());
			row.RegisterCallback<PointerDownEvent>(evt => OnEntryPointerDown(evt, entry));

			listRoot.Add(row);
			entries.Add(entry);

			ApplyFilter();
		}

		/// <summary>Picks an entry up onto the drag object, or says why it cannot be.</summary>
		private void OnEntryPointerDown(PointerDownEvent evt, AbilityEntry entry)
		{
			if (evt.button != 0 || Character == null)
			{
				return;
			}

			Select(entry);

			if (!UIManager.TryGetTK(DRAG_OBJECT_NAME, out UITKDragObject dragObject))
			{
				return;
			}

			if (dragObject.Visible)
			{
				dragObject.Clear();
				return;
			}

			if (entry.Tab == AbilityTabType.Ability &&
				Character.TryGet(out IAbilityController abilityController) &&
				abilityController.KnownAbilities.ContainsKey(entry.ReferenceID))
			{
				dragObject.SetReference(entry.IconSprite, entry.ReferenceID, entry.Type);
				return;
			}

			ExplainRefusedPickup(entry);
		}

		/// <summary>
		/// Writes why an entry could not be picked up. This was silence, and silence read as a
		/// broken drag feature rather than as a different kind of entry.
		/// </summary>
		private void ExplainRefusedPickup(AbilityEntry entry)
		{
			if (entry.Tab == AbilityTabType.Knowledge)
			{
				SetStatus(entry.Kind == KnowledgeFilterType.Effects ? EFFECT_REFUSAL : TEMPLATE_REFUSAL);
				return;
			}

			/* An Abilities-tab row the controller no longer knows — removed under the panel, most
			 * likely. Nothing to craft; nothing useful to say beyond that. */
			SetStatus("That ability is no longer available.");
		}

		/// <summary>Marks an entry as the selected one and shows it in the details pane.</summary>
		private void Select(AbilityEntry entry)
		{
			selectedReferenceID = entry.ReferenceID;
			selectedTab = entry.Tab;

			foreach (AbilityEntry other in entries)
			{
				other.Root?.EnableInClassList("fish-row--selected", ReferenceEquals(other, entry));
			}
			ShowDetails(entry);
		}

		/// <summary>Drops the selection and empties the details pane.</summary>
		private void ClearSelection()
		{
			selectedReferenceID = 0;
			foreach (AbilityEntry entry in entries)
			{
				entry.Root?.RemoveFromClassList("fish-row--selected");
			}
			ShowDetails(null);
		}

		/// <summary>Renders an entry's description into the details pane.</summary>
		private void ShowDetails(AbilityEntry entry)
		{
			if (detailsRoot == null)
			{
				return;
			}

			UITKTooltipView.Render(detailsRoot, entry?.Content);

			if (detailsEmpty != null)
			{
				detailsEmpty.style.display = entry == null ? DisplayStyle.Flex : DisplayStyle.None;
			}
		}

		/// <summary>Puts the selected entry back in the pane after a hover preview ends.</summary>
		private void RestoreSelectedDetails()
		{
			AbilityEntry selected = FindSelected();
			ShowDetails(selected);
		}

		/// <summary>The selected entry, or null when nothing is selected or it is gone.</summary>
		private AbilityEntry FindSelected()
		{
			if (selectedReferenceID == 0)
			{
				return null;
			}

			foreach (AbilityEntry entry in entries)
			{
				if (entry.Tab == selectedTab && entry.ReferenceID == selectedReferenceID)
				{
					return entry;
				}
			}
			return null;
		}

		/// <summary>Applies a new search string and re-filters.</summary>
		private void OnSearchChanged(ChangeEvent<string> evt)
		{
			searchText = evt.newValue ?? string.Empty;
			ApplyFilter();
		}

		/// <summary>Switches the visible tab.</summary>
		/// <param name="tab">The tab to display.</param>
		public void SwitchTab(AbilityTabType tab)
		{
			currentTab = tab;

			abilityTab?.EnableInClassList("fish-tab--active", tab == AbilityTabType.Ability);
			knowledgeTab?.EnableInClassList("fish-tab--active", tab == AbilityTabType.Knowledge);

			/* The filter chips belong to the knowledge tab and are meaningless beside a list of
			 * finished abilities, so they are hidden rather than left there doing nothing. */
			bool knowledge = tab == AbilityTabType.Knowledge;
			SetDisplayed(filterAll, knowledge);
			SetDisplayed(filterBases, knowledge);
			SetDisplayed(filterEffects, knowledge);

			SetStatus(tab == AbilityTabType.Ability ? ABILITY_TAB_HINT : KNOWLEDGE_TAB_HINT);
			ApplyFilter();
		}

		/// <summary>Narrows the knowledge tab to one kind of part.</summary>
		/// <param name="filter">The filter to apply.</param>
		public void SwitchFilter(KnowledgeFilterType filter)
		{
			currentFilter = filter;
			ApplyFilter();
		}

		/// <summary>
		/// Shows the rows that match the tab, the filter and the search, and hides the rest.
		/// </summary>
		private void ApplyFilter()
		{
			filterAll?.EnableInClassList("fish-tab--active", currentFilter == KnowledgeFilterType.All);
			filterBases?.EnableInClassList("fish-tab--active", currentFilter == KnowledgeFilterType.BaseAbilities);
			filterEffects?.EnableInClassList("fish-tab--active", currentFilter == KnowledgeFilterType.Effects);

			string search = string.IsNullOrEmpty(searchText) ? null : searchText.ToLowerInvariant();
			int shown = 0;
			int inTab = 0;

			foreach (AbilityEntry entry in entries)
			{
				bool tabMatch = entry.Tab == currentTab;
				if (tabMatch)
				{
					++inTab;
				}

				bool filterMatch = currentTab != AbilityTabType.Knowledge ||
					currentFilter == KnowledgeFilterType.All ||
					entry.Kind == currentFilter;

				bool searchMatch = search == null ||
					(entry.SearchKey != null && entry.SearchKey.Contains(search));

				bool visible = tabMatch && filterMatch && searchMatch;
				if (entry.Root != null)
				{
					entry.Root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
				}
				if (visible)
				{
					++shown;
				}
			}

			if (emptyLabel != null)
			{
				emptyLabel.style.display = shown == 0 ? DisplayStyle.Flex : DisplayStyle.None;
				emptyLabel.text = inTab == 0
					? (currentTab == AbilityTabType.Ability ? ABILITY_EMPTY : KNOWLEDGE_EMPTY)
					: SEARCH_EMPTY;
			}

			/* The header describes what the player is looking at, so both follow the tab. */
			if (countLabel != null)
			{
				countLabel.text = shown.ToString();
			}
			if (subtitleLabel != null)
			{
				subtitleLabel.text = currentTab == AbilityTabType.Ability
					? "Ready to use"
					: "Parts for crafting";
			}

			RestoreSelectedDetails();
		}

		/// <summary>Shows or hides an element without disturbing its layout rules.</summary>
		private static void SetDisplayed(VisualElement element, bool displayed)
		{
			if (element != null)
			{
				element.style.display = displayed ? DisplayStyle.Flex : DisplayStyle.None;
			}
		}

		/// <summary>Writes the status line under the list.</summary>
		private void SetStatus(string text)
		{
			if (statusLabel != null)
			{
				statusLabel.text = text;
			}
		}

		/// <summary>Removes every row.</summary>
		private void ClearEntries()
		{
			foreach (AbilityEntry entry in entries)
			{
				entry.Root?.RemoveFromHierarchy();
			}
			entries.Clear();
			selectedReferenceID = 0;
			ShowDetails(null);
		}
	}
}
