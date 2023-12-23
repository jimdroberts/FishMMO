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
		public RectTransform AbilityEventParent;
		public UITooltipButton AbilityEventPrefab;

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
				Client.NetworkManager.ClientManager.RegisterBroadcast<AbilityCraftBroadcast>(OnClientAbilityCraftBroadcastReceived);
			}
			else if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				Client.NetworkManager.ClientManager.UnregisterBroadcast<AbilityCraftBroadcast>(OnClientAbilityCraftBroadcastReceived);
			}
		}

		private void OnClientAbilityCraftBroadcastReceived(AbilityCraftBroadcast msg)
		{
			Show();
		}

		private void MainEntry_OnLeftClick(int index, object[] optionalParams)
		{
			if (Character != null &&
				Character.AbilityController != null &&
				UIManager.TryGet("UISelector", out UISelector uiSelector))
			{
				List<ICachedObject> templates = AbilityTemplate.Get<AbilityTemplate>(Character.AbilityController.KnownTemplates);
				uiSelector.Open(templates, (i) =>
				{
					AbilityTemplate template = AbilityTemplate.Get<AbilityTemplate>(i);
					if (template != null)
					{
						MainEntry.Initialize(Character, template);
						AbilityDescription.text = template.Tooltip();
						SetEventSlots(template.EventSlots);
					}
				});
			}
		}

		private void MainEntry_OnRightClick(int index, object[] optionalParams)
		{
			MainEntry.Clear();
			ClearSlots();
		}

		private void EventEntry_OnLeftClick(int index, object[] optionalParams)
		{
			if (index > -1 && index < EventSlots.Count &&
				Character != null &&
				Character.AbilityController != null &&
				UIManager.TryGet("UISelector", out UISelector uiSelector))
			{
				List<ICachedObject> templates = AbilityEvent.Get<AbilityEvent>(Character.AbilityController.KnownEvents);
				uiSelector.Open(templates, (i) =>
				{
					AbilityEvent template = AbilityEvent.Get<AbilityEvent>(i);
					if (template != null)
					{
						EventSlots[index].Initialize(Character, template);
					}
				});
			}
		}

		private void EventEntry_OnRightClick(int index, object[] optionalParams)
		{
			if (index > -1 && index < EventSlots.Count)
			{
				EventSlots[index].Clear();
			}
		}

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
		}
	}
}