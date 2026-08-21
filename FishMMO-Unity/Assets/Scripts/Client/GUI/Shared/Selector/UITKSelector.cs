using System;
using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using UnityEngine.UIElements;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit selector. Presents a list of selectable cached objects, lets the user pick one,
	/// and confirms or cancels the selection. Each option row is a plain <see cref="Button"/>, so
	/// no separate tooltip-button class is required.
	/// </summary>
	public class UITKSelector : UITKControl
	{
		/// <summary>Draw order tier for this panel. See <see cref="UITKPanelLayer"/>.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.Popup;

		/// <summary>
		/// Name of the selector list container element.
		/// </summary>
		private const string BUTTON_PARENT_NAME = "selector-list";
		/// <summary>
		/// Name of the accept button element.
		/// </summary>
		private const string ACCEPT_BUTTON_NAME = "selector-accept-btn";
		/// <summary>
		/// Name of the cancel button element.
		/// </summary>
		private const string CANCEL_BUTTON_NAME = "selector-cancel-btn";
		/// <summary>
		/// USS class applied to the currently selected item.
		/// </summary>
		private const string SELECTED_CLASS = "fish-row--selected";

		/// <summary>
		/// The container element for the option buttons.
		/// </summary>
		private VisualElement buttonParent;
		/// <summary>
		/// The accept button.
		/// </summary>
		private Button acceptButton;
		/// <summary>
		/// The cancel button.
		/// </summary>
		private Button cancelButton;

		/// <summary>
		/// Callback invoked with the selected object's ID when accepted.
		/// </summary>
		private Action<int> onAccept;
		/// <summary>
		/// Index of the currently selected item, or -1 if none.
		/// </summary>
		private int selectedIndex = -1;
		/// <summary>
		/// List of cached objects available for selection.
		/// </summary>
		private List<ICachedObject> cachedObjects;
		/// <summary>
		/// Pool of button elements for each selectable option.
		/// </summary>
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
				/* A row in a list, not a button in a toolbar: .fish-row gives it the shared
				 * hover and selection treatment every other list in the game uses, where
				 * .fish-button would have made a stack of separate-looking controls. */
				button.AddToClassList("fish-row");
				button.AddToClassList("fish-row__name");
				button.AddToClassList("selector-entry");

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
