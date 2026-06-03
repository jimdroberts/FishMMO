using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit dropdown / context menu. Dynamically builds buttons and toggles and positions
	/// itself at the mouse cursor. Mirrors the legacy UGUI <c>UIDropdown</c> API.
	/// </summary>
	public class UITKDropdown : UITKControl
	{
		private const string DROPDOWN_MENU_NAME = "dropdown-menu";

		/// <summary>
		/// Dictionary mapping button names to their Button instances.
		/// </summary>
		public Dictionary<string, Button> Buttons = new Dictionary<string, Button>();

		/// <summary>
		/// Dictionary mapping toggle names to their Toggle instances.
		/// </summary>
		public Dictionary<string, Toggle> Toggles = new Dictionary<string, Toggle>();

		private VisualElement dropdownMenu;

		/// <summary>
		/// Resolves cached elements and configures the dropdown for outside-click dismissal.
		/// </summary>
		public override void OnStarting()
		{
			if (Root == null)
			{
				return;
			}

			dropdownMenu = Root.Q<VisualElement>(DROPDOWN_MENU_NAME);
			if (dropdownMenu != null)
			{
				dropdownMenu.style.position = Position.Absolute;
			}

			// The root spans the screen so it can detect clicks outside the menu.
			Root.RegisterCallback<PointerDownEvent>(OnRootPointerDown, TrickleDown.TrickleDown);
		}

		/// <summary>
		/// Unregisters the outside-click handler.
		/// </summary>
		public override void OnDestroying()
		{
			if (Root != null)
			{
				Root.UnregisterCallback<PointerDownEvent>(OnRootPointerDown, TrickleDown.TrickleDown);
			}
		}

		/// <summary>
		/// Shows the dropdown and moves it to the current mouse position.
		/// </summary>
		public override void Show()
		{
			base.Show();

			if (dropdownMenu == null || Root == null || Root.panel == null)
			{
				return;
			}

			Mouse mouse = Mouse.current;
			if (mouse != null)
			{
				Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(Root.panel, mouse.position.ReadValue());
				dropdownMenu.style.left = panelPosition.x;
				dropdownMenu.style.top = panelPosition.y;
			}
		}

		/// <summary>
		/// Hides the dropdown and removes all buttons and toggles.
		/// </summary>
		public override void Hide()
		{
			base.Hide();

			ClearItems();
		}

		/// <summary>
		/// Adds a new button to the dropdown with the specified name and click callback.
		/// </summary>
		/// <param name="buttonName">Name of the button.</param>
		/// <param name="onClick">Callback for button click.</param>
		public void AddButton(string buttonName, UnityAction onClick)
		{
			if (dropdownMenu == null || Buttons.ContainsKey(buttonName))
			{
				return;
			}

			Button button = new Button(() =>
			{
				onClick?.Invoke();
				Hide();
			})
			{
				text = buttonName,
				name = buttonName,
			};
			button.AddToClassList("fish-button");
			button.AddToClassList("dropdown-item");

			dropdownMenu.Add(button);
			Buttons.Add(buttonName, button);
		}

		/// <summary>
		/// Adds a new toggle to the dropdown with the specified name and state change callback.
		/// </summary>
		/// <param name="toggleName">Name of the toggle.</param>
		/// <param name="onToggleStateChanged">Callback for toggle state change.</param>
		public void AddToggle(string toggleName, UnityAction<bool> onToggleStateChanged)
		{
			if (dropdownMenu == null || Toggles.ContainsKey(toggleName))
			{
				return;
			}

			Toggle toggle = new Toggle(toggleName)
			{
				name = toggleName,
			};
			toggle.AddToClassList("dropdown-item");
			toggle.RegisterValueChangedCallback((evt) => onToggleStateChanged?.Invoke(evt.newValue));

			dropdownMenu.Add(toggle);
			Toggles.Add(toggleName, toggle);
		}

		/// <summary>
		/// Removes and clears all tracked buttons and toggles from the menu.
		/// </summary>
		private void ClearItems()
		{
			if (dropdownMenu != null)
			{
				dropdownMenu.Clear();
			}
			Buttons.Clear();
			Toggles.Clear();
		}

		/// <summary>
		/// Hides the dropdown when a pointer-down occurs outside the menu bounds.
		/// </summary>
		private void OnRootPointerDown(PointerDownEvent evt)
		{
			if (!Visible || dropdownMenu == null)
			{
				return;
			}

			if (evt.target is VisualElement target && (target == dropdownMenu || dropdownMenu.Contains(target)))
			{
				return;
			}

			Hide();
		}
	}
}
