using System;
using System.Collections.Generic;
using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Client
{
	public class UISelector : UIControl
	{
		private Action<int> onAccept;
		private int selectedIndex = -1;
		private List<ICachedObject> cachedObjects;
		private List<UITooltipButton> ButtonSlots;

		public RectTransform ButtonParent;
		public UITooltipButton ButtonPrefab;

		public override void OnStarting()
		{
		}

		public override void OnDestroying()
		{
		}

		public void Open(List<ICachedObject> cachedObjects, Action<int> onAccept)
		{
			if (Visible || cachedObjects == null || cachedObjects.Count < 1)
			{
				return;
			}
			this.cachedObjects = cachedObjects;
			SetEventSlots(cachedObjects.Count);
			this.onAccept = onAccept;
			Visible = true;
		}

		private void ClearSlots()
		{
			if (ButtonSlots != null)
			{
				for (int i = 0; i < ButtonSlots.Count; ++i)
				{
					if (ButtonSlots[i] == null)
					{
						continue;
					}
					ButtonSlots[i].OnLeftClick = null;
					if (ButtonSlots[i].gameObject != null)
					{
						Destroy(ButtonSlots[i].gameObject);
					}
				}
				ButtonSlots.Clear();
			}
		}
		private void SetEventSlots(int count)
		{
			ClearSlots();

			ButtonSlots = new List<UITooltipButton>();

			for (int i = 0; i < count; ++i)
			{
				UITooltipButton eventButton = Instantiate(ButtonPrefab, ButtonParent);
				eventButton.Initialize(i, EventEntry_OnLeftClick, null);
				ButtonSlots.Add(eventButton);
			}
		}

		private void EventEntry_OnLeftClick(int index)
		{
			if (index > -1 && index < ButtonSlots.Count)
			{
				selectedIndex = index;
			}
		}

		public void OnClick_Accept()
		{
			if (selectedIndex > -1 &&
				cachedObjects != null &&
				cachedObjects.Count < selectedIndex)
			{
				onAccept?.Invoke(cachedObjects[selectedIndex].ID);
			}
			OnClick_Cancel();
		}

		public void OnClick_Cancel()
		{
			ClearSlots();
			cachedObjects = null;
			selectedIndex = -1;
			onAccept = null;
			Visible = false;
		}
	}
}