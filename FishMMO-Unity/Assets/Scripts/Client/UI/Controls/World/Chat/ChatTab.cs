using System.Collections.Generic;
using TMPro;
using UnityEngine;
using FishMMO.Shared;
using System;

namespace FishMMO.Client
{
	public class ChatTab : MonoBehaviour
	{
		/// <summary>
		/// Event triggered when this tab is removed.
		/// </summary>
		public Action<ChatTab> OnRemoveTab;

		/// <summary>
		/// The label text component for the tab name.
		/// </summary>
		public TMP_Text Label;

		/// <summary>
		/// The set of active chat channels for this tab. All channels are active by default.
		/// </summary>
		public HashSet<ChatChannel> ActiveChannels = new HashSet<ChatChannel>()
		{
			ChatChannel.Say,
			ChatChannel.World,
			ChatChannel.Region,
			ChatChannel.Party,
			ChatChannel.Guild,
			ChatChannel.Tell,
			ChatChannel.Trade,
			ChatChannel.System,
		};

		/// <summary>
		/// Removes this chat tab from the UI and triggers the OnRemoveTab event.
		/// </summary>
		public void RemoveTab()
		{
			// Notify listeners that this tab is being removed.
			OnRemoveTab?.Invoke(this);
			OnRemoveTab = null;
			// Hide and destroy the tab GameObject.
			this.gameObject.SetActive(false);
			Destroy(this.gameObject);
		}

		/// <summary>
		/// Toggles the visibility of the chat channel picker UI and activates it for this tab if visible.
		/// </summary>
		public void ToggleUIChatChannelPicker()
		{
			// Try to get the channel picker UI and toggle its visibility.
			if (UIManager.TryGet("UIChatChannelPicker", out UIChatChannelPicker channelPicker))
			{
				channelPicker.ToggleVisibility();
				if (channelPicker.Visible)
				{
					// Activate the picker for this tab, passing current channels and position.
					channelPicker.Activate(ActiveChannels, name, transform.position);
				}
			}
		}

		/// <summary>
		/// Toggles the active state of a chat channel for this tab.
		/// </summary>
		/// <param name="channel">The chat channel to toggle.</param>
		/// <param name="value">True to activate, false to deactivate.</param>
		public void ToggleActiveChannel(ChatChannel channel, bool value)
		{
			// If the channel is already active and value is false, remove it. Otherwise, add it if value is true.
			if (ActiveChannels.Contains(channel))
			{
				if (!value)
				{
					ActiveChannels.Remove(channel);
				}
			}
			else if (value)
			{
				ActiveChannels.Add(channel);
			}
		}
	}
}