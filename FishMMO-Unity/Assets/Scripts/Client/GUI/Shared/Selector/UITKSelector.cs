using System;
using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using UnityEngine.UIElements;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit selector. Presents a list of selectable cached objects, lets the user pick one,
	/// and confirms or cancels the selection. Mirrors the legacy UGUI <c>UISelector</c> API.
	/// In UI Toolkit each option row is a plain <see cref="Button"/>, so no separate tooltip-button
	/// class is required.
	/// </summary>
	public class UITKSelector : UITKControl
	{
		private const string BUTTON_PARENT_NAME = "selector-list";
		private const string ACCEPT_BUTTON_NAME = "selector-accept-btn";
		private const string CANCEL_BUTTON_NAME = "selector-cancel-btn";
		private const string SELECTED_CLASS = "selector-item-selected";

		private VisualElement buttonParent;
		private Button acceptButton;
		private Button cancelButton;

		private Action<int> onAccept;
		private int selectedIndex = -1;
		private List<ICachedObject> cachedObjects;
		private readonly List<Button> buttonSlots = new List<Button>();

		/// <summary>
		/// Resolves cached elements and wires the accept/cancel buttons.
		/// </summary>
		public override void OnStarting()
		{
			if (Root == null)
			{
				return;
			}

			buttonParent = Root.Q<VisualElement>(BUTTON_PARENT_NAME);
			acceptButton = Root.Q<Button>(ACCEPT_BUTTON_NAME);
			cancelButton = Root.Q<Button>(CANCEL_BUTTON_NAME);

			if (acceptButton != null)
			{
				acceptButton.clicked += OnClick_Accept;
			}
			if (cancelButton != null)
			{
				cancelButton.clicked += OnClick_Cancel;
			}
		}

		/// <summary>
		/// Clears button slots when the control is destroyed.
		/// </summary>
		public override void OnDestroying()
		{
			ClearSlots();
		}

		/// <summary>
		/// Opens the selector with the provided cached objects and accept callback.
		/// </summary>
		/// <param name="cachedObjects">List of objects to select from.</param>
		/// <param name="onAccept">Callback invoked with the selected object's ID when accepted.</param>
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
		/// Removes all option rows and clears tooltips.
		/// </summary>
		private void ClearSlots()
		{
			if (buttonParent != null)
			{
				buttonParent.Clear();
			}
			buttonSlots.Clear();
		}

		/// <summary>
		/// Rebuilds the option rows from the current cached objects.
		/// </summary>
		private void UpdateEventSlots()
		{
			ClearSlots();

			if (cachedObjects == null || buttonParent == null)
			{
				return;
			}

			for (int i = 0; i < cachedObjects.Count; ++i)
			{
				if (!(cachedObjects[i] is ITooltip tooltipObject))
				{
					continue;
				}

				int index = i;
				Button button = new Button(() => EventEntry_OnLeftClick(index))
				{
					text = tooltipObject.Name,
				};
				button.AddToClassList("fish-button");
				button.AddToClassList("selector-item");

				string tooltipText = tooltipObject.Tooltip();
				button.RegisterCallback<PointerEnterEvent>((evt) =>
				{
					if (!string.IsNullOrEmpty(tooltipText) && UIManager.TryGetTK("UITooltip", out UITKTooltip tooltip))
					{
						tooltip.Open(tooltipText);
					}
				});
				button.RegisterCallback<PointerLeaveEvent>((evt) =>
				{
					if (UIManager.TryGetTK("UITooltip", out UITKTooltip tooltip))
					{
						tooltip.Hide();
					}
				});

				buttonParent.Add(button);
				buttonSlots.Add(button);
			}
		}

		/// <summary>
		/// Updates the selected index and highlights the chosen row.
		/// </summary>
		/// <param name="index">Index of the clicked button.</param>
		private void EventEntry_OnLeftClick(int index)
		{
			if (index < 0 || index >= buttonSlots.Count)
			{
				return;
			}

			if (selectedIndex > -1 && selectedIndex < buttonSlots.Count)
			{
				buttonSlots[selectedIndex].RemoveFromClassList(SELECTED_CLASS);
			}

			selectedIndex = index;
			buttonSlots[selectedIndex].AddToClassList(SELECTED_CLASS);
		}

		/// <summary>
		/// Invokes the accept callback with the selected object's ID and closes the selector.
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
		/// Clears slots and closes the selector without making a selection.
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
