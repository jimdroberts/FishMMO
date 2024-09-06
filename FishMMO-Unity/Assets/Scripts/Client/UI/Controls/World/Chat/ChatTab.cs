using System.Collections.Generic;
using TMPro;
using UnityEngine;
using FishMMO.Shared;
using System;

namespace FishMMO.Client
{
	public class ChatTab : MonoBehaviour
	{
		public Action<ChatTab> OnRemoveTab;

		public TMP_Text Label;

		// all tabs are active by default
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

		public void RemoveTab()
		{
			OnRemoveTab?.Invoke(this);
			OnRemoveTab = null;
			this.gameObject.SetActive(false);
			Destroy(this.gameObject);
		}

		public void ToggleUIChatChannelPicker()
		{
			if (UIManager.TryGet("UIChatChannelPicker", out UIChatChannelPicker channelPicker))
			{
				channelPicker.ToggleVisibility();
				if (channelPicker.Visible)
				{
					channelPicker.Activate(ActiveChannels, name, transform.position);
				}
			}
		}

		public void ToggleActiveChannel(ChatChannel channel, bool value)
		{
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