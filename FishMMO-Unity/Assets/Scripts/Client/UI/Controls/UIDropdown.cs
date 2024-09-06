using System.Collections.Generic;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine;
using TMPro;

namespace FishMMO.Client
{
	public class UIDropdown : UIControl
	{
		public Button ButtonPrefab;
		public Dictionary<string, Button> Buttons = new Dictionary<string, Button>();
		public Toggle TogglePrefab;
		public Dictionary<string, Toggle> Toggles = new Dictionary<string, Toggle>();

		public override void OnStarting()
		{
			OnLoseFocus += Hide;
		}

		public override void OnDestroying()
		{
			OnLoseFocus -= Hide;
		}

		public override void Show()
		{
			base.Show();

			transform.position = Input.mousePosition;
		}

		public override void Hide()
		{
			base.Hide();

			foreach (Button button in new List<Button>(Buttons.Values))
			{
				button.onClick.RemoveAllListeners();
				Buttons.Remove(button.gameObject.name);
				Destroy(button.gameObject);
			}
			foreach (Toggle toggle in new List<Toggle>(Toggles.Values))
			{
				toggle.onValueChanged.RemoveAllListeners();
				Toggles.Remove(toggle.gameObject.name);
				Destroy(toggle.gameObject);
			}
		}

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