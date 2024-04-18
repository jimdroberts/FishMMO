using FishNet.Transporting;
using UnityEngine;
using TMPro;
using System.Collections.Generic;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIAbilityCraft : UICharacterControl
	{
		private const int MAX_CRAFT_EVENT_SLOTS = 10;

		public UITooltipButton MainEntry;
		public TMP_Text AbilityDescription;
		public TMP_Text CraftCost;
		public RectTransform AbilityEventParent;
		public UITooltipButton AbilityEventPrefab;

		private int lastInteractableID = 0;
		private List<UITooltipButton> EventSlots;

		public override void OnStarting()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
			if (MainEntry != null)
			{
				MainEntry.OnLeftClick += MainEntry_OnLeftClick;
				MainEntry.OnRightClick += MainEntry_OnRightClick;
			}
		}

		public override void OnDestroying()
		{
			Client.NetworkManager.ClientManager.OnClientConnectionState -= ClientManager_OnClientConnectionState;

			if (MainEntry != null)
			{
				MainEntry.OnLeftClick -= MainEntry_OnLeftClick;
				MainEntry.OnRightClick -= MainEntry_OnRightClick;
			}

			ClearSlots();
		}

		public void ClientManager_OnClientConnectionState(ClientConnectionStateArgs args)
		{
			if (args.ConnectionState == LocalConnectionState.Started)
			{
				Client.NetworkManager.ClientManager.RegisterBroadcast<AbilityCrafterBroadcast>(OnClientAbilityCrafterBroadcastReceived);
			}
			else if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				Client.NetworkManager.ClientManager.UnregisterBroadcast<AbilityCrafterBroadcast>(OnClientAbilityCrafterBroadcastReceived);
			}
		}

		private void OnClientAbilityCrafterBroadcastReceived(AbilityCrafterBroadcast msg, Channel channel)
		{
			lastInteractableID = msg.interactableID;
			Show();
		}

		private void MainEntry_OnLeftClick(int index, object[] optionalParams)
		{
			if (Character != null &&
				Character.TryGet(out IAbilityController abilityController) &&
				UIManager.TryGet("UISelector", out UISelector uiSelector))
			{
				List<ICachedObject> templates = AbilityTemplate.Get<AbilityTemplate>(abilityController.KnownBaseAbilities);
				uiSelector.Open(templates, (i) =>
				{
					AbilityTemplate template = AbilityTemplate.Get<AbilityTemplate>(i);
					if (template != null)
					{
						MainEntry.Initialize(Character, template);
						SetEventSlots(template.EventSlots);

						// update the main description text
						UpdateMainDescription();
					}
				});
			}
		}

		private void MainEntry_OnRightClick(int index, object[] optionalParams)
		{
			MainEntry.Clear();
			ClearSlots();

			// update the main description text
			UpdateMainDescription();
		}

		private void EventEntry_OnLeftClick(int index, object[] optionalParams)
		{
			if (index > -1 && index < EventSlots.Count &&
				Character != null &&
				Character.TryGet(out IAbilityController abilityController) &&
				UIManager.TryGet("UISelector", out UISelector uiSelector))
			{
				List<ICachedObject> templates = AbilityEvent.Get<AbilityEvent>(abilityController.KnownEvents);
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

		private void EventEntry_OnRightClick(int index, object[] optionalParams)
		{
			if (index > -1 && index < EventSlots.Count)
			{
				EventSlots[index].Clear();

				// update the main description text
				UpdateMainDescription();
			}
		}

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
				foreach (UITooltipButton button in EventSlots)
				{
					if (button.Tooltip == null)
					{
						continue;
					}
					tooltips.Add(button.Tooltip);

					AbilityEvent eventTemplate = button.Tooltip as AbilityEvent;
					if (eventTemplate != null)
					{
						price += eventTemplate.Price;
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

		private void ClearSlots()
		{
			lastInteractableID = 0;
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

		private void SetEventSlots(int count)
		{
			ClearSlots();

			EventSlots = new List<UITooltipButton>();

			for (int i = 0; i < count && i < MAX_CRAFT_EVENT_SLOTS; ++i)
			{
				UITooltipButton eventButton = Instantiate(AbilityEventPrefab, AbilityEventParent);
				eventButton.Initialize(i, EventEntry_OnLeftClick, EventEntry_OnRightClick);
				EventSlots.Add(eventButton);
			}
		}

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
					AbilityEvent template = EventSlots[i].Tooltip as AbilityEvent;
					if (template != null)
					{
						eventIds.Add(template.ID);
						price += template.Price;
					}
				}
			}

			if (Character.Currency.Value < price)
			{
				return;
			}

			AbilityCraftBroadcast abilityAddBroadcast = new AbilityCraftBroadcast()
			{
				interactableID = lastInteractableID,
				templateID = main.ID,
				events = eventIds,
			};

			Client.Broadcast(abilityAddBroadcast, Channel.Reliable);

			MainEntry.Clear();
			ClearSlots();

			// update the main description text
			UpdateMainDescription();
		}
	}
}