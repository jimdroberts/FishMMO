using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// UI control for picking chat channels and renaming chat tabs.
	/// </summary>
	public class UIChatChannelPicker : UIControl
	{
		/// <summary>
		/// Prefab used to instantiate channel toggle buttons.
		/// </summary>
		public Toggle ChannelTogglePrefab;

		/// <summary>
		/// List of all channel toggle buttons currently displayed.
		/// </summary>
		public List<Toggle> Toggles = new List<Toggle>();

		/// <summary>
		/// Called when the UI is starting. Initializes channel toggles for each chat channel except Command.
		/// </summary>
		public override void OnStarting()
		{
			OnLoseFocus += Hide;

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

		/// <summary>
		/// Called when the UI is being destroyed. Unsubscribes from lose focus event.
		/// </summary>
		public override void OnDestroying()
		{
			OnLoseFocus -= Hide;
		}

		/// <summary>
		/// Sets up the toggles for the active channels for the selected tab, sets the input value to the name of the tab, and moves the picker to the specified position.
		/// </summary>
		/// <param name="activeChannels">The set of active chat channels for the tab.</param>
		/// <param name="name">The name of the tab.</param>
		/// <param name="position">The position to move the picker to.</param>
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

		/// <summary>
		/// Changes the name of the current chat tab to the value in the input field.
		/// Resets the input if renaming fails.
		/// </summary>
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
							// Reset the input to the old name if we fail to rename the tab
							InputField.text = currentName;
						}
					}
				}
			}
		}

		/// <summary>
		/// Sets the active state of a chat channel when its toggle is changed.
		/// </summary>
		/// <param name="toggle">The toggle for the chat channel.</param>
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