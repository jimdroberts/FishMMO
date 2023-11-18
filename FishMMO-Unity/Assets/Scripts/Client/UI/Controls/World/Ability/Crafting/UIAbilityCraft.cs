using UnityEngine;
using TMPro;
using System.Collections.Generic;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIAbilityCraft : UIControl
	{
		private const int MAX_CRAFT_EVENT_SLOTS = 10;

		public UIAbilityEntry MainEntry;
		public TMP_Text AbilityDescription; 
		public RectTransform AbilityEventParent;
		public UIAbilityCraftEntry AbilityEventPrefab;

		private List<UIAbilityCraftEntry> EventSlots;

		public override void OnStarting()
		{
			if (MainEntry != null)
			{
				MainEntry.OnLeft += MainEntry_OnLeft;
				MainEntry.OnRight += MainEntry_OnRight;
			}
		}

		public override void OnDestroying()
		{
			if (MainEntry != null)
			{
				MainEntry.OnLeft -= MainEntry_OnLeft;
				MainEntry.OnRight -= MainEntry_OnRight;
			}

			ClearSlots();
		}

		private void MainEntry_OnLeft(int index)
		{
			/*AbilityTemplate template = AbilityTemplate.Get<AbilityTemplate>(abilityID);
			if (template != null)
			{
				SetEventSlots(template.EventSlots);
			}*/
		}

		private void MainEntry_OnRight(int index)
		{
			ClearSlots();
		}

		private void ClearSlots()
		{
			if (EventSlots != null)
			{
				for (int i = 0; i < EventSlots.Count; ++i)
				{
					Destroy(EventSlots[i].gameObject);
				}
				EventSlots.Clear();
			}
		}

		private void SetEventSlots(int count)
		{
			ClearSlots();

			EventSlots = new List<UIAbilityCraftEntry>();

			for (int i = 0; i < count && i < MAX_CRAFT_EVENT_SLOTS; ++i)
			{
				UIAbilityCraftEntry eventButton = Instantiate(AbilityEventPrefab, AbilityEventParent);
				EventSlots.Add(eventButton);
			}
		}

		public void OnCraft()
		{
			// craft it on the server
		}
	}
}