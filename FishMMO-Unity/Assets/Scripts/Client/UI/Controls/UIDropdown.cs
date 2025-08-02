using System.Collections.Generic;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine;
using TMPro;

namespace FishMMO.Client
{
	/// <summary>
	/// Manages the dropdown UI control, allowing the addition of buttons and toggles.
	/// </summary>
	public class UIDropdown : UIControl
	{
		/// <summary>
		/// Prefab used to instantiate new dropdown buttons.
		/// </summary>
		public Button ButtonPrefab;
		/// <summary>
		/// Dictionary mapping button names to their Button instances.
		/// </summary>
		public Dictionary<string, Button> Buttons = new Dictionary<string, Button>();
		/// <summary>
		/// Prefab used to instantiate new dropdown toggles.
		/// </summary>
		public Toggle TogglePrefab;
		/// <summary>
		/// Dictionary mapping toggle names to their Toggle instances.
		/// </summary>
		public Dictionary<string, Toggle> Toggles = new Dictionary<string, Toggle>();

		/// <summary>
		/// Called when the control is starting. Registers Hide to OnLoseFocus event.
		/// </summary>
		public override void OnStarting()
		{
			OnLoseFocus += Hide;
		}

		/// <summary>
		/// Called when the control is being destroyed. Unregisters Hide from OnLoseFocus event.
		/// </summary>
		public override void OnDestroying()
		{
			OnLoseFocus -= Hide;
		}

		/// <summary>
		/// Shows the dropdown and moves it to the current mouse position.
		/// </summary>
		public override void Show()
		{
			base.Show();

			transform.position = Input.mousePosition;
		}

		/// <summary>
		/// Hides the dropdown and destroys all buttons and toggles.
		/// </summary>
		public override void Hide()
		{
			base.Hide();

			// Remove and destroy all buttons
			foreach (Button button in new List<Button>(Buttons.Values))
			{
				button.onClick.RemoveAllListeners();
				Buttons.Remove(button.gameObject.name);
				Destroy(button.gameObject);
			}
			// Remove and destroy all toggles
			foreach (Toggle toggle in new List<Toggle>(Toggles.Values))
			{
				toggle.onValueChanged.RemoveAllListeners();
				Toggles.Remove(toggle.gameObject.name);
				Destroy(toggle.gameObject);
			}
		}

		/// <summary>
		/// Adds a new button to the dropdown with the specified name and click callback.
		/// </summary>
		/// <param name="buttonName">Name of the button.</param>
		/// <param name="onClick">Callback for button click.</param>
		public void AddButton(string buttonName, UnityAction onClick)
		{
			if (Buttons.ContainsKey(buttonName))
			{
				return;
			}
			Button button = Instantiate(ButtonPrefab, transform);
			if (button != null)
			{
				button.onClick.AddListener(onClick);
				button.gameObject.name = buttonName;
				button.gameObject.SetActive(true);
				TMP_Text label = button.GetComponentInChildren<TMP_Text>();
				if (label != null)
				{
					label.text = button.gameObject.name;
				}
				Buttons.Add(button.gameObject.name, button);
			}
		}

		/// <summary>
		/// Adds a new toggle to the dropdown with the specified name and state change callback.
		/// </summary>
		/// <param name="toggleName">Name of the toggle.</param>
		/// <param name="onToggleStateChanged">Callback for toggle state change.</param>
		public void AddToggle(string toggleName, UnityAction<bool> onToggleStateChanged)
		{
			if (Toggles.ContainsKey(toggleName))
			{
				return;
			}
			Toggle toggle = Instantiate(TogglePrefab, transform);
			if (toggle != null)
			{
				toggle.onValueChanged.AddListener(onToggleStateChanged);
				toggle.gameObject.name = toggleName;
				toggle.gameObject.SetActive(true);
				Text label = toggle.GetComponentInChildren<Text>();
				if (label != null)
				{
					label.text = toggle.gameObject.name;
				}
				Toggles.Add(toggle.gameObject.name, toggle);
			}
		}
	}
}