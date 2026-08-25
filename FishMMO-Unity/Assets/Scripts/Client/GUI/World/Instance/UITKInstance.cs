using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using FishNet.Transporting;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit instance-management panel. Shows which instance the character is in, how long it
	/// has left, and who else is in it; lets the member who opened it remove others, and lets
	/// anyone leave.
	/// </summary>
	/// <remarks>
	/// The roster is a MODEL (<see cref="members"/>, plain data) rendered into a VIEW rebuilt from
	/// scratch on every open, because <c>UIDocument</c> re-clones the UXML each time it is enabled
	/// — the rule <see cref="UITKParty"/> documents at length.
	/// <para>
	/// <b>Every open asks the server, and it keeps asking.</b> Membership changes without this
	/// client being told — someone leaves, someone is removed, the leader walks out — and there is
	/// no push channel for it, so the panel refreshes on a timer while it is visible rather than
	/// showing a roster that quietly goes stale. The remaining time counts down locally between
	/// those refreshes, so the number moves without a message per second.
	/// </para>
	/// <para>
	/// <b>The leader check here only decides what is drawn.</b> `ViewerIsLeader` arrives from the
	/// server, and the server re-derives it from the scene row's owner when a removal actually
	/// arrives. A drawn control is not an authorisation, and the broadcast can be sent without one.
	/// </para>
	/// </remarks>
	public class UITKInstance : UITKCharacterControl
	{
		/// <summary>
		/// Draw order tier. See <see cref="UITKPanelLayer"/>.
		/// </summary>
		/// <remarks>
		/// <see cref="UITKPanelLayer.Popup"/> for the same reason the scene-channel picker uses it:
		/// this opens from the game menu and has to clear it, and UI Toolkit orders input as well as
		/// drawing by sorting order, so a panel that loses that ordering receives no pointer events
		/// at all. Sharing the `Settings` tier with Options would leave the winner decided by which
		/// scene happened to load first.
		/// </remarks>
		protected override UITKPanelLayer Layer => UITKPanelLayer.Popup;

		private const string TITLE_NAME = "instance-title";
		private const string SUBTITLE_NAME = "instance-subtitle";
		private const string TIMER_NAME = "instance-timer";
		private const string LEADER_NAME = "instance-leader";
		private const string COLUMNS_NAME = "instance-columns";
		private const string LIST_NAME = "instance-list";
		private const string STATUS_NAME = "instance-status";
		private const string REFRESH_BUTTON_NAME = "instance-refresh";
		private const string PRIVACY_TOGGLE_NAME = "instance-privacy";
		private const string LEAVE_BUTTON_NAME = "instance-leave";
		private const string CLOSE_BUTTON_NAME = "instance-close";

		private const string ROW_CLASS = "instance-row";
		private const string ROW_NAME_CLASS = "instance-row__name";
		private const string ROW_ACTION_CLASS = "instance-row__action";
		private const string ROW_SELF_CLASS = "instance-row--self";
		private const string ROW_LEADER_CLASS = "instance-row--leader";

		/// <summary>How long the panel waits for a reply before saying it got none.</summary>
		/// <remarks>
		/// The server debounces these requests per connection and answers nothing when it declines,
		/// so a request is not guaranteed a reply and a panel that waited forever would present an
		/// empty window with no explanation.
		/// </remarks>
		private const float RequestTimeoutSeconds = 8.0f;

		/// <summary>Seconds between automatic refreshes while the panel is open.</summary>
		/// <remarks>
		/// Comfortably above the server's own per-connection debounce, so the timer cannot produce
		/// a request the server will silently drop.
		/// </remarks>
		private const float RefreshIntervalSeconds = 5.0f;

		/// <summary>What the panel is waiting on.</summary>
		private enum RequestState : byte
		{
			/// <summary>Nothing has been asked for.</summary>
			Idle = 0,
			/// <summary>A request is outstanding.</summary>
			Waiting = 1,
			/// <summary>An answer arrived and the character is in an instance.</summary>
			InInstance = 2,
			/// <summary>An answer arrived and the character is not in an instance.</summary>
			NotInInstance = 3,
			/// <summary>No answer arrived within <see cref="RequestTimeoutSeconds"/>.</summary>
			TimedOut = 4,
			/// <summary>There is no character to ask about, so nothing was sent.</summary>
			NoCharacter = 5,
		}

		/// <summary>The roster MODEL. Survives visual-tree rebuilds.</summary>
		private readonly List<InstanceMemberData> members = new List<InstanceMemberData>();

		/// <summary>What the panel is waiting on.</summary>
		private RequestState state = RequestState.Idle;

		/// <summary>Instance scene name, as last reported.</summary>
		private string instanceName;

		/// <summary>Name of the party leader, or null when they are not on this scene server.</summary>
		private string leaderName;

		/// <summary>Whether this character may remove others, as last reported by the server.</summary>
		private bool viewerIsLeader;

		/// <summary>Difficulty the run is being played at, or null when the dungeon offers one.</summary>
		private string difficultyName;

		/// <summary>Whether the run is hidden from the dungeon finder, as last reported.</summary>
		private bool isPrivate;

		/// <summary>
		/// Set while the panel is writing the privacy toggle, so its own write is not read back as
		/// a click.
		/// </summary>
		/// <remarks>
		/// <c>Toggle.value</c> raises a change event whether a person or the code set it, and the
		/// panel rewrites the toggle from every server reply. Without this guard each reply would
		/// send a request that produced another reply — a loop that flips the run's visibility for
		/// as long as the panel stays open.
		/// </remarks>
		private bool suppressPrivacyCallback;

		/// <summary>
		/// Seconds left on the instance, counted down locally between refreshes.
		/// </summary>
		/// <remarks>
		/// Negative means the instance is not time-bounded and no countdown is shown; zero or above
		/// is a real remaining time. Kept as a float so the local tick is smooth and only the
		/// display is rounded.
		/// </remarks>
		private float remainingSeconds = -1.0f;

		/// <summary>Seconds until the outstanding request is declared unanswered.</summary>
		private float requestTimeoutRemaining;

		/// <summary>Seconds until the next automatic refresh.</summary>
		private float refreshRemaining;

		private Label subtitleLabel;
		private Label timerLabel;
		private Label leaderLabel;
		private Label statusLabel;
		private VisualElement columns;
		private VisualElement memberList;
		private Button refreshButton;
		private Button leaveButton;
		private Toggle privacyToggle;

		/// <summary>
		/// Queries the elements and wires the footer buttons.
		/// </summary>
		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			this.subtitleLabel = root.Q<Label>(SUBTITLE_NAME);
			this.timerLabel = root.Q<Label>(TIMER_NAME);
			this.leaderLabel = root.Q<Label>(LEADER_NAME);
			this.statusLabel = root.Q<Label>(STATUS_NAME);
			this.columns = root.Q(COLUMNS_NAME);
			this.memberList = root.Q(LIST_NAME);

			this.refreshButton = root.Q<Button>(REFRESH_BUTTON_NAME);
			if (this.refreshButton != null)
			{
				this.refreshButton.clicked += RequestDetails;
			}

			this.leaveButton = root.Q<Button>(LEAVE_BUTTON_NAME);
			if (this.leaveButton != null)
			{
				this.leaveButton.clicked += OnClick_Leave;
			}

			this.privacyToggle = root.Q<Toggle>(PRIVACY_TOGGLE_NAME);
			if (this.privacyToggle != null)
			{
				this.privacyToggle.RegisterValueChangedCallback(OnPrivacyToggled);
			}

			Button closeButton = root.Q<Button>(CLOSE_BUTTON_NAME);
			if (closeButton != null)
			{
				closeButton.clicked += Hide;
			}
		}

		/// <summary>Subscribes to the instance readout.</summary>
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<InstanceDetailsBroadcast>(OnClientInstanceDetailsBroadcastReceived);
		}

		/// <summary>Unsubscribes from the instance readout.</summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<InstanceDetailsBroadcast>(OnClientInstanceDetailsBroadcastReceived);
		}

		/// <summary>Re-renders after the visual tree has been rebuilt.</summary>
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();
			Render();
		}

		/// <summary>Asks for a fresh readout every time the panel opens.</summary>
		protected override void OnAfterShow()
		{
			RequestDetails();
		}

		/// <summary>
		/// Runs the local countdown, the refresh timer and the request timeout.
		/// </summary>
		protected override void OnTick()
		{
			if (!Visible)
			{
				return;
			}

			float dt = UnityEngine.Time.unscaledDeltaTime;

			// The clock keeps moving between refreshes, so the number on screen is never more than
			// a frame stale even though the server is asked every few seconds.
			if (this.state == RequestState.InInstance && this.remainingSeconds > 0.0f)
			{
				this.remainingSeconds -= dt;
				if (this.remainingSeconds < 0.0f)
				{
					this.remainingSeconds = 0.0f;
				}
				RenderTimer();
			}

			if (this.state == RequestState.Waiting)
			{
				this.requestTimeoutRemaining -= dt;
				if (this.requestTimeoutRemaining <= 0.0f)
				{
					this.state = RequestState.TimedOut;
					Render();
				}
				return;
			}

			this.refreshRemaining -= dt;
			if (this.refreshRemaining <= 0.0f)
			{
				RequestDetails();
			}
		}

		/// <summary>
		/// Drops the roster when the character goes away.
		/// </summary>
		/// <remarks>
		/// The roster describes one instance on one scene server. Carrying it across a scene
		/// transfer would show members of a dungeon this character has left, and offer a Remove
		/// button whose request means nothing on the server it would be sent to.
		/// </remarks>
		public override void OnPostUnsetCharacter()
		{
			base.OnPostUnsetCharacter();
			ClearModel();
			this.state = RequestState.NoCharacter;
			Render();
		}

		/// <summary>Drops everything the panel knows about the instance.</summary>
		private void ClearModel()
		{
			this.members.Clear();
			this.instanceName = null;
			this.leaderName = null;
			this.viewerIsLeader = false;
			this.difficultyName = null;
			this.isPrivate = false;
			this.remainingSeconds = -1.0f;
		}

		/// <summary>Sends a readout request and arms the timeout.</summary>
		private void RequestDetails()
		{
			this.refreshRemaining = RefreshIntervalSeconds;

			if (Client == null || Character == null)
			{
				ClearModel();
				this.state = RequestState.NoCharacter;
				Render();
				return;
			}

			this.state = RequestState.Waiting;
			this.requestTimeoutRemaining = RequestTimeoutSeconds;
			Render();

			Client.Broadcast(new RequestInstanceDetailsBroadcast(), Channel.Reliable);
		}

		/// <summary>Adopts a readout from the server.</summary>
		private void OnClientInstanceDetailsBroadcastReceived(InstanceDetailsBroadcast msg, Channel channel)
		{
			ClearModel();

			if (!msg.InInstance)
			{
				this.state = RequestState.NotInInstance;
				Render();
				return;
			}

			this.instanceName = msg.SceneName;
			this.leaderName = msg.LeaderName;
			this.viewerIsLeader = msg.ViewerIsLeader;
			this.difficultyName = msg.DifficultyName;
			this.isPrivate = msg.IsPrivate;
			// 0 means "not time-bounded", which is a different thing from "no time left".
			this.remainingSeconds = msg.RemainingSeconds > 0 ? msg.RemainingSeconds : -1.0f;

			if (msg.Members != null)
			{
				this.members.AddRange(msg.Members);
			}

			this.state = RequestState.InInstance;
			Render();
		}

		/// <summary>Rebuilds the panel from the model.</summary>
		private void Render()
		{
			bool inInstance = this.state == RequestState.InInstance;

			if (this.refreshButton != null)
			{
				this.refreshButton.SetEnabled(this.state != RequestState.Waiting);
			}
			if (this.leaveButton != null)
			{
				// Leaving is always the player's own decision, but there has to be something to
				// leave — offering it outside an instance produces a request the server ignores.
				this.leaveButton.SetEnabled(inInstance);
			}

			if (this.subtitleLabel != null)
			{
				/* The difficulty is appended to the name rather than given a control of its own.
				 * It cannot be changed once a run has started, so it is a fact about this dungeon
				 * rather than a setting — and a dungeon that offers only one way to be played
				 * reports no name at all, which correctly renders as just the dungeon. */
				this.subtitleLabel.text = !inInstance || string.IsNullOrEmpty(this.instanceName)
					? "Not in a dungeon"
					: string.IsNullOrEmpty(this.difficultyName)
						? this.instanceName
						: $"{this.instanceName} · {this.difficultyName}";
			}

			if (this.leaderLabel != null)
			{
				this.leaderLabel.style.display = inInstance ? DisplayStyle.Flex : DisplayStyle.None;
				if (inInstance)
				{
					/* The leader can be absent — they opened the instance and left, or have not
					 * arrived yet. The instance keeps its owner either way, so this says the name
					 * is unknown rather than claiming there is no leader. */
					/* The leader can be unnameable here — they lead the party from outside the
					 * instance, or from another scene server entirely. There is still a leader, so
					 * this says the name is unknown rather than claiming the run has none. */
					this.leaderLabel.text = this.viewerIsLeader
						? "You lead this party."
						: string.IsNullOrEmpty(this.leaderName)
							? "Led by a party member who is not here."
							: $"Led by {this.leaderName}.";
				}
			}

			RenderTimer();

			if (this.columns != null)
			{
				this.columns.style.display = inInstance && this.members.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
			}

			if (this.memberList != null)
			{
				this.memberList.Clear();
				if (inInstance)
				{
					for (int i = 0; i < this.members.Count; ++i)
					{
						this.memberList.Add(BuildRow(this.members[i]));
					}
				}
			}

			if (this.privacyToggle != null)
			{
				/* Shown to everybody, changeable only by the leader. Whether strangers can walk
				 * into the run they are in is something every member has an interest in knowing;
				 * only one of them decides it. */
				this.privacyToggle.style.display = inInstance ? DisplayStyle.Flex : DisplayStyle.None;
				this.privacyToggle.SetEnabled(inInstance && this.viewerIsLeader);

				suppressPrivacyCallback = true;
				try
				{
					// Inverted: the server records "private", the player is offered "open to others".
					this.privacyToggle.value = inInstance && !this.isPrivate;
				}
				finally
				{
					suppressPrivacyCallback = false;
				}
			}

			if (this.statusLabel != null)
			{
				bool hasRows = inInstance && this.members.Count > 0;
				this.statusLabel.style.display = hasRows ? DisplayStyle.None : DisplayStyle.Flex;
				this.statusLabel.text = DescribeState();
			}
		}

		/// <summary>Writes the remaining-time badge.</summary>
		private void RenderTimer()
		{
			if (this.timerLabel == null)
			{
				return;
			}

			if (this.state != RequestState.InInstance || this.remainingSeconds < 0.0f)
			{
				// An instance with no lifetime cap configured has no countdown to show, and a dash
				// says that rather than implying it is about to close.
				this.timerLabel.text = "--:--";
				return;
			}

			int total = (int)Math.Ceiling(this.remainingSeconds);
			this.timerLabel.text = $"{total / 60:00}:{total % 60:00}";
		}

		/// <summary>The message shown in place of the roster.</summary>
		private string DescribeState()
		{
			switch (this.state)
			{
				case RequestState.Waiting:
					return "Asking the server...";
				case RequestState.TimedOut:
					return "The server did not answer. Use Refresh to try again.";
				case RequestState.NotInInstance:
					return "You are not in a dungeon.";
				case RequestState.NoCharacter:
					return "Dungeon details are only available while you are in the world.";
				case RequestState.InInstance:
					// Reachable: the readout is a snapshot, and the last member can leave between
					// the server building it and this rendering it.
					return "Nobody is in this dungeon.";
				default:
					return "Use Refresh to load the dungeon details.";
			}
		}

		/// <summary>Builds one member row.</summary>
		/// <param name="member">The member the row represents.</param>
		private VisualElement BuildRow(InstanceMemberData member)
		{
			VisualElement row = new VisualElement();
			row.AddToClassList("fish-row");
			row.AddToClassList(ROW_CLASS);
			if (member.IsSelf)
			{
				row.AddToClassList(ROW_SELF_CLASS);
			}
			if (member.IsLeader)
			{
				row.AddToClassList(ROW_LEADER_CLASS);
			}

			string suffix = member.IsLeader && member.IsSelf ? " (you, leader)"
						  : member.IsLeader ? " (leader)"
						  : member.IsSelf ? " (you)"
						  : string.Empty;

			Label name = new Label((member.Name ?? "Unknown") + suffix);
			name.AddToClassList("fish-row__name");
			name.AddToClassList(ROW_NAME_CLASS);
			row.Add(name);

			/* The Remove control exists only where it can do something: the viewer must be the
			 * leader, and nobody removes themselves — that is Leave, which has different rules and
			 * is always available. Drawing a disabled button for every other row would be four
			 * dead controls on a full party. */
			if (this.viewerIsLeader && !member.IsSelf)
			{
				long targetID = member.CharacterID;
				Button remove = new Button(() => OnClick_Remove(targetID)) { text = "Remove" };
				remove.AddToClassList("fish-button");
				remove.AddToClassList("fish-button--danger");
				remove.AddToClassList(ROW_ACTION_CLASS);
				row.Add(remove);
			}

			return row;
		}

		/// <summary>
		/// Asks the server to remove a member from the instance.
		/// </summary>
		/// <remarks>
		/// The row is not removed locally. The server answers a successful removal with a fresh
		/// readout, so what changes the list is the server saying the member has gone — the same
		/// rule the container panel follows, and the reason a refused removal cannot leave the
		/// panel showing a roster the server does not agree with.
		/// </remarks>
		/// <param name="characterID">Member to remove.</param>
		private void OnClick_Remove(long characterID)
		{
			if (Client == null || Character == null || characterID <= 0)
			{
				return;
			}

			Client.Broadcast(new InstanceKickBroadcast { CharacterID = characterID }, Channel.Reliable);
		}

		/// <summary>
		/// Leaves the instance and closes the panel.
		/// </summary>
		/// <remarks>
		/// Closes on send for the same reason the dungeon finder does: leaving is performed as a
		/// release-and-re-route, so the next thing the player sees either way is a loading screen,
		/// and a panel left open over it only invites a second click. A refusal — leaving is gated
		/// in combat, like every other voluntary transfer — arrives as
		/// <c>SceneTransferRefusedBroadcast</c> and raises its own message.
		/// </remarks>
		/// <summary>
		/// Asks the server to show or hide the run in the dungeon finder.
		/// </summary>
		/// <remarks>
		/// The toggle is not the state — the server's next reply is. It is left showing what the
		/// player clicked until that reply arrives and Render writes the real answer over it,
		/// which is the correct behaviour for a refusal: a request the server turns down is
		/// visibly undone rather than leaving the panel claiming something that is not true.
		/// </remarks>
		private void OnPrivacyToggled(ChangeEvent<bool> evt)
		{
			// The panel's own writes come back through here too. See suppressPrivacyCallback.
			if (suppressPrivacyCallback ||
				this.state != RequestState.InInstance ||
				!this.viewerIsLeader)
			{
				return;
			}

			// Inverted: the toggle offers "open to others", the server records "private".
			Client.Broadcast(new InstancePrivacyBroadcast()
			{
				IsPrivate = !evt.newValue,
			});
		}

		public void OnClick_Leave()
		{
			if (Client == null || Character == null || this.state != RequestState.InInstance)
			{
				return;
			}

			Hide();
			Client.Broadcast(new RequestLeaveInstanceBroadcast(), Channel.Reliable);
		}
	}
}
