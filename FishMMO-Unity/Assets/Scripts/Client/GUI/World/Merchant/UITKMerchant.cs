using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit merchant panel. Replaces the legacy UGUI <see cref="UIMerchant"/>:
	/// receives the merchant template via a server broadcast and renders its items, abilities, and
	/// ability events as tabbed entry slots. Hovering shows the entry tooltip; Ctrl+Left click sends a
	/// purchase request. The <see cref="MerchantPurchaseBroadcast"/> is preserved verbatim.
	/// </summary>
	public class UITKMerchant : UITKCharacterControl
	{
		/// <summary>Name of the items tab button.</summary>
		private const string ITEMS_TAB_NAME = "merchant-tab-items";

		/// <summary>Name of the abilities tab button.</summary>
		private const string ABILITIES_TAB_NAME = "merchant-tab-abilities";

		/// <summary>Name of the ability events tab button.</summary>
		private const string EVENTS_TAB_NAME = "merchant-tab-events";

		/// <summary>Name of the items entry container.</summary>
		private const string ITEMS_LIST_NAME = "merchant-items";

		/// <summary>Name of the abilities entry container.</summary>
		private const string ABILITIES_LIST_NAME = "merchant-abilities";

		/// <summary>Name of the ability events entry container.</summary>
		private const string EVENTS_LIST_NAME = "merchant-events";

		/// <summary>USS class applied to each generated entry slot.</summary>
		private const string ENTRY_CLASS = "merchant-entry";

		/// <summary>USS class applied to an entry's icon element.</summary>
		private const string ENTRY_ICON_CLASS = "merchant-entry__icon";

		/// <summary>USS class applied to an entry's name label.</summary>
		private const string ENTRY_NAME_CLASS = "merchant-entry__name";

		/// <summary>Tooltip hint appended to entry tooltips.</summary>
		private const string PURCHASE_HINT = "\r\n\r\nCtrl+Left Mouse Button to purchase.";

		/// <summary>Name of the shared UGUI tooltip overlay.</summary>
		private const string TOOLTIP_NAME = "UITooltip";

		private Button itemsTab;
		private Button abilitiesTab;
		private Button eventsTab;
		private VisualElement itemsList;
		private VisualElement abilitiesList;
		private VisualElement eventsList;

		private long lastMerchantID;
		private int currentTemplateID;
		private MerchantTabType currentTab = MerchantTabType.Item;

		/// <summary>
		/// Queries the tab buttons and entry containers and wires up tab switching.
		/// </summary>
		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			itemsTab = root.Q<Button>(ITEMS_TAB_NAME);
			abilitiesTab = root.Q<Button>(ABILITIES_TAB_NAME);
			eventsTab = root.Q<Button>(EVENTS_TAB_NAME);
			itemsList = root.Q(ITEMS_LIST_NAME);
			abilitiesList = root.Q(ABILITIES_LIST_NAME);
			eventsList = root.Q(EVENTS_LIST_NAME);

			if (itemsTab != null)
			{
				itemsTab.clicked += () => SwitchTab(MerchantTabType.Item);
			}
			if (abilitiesTab != null)
			{
				abilitiesTab.clicked += () => SwitchTab(MerchantTabType.Ability);
			}
			if (eventsTab != null)
			{
				eventsTab.clicked += () => SwitchTab(MerchantTabType.AbilityEvent);
			}
		}

		/// <summary>
		/// Registers the merchant broadcast handler when the client is set.
		/// </summary>
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<MerchantBroadcast>(OnClientMerchantBroadcastReceived);
		}

		/// <summary>
		/// Unregisters the merchant broadcast handler when the client is unset.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<MerchantBroadcast>(OnClientMerchantBroadcastReceived);
		}

		/// <summary>
		/// Clears all entry slots when the control is destroyed.
		/// </summary>
		public override void OnDestroying()
		{
			ClearAll();
		}

		/// <summary>
		/// Handles the merchant broadcast: builds entry slots for each tab and shows the first populated tab.
		/// </summary>
		/// <param name="msg">The merchant broadcast.</param>
		/// <param name="channel">The network channel.</param>
		private void OnClientMerchantBroadcastReceived(MerchantBroadcast msg, Channel channel)
		{
			lastMerchantID = msg.InteractableID;
			currentTemplateID = msg.TemplateID;

			MerchantTemplate template = MerchantTemplate.Get<MerchantTemplate>(currentTemplateID);
			if (template == null)
			{
				return;
			}

			int abilityCount = BuildEntries(abilitiesList, template.Abilities, MerchantTabType.Ability);
			int eventCount = BuildEntries(eventsList, template.AbilityEvents, MerchantTabType.AbilityEvent);
			int itemCount = BuildEntries(itemsList, template.Items, MerchantTabType.Item);

			SetTabEnabled(abilitiesTab, abilityCount > 0);
			SetTabEnabled(eventsTab, eventCount > 0);
			SetTabEnabled(itemsTab, itemCount > 0);

			if (abilityCount > 0)
			{
				SwitchTab(MerchantTabType.Ability);
				Show();
			}
			else if (eventCount > 0)
			{
				SwitchTab(MerchantTabType.AbilityEvent);
				Show();
			}
			else if (itemCount > 0)
			{
				SwitchTab(MerchantTabType.Item);
				Show();
			}
			else
			{
				Hide();
			}
		}

		/// <summary>
		/// Builds entry slots for a list of templates into the given container.
		/// </summary>
		/// <typeparam name="T">A tooltip-providing template type.</typeparam>
		/// <param name="container">The container to populate.</param>
		/// <param name="entries">The source entries.</param>
		/// <param name="tab">The tab the entries belong to.</param>
		/// <returns>The number of entries created.</returns>
		private int BuildEntries<T>(VisualElement container, List<T> entries, MerchantTabType tab) where T : class, ITooltip
		{
			if (container == null)
			{
				return 0;
			}

			container.Clear();

			if (entries == null)
			{
				return 0;
			}

			int created = 0;
			for (int i = 0; i < entries.Count; ++i)
			{
				ITooltip entry = entries[i];
				if (entry == null)
				{
					continue;
				}

				int index = i;
				CreateEntry(container, entry, tab, index);
				++created;
			}
			return created;
		}

		/// <summary>
		/// Creates a single merchant entry slot with icon, name, tooltip, and purchase handling.
		/// </summary>
		/// <param name="container">The container to add the slot to.</param>
		/// <param name="entry">The tooltip entry.</param>
		/// <param name="tab">The tab the entry belongs to.</param>
		/// <param name="index">The entry index within its tab list.</param>
		private void CreateEntry(VisualElement container, ITooltip entry, MerchantTabType tab, int index)
		{
			VisualElement slot = new VisualElement();
			slot.AddToClassList(ENTRY_CLASS);

			VisualElement icon = new VisualElement();
			icon.AddToClassList(ENTRY_ICON_CLASS);
			if (entry.Icon != null)
			{
				icon.style.backgroundImage = new StyleBackground(entry.Icon);
			}
			slot.Add(icon);

			Label name = new Label(entry.Name);
			name.AddToClassList(ENTRY_NAME_CLASS);
			slot.Add(name);

			slot.RegisterCallback<PointerEnterEvent>(evt => OnEntryPointerEnter(entry));
			slot.RegisterCallback<PointerLeaveEvent>(evt => OnEntryPointerLeave());
			slot.RegisterCallback<PointerDownEvent>(evt => OnEntryPointerDown(evt, tab, index));

			container.Add(slot);
		}

		/// <summary>
		/// Shows the entry tooltip on hover.
		/// </summary>
		/// <param name="entry">The hovered entry.</param>
		private void OnEntryPointerEnter(ITooltip entry)
		{
			if (UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				tooltip.Open(entry.Tooltip() + PURCHASE_HINT);
			}
		}

		/// <summary>
		/// Hides the entry tooltip when the pointer leaves.
		/// </summary>
		private void OnEntryPointerLeave()
		{
			if (UIManager.TryGetTK(TOOLTIP_NAME, out UITKTooltip tooltip))
			{
				tooltip.Hide();
			}
		}

		/// <summary>
		/// Sends a purchase request when an entry is Ctrl+Left clicked.
		/// </summary>
		/// <param name="evt">The pointer-down event.</param>
		/// <param name="tab">The tab the entry belongs to.</param>
		/// <param name="index">The entry index within its tab list.</param>
		private void OnEntryPointerDown(PointerDownEvent evt, MerchantTabType tab, int index)
		{
			if (evt.button != 0 || !evt.ctrlKey || Character == null)
			{
				return;
			}

			Client.Broadcast(new MerchantPurchaseBroadcast()
			{
				InteractableID = lastMerchantID,
				ID = currentTemplateID,
				Index = index,
				Type = tab,
			}, Channel.Reliable);
		}

		/// <summary>
		/// Switches the visible merchant tab.
		/// </summary>
		/// <param name="tab">The tab to display.</param>
		private void SwitchTab(MerchantTabType tab)
		{
			currentTab = tab;

			SetListVisible(itemsList, tab == MerchantTabType.Item);
			SetListVisible(abilitiesList, tab == MerchantTabType.Ability);
			SetListVisible(eventsList, tab == MerchantTabType.AbilityEvent);

			SetTabActive(itemsTab, tab == MerchantTabType.Item);
			SetTabActive(abilitiesTab, tab == MerchantTabType.Ability);
			SetTabActive(eventsTab, tab == MerchantTabType.AbilityEvent);
		}

		/// <summary>
		/// Toggles an entry container's visibility and layout participation.
		/// </summary>
		/// <param name="list">The container.</param>
		/// <param name="visible">Whether the container should be visible.</param>
		private void SetListVisible(VisualElement list, bool visible)
		{
			if (list != null)
			{
				list.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
			}
		}

		/// <summary>
		/// Toggles a tab button's enabled state and layout participation.
		/// </summary>
		/// <param name="tab">The tab button.</param>
		/// <param name="enabled">Whether the tab should be shown.</param>
		private void SetTabEnabled(Button tab, bool enabled)
		{
			if (tab != null)
			{
				tab.style.display = enabled ? DisplayStyle.Flex : DisplayStyle.None;
			}
		}

		/// <summary>
		/// Applies the active styling class to the selected tab button.
		/// </summary>
		/// <param name="tab">The tab button.</param>
		/// <param name="active">Whether the tab is the active tab.</param>
		private void SetTabActive(Button tab, bool active)
		{
			if (tab != null)
			{
				tab.EnableInClassList("fish-tab--active", active);
			}
		}

		/// <summary>
		/// Clears all entry containers and resets merchant state.
		/// </summary>
		private void ClearAll()
		{
			lastMerchantID = 0;
			itemsList?.Clear();
			abilitiesList?.Clear();
			eventsList?.Clear();
		}
	}
}
