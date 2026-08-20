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
		/// Whether the next channel list to arrive was asked for by the player.
		/// </summary>
		/// <remarks>
		/// The server pushes a channel list unprompted when a character finishes loading, and
		/// this control used to <c>Show()</c> on any list it received — so the channel picker
		/// opened itself over the world on every single login. Only a list the player asked for
		/// opens the window; an unsolicited one just fills it in, ready for whenever they do.
		/// </remarks>
		private bool listRequested;

		/// <summary>
		/// Seconds a player-initiated request stays eligible to open the window.
		/// </summary>
		/// <remarks>
		/// The server answers a channel-list request only when it has something to send: an
		/// unavailable database, or a scene with no other instances, produces no reply at all. A
		/// latch with no deadline therefore stayed armed indefinitely after such a request, and
		/// the next list the server pushed on its own — one arrives whenever a character finishes
		/// loading — opened the picker over the world, which is the behaviour the latch exists to
		/// prevent.
		/// </remarks>
		private const float RequestedListTimeout = 15.0f;

		/// <summary>Time left on <see cref="listRequested"/>.</summary>
		private float requestedListExpiry;

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

			if (listRequested)
			{
				requestedListExpiry -= Time.deltaTime;
				if (requestedListExpiry <= 0f)
				{
					listRequested = false;
				}
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
				listRequested = false;
				Hide();
				return;
			}

			if (msg.Addresses == null || msg.Addresses.Length == 0)
			{
				/* The server always answers a list request now, including with nothing. If the
				 * player asked, say so — closing a window they just opened, with no explanation,
				 * is the same dead end as never replying at all. An unsolicited empty list (sent
				 * on character load) just closes. */
				bool playerAsked = listRequested;
				listRequested = false;
				DestroyChannelButtons();
				Hide();

				if (playerAsked && UIManager.TryGet("UIDialogBox", out UIDialogBox dialogBox))
				{
					dialogBox.Open("There are no other channels available for this scene.");
				}
				return;
			}

			DestroyChannelButtons();
			channelButtons = new List<ChannelDetailsButton>(msg.Addresses.Length);

			/* The server tells us which channel we are on; the character cannot.
			 *
			 * IPlayerCharacter.SceneHandle is server-side state and is never replicated, so the
			 * client's copy is always zero — this comparison could not match any real channel,
			 * and the player was shown a list of interchangeable-looking channels with no
			 * indication of where they already were. */
			long currentHandle = msg.CurrentSceneHandle;

			for (int i = 0; i < msg.Addresses.Length; ++i)
			{
				ChannelDetailsButton button = Instantiate(ChannelButtonPrefab, Content);
				button.Initialize(msg.Addresses[i], i);
				button.OnChannelSelected += OnChannelSelected;
				channelButtons.Add(button);

				// Highlight the channel the player is currently on
				if (currentHandle != 0 && msg.Addresses[i].SceneHandle == currentHandle)
				{
					button.SetLabelColors(Color.yellow);
				}
			}

			// Only a list the player asked for opens the window; see listRequested.
			if (listRequested)
			{
				listRequested = false;
				Show();
			}
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
				listRequested = true;
				requestedListExpiry = RequestedListTimeout;
				Client.Broadcast(new RequestSceneChannelListBroadcast(), Channel.Reliable);
			}
		}

		/// <summary>
		/// Opens the channel picker and asks the server for a current list.
		/// </summary>
		/// <remarks>
		/// The entry point for whatever opens this control — a menu button, a hotkey — so the
		/// player sees live channel populations rather than whatever was cached at login.
		/// </remarks>
		public void OnClick_Open()
		{
			listRequested = true;
			requestedListExpiry = RequestedListTimeout;
			Show();
			OnClick_Refresh();
		}

		/// <summary>
		/// Called when the Close button is clicked. Hides the channel selection UI.
		/// </summary>
		public void OnClick_Close()
		{
			listRequested = false;
			Hide();
		}

		/// <summary>Called before the character is set.</summary>
		public override void OnPreSetCharacter() { }

		/// <summary>Called after the character is set.</summary>
		public override void OnPostSetCharacter() { }
	}
}