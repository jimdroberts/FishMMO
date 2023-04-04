using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Client
{
	public class ChatTab : MonoBehaviour
	{
		public TMP_Text label;

		// all tabs are active by default
		public HashSet<ChatChannel> activeChannels = new HashSet<ChatChannel>()
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

		public void ToggleUIChatChannelPicker()
		{
			if (UIManager.TryGet("UIChatChannelPicker", out UIChatChannelPicker channelPicker))
			{
				channelPicker.visible = !channelPicker.visible;
				if (channelPicker.visible)
				{
					channelPicker.Activate(activeChannels, name, transform.position);
				}
			}
		}

		public void ToggleActiveChannel(ChatChannel channel, bool value)
		{
			if (activeChannels.Contains(channel))
			{
				if (!value)
				{
					activeChannels.Remove(channel);
				}
			}
			else if (value)
			{
				activeChannels.Add(channel);
			}
		}
	}
}