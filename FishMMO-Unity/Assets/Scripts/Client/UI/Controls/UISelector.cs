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
			ClearSlots();
		}

		public void Open(List<ICachedObject> cachedObjects, Action<int> onAccept)
		{
			if (Visible || cachedObjects == null || cachedObjects.Count < 1)
			{
				return;
			}
			this.cachedObjects = cachedObjects;
			UpdateEventSlots();
			this.onAccept = onAccept;
			Show();
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
					if (ButtonSlots[i].gameObject != null)
					{
						Destroy(ButtonSlots[i].gameObject);
					}
				}
				ButtonSlots.Clear();
			}
		}
		private void UpdateEventSlots()
		{
			ClearSlots();

			ButtonSlots = new List<UITooltipButton>();

			if (cachedObjects == null)
			{
				return;
			}
			for (int i = 0; i < cachedObjects.Count; ++i)
			{
				ITooltip cachedObject = cachedObjects[i] as ITooltip;
				if (cachedObject == null)
				{
					continue;
				}

				UITooltipButton eventButton = Instantiate(ButtonPrefab, ButtonParent);
				eventButton.Initialize(i, EventEntry_OnLeftClick, null, cachedObject);
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
				selectedIndex < cachedObjects.Count)
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
			Hide();
		}
	}
}