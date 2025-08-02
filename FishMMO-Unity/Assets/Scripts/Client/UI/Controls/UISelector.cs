using System;
using System.Collections.Generic;
using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// UISelector is a control that presents a list of selectable items to the user,
	/// allowing them to choose one option. It manages the creation and destruction
	/// of UI elements representing these options, and handles user input to select
	/// an option and confirm or cancel their selection.
	/// </summary>
	public class UISelector : UIControl
	{
		/// <summary>
		/// Callback invoked when the user accepts a selection.
		/// </summary>
		private Action<int> onAccept;
		/// <summary>
		/// Index of the currently selected item.
		/// </summary>
		private int selectedIndex = -1;
		/// <summary>
		/// List of cached objects available for selection.
		/// </summary>
		private List<ICachedObject> cachedObjects;
		/// <summary>
		/// List of button slots representing selectable options in the UI.
		/// </summary>
		private List<UITooltipButton> ButtonSlots;

		/// <summary>
		/// Parent RectTransform for dynamically created buttons.
		/// </summary>
		public RectTransform ButtonParent;
		/// <summary>
		/// Prefab used to instantiate selectable buttons.
		/// </summary>
		public UITooltipButton ButtonPrefab;

		/// <summary>
		/// Called when the UISelector is being destroyed. Cleans up button slots.
		/// </summary>
		public override void OnDestroying()
		{
			ClearSlots();
		}

		/// <summary>
		/// Opens the selector UI with the provided cached objects and accept callback.
		/// </summary>
		/// <param name="cachedObjects">List of objects to select from.</param>
		/// <param name="onAccept">Callback invoked when a selection is accepted.</param>
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

		/// <summary>
		/// Clears all button slots and destroys their associated GameObjects.
		/// </summary>
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

		/// <summary>
		/// Updates the button slots to match the current cached objects, creating new buttons as needed.
		/// </summary>
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

		/// <summary>
		/// Called when a button is left-clicked. Updates the selected index.
		/// </summary>
		/// <param name="index">Index of the clicked button.</param>
		/// <param name="optionalParams">Optional parameters (unused).</param>
		private void EventEntry_OnLeftClick(int index, object[] optionalParams)
		{
			if (index > -1 && index < ButtonSlots.Count)
			{
				selectedIndex = index;
			}
		}

		/// <summary>
		/// Called when the accept button is clicked. Invokes the accept callback and closes the selector.
		/// </summary>
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

		/// <summary>
		/// Called when the cancel button is clicked. Clears slots and closes the selector.
		/// </summary>
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