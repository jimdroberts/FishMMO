using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit abilities panel. Renders the character's abilities, known ability templates, and
	/// known ability events as tabbed icon slots. Hovering shows the entry tooltip; left-clicking a
	/// usable ability picks it up onto the shared drag object so it can be assigned to a hotkey.
	/// </summary>
	/// <remarks>
	/// <para><b>Only the Abilities tab drags.</b> A template bought from a merchant lands on the
	/// Templates tab and is not an ability yet — it becomes one at an Ability Crafter, where its
	/// effects are chosen. The Effects tab holds those effects. Neither can go on the hotkey bar,
	/// and neither could ever be picked up; the defect (issue #247) was that nothing said so. The
	/// slots on those tabs looked identical to usable ones, a click on them did nothing, and the
	/// player concluded dragging was broken.</para>
	///
	/// <para>Three things now say so. The slot itself is marked — a corner badge and a dimmed
	/// icon — so a template reads differently from an ability before anything is touched. The
	/// tooltip ends with what the entry is and where it goes next. And a click that cannot pick
	/// the entry up writes the reason to the status line under the list, where the tab's standing
	/// hint otherwise sits.</para>
	/// </remarks>
	public class UITKAbilities : UITKCharacterControl
	{
		/// <summary>Name of the abilities tab button.</summary>
		private const string ABILITY_TAB_NAME = "ability-tab-abilities";

		/// <summary>Name of the header close button element.</summary>
		private const string CLOSE_BTN_NAME = "close-button";

		/// <summary>Name of the known abilities tab button.</summary>
		private const string KNOWN_TAB_NAME = "ability-tab-known";

		/// <summary>Name of the known ability events tab button.</summary>
		private const string EVENTS_TAB_NAME = "ability-tab-events";

		/// <summary>Name of the abilities entry container.</summary>
		private const string ABILITY_LIST_NAME = "ability-list-abilities";

		/// <summary>Name of the known abilities entry container.</summary>
		private const string KNOWN_LIST_NAME = "ability-list-known";

		/// <summary>Name of the known ability events entry container.</summary>
		private const string EVENTS_LIST_NAME = "ability-list-events";

		/// <summary>USS class applied to each generated ability slot.</summary>
		private const string SLOT_CLASS = "ability-slot";

		/// <summary>USS class applied to an ability slot's icon element.</summary>
		private const string SLOT_ICON_CLASS = "ability-slot__icon";

		/// <summary>USS class marking a slot that holds a template rather than a usable ability.</summary>
		private const string SLOT_TEMPLATE_CLASS = "ability-slot--template";

		/// <summary>USS class marking a slot that holds an ability effect.</summary>
		private const string SLOT_EFFECT_CLASS = "ability-slot--effect";

		/// <summary>USS class applied to the corner badge naming what a non-draggable slot holds.</summary>
		private const string SLOT_BADGE_CLASS = "ability-slot__badge";

		/// <summary>Name of the status line under the list.</summary>
		private const string STATUS_LABEL_NAME = "ability-status";

		/// <summary>Tooltip suffix for a known template.</summary>
		private const string TEMPLATE_TOOLTIP_HINT = "\r\n\r\nAbility template. Take it to an Ability Crafter to craft a usable ability from it. Templates cannot be placed on the hotkey bar.";

		/// <summary>Tooltip suffix for a known effect.</summary>
		private const string EFFECT_TOOLTIP_HINT = "\r\n\r\nAbility effect. Add it to an ability at an Ability Crafter. Effects cannot be placed on the hotkey bar.";

		/// <summary>Standing hint for the Abilities tab.</summary>
		private const string ABILITY_TAB_HINT = "Click an ability to pick it up, then click a hotkey slot.";

		/// <summary>Standing hint for the Templates tab.</summary>
		private const string TEMPLATE_TAB_HINT = "Templates become abilities at an Ability Crafter.";

		/// <summary>Standing hint for the Effects tab.</summary>
		private const string EFFECT_TAB_HINT = "Effects are added to abilities at an Ability Crafter.";

		/// <summary>What the status line says when a template is clicked.</summary>
		private const string TEMPLATE_REFUSAL = "That is a template, not an ability yet. Craft it at an Ability Crafter first.";

		/// <summary>What the status line says when an effect is clicked.</summary>
		private const string EFFECT_REFUSAL = "That is an effect. Add it to an ability at an Ability Crafter.";

		/// <summary>Empty-list wording per tab.</summary>
		private const string ABILITY_EMPTY = "No abilities learned.";
		private const string TEMPLATE_EMPTY = "No ability templates known.";
		private const string EFFECT_EMPTY = "No ability effects known.";

		/// <summary>Name of the shared drag object overlay panel.</summary>
		private const string DRAG_OBJECT_NAME = "UIDragObject";

		/// <summary>Name of the shared tooltip overlay panel.</summary>
		private const string TOOLTIP_NAME = "UITooltip";

		/// <summary>
		/// Visual element and state backing a single ability slot.
		/// </summary>
		private sealed class AbilitySlot
		{
			/// <summary>Root container for the slot.</summary>
			public VisualElement Root;
			/// <summary>Icon element.</summary>
			public VisualElement Icon;
			/// <summary>Icon sprite (used when picking up onto the drag object).</summary>
			public Sprite IconSprite;
			/// <summary>Reference ID of the ability/event.</summary>
			public long ReferenceID;
			/// <summary>Reference button type.</summary>
			public ReferenceButtonType Type;
			/// <summary>Cached tooltip text.</summary>
			public string Tooltip;
			/// <summary>The tab the slot belongs to, which decides whether it can be picked up.</summary>
			public AbilityTabType Tab;
		}

		/// <summary>Usable ability slots currently rendered.</summary>
		private readonly List<AbilitySlot> abilities = new List<AbilitySlot>();
		/// <summary>Known ability template slots currently rendered.</summary>
		private readonly List<AbilitySlot> knownAbilities = new List<AbilitySlot>();
		/// <summary>Known ability event slots currently rendered.</summary>
		private readonly List<AbilitySlot> knownAbilityEvents = new List<AbilitySlot>();

		/// <summary>Status line under the list: the tab's standing hint, or why a click was refused.</summary>
		private Label statusLabel;

		/// <summary>Abilities tab button.</summary>
		private Button abilityTab;
		/// <summary>Known abilities tab button.</summary>
		private Button knownTab;
		/// <summary>Known ability events tab button.</summary>
		private Button eventsTab;
		/// <summary>Abilities entry container.</summary>
		private VisualElement abilityList;
		/// <summary>Known abilities entry container.</summary>
		private VisualElement knownList;
		/// <summary>Known ability events entry container.</summary>
		private VisualElement eventsList;

		/// <summary>The currently active ability tab type.</summary>
		private AbilityTabType currentTab = AbilityTabType.Ability;

		/// <summary>
		/// Queries tab buttons and containers, wires up tab switching, and subscribes to lifecycle events.
		/// </summary>
		public override void OnStarting()
		{
			/* Every slot in these lists is a VisualElement from the tree that was just discarded.
			 * Keeping them would leave the panel rendering nothing while still believing it is
			 * full, and every later add would append to a dead container. */
			abilities.Clear();
			knownAbilities.Clear();
			knownAbilityEvents.Clear();

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
			knownTab = root.Q<Button>(KNOWN_TAB_NAME);
			eventsTab = root.Q<Button>(EVENTS_TAB_NAME);
			abilityList = root.Q(ABILITY_LIST_NAME);
			knownList = root.Q(KNOWN_LIST_NAME);
			eventsList = root.Q(EVENTS_LIST_NAME);
			statusLabel = root.Q<Label>(STATUS_LABEL_NAME);

			if (abilityTab != null)
			{
				abilityTab.clicked += () => SwitchTab(AbilityTabType.Ability);
			}
			if (knownTab != null)
			{
				knownTab.clicked += () => SwitchTab(AbilityTabType.KnownAbility);
			}
			if (eventsTab != null)
			{
				eventsTab.clicked += () => SwitchTab(AbilityTabType.KnownAbilityEvent);
			}

			/* Static event, and OnStarting re-runs on every tree rebuild — remove before add so the
			 * pair is idempotent instead of leaking one handler per rebuild. */
			IPlayerCharacter.OnStopLocalClient -= PlayerCharacter_OnStopLocalClient;
			IPlayerCharacter.OnStopLocalClient += PlayerCharacter_OnStopLocalClient;

			SwitchTab(AbilityTabType.Ability);
		}

		/// <summary>
		/// Unsubscribes from events and clears all slots when the control is destroyed.
		/// </summary>
		public override void OnDestroying()
		{
			IPlayerCharacter.OnStopLocalClient -= PlayerCharacter_OnStopLocalClient;

			UnsubscribeAbilityController();

			ClearAllSlots();

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

		/// <summary>
		/// Drops every ability-controller subscription held on the current character.
		/// </summary>
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
		/// Repopulates all three tabs from the character's current knowledge.
		/// </summary>
		/// <param name="abilityController">The character's ability controller.</param>
		/// <remarks>
		/// The panel is otherwise fed exclusively by the <c>OnAdd*</c> events, which the controller
		/// raises once, on <c>OnStartCharacter</c>. Anything that rebuilds the visual tree after
		/// that point — the first hide/show, a theme reload — left the panel permanently empty,
		/// because the events that would have filled it had already fired.
		/// </remarks>
		private void RebuildFromCharacter(IAbilityController abilityController)
		{
			ClearAllSlots();

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

			SwitchTab(currentTab);
		}

		/// <summary>
		/// Removes the slot for an ability the character no longer knows.
		/// </summary>
		/// <param name="referenceID">The removed ability's reference ID.</param>
		public void RemoveAbility(long referenceID)
		{
			for (int i = abilities.Count - 1; i >= 0; --i)
			{
				if (abilities[i].ReferenceID != referenceID)
				{
					continue;
				}

				abilities[i].Root?.RemoveFromHierarchy();
				abilities.RemoveAt(i);
			}

			SwitchTab(currentTab);
		}

		/// <summary>
		/// Unsubscribes from ability controller events and clears slots before the character is unset.
		/// </summary>
		public override void OnPreUnsetCharacter()
		{
			UnsubscribeAbilityController();

			ClearAllSlots();
		}

		/// <summary>
		/// Clears all slots when quitting to login.
		/// </summary>
		public override void OnQuitToLogin()
		{
			ClearAllSlots();
		}

		/// <summary>
		/// Returns whether ability manipulation is allowed. Blocked while mouse mode is active.
		/// </summary>
		/// <returns>True if manipulation is allowed.</returns>
		private bool CanManipulateAbility()
		{
			return !PlayerInputController.MouseMode;
		}

		/// <summary>
		/// Handles the local client stopping by clearing all slots.
		/// </summary>
		/// <param name="character">The local player character.</param>
		private void PlayerCharacter_OnStopLocalClient(IPlayerCharacter character)
		{
			ClearAllSlots();
		}

		/// <summary>
		/// Adds an ability slot for a usable ability.
		/// </summary>
		/// <param name="ability">The ability to add.</param>
		public void AddAbility(Ability ability)
		{
			if (ability == null)
			{
				return;
			}

			CreateSlot(ability.ID, ability.Template.Icon, ReferenceButtonType.Ability, AbilityTabType.Ability, ability.Tooltip(), abilities, abilityList);
		}

		/// <summary>
		/// Adds a slot for a known ability template.
		/// </summary>
		/// <param name="template">The ability template to add.</param>
		public void AddKnownAbility(BaseAbilityTemplate template)
		{
			if (template == null)
			{
				return;
			}

			CreateSlot(template.ID, template.Icon, ReferenceButtonType.None, AbilityTabType.KnownAbility, template.Tooltip() + TEMPLATE_TOOLTIP_HINT, knownAbilities, knownList);
		}

		/// <summary>
		/// Adds a slot for a known ability event.
		/// </summary>
		/// <param name="abilityEvent">The ability event to add.</param>
		public void AddKnownAbilityEvent(AbilityEvent abilityEvent)
		{
			if (abilityEvent == null)
			{
				return;
			}

			CreateSlot(abilityEvent.ID, abilityEvent.Icon, ReferenceButtonType.None, AbilityTabType.KnownAbilityEvent, abilityEvent.Tooltip() + EFFECT_TOOLTIP_HINT, knownAbilityEvents, eventsList);
		}

		/// <summary>
		/// Builds a single ability slot and adds it to the given container/list.
		/// </summary>
		/// <param name="id">The reference ID.</param>
		/// <param name="icon">The icon sprite.</param>
		/// <param name="buttonType">The reference button type.</param>
		/// <param name="tabType">The owning tab.</param>
		/// <param name="tooltip">The tooltip text.</param>
		/// <param name="slots">The backing slot list.</param>
		/// <param name="container">The visual container.</param>
		private void CreateSlot(long id, Sprite icon, ReferenceButtonType buttonType, AbilityTabType tabType, string tooltip, List<AbilitySlot> slots, VisualElement container)
		{
			if (container == null)
			{
				return;
			}

			VisualElement slotRoot = new VisualElement();
			slotRoot.AddToClassList("fish-slot");
			slotRoot.AddToClassList(SLOT_CLASS);

			VisualElement iconElement = new VisualElement();
			iconElement.AddToClassList(SLOT_ICON_CLASS);
			if (icon != null)
			{
				iconElement.style.backgroundImage = new StyleBackground(icon);
			}
			slotRoot.Add(iconElement);

			/* The mark that tells a template from an ability at a glance. Applied to the slot so
			 * the USS can dim the icon and colour the border, plus a corner badge naming what the
			 * slot holds — the tooltip and status line explain, this is what makes the player look
			 * for an explanation in the first place. */
			string badge = null;
			if (tabType == AbilityTabType.KnownAbility)
			{
				slotRoot.AddToClassList(SLOT_TEMPLATE_CLASS);
				badge = "craft";
			}
			else if (tabType == AbilityTabType.KnownAbilityEvent)
			{
				slotRoot.AddToClassList(SLOT_EFFECT_CLASS);
				badge = "effect";
			}
			if (badge != null)
			{
				Label badgeLabel = new Label(badge);
				badgeLabel.AddToClassList(SLOT_BADGE_CLASS);
				badgeLabel.pickingMode = PickingMode.Ignore;
				slotRoot.Add(badgeLabel);
			}

			AbilitySlot slot = new AbilitySlot
			{
				Root = slotRoot,
				Icon = iconElement,
				IconSprite = icon,
				ReferenceID = id,
				Type = buttonType,
				Tooltip = tooltip,
				Tab = tabType,
			};

			slotRoot.RegisterCallback<PointerEnterEvent>(evt => OnSlotPointerEnter(slot));
			slotRoot.RegisterCallback<PointerLeaveEvent>(evt => OnSlotPointerLeave());
			slotRoot.RegisterCallback<PointerDownEvent>(evt => OnSlotPointerDown(evt, slot));

			container.Add(slotRoot);
			slots.Add(slot);

			slotRoot.style.display = currentTab == tabType ? DisplayStyle.Flex : DisplayStyle.None;
		}

		/// <summary>
		/// Shows the slot's tooltip on hover.
		/// </summary>
		/// <param name="slot">The hovered slot.</param>
		private void OnSlotPointerEnter(AbilitySlot slot)
		{
			if (!string.IsNullOrEmpty(slot.Tooltip) && UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				tooltip.Open(slot.Tooltip, slot.Root);
			}
		}

		/// <summary>
		/// Hides the tooltip when the pointer leaves a slot.
		/// </summary>
		private void OnSlotPointerLeave()
		{
			if (UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				tooltip.Hide();
			}
		}

		/// <summary>
		/// Handles left-click drag pickup for a slot. Mirrors the legacy UIAbilityButton behaviour:
		/// toggles the drag object, picking up usable abilities so they can be dropped on a hotkey.
		/// A slot that cannot be picked up says why instead of doing nothing.
		/// </summary>
		/// <param name="evt">The pointer-down event.</param>
		/// <param name="slot">The clicked slot.</param>
		private void OnSlotPointerDown(PointerDownEvent evt, AbilitySlot slot)
		{
			if (evt.button != 0 || Character == null)
			{
				return;
			}

			if (!UIManager.TryGetTK(DRAG_OBJECT_NAME, out UITKDragObject dragObject))
			{
				return;
			}

			if (dragObject.Visible)
			{
				dragObject.Clear();
				return;
			}

			if (Character.TryGet(out IAbilityController abilityController) &&
				abilityController.KnownAbilities.ContainsKey(slot.ReferenceID))
			{
				dragObject.SetReference(slot.IconSprite, slot.ReferenceID, slot.Type);
				return;
			}

			ExplainRefusedPickup(slot);
		}

		/// <summary>
		/// Writes why a slot could not be picked up. This was silence, and silence read as a
		/// broken drag feature rather than as a different kind of entry.
		/// </summary>
		/// <param name="slot">The slot the player clicked.</param>
		private void ExplainRefusedPickup(AbilitySlot slot)
		{
			switch (slot.Tab)
			{
				case AbilityTabType.KnownAbility:
					SetStatus(TEMPLATE_REFUSAL);
					break;
				case AbilityTabType.KnownAbilityEvent:
					SetStatus(EFFECT_REFUSAL);
					break;
				default:
					/* An Abilities-tab slot the controller no longer knows — removed under the
					 * panel, most likely. Nothing to craft; nothing useful to say beyond that. */
					SetStatus("That ability is no longer available.");
					break;
			}
		}

		/// <summary>
		/// Writes the status line under the list.
		/// </summary>
		private void SetStatus(string text)
		{
			if (statusLabel != null)
			{
				statusLabel.text = text;
			}
		}

		/// <summary>The standing hint for a tab, shown until a click replaces it.</summary>
		private static string TabHint(AbilityTabType tab)
		{
			switch (tab)
			{
				case AbilityTabType.KnownAbility: return TEMPLATE_TAB_HINT;
				case AbilityTabType.KnownAbilityEvent: return EFFECT_TAB_HINT;
				default: return ABILITY_TAB_HINT;
			}
		}

		/// <summary>What an empty tab says. "No abilities learned" over an empty Templates tab is
		/// the wrong sentence.</summary>
		private static string EmptyText(AbilityTabType tab)
		{
			switch (tab)
			{
				case AbilityTabType.KnownAbility: return TEMPLATE_EMPTY;
				case AbilityTabType.KnownAbilityEvent: return EFFECT_EMPTY;
				default: return ABILITY_EMPTY;
			}
		}

		/// <summary>
		/// Switches the visible ability tab.
		/// </summary>
		/// <param name="tab">The tab to display.</param>
		public void SwitchTab(AbilityTabType tab)
		{
			currentTab = tab;

			ShowEntries(abilities, tab == AbilityTabType.Ability);
			ShowEntries(knownAbilities, tab == AbilityTabType.KnownAbility);
			ShowEntries(knownAbilityEvents, tab == AbilityTabType.KnownAbilityEvent);

			SetListVisible(abilityList, tab == AbilityTabType.Ability);
			SetListVisible(knownList, tab == AbilityTabType.KnownAbility);
			SetListVisible(eventsList, tab == AbilityTabType.KnownAbilityEvent);

			/* The header count describes what the player is looking at, so it follows the tab.
			 * Binding it once at startup would leave it reporting the ability list while the
			 * effects tab is open. */
			VisualElement active =
				tab == AbilityTabType.Ability ? abilityList :
				tab == AbilityTabType.KnownAbility ? knownList : eventsList;
			Label empty = Root?.Q<Label>("ability-empty");
			if (empty != null)
			{
				empty.text = EmptyText(tab);
			}
			BindListChrome(
				active,
				Root?.Q<Label>("ability-count"),
				Root?.Q<Label>("ability-subtitle"),
				empty,
				tab == AbilityTabType.Ability ? "ability" : tab == AbilityTabType.KnownAbility ? "template" : "effect",
				tab == AbilityTabType.Ability ? "abilities" : tab == AbilityTabType.KnownAbility ? "templates" : "effects");

			// A tab switch clears any refusal and restores the tab's own hint.
			SetStatus(TabHint(tab));

			SetTabActive(abilityTab, tab == AbilityTabType.Ability);
			SetTabActive(knownTab, tab == AbilityTabType.KnownAbility);
			SetTabActive(eventsTab, tab == AbilityTabType.KnownAbilityEvent);
		}

		/// <summary>
		/// Shows or hides all slots in a list.
		/// </summary>
		/// <param name="slots">The slots to toggle.</param>
		/// <param name="show">Whether to show the slots.</param>
		private void ShowEntries(List<AbilitySlot> slots, bool show)
		{
			for (int i = 0; i < slots.Count; ++i)
			{
				slots[i].Root.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
			}
		}

		/// <summary>
		/// Toggles a container's visibility.
		/// </summary>
		/// <param name="list">The container.</param>
		/// <param name="visible">Whether the container is visible.</param>
		private void SetListVisible(VisualElement list, bool visible)
		{
			if (list != null)
			{
				list.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
			}
		}

		/// <summary>
		/// Applies the active styling class to the selected tab button.
		/// </summary>
		/// <param name="tab">The tab button.</param>
		/// <param name="active">Whether the tab is active.</param>
		private void SetTabActive(Button tab, bool active)
		{
			if (tab != null)
			{
				tab.EnableInClassList("fish-tab--active", active);
			}
		}

		/// <summary>
		/// Clears all ability, known ability, and known ability event slots.
		/// </summary>
		public void ClearAllSlots()
		{
			ClearSlots(abilities);
			ClearSlots(knownAbilities);
			ClearSlots(knownAbilityEvents);
		}

		/// <summary>
		/// Removes all slots in a list from the hierarchy and clears the list.
		/// </summary>
		/// <param name="slots">The slots to clear.</param>
		private void ClearSlots(List<AbilitySlot> slots)
		{
			for (int i = 0; i < slots.Count; ++i)
			{
				slots[i].Root?.RemoveFromHierarchy();
			}
			slots.Clear();
		}
	}
}
