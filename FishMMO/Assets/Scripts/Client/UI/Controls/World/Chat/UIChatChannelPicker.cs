using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Client
{
	public class UIChatChannelPicker : UIControl
	{
		public TMP_InputField input;
		public Toggle channelTogglePrefab;
		public List<Toggle> toggles = new List<Toggle>();

		public override void OnStarting()
		{
			if (channelTogglePrefab != null)
			{
				foreach (string channel in Enum.GetNames(typeof(ChatChannel)))
				{
					Toggle toggle = Instantiate(channelTogglePrefab, transform);
					if (toggle != null)
					{
						toggle.gameObject.SetActive(true);
						Text label = toggle.GetComponentInChildren<Text>();
						if (label != null)
						{
							label.text = channel;
						}
						toggles.Add(toggle);
					}
				}
			}
		}

		public override void OnDestroying()
		{
		}

		/// <summary>
		/// Sets up the toggles for the active channels for the selected tab, sets the input value to the name of the tab and moves the picker to the specified position.
		/// </summary>
		public void Activate(HashSet<ChatChannel> activeChannels, string name, Vector3 position)
		{
			foreach (Toggle toggle in toggles)
			{
				Text label = toggle.GetComponentInChildren<Text>();
				if (!Enum.TryParse(label.text, out ChatChannel channel) || !activeChannels.Contains(channel))
				{
					toggle.SetIsOnWithoutNotify(false);
				}
				else
				{
					toggle.SetIsOnWithoutNotify(true);
				}
			}
			transform.position = position;
			if (input != null)
			{
				input.text = name;
			}
		}

		public void ChangeTabName()
		{
			if (input != null)
			{
				if (!string.IsNullOrWhiteSpace(input.text))
				{
					if (UIManager.TryGet("UIChat", out UIChat chat))
					{
						string currentName = chat.currentTab;
						if (!chat.RenameCurrentTab(input.text))
						{
							// reset the input to the old name if we fail to rename the tab
							input.text = currentName;
						}
					}
				}
			}
		}

		public void SetActiveChannel(Toggle toggle)
		{
			if (toggle != null)
			{
				toggle.gameObject.SetActive(true);
				Text label = toggle.GetComponentInChildren<Text>();
				if (label != null && UIManager.TryGet("UIChat", out UIChat chat))
				{
					if (Enum.TryParse(label.text, out ChatChannel channel))
					{
						chat.ToggleChannel(channel, toggle.isOn);
					}
				}
			}
		}
	}
}