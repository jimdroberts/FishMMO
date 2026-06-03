using System.Collections.Generic;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit ability crafting panel. Mirrors the legacy UGUI <see cref="UIAbilityCraft"/>:
	/// the player selects a base ability for the main entry and optional ability events for each
	/// additional event slot, then crafts the resulting ability for a currency cost. Slot buttons
	/// are built at runtime (replacing the legacy UITooltipButton prefab) and use the shared
	/// <see cref="UITKSelector"/> for picking and <see cref="UITKTooltip"/> for hover info.
	/// </summary>
	public class UITKAbilityCraft : UITKCharacterControl
	{
		/// <summary>The maximum number of event slots allowed for crafting an ability.</summary>
		private const int MAX_CRAFT_EVENT_SLOTS = 10;

		/// <summary>Name of the main ability entry button inside the UXML.</summary>
		private const string MAIN_ENTRY_NAME = "craft-main-entry";

		/// <summary>Name of the ability description label inside the UXML.</summary>
		private const string DESCRIPTION_NAME = "craft-description";

		/// <summary>Name of the crafting cost label inside the UXML.</summary>
		private const string COST_NAME = "craft-cost";

		/// <summary>Name of the event slot container inside the UXML.</summary>
		private const string EVENT_LIST_NAME = "craft-event-list";

		/// <summary>Name of the craft confirmation button inside the UXML.</summary>
		private const string CRAFT_BUTTON_NAME = "craft-confirm-btn";

		/// <summary>Name of the close button inside the UXML.</summary>
		private const string CLOSE_BUTTON_NAME = "craft-close-btn";

		/// <summary>USS class applied to runtime-created event slot buttons.</summary>
		private const string SLOT_CLASS = "craft-slot";

		/// <summary>
		/// The template ID for the currency used to craft abilities.
		/// </summary>
		[TemplateReference(typeof(CharacterAttributeTemplate))]
		public int CurrencyTemplateID;

		/// <summary>The last interactable ID used for crafting.</summary>
		private long lastInteractableID = 0;

		/// <summary>The main ability entry button.</summary>
		private Button mainEntryButton;

		/// <summary>The tooltip data currently assigned to the main entry.</summary>
		private ITooltip mainTooltip;

		/// <summary>The label displaying the ability description.</summary>
		private Label descriptionLabel;

		/// <summary>The label displaying the crafting cost.</summary>
		private Label costLabel;

		/// <summary>The container holding the event slot buttons.</summary>
		private VisualElement eventListContainer;

		/// <summary>Runtime-created event slots.</summary>
		private readonly List<EventSlot> eventSlots = new List<EventSlot>();

		/// <summary>
		/// Represents a single ability event slot: its button and assigned tooltip data.
		/// </summary>
		private sealed class EventSlot
		{
			/// <summary>The slot index.</summary>
			public int Index;

			/// <summary>The slot button element.</summary>
			public Button Button;

			/// <summary>The tooltip data assigned to this slot, if any.</summary>
			public ITooltip Tooltip;
		}

		/// <summary>
		/// Registers the ability crafter broadcast handler when the client is set.
		/// </summary>
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<AbilityCrafterBroadcast>(OnClientAbilityCrafterBroadcastReceived);
		}

		/// <summary>
		/// Unregisters the ability crafter broadcast handler when the client is unset.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<AbilityCrafterBroadcast>(OnClientAbilityCrafterBroadcastReceived);
		}

		/// <summary>
		/// Queries panel elements and wires the main entry, craft, and close buttons.
		/// </summary>
		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			descriptionLabel = root.Q<Label>(DESCRIPTION_NAME);
			costLabel = root.Q<Label>(COST_NAME);
			eventListContainer = root.Q(EVENT_LIST_NAME);

			mainEntryButton = root.Q<Button>(MAIN_ENTRY_NAME);
			if (mainEntryButton != null)
			{
				mainEntryButton.clicked += MainEntry_OnLeftClick;
				mainEntryButton.RegisterCallback<PointerDownEvent>(OnMainEntryPointerDown);
				mainEntryButton.RegisterCallback<PointerEnterEvent>(OnMainEntryPointerEnter);
				mainEntryButton.RegisterCallback<PointerLeaveEvent>(OnSlotPointerLeave);
			}

			Button craftButton = root.Q<Button>(CRAFT_BUTTON_NAME);
			if (craftButton != null)
			{
				craftButton.clicked += OnCraft;
			}

			Button closeButton = root.Q<Button>(CLOSE_BUTTON_NAME);
			if (closeButton != null)
			{
				closeButton.clicked += Hide;
			}

			UpdateMainDescription();
		}

		/// <summary>
		/// Clears all event slots when the UI is being destroyed.
		/// </summary>
		public override void OnDestroying()
		{
			ClearSlots();
		}

		/// <summary>
		/// Handles the broadcast message for ability crafting. Updates the interactable ID and shows the UI.
		/// </summary>
		/// <param name="msg">The broadcast message containing ability crafting info.</param>
		/// <param name="channel">The network channel.</param>
		private void OnClientAbilityCrafterBroadcastReceived(AbilityCrafterBroadcast msg, Channel channel)
		{
			lastInteractableID = msg.InteractableID;
			Show();
		}

		/// <summary>
		/// Opens the selector for base abilities to assign the main entry.
		/// </summary>
		private void MainEntry_OnLeftClick()
		{
			if (Character != null &&
				Character.TryGet(out IAbilityController abilityController) &&
				UIManager.TryGetTK("UISelector", out UITKSelector uiSelector))
			{
				List<ICachedObject> templates = AbilityTemplate.Get<AbilityTemplate>(abilityController.KnownBaseAbilities);

				// remove abilities we already have learned, you must forget an old ability before you can craft it again
				templates.RemoveAll(t => abilityController.KnowsLearnedAbility(t.ID));

				uiSelector.Open(templates, (i) =>
				{
					AbilityTemplate template = AbilityTemplate.Get<AbilityTemplate>(i);
					if (template != null)
					{
						SetMainEntry(template);
						SetEventSlots(template.AdditionalEventSlots);

						// update the main description text
						UpdateMainDescription();
					}
				});
			}
		}

		/// <summary>
		/// Clears the main entry and event slots on right-click.
		/// </summary>
		private void MainEntry_OnRightClick()
		{
			SetMainEntry(null);
			ClearSlots();

			// update the main description text
			UpdateMainDescription();
		}

		/// <summary>
		/// Opens the selector for ability events to assign an event slot.
		/// </summary>
		/// <param name="index">The index of the event slot.</param>
		private void EventEntry_OnLeftClick(int index)
		{
			if (index < 0 ||
				index >= eventSlots.Count)
			{
				return;
			}

			if (Character != null &&
				Character.TryGet(out IAbilityController abilityController) &&
				UIManager.TryGetTK("UISelector", out UITKSelector uiSelector))
			{
				List<ICachedObject> templates = AbilityEvent.Get<AbilityEvent>(abilityController.KnownAbilityEvents);

				// remove duplicate events
				foreach (EventSlot slot in eventSlots)
				{
					if (slot.Tooltip is AbilityTypeOverrideEventType)
					{
						templates.RemoveAll(t => t is AbilityTypeOverrideEventType);
					}
					if (slot.Tooltip is ICachedObject cached)
					{
						templates.Remove(cached);
					}
				}

				uiSelector.Open(templates, (i) =>
				{
					AbilityEvent template = AbilityEvent.Get<AbilityEvent>(i);
					if (template != null)
					{
						SetEventSlot(index, template);
					}

					// update the main description text
					UpdateMainDescription();
				});
			}
		}

		/// <summary>
		/// Clears an event slot on right-click.
		/// </summary>
		/// <param name="index">The index of the event slot.</param>
		private void EventEntry_OnRightClick(int index)
		{
			if (index > -1 &&
				index < eventSlots.Count)
			{
				SetEventSlot(index, null);

				// update the main description text
				UpdateMainDescription();
			}
		}

		/// <summary>
		/// Assigns the main entry tooltip and updates its icon and label.
		/// </summary>
		/// <param name="tooltip">The tooltip data, or null to clear.</param>
		private void SetMainEntry(ITooltip tooltip)
		{
			mainTooltip = tooltip;
			if (mainEntryButton != null)
			{
				SetButtonIcon(mainEntryButton, tooltip != null ? tooltip.Icon : null);
				mainEntryButton.text = tooltip != null ? tooltip.Name : "";
			}
		}

		/// <summary>
		/// Assigns an event slot's tooltip and updates its icon and label.
		/// </summary>
		/// <param name="index">The index of the event slot.</param>
		/// <param name="tooltip">The tooltip data, or null to clear.</param>
		private void SetEventSlot(int index, ITooltip tooltip)
		{
			if (index < 0 ||
				index >= eventSlots.Count)
			{
				return;
			}

			EventSlot slot = eventSlots[index];
			slot.Tooltip = tooltip;
			SetButtonIcon(slot.Button, tooltip != null ? tooltip.Icon : null);
			slot.Button.text = tooltip != null ? tooltip.Name : "";
		}

		/// <summary>
		/// Updates the main ability description and crafting cost display.
		/// </summary>
		private void UpdateMainDescription()
		{
			if (descriptionLabel == null)
			{
				return;
			}

			if (mainTooltip == null)
			{
				descriptionLabel.text = "";
				if (costLabel != null)
				{
					costLabel.text = "Cost: ";
				}
				return;
			}

			long price = 0;
			AbilityTemplate abilityTemplate = mainTooltip as AbilityTemplate;
			if (abilityTemplate != null)
			{
				price = abilityTemplate.Price;
			}

			if (eventSlots.Count > 0)
			{
				List<ITooltip> tooltips = new List<ITooltip>();
				foreach (EventSlot slot in eventSlots)
				{
					if (slot.Tooltip == null)
					{
						continue;
					}
					tooltips.Add(slot.Tooltip);

					AbilityEvent abilityEvent = slot.Tooltip as AbilityEvent;
					if (abilityEvent != null)
					{
						price += abilityEvent.Price;
					}
				}

				if (mainTooltip is AbilityTemplate mainAbilityTemplate)
				{
					descriptionLabel.text = mainAbilityTemplate.TooltipWithEvents(tooltips);
				}
				else
				{
					descriptionLabel.text = mainTooltip.Tooltip();
				}
			}
			else
			{
				descriptionLabel.text = mainTooltip.Tooltip();
			}

			if (costLabel != null)
			{
				costLabel.text = "Cost: " + price.ToString();
			}
		}

		/// <summary>
		/// Removes all event slot buttons from the UI.
		/// </summary>
		private void ClearSlots()
		{
			foreach (EventSlot slot in eventSlots)
			{
				if (slot.Button != null)
				{
					slot.Button.RemoveFromHierarchy();
				}
			}
			eventSlots.Clear();
		}

		/// <summary>
		/// Builds the specified number of event slot buttons for ability crafting.
		/// </summary>
		/// <param name="count">The number of event slots to create.</param>
		private void SetEventSlots(int count)
		{
			ClearSlots();

			if (eventListContainer == null)
			{
				return;
			}

			for (int i = 0; i < count && i < MAX_CRAFT_EVENT_SLOTS; ++i)
			{
				EventSlot slot = new EventSlot()
				{
					Index = i,
				};

				Button button = new Button();
				button.AddToClassList("fish-slot");
				button.AddToClassList(SLOT_CLASS);

				int captured = i;
				button.clicked += () => EventEntry_OnLeftClick(captured);
				button.RegisterCallback<PointerDownEvent>((evt) =>
				{
					if (evt.button == 1)
					{
						EventEntry_OnRightClick(captured);
					}
				});
				button.RegisterCallback<PointerEnterEvent>((evt) => OpenTooltip(slot.Tooltip));
				button.RegisterCallback<PointerLeaveEvent>(OnSlotPointerLeave);

				slot.Button = button;
				eventSlots.Add(slot);
				eventListContainer.Add(button);
			}
		}

		/// <summary>
		/// Validates currency, broadcasts the craft request to the server, and resets the panel.
		/// </summary>
		public void OnCraft()
		{
			AbilityTemplate main = mainTooltip as AbilityTemplate;
			if (main == null)
			{
				return;
			}

			long price = main.Price;

			List<int> eventIds = new List<int>();
			foreach (EventSlot slot in eventSlots)
			{
				AbilityEvent abilityEvent = slot.Tooltip as AbilityEvent;
				if (abilityEvent != null)
				{
					eventIds.Add(abilityEvent.ID);
					price += abilityEvent.Price;
				}
			}

			// do we have enough currency to purchase this?
			if (CurrencyTemplateID == 0)
			{
				Log.Debug("UITKAbilityCraft", "CurrencyTemplateID is not set.");
				return;
			}
			if (Character == null ||
				!Character.TryGet(out ICharacterAttributeController attributeController) ||
				!attributeController.TryGetAttribute(CurrencyTemplateID, out CharacterAttribute currency) ||
				currency.FinalValue < price)
			{
				return;
			}

			AbilityCraftBroadcast abilityAddBroadcast = new AbilityCraftBroadcast()
			{
				InteractableID = lastInteractableID,
				TemplateID = main.ID,
				Events = eventIds,
			};

			Client.Broadcast(abilityAddBroadcast, Channel.Reliable);

			SetMainEntry(null);
			ClearSlots();

			// update the main description text
			UpdateMainDescription();
		}

		/// <summary>
		/// Handles right-click on the main entry button.
		/// </summary>
		/// <param name="evt">The pointer down event.</param>
		private void OnMainEntryPointerDown(PointerDownEvent evt)
		{
			if (evt.button == 1)
			{
				MainEntry_OnRightClick();
			}
		}

		/// <summary>
		/// Opens the tooltip for the main entry on hover.
		/// </summary>
		/// <param name="evt">The pointer enter event.</param>
		private void OnMainEntryPointerEnter(PointerEnterEvent evt)
		{
			OpenTooltip(mainTooltip);
		}

		/// <summary>
		/// Hides the tooltip when the pointer leaves a slot or the main entry.
		/// </summary>
		/// <param name="evt">The pointer leave event.</param>
		private void OnSlotPointerLeave(PointerLeaveEvent evt)
		{
			CloseTooltip();
		}

		/// <summary>
		/// Opens the shared tooltip for the provided tooltip data, if any.
		/// </summary>
		/// <param name="tooltip">The tooltip data to display.</param>
		private void OpenTooltip(ITooltip tooltip)
		{
			if (tooltip != null &&
				UIManager.TryGetTK("UITooltip", out UITKTooltip uiTooltip))
			{
				uiTooltip.Open(tooltip.Tooltip());
			}
		}

		/// <summary>
		/// Hides the shared tooltip.
		/// </summary>
		private void CloseTooltip()
		{
			if (UIManager.TryGetTK("UITooltip", out UITKTooltip uiTooltip))
			{
				uiTooltip.Hide();
			}
		}

		/// <summary>
		/// Sets a button's background image to the supplied icon, or clears it when null.
		/// </summary>
		/// <param name="button">The button to update.</param>
		/// <param name="icon">The icon sprite, or null to clear.</param>
		private static void SetButtonIcon(Button button, Sprite icon)
		{
			if (button == null)
			{
				return;
			}

			if (icon != null)
			{
				button.style.backgroundImage = new StyleBackground(icon);
			}
			else
			{
				button.style.backgroundImage = StyleKeyword.None;
			}
		}
	}
}
