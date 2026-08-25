using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using FishNet.Transporting;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit scene-channel picker. Lists the other instances ("channels") of the open-world
	/// scene the character is standing in, and asks the server to move the character to the one
	/// the player chooses.
	/// </summary>
	/// <remarks>
	/// Distinct from <see cref="UITKChatChannelPicker"/>, which selects a chat channel and has
	/// nothing to do with scene instances.
	/// <para>
	/// The list is a MODEL (<see cref="channels"/>, plain data) rendered into a VIEW that is
	/// rebuilt from scratch on every open. <c>UIDocument</c> re-clones the UXML each time it is
	/// enabled, so anything written into elements before <see cref="UITKControl.Show"/> belongs to
	/// a tree that is discarded microseconds later — the same rule <see cref="UITKParty"/>
	/// documents at length.
	/// </para>
	/// <para>
	/// <b>Every open asks the server.</b> A channel list is a population snapshot that goes stale
	/// in seconds, and the server has no way to push an update: it answers
	/// <see cref="RequestSceneChannelListBroadcast"/> and nothing else. Rendering a cached list
	/// would show the player capacities that no longer exist and then refuse the switch they made
	/// on the strength of them.
	/// </para>
	/// <para>
	/// <b>The wait is bounded.</b> The server debounces list requests per connection and drops
	/// one that arrives while another is in flight, without replying — so a request is not
	/// guaranteed an answer, and a picker that waited forever would present an empty window with
	/// no explanation. See <see cref="RequestTimeoutSeconds"/>.
	/// </para>
	/// </remarks>
	public class UITKSceneChannel : UITKCharacterControl
	{
		/// <summary>
		/// Draw order tier for this panel. See <see cref="UITKPanelLayer"/>.
		/// </summary>
		/// <remarks>
		/// UI Toolkit orders drawing <em>and</em> input by sorting order alone, so a panel opened
		/// from the game menu has to clear it: at the default <c>Window</c> tier this would appear
		/// underneath the menu that opened it and receive no pointer events at all.
		/// <para>
		/// <see cref="UITKPanelLayer.Popup"/> rather than <see cref="UITKPanelLayer.Settings"/>,
		/// even though Options is opened from the same place. Panels sharing a tier fall back to
		/// the order they happened to register in, which is scene load order — and Options lives in
		/// ClientPreboot while this lives in ClientWorldGUI, so with both open the winner would be
		/// decided by which scene loaded first. Popup is the tier the chat channel picker and the
		/// other choosers already use, and nothing is raised from inside this panel that would need
		/// to sit above it.
		/// </para>
		/// </remarks>
		protected override UITKPanelLayer Layer => UITKPanelLayer.Popup;

		/// <summary>Name of the container that holds the generated channel rows.</summary>
		private const string CHANNEL_LIST_NAME = "scenechannel-list";

		/// <summary>Name of the header subtitle label.</summary>
		private const string SUBTITLE_NAME = "scenechannel-subtitle";

		/// <summary>Name of the header badge showing how many channels are listed.</summary>
		private const string COUNT_NAME = "scenechannel-count";

		/// <summary>Name of the status label shown in place of the list.</summary>
		private const string STATUS_NAME = "scenechannel-status";

		/// <summary>Name of the column caption strip.</summary>
		private const string COLUMNS_NAME = "scenechannel-columns";

		/// <summary>Name of the refresh button.</summary>
		private const string REFRESH_BUTTON_NAME = "scenechannel-refresh";

		/// <summary>Name of the close button.</summary>
		private const string CLOSE_BUTTON_NAME = "scenechannel-close";

		/// <summary>USS class applied to each generated channel row.</summary>
		private const string ROW_CLASS = "scenechannel-row";

		/// <summary>USS class applied to a row's name label.</summary>
		private const string ROW_NAME_CLASS = "scenechannel-row__name";

		/// <summary>USS class applied to a row's population label.</summary>
		private const string ROW_POPULATION_CLASS = "scenechannel-row__population";

		/// <summary>USS class applied to the row representing the character's current channel.</summary>
		private const string ROW_CURRENT_CLASS = "scenechannel-row--current";

		/// <summary>
		/// How long the picker waits for a channel list before telling the player it got nothing.
		/// </summary>
		/// <remarks>
		/// Comfortably longer than the server's own work — two database queries and a main-thread
		/// hop — so a merely busy server is never reported as silent. It exists for the requests
		/// that genuinely get no reply: the server's ingress guard drops a list request that is
		/// debounced or that arrives while another is still in flight, and says nothing when it
		/// does.
		/// </remarks>
		private const float RequestTimeoutSeconds = 8.0f;

		/// <summary>What this panel is currently waiting on.</summary>
		private enum RequestState : byte
		{
			/// <summary>Nothing has been asked for.</summary>
			Idle = 0,
			/// <summary>A list request is outstanding.</summary>
			Waiting = 1,
			/// <summary>A list arrived and is in <see cref="channels"/>.</summary>
			Answered = 2,
			/// <summary>No list arrived within <see cref="RequestTimeoutSeconds"/>.</summary>
			TimedOut = 3,
			/// <summary>
			/// There is no character to ask about, so nothing was sent.
			/// </summary>
			/// <remarks>
			/// Distinct from <see cref="TimedOut"/>: reporting "the server did not answer" for a
			/// request that was never made sends the player looking for a fault that is not there.
			/// Reachable by leaving the panel open across a scene transfer, which unsets the
			/// character while the panel is still on screen.
			/// </remarks>
			NoCharacter = 4,
		}

		/// <summary>
		/// The channel list MODEL. Survives visual-tree rebuilds; see the class remarks.
		/// </summary>
		private readonly List<ChannelAddress> channels = new List<ChannelAddress>();

		/// <summary>
		/// The channel the character is on right now, as a <c>scenes.id</c>.
		/// </summary>
		/// <remarks>
		/// Sent by the server in <see cref="SceneChannelListBroadcast.CurrentSceneHandle"/> because
		/// the client cannot work it out: <c>IPlayerCharacter.SceneHandle</c> is server-side state
		/// that is never replicated, so on this side it is always zero.
		/// </remarks>
		private long currentSceneHandle;

		/// <summary>What the panel is waiting on. See <see cref="RequestState"/>.</summary>
		private RequestState state = RequestState.Idle;

		/// <summary>Seconds remaining before an outstanding request is declared unanswered.</summary>
		private float requestTimeoutRemaining;

		/// <summary>The container element that holds the generated channel rows.</summary>
		private VisualElement channelList;

		/// <summary>Header label describing what is being listed.</summary>
		private Label subtitleLabel;

		/// <summary>Header badge showing the number of channels.</summary>
		private Label countLabel;

		/// <summary>Label shown in place of the list while it is empty, loading or unanswered.</summary>
		private Label statusLabel;

		/// <summary>Column caption strip, hidden while there are no rows.</summary>
		private VisualElement columns;

		/// <summary>Refresh button, disabled while a request is outstanding.</summary>
		private Button refreshButton;

		/// <summary>
		/// Queries the list container and wires the footer buttons.
		/// </summary>
		/// <remarks>
		/// Runs against a fresh tree every time the document is enabled, so every cached element
		/// reference is replaced here and the previously generated rows are dropped — they belong
		/// to a tree that no longer exists.
		/// </remarks>
		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			this.channelList = root.Q(CHANNEL_LIST_NAME);
			this.subtitleLabel = root.Q<Label>(SUBTITLE_NAME);
			this.countLabel = root.Q<Label>(COUNT_NAME);
			this.statusLabel = root.Q<Label>(STATUS_NAME);
			this.columns = root.Q(COLUMNS_NAME);

			this.refreshButton = root.Q<Button>(REFRESH_BUTTON_NAME);
			if (this.refreshButton != null)
			{
				this.refreshButton.clicked += RequestChannelList;
			}

			Button closeButton = root.Q<Button>(CLOSE_BUTTON_NAME);
			if (closeButton != null)
			{
				closeButton.clicked += Hide;
			}
		}

		/// <summary>
		/// Subscribes to the channel list broadcast.
		/// </summary>
		public override void OnClientSet()
		{
			/* Guarded. SetClient runs from UIManager as soon as a Client exists, and the network
			 * manager is resolved separately — a panel that assumed both were up threw here during
			 * a reconnect and took the rest of the panel wiring down with it. */
			if (Client == null || Client.NetworkManager == null || Client.NetworkManager.ClientManager == null)
			{
				return;
			}
			Client.NetworkManager.ClientManager.RegisterBroadcast<SceneChannelListBroadcast>(OnClientSceneChannelListBroadcastReceived);
		}

		/// <summary>
		/// Unsubscribes from the channel list broadcast.
		/// </summary>
		public override void OnClientUnset()
		{
			if (Client == null || Client.NetworkManager == null || Client.NetworkManager.ClientManager == null)
			{
				return;
			}
			Client.NetworkManager.ClientManager.UnregisterBroadcast<SceneChannelListBroadcast>(OnClientSceneChannelListBroadcastReceived);
		}

		/// <summary>
		/// Re-renders after the visual tree has been rebuilt.
		/// </summary>
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();
			Render();
		}

		/// <summary>
		/// Asks the server for a fresh list every time the panel opens.
		/// </summary>
		/// <remarks>
		/// See the class remarks for why the previous answer is never reused.
		/// </remarks>
		protected override void OnAfterShow()
		{
			RequestChannelList();
		}

		/// <summary>
		/// Ends an outstanding wait that the server never answered.
		/// </summary>
		protected override void OnTick()
		{
			if (this.state != RequestState.Waiting)
			{
				return;
			}

			this.requestTimeoutRemaining -= UnityEngine.Time.unscaledDeltaTime;
			if (this.requestTimeoutRemaining > 0f)
			{
				return;
			}

			this.state = RequestState.TimedOut;
			Render();
		}

		/// <summary>
		/// Drops the list when the character goes away.
		/// </summary>
		/// <remarks>
		/// The channels belong to one character's scene on one scene server. Carrying them across a
		/// character switch or a scene transfer would offer destinations that mean nothing on the
		/// new server — the server validates every selection against the character's own scene and
		/// refuses, so this was never exploitable, but it would show the player a list of places
		/// they cannot go.
		/// </remarks>
		public override void OnPostUnsetCharacter()
		{
			base.OnPostUnsetCharacter();
			this.channels.Clear();
			this.currentSceneHandle = 0;
			this.state = RequestState.NoCharacter;
			Render();
		}

		/// <summary>
		/// Sends a channel list request and puts the panel into its waiting state.
		/// </summary>
		private void RequestChannelList()
		{
			if (Client == null || Character == null)
			{
				this.state = RequestState.NoCharacter;
				Render();
				return;
			}

			this.state = RequestState.Waiting;
			this.requestTimeoutRemaining = RequestTimeoutSeconds;
			Render();

			Client.Broadcast(new RequestSceneChannelListBroadcast(), Channel.Reliable);
		}

		/// <summary>
		/// Adopts a channel list from the server.
		/// </summary>
		/// <param name="msg">The list, possibly empty.</param>
		/// <param name="channel">The network channel.</param>
		private void OnClientSceneChannelListBroadcastReceived(SceneChannelListBroadcast msg, Channel channel)
		{
			this.channels.Clear();
			if (msg.Addresses != null)
			{
				this.channels.AddRange(msg.Addresses);
			}

			/* Ordered by scene row id so the numbering the player reads is stable.
			 *
			 * Every channel of a scene shares that scene's name, so the only thing distinguishing
			 * one row from another is its position in the list — and the server builds that list
			 * from a database query with no ORDER BY, so an unsorted list can come back in a
			 * different order on every refresh. "Channel 2" would then be a different destination
			 * each time the player looked, which is worse than no label at all. Row ids are
			 * assigned in creation order and never reused, so sorting on them also puts the
			 * longest-lived instance first. */
			this.channels.Sort((a, b) => a.SceneHandle.CompareTo(b.SceneHandle));

			this.currentSceneHandle = msg.CurrentSceneHandle;
			this.state = RequestState.Answered;

			Render();
		}

		/// <summary>
		/// Rebuilds the visible list from the model.
		/// </summary>
		private void Render()
		{
			if (this.refreshButton != null)
			{
				this.refreshButton.SetEnabled(this.state != RequestState.Waiting);
			}

			if (this.channelList == null)
			{
				return;
			}

			this.channelList.Clear();

			bool hasRows = this.state == RequestState.Answered && this.channels.Count > 0;

			if (hasRows)
			{
				for (int i = 0; i < this.channels.Count; ++i)
				{
					this.channelList.Add(BuildRow(this.channels[i], i + 1));
				}
			}

			if (this.columns != null)
			{
				this.columns.style.display = hasRows ? DisplayStyle.Flex : DisplayStyle.None;
			}

			if (this.statusLabel != null)
			{
				this.statusLabel.style.display = hasRows ? DisplayStyle.None : DisplayStyle.Flex;
				this.statusLabel.text = DescribeState();
			}

			if (this.countLabel != null)
			{
				this.countLabel.text = hasRows ? this.channels.Count.ToString() : "-";
			}

			if (this.subtitleLabel != null)
			{
				/* The scene's name, when the server sent one. Every channel of a scene shares it,
				 * so it belongs in the header rather than on each identical row — and without it
				 * the panel is a list of numbered destinations with nothing saying what they are
				 * numbered instances OF. */
				string sceneName = hasRows ? this.channels[0].SceneName : null;

				this.subtitleLabel.text = !hasRows
					? "No channels to show"
					: string.IsNullOrEmpty(sceneName)
						? "Pick a channel to travel to"
						: $"Pick a channel of {sceneName}";
			}
		}

		/// <summary>
		/// The message shown in place of the list.
		/// </summary>
		/// <remarks>
		/// Every non-list outcome says something specific. "Nothing on screen and nothing
		/// happening" is the one state a picker must never produce: the player's response to it is
		/// to click again, which the server's own cooldown then refuses.
		/// </remarks>
		private string DescribeState()
		{
			switch (this.state)
			{
				case RequestState.Waiting:
					return "Asking the server for channels...";
				case RequestState.TimedOut:
					return "The server did not answer. Use Refresh to try again.";
				case RequestState.NoCharacter:
					return "Channels are only available while you are in the world.";
				case RequestState.Answered:
					/* An empty answer is a real answer, and the server sends one for instanced
					 * content as well as for a scene that only has the one instance. It cannot tell
					 * the two apart from here, so the wording covers both. */
					return "No other channels are available here.";
				default:
					return "Use Refresh to look for channels.";
			}
		}

		/// <summary>
		/// Builds one channel row.
		/// </summary>
		/// <param name="address">The channel the row represents.</param>
		/// <param name="ordinal">
		/// The channel's 1-based position in the sorted list — the only handle a player has on it.
		/// See <see cref="OnClientSceneChannelListBroadcastReceived"/> for why the order is fixed.
		/// </param>
		private VisualElement BuildRow(ChannelAddress address, int ordinal)
		{
			bool isCurrent = address.SceneHandle == this.currentSceneHandle;

			VisualElement row = new VisualElement();
			row.AddToClassList("fish-row");
			row.AddToClassList(ROW_CLASS);
			if (isCurrent)
			{
				row.AddToClassList(ROW_CURRENT_CLASS);
			}

			Label name = new Label(isCurrent
				? $"Channel {ordinal} (current)"
				: $"Channel {ordinal}");
			name.AddToClassList("fish-row__name");
			name.AddToClassList(ROW_NAME_CLASS);
			row.Add(name);

			Label population = new Label(address.CharacterCount.ToString());
			population.AddToClassList("fish-row__value");
			population.AddToClassList(ROW_POPULATION_CLASS);
			row.Add(population);

			if (isCurrent)
			{
				/* Not clickable. The server refuses a switch to the channel the character is
				 * already on, and the picker closes itself on send — so allowing the click would
				 * shut the window and then explain, through a separate notice, that nothing
				 * happened. */
				row.SetEnabled(false);
				return row;
			}

			/* ClickEvent, not PointerDownEvent. UI Toolkit raises a click only when the press AND
			 * the release both land on the element, which is what makes a drag that started on a
			 * row — the obvious way to scroll a list with more channels than fit — not a
			 * selection. A press alone committed the player to leaving the world. */
			ChannelAddress selected = address;
			int selectedOrdinal = ordinal;
			row.RegisterCallback<ClickEvent>(evt =>
			{
				// 0 is the left button. Nothing is bound to the others here.
				if (evt.button != 0)
				{
					return;
				}

				evt.StopPropagation();
				ConfirmChannel(selected, selectedOrdinal);
			});
			return row;
		}

		/// <summary>
		/// Asks the player to confirm a channel switch before committing to it.
		/// </summary>
		/// <param name="address">The channel the row represents.</param>
		/// <param name="ordinal">The channel's position in the list, as the row labels it.</param>
		/// <remarks>
		/// A channel switch is not a window change: the scene server releases the character and
		/// drops the connection, and the player comes back through the world server behind a
		/// loading screen. There is no undo, it interrupts whatever they were doing, and the rows
		/// it is chosen from are a list of near-identical entries a few pixels apart. The game menu
		/// confirms its two destructive actions for exactly this reason; this belongs in the same
		/// category and was the one route out of the world that did not ask.
		/// <para>
		/// The population is repeated in the prompt because it is the only thing distinguishing one
		/// channel from another, and it is what a player picking a quieter instance is actually
		/// choosing on.
		/// </para>
		/// </remarks>
		private void ConfirmChannel(ChannelAddress address, int ordinal)
		{
			if (Client == null || Character == null)
			{
				return;
			}

			// Stale snapshot: the list could have been redrawn between the click and this running.
			if (address.SceneHandle == this.currentSceneHandle)
			{
				return;
			}

			string population = address.CharacterCount == 1
				? "1 player"
				: $"{address.CharacterCount} players";

			if (!UIManager.TryGetTK("UIDialogBox", out UITKDialogBox confirm))
			{
				/* No dialog to ask with. Travelling anyway would be the safer-looking choice and is
				 * the wrong one: the whole point of the prompt is that this cannot be taken back,
				 * so a missing prompt has to mean the action does not happen. */
				if (this.statusLabel != null)
				{
					this.statusLabel.style.display = DisplayStyle.Flex;
					this.statusLabel.text = "The confirmation dialog is unavailable; the channel switch was not sent.";
				}
				return;
			}

			confirm.Open(
				$"Travel to Channel {ordinal} ({population})?\n\nYou will be disconnected and reloaded into the new channel.",
				() => SelectChannel(address));
		}

		/// <summary>
		/// Asks the server to move the character to <paramref name="address"/>.
		/// </summary>
		/// <remarks>
		/// The panel closes on send rather than waiting for a result. There is no success message
		/// to wait for: the server applies a switch by releasing the character and dropping the
		/// connection, so the next thing the player sees either way is a loading screen. A refusal
		/// arrives as <c>SceneTransferRefusedBroadcast</c> and is presented by <see cref="Client"/>
		/// for exactly this reason.
		/// <para>
		/// Closing also removes the second click. The request takes a database round trip to
		/// answer, and clicking again inside that window is debounced by the server into
		/// <c>SceneTransferRefusalReason.OnCooldown</c> — so an impatient player would be told they
		/// were travelling too often on top of a request that was already succeeding.
		/// </para>
		/// </remarks>
		/// <param name="address">The channel to travel to.</param>
		private void SelectChannel(ChannelAddress address)
		{
			if (Client == null || Character == null)
			{
				return;
			}

			// Stale snapshot: the list could have been redrawn between the click being queued and
			// this running. The server re-checks anyway; this keeps the obvious no-op local.
			if (address.SceneHandle == this.currentSceneHandle)
			{
				return;
			}

			Hide();

			Client.Broadcast(new SceneChannelSelectBroadcast()
			{
				Channel = address,
			}, Channel.Reliable);
		}
	}
}
