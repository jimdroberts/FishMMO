using FishNet.Transporting;
using UnityEngine;
using TMPro;
using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Client
{
	public class UIAbilityCraft : UICharacterControl
	{
		/// <summary>
		/// The maximum number of event slots allowed for crafting an ability.
		/// </summary>
		private const int MAX_CRAFT_EVENT_SLOTS = 10;

		/// <summary>
		/// The main entry button for the ability to craft.
		/// </summary>
		public UITooltipButton MainEntry;
		/// <summary>
		/// The text field displaying the ability description.
		/// </summary>
		public TMP_Text AbilityDescription;
		/// <summary>
		/// The text field displaying the crafting cost.
		/// </summary>
		public TMP_Text CraftCost;
		/// <summary>
		/// The parent RectTransform for ability event buttons.
		/// </summary>
		public RectTransform AbilityEventParent;
		/// <summary>
		/// The prefab used to instantiate ability event buttons.
		/// </summary>
		public UITooltipButton AbilityEventPrefab;
		/// <summary>
		/// The template for the currency used to craft abilities.
		/// </summary>
		public CharacterAttributeTemplate CurrencyTemplate;

		/// <summary>
		/// The last interactable ID used for crafting.
		/// </summary>
		private long lastInteractableID = 0;
		/// <summary>
		/// Dictionary mapping event slot indices to their tooltip buttons.
		/// </summary>
		private Dictionary<int, UITooltipButton> EventSlots;

		/// <summary>
		/// Called when the client is set. Registers the broadcast handler for ability crafting.
		/// </summary>
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<AbilityCrafterBroadcast>(OnClientAbilityCrafterBroadcastReceived);
		}

		/// <summary>
		/// Called when the client is unset. Unregisters the broadcast handler for ability crafting.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<AbilityCrafterBroadcast>(OnClientAbilityCrafterBroadcastReceived);
		}

		/// <summary>
		/// Called when the UI is starting. Subscribes to main entry button events.
		/// </summary>
		public override void OnStarting()
		{
			if (MainEntry != null)
			{
				MainEntry.OnLeftClick += MainEntry_OnLeftClick;
				MainEntry.OnRightClick += MainEntry_OnRightClick;
			}
		}

		/// <summary>
		/// Called when the UI is being destroyed. Unsubscribes from main entry button events and clears event slots.
		/// </summary>
		public override void OnDestroying()
		{
			if (MainEntry != null)
			{
				MainEntry.OnLeftClick -= MainEntry_OnLeftClick;
				MainEntry.OnRightClick -= MainEntry_OnRightClick;
			}
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
		/// Event handler for left-clicking the main entry. Opens the selector for base abilities.
		/// </summary>
		/// <param name="index">The index of the entry.</param>
		/// <param name="optionalParams">Optional parameters for the click event.</param>
		private void MainEntry_OnLeftClick(int index, object[] optionalParams)
		{
			if (Character != null &&
				Character.TryGet(out IAbilityController abilityController) &&
				UIManager.TryGet("UISelector", out UISelector uiSelector))
			{
				List<ICachedObject> templates = AbilityTemplate.Get<AbilityTemplate>(abilityController.KnownBaseAbilities);

				// remove abilities we already have learned, you must forget an old ability before you can craft it again
				templates.RemoveAll(t => abilityController.KnowsLearnedAbility(t.ID));

				uiSelector.Open(templates, (i) =>
				{
					AbilityTemplate template = AbilityTemplate.Get<AbilityTemplate>(i);
					if (template != null)
					{
						MainEntry.Initialize(Character, template);
						SetEventSlots(template.AdditionalEventSlots);

						// update the main description text
						UpdateMainDescription();
					}
				});
			}
		}

		/// <summary>
		/// Event handler for right-clicking the main entry. Clears the main entry and event slots.
		/// </summary>
		/// <param name="index">The index of the entry.</param>
		/// <param name="optionalParams">Optional parameters for the click event.</param>
		private void MainEntry_OnRightClick(int index, object[] optionalParams)
		{
			MainEntry.Clear();
			ClearSlots();

			// update the main description text
			UpdateMainDescription();
		}

		/// <summary>
		/// Event handler for left-clicking an event entry. Opens the selector for ability events.
		/// </summary>
		/// <param name="index">The index of the event slot.</param>
		/// <param name="optionalParams">Optional parameters for the click event.</param>
		private void EventEntry_OnLeftClick(int index, object[] optionalParams)
		{
			if (EventSlots.ContainsKey(index) &&
				Character != null &&
				Character.TryGet(out IAbilityController abilityController) &&
				UIManager.TryGet("UISelector", out UISelector uiSelector))
			{
				List<ICachedObject> templates = AbilityEvent.Get<AbilityEvent>(abilityController.KnownAbilityEvents);

				// remove duplicate events
				foreach (UITooltipButton button in EventSlots.Values)
				{
					if (button.Tooltip is AbilityTypeOverrideEventType)
					{
						templates.RemoveAll(t => t is AbilityTypeOverrideEventType);
					}
					templates.Remove(button.Tooltip);
				}

				uiSelector.Open(templates, (i) =>
				{
					AbilityEvent template = AbilityEvent.Get<AbilityEvent>(i);
					if (template != null)
					{
						EventSlots[index].Initialize(Character, template);
					}

					// update the main description text
					UpdateMainDescription();
				});
			}
		}

		/// <summary>
		/// Event handler for right-clicking an event entry. Clears the event slot.
		/// </summary>
		/// <param name="index">The index of the event slot.</param>
		/// <param name="optionalParams">Optional parameters for the click event.</param>
		private void EventEntry_OnRightClick(int index, object[] optionalParams)
		{
			if (index > -1 && index < EventSlots.Count)
			{
				EventSlots[index].Clear();

				// update the main description text
				UpdateMainDescription();
			}
		}

		/// <summary>
		/// Updates the main ability description and crafting cost display.
		/// </summary>
		private void UpdateMainDescription()
		{
			if (AbilityDescription == null)
			{
				return;
			}
			if (MainEntry == null ||
				MainEntry.Tooltip == null)
			{
				AbilityDescription.text = "";
				if (CraftCost != null)
				{
					CraftCost.text = "Cost: ";
				}
				return;
			}

			long price = 0;
			AbilityTemplate abilityTemplate = MainEntry.Tooltip as AbilityTemplate;
			if (abilityTemplate != null)
			{
				price = abilityTemplate.Price;
			}
			if (EventSlots != null &&
				EventSlots.Count > 0)
			{
				List<ITooltip> tooltips = new List<ITooltip>();
				foreach (UITooltipButton button in EventSlots.Values)
				{
					if (button.Tooltip == null)
					{
						continue;
					}
					tooltips.Add(button.Tooltip);

					AbilityEvent abilityEvent = button.Tooltip as AbilityEvent;
					if (abilityEvent != null)
					{
						price += abilityEvent.Price;
					}
				}
				AbilityDescription.text = MainEntry.Tooltip.Tooltip(tooltips);
			}
			else
			{
				AbilityDescription.text = MainEntry.Tooltip.Tooltip();
			}
			if (CraftCost != null)
			{
				CraftCost.text = "Cost: " + price.ToString();
			}
		}

		/// <summary>
		/// Clears all event slots from the UI.
		/// </summary>
		private void ClearSlots()
		{
			if (EventSlots != null)
			{
				for (int i = 0; i < EventSlots.Count; ++i)
				{
					if (EventSlots[i] == null)
					{
						continue;
					}
					if (EventSlots[i].gameObject != null)
					{
						Destroy(EventSlots[i].gameObject);
					}
				}
				EventSlots.Clear();
			}
		}

		/// <summary>
		/// Sets up the specified number of event slots for ability crafting.
		/// </summary>
		/// <param name="count">The number of event slots to create.</param>
		private void SetEventSlots(int count)
		{
			ClearSlots();

			EventSlots = new Dictionary<int, UITooltipButton>();

			for (int i = 0; i < count && i < MAX_CRAFT_EVENT_SLOTS; ++i)
			{
				UITooltipButton eventButton = Instantiate(AbilityEventPrefab, AbilityEventParent);
				eventButton.Initialize(i, EventEntry_OnLeftClick, EventEntry_OnRightClick);
				EventSlots.Add(i, eventButton);
			}
		}

		/// <summary>
		/// Handles the crafting action, validates currency, and broadcasts the craft event to the server.
		/// </summary>
		public void OnCraft()
		{
			// craft it on the server
			if (MainEntry == null)
			{
				return;
			}

			AbilityTemplate main = MainEntry.Tooltip as AbilityTemplate;
			if (main == null)
			{
				return;
			}

			long price = main.Price;

			List<int> eventIds = new List<int>();

			if (EventSlots != null)
			{
				for (int i = 0; i < EventSlots.Count; ++i)
				{
					AbilityEvent abilityEvent = EventSlots[i].Tooltip as AbilityEvent;
					if (abilityEvent != null)
					{
						eventIds.Add(abilityEvent.ID);
						price += abilityEvent.Price;
					}
				}
			}

			// do we have enough currency to purchase this?
			if (CurrencyTemplate == null)
			{
				Log.Debug("UIAbilityCraft", "CurrencyTemplate is null.");
				return;
			}
			if (!Character.TryGet(out ICharacterAttributeController attributeController) ||
				!attributeController.TryGetAttribute(CurrencyTemplate, out CharacterAttribute currency) ||
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

			MainEntry.Clear();
			ClearSlots();

			// update the main description text
			UpdateMainDescription();
		}
	}
}