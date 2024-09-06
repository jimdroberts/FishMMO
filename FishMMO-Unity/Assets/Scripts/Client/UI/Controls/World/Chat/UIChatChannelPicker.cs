using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIChatChannelPicker : UIControl
	{
		public Toggle ChannelTogglePrefab;
		public List<Toggle> Toggles = new List<Toggle>();

		public override void OnStarting()
		{
			if (ChannelTogglePrefab != null)
			{
				foreach (string channel in Enum.GetNames(typeof(ChatChannel)))
				{
					if (channel.Equals("Command"))
					{
						continue;
					}
					Toggle toggle = Instantiate(ChannelTogglePrefab, transform);
					if (toggle != null)
					{
						toggle.gameObject.SetActive(true);
						Text label = toggle.GetComponentInChildren<Text>();
						if (label != null)
						{
							label.text = channel;
						}
						Toggles.Add(toggle);
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
			foreach (Toggle toggle in Toggles)
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
			if (InputField != null)
			{
				InputField.text = name;
			}
		}

		public void ChangeTabName()
		{
			if (InputField != null)
			{
				if (!string.IsNullOrWhiteSpace(InputField.text))
				{
					if (UIManager.TryGet("UIChat", out UIChat chat))
					{
						string currentName = chat.CurrentTab;
						if (!chat.RenameCurrentTab(InputField.text))
						{
							// reset the input to the old name if we fail to rename the tab
							InputField.text = currentName;
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