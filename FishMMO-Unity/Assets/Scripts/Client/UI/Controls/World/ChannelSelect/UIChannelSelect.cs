using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using FishNet.Transporting;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// Client UI for displaying and selecting scene channels.
	/// Allows players to view available channels for their current open world scene and switch between them.
	/// Channels are multiple instances of the same scene on the same world server.
	/// </summary>
	public class UIChannelSelect : UICharacterControl
	{
		/// <summary>Parent transform for channel button instances.</summary>
		public RectTransform Content;
		/// <summary>Prefab for individual channel buttons.</summary>
		public ChannelDetailsButton ChannelButtonPrefab;
		/// <summary>Button to confirm the selected channel switch.</summary>
		public Button SwitchButton;
		/// <summary>Minimum seconds between refresh requests.</summary>
		public float RefreshRate = 5.0f;

		private List<ChannelDetailsButton> channelButtons = new List<ChannelDetailsButton>();
		private ChannelDetailsButton selectedChannel;
		private float nextRefresh;

		/// <summary>
		/// Registers the channel list broadcast handler when the client is set.
		/// </summary>
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<SceneChannelListBroadcast>(OnChannelListReceived);
		}

		/// <summary>
		/// Unregisters the channel list broadcast handler when the client is unset.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<SceneChannelListBroadcast>(OnChannelListReceived);
		}

		/// <summary>
		/// Cleans up channel button instances when the UI is destroyed.
		/// </summary>
		public override void OnDestroying()
		{
			DestroyChannelButtons();
		}

		private void Update()
		{
			if (nextRefresh > 0f)
			{
				nextRefresh -= Time.deltaTime;
			}
		}

		/// <summary>
		/// Destroys all channel button instances and resets selection state.
		/// </summary>
		private void DestroyChannelButtons()
		{
			for (int i = 0; i < channelButtons.Count; ++i)
			{
				if (channelButtons[i] != null)
				{
					channelButtons[i].OnChannelSelected -= OnChannelSelected;
					Destroy(channelButtons[i].gameObject);
				}
			}
			channelButtons.Clear();
			selectedChannel = null;
		}

		/// <summary>
		/// Handles the channel list broadcast received from the server.
		/// Creates channel buttons for each available channel and highlights the current one.
		/// </summary>
		/// <param name="msg">The broadcast message containing the list of available channels.</param>
		/// <param name="channel">The network transport channel.</param>
		private void OnChannelListReceived(SceneChannelListBroadcast msg, Channel channel)
		{
			if (Character == null)
			{
				Hide();
				return;
			}

			if (msg.Addresses == null || msg.Addresses.Length == 0)
			{
				Hide();
				return;
			}

			DestroyChannelButtons();
			channelButtons = new List<ChannelDetailsButton>(msg.Addresses.Length);

			int currentHandle = Character.SceneHandle;

			for (int i = 0; i < msg.Addresses.Length; ++i)
			{
				ChannelDetailsButton button = Instantiate(ChannelButtonPrefab, Content);
				button.Initialize(msg.Addresses[i], i);
				button.OnChannelSelected += OnChannelSelected;
				channelButtons.Add(button);

				// Highlight the channel the player is currently on
				if (msg.Addresses[i].SceneHandle == currentHandle)
				{
					button.SetLabelColors(Color.yellow);
				}
			}

			Show();
		}

		/// <summary>
		/// Handles channel button selection, updating visual feedback.
		/// </summary>
		/// <param name="button">The channel button that was selected.</param>
		private void OnChannelSelected(ChannelDetailsButton button)
		{
			if (selectedChannel != null)
			{
				selectedChannel.ResetLabelColor();
			}

			selectedChannel = button;

			if (selectedChannel != null)
			{
				selectedChannel.SetLabelColors(Color.green);
			}
		}

		/// <summary>
		/// Called when the Switch button is clicked. Sends the channel selection to the server.
		/// </summary>
		public void OnClick_SwitchChannel()
		{
			if (Client.IsConnectionReady() && selectedChannel != null)
			{
				Client.Broadcast(new SceneChannelSelectBroadcast
				{
					Channel = selectedChannel.Channel,
				}, Channel.Reliable);
				Hide();
			}
		}

		/// <summary>
		/// Called when the Refresh button is clicked. Requests an updated channel list from the server.
		/// </summary>
		public void OnClick_Refresh()
		{
			if (nextRefresh <= 0f)
			{
				nextRefresh = RefreshRate;
				Client.Broadcast(new RequestSceneChannelListBroadcast(), Channel.Reliable);
			}
		}

		/// <summary>
		/// Called when the Close button is clicked. Hides the channel selection UI.
		/// </summary>
		public void OnClick_Close()
		{
			Hide();
		}

		/// <summary>Called before the character is set.</summary>
		public override void OnPreSetCharacter() { }

		/// <summary>Called after the character is set.</summary>
		public override void OnPostSetCharacter() { }
	}
}