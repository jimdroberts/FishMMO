using System;
using FishMMO.Shared;
using UnityEngine;
using UnityEngine.UIElements;

namespace FishMMO.Client
{
	/// <summary>
	/// The client's view of its wait in either of the connection pipeline's queues — the world
	/// server's scene routing, or the login server's admission: where it stands, how long it may
	/// take, how long it has waited, and a way out. When the server gives up on the wait, the
	/// same panel says which wait failed and what can be done about it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The world server places waiting players strictly in the order they arrived, and only as
	/// many as there are free slots, so a player can legitimately wait while a zone is full. That
	/// wait used to be a line of text in the shared dialog box. The dialog is a
	/// <see cref="UITKPanelLayer.Modal"/> panel, underneath the loading overlay, and every
	/// return through the world server after a zone change, a channel switch or a bind-point
	/// respawn happens with that overlay up: the one message explaining the wait was drawn behind
	/// it, and the player watched a loading screen that never moved. When the wait ended, the
	/// client quit to the login screen with no choice offered.
	/// </para>
	/// <para>
	/// The login server's queue had the same dialog and moved here too, as a second
	/// <see cref="QueueKind"/> with its own wording. It has one wait rather than three, keeps no
	/// place across connections, and cannot be rejoined from here (that means signing in again),
	/// so its ended state offers only Close.
	/// </para>
	/// <para>
	/// This panel sits at <see cref="UITKPanelLayer.SystemStatus"/>, above the overlay and below
	/// the reconnect display. <see cref="Client"/> owns the connection decisions — it stops the
	/// connection manager redialling on its own when a world wait ends, performs the rejoin, and
	/// retries the login handshake on admission — and forwards each
	/// <see cref="WorldSceneQueuePositionBroadcast"/> and <see cref="LoginQueuePositionBroadcast"/>
	/// here. The wording and arithmetic live in <see cref="WorldQueuePresentation"/>.
	/// </para>
	/// </remarks>
	public class UITKWorldQueueDisplay : UITKControl
	{
		/// <summary>Name this panel registers under with <see cref="UIManager"/>: its GameObject name.</summary>
		public const string PanelName = "UIWorldQueueDisplay";

		/// <summary>Draw order tier for this panel. See <see cref="UITKPanelLayer.SystemStatus"/>.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.SystemStatus;

		/// <summary>A full-screen status panel, not a window: nowhere to drag it to.</summary>
		protected override bool CanDrag => false;

		private const string TITLE_NAME = "world-queue-title";
		private const string LAMP_NAME = "world-queue-lamp";
		private const string HEADLINE_NAME = "world-queue-headline";
		private const string DETAIL_NAME = "world-queue-detail";
		private const string STATS_NAME = "world-queue-stats";
		private const string POSITION_NAME = "world-queue-position";
		private const string ESTIMATE_NAME = "world-queue-estimate";
		private const string ELAPSED_NAME = "world-queue-elapsed";
		private const string PROGRESS_NAME = "world-queue-progress";
		private const string PROGRESS_FILL_NAME = "world-queue-progress-fill";
		private const string NOTE_NAME = "world-queue-note";
		private const string LEAVE_BUTTON_NAME = "world-queue-leave-btn";
		private const string RETRY_BUTTON_NAME = "world-queue-retry-btn";

		private const string LampDimClass = "world-queue-header__icon--dim";
		private const string LampEndedClass = "world-queue-header__icon--ended";

		/// <summary>What the panel is showing.</summary>
		public enum DisplayState
		{
			/// <summary>Not on screen.</summary>
			Hidden,
			/// <summary>The client is in the queue.</summary>
			Waiting,
			/// <summary>The world server abandoned the wait; the player chooses what next.</summary>
			Ended,
		}

		/// <summary>What the panel is showing.</summary>
		public DisplayState State { get; private set; } = DisplayState.Hidden;

		/// <summary>Which queue the panel is showing, or showed last.</summary>
		public QueueKind Kind { get; private set; } = QueueKind.World;

		private Label titleLabel;
		private VisualElement lamp;
		private Label headlineLabel;
		private Label detailLabel;
		private VisualElement stats;
		private Label positionLabel;
		private Label estimateLabel;
		private Label elapsedLabel;
		private VisualElement progress;
		private VisualElement progressFill;
		private Label noteLabel;
		private Button leaveButton;
		private Button retryButton;

		/// <summary>Last reported position, 1-based.</summary>
		private int shownPosition;
		/// <summary>Last reported size of the group being waited in.</summary>
		private int shownTotal;
		/// <summary>Last reported estimate in seconds; 0 when the server has none.</summary>
		private int shownEstimateSeconds;
		/// <summary>What the wait is for, or what the wait that ended was for.</summary>
		private WorldSceneQueueReason shownReason;
		/// <summary>Highest position this wait has reported. See <see cref="WorldQueuePresentation.Progress"/>.</summary>
		private int startPosition;
		/// <summary><see cref="Time.unscaledTime"/> when this wait was first reported.</summary>
		private float waitStartedAt;
		/// <summary>Whole seconds last written to the elapsed label, so the label is rewritten once a second, not every frame.</summary>
		private int shownElapsedSeconds = -1;

		/// <summary>
		/// Resolves cached elements and wires the buttons and keys. Runs once, at scene load.
		/// </summary>
		public override void OnStarting()
		{
			if (Root == null)
			{
				return;
			}

			titleLabel = Root.Q<Label>(TITLE_NAME);
			lamp = Root.Q<VisualElement>(LAMP_NAME);
			headlineLabel = Root.Q<Label>(HEADLINE_NAME);
			detailLabel = Root.Q<Label>(DETAIL_NAME);
			stats = Root.Q<VisualElement>(STATS_NAME);
			positionLabel = Root.Q<Label>(POSITION_NAME);
			estimateLabel = Root.Q<Label>(ESTIMATE_NAME);
			elapsedLabel = Root.Q<Label>(ELAPSED_NAME);
			progress = Root.Q<VisualElement>(PROGRESS_NAME);
			progressFill = Root.Q<VisualElement>(PROGRESS_FILL_NAME);
			noteLabel = Root.Q<Label>(NOTE_NAME);
			leaveButton = Root.Q<Button>(LEAVE_BUTTON_NAME);
			retryButton = Root.Q<Button>(RETRY_BUTTON_NAME);

			if (leaveButton != null)
			{
				leaveButton.clicked += OnLeaveClicked;
			}
			if (retryButton != null)
			{
				retryButton.clicked += OnRetryClicked;
			}

			/* Enter tries again and Escape goes back, but only once the wait has ended. While the
			 * player is queued neither key does anything: leaving costs them their place and the
			 * session, and that must never be one stray keystroke away. */
			LoginKeys.Attach(this, Root, onSubmit: OnSubmitKey, onCancel: OnCancelKey);
		}

		/// <summary>Steps aside for the reconnect display whenever a reconnect is armed or running.</summary>
		public override void OnClientSet()
		{
			Client.OnReconnectPending += OnReconnectStarted;
			Client.OnReconnectAttempt += OnReconnectAttempt;
		}

		/// <inheritdoc/>
		public override void OnClientUnset()
		{
			Client.OnReconnectPending -= OnReconnectStarted;
			Client.OnReconnectAttempt -= OnReconnectAttempt;
		}

		/// <summary>
		/// Shows, or updates, the waiting state.
		/// </summary>
		/// <param name="position">1-based position in the queue.</param>
		/// <param name="totalQueued">Size of the group being waited in.</param>
		/// <param name="estimatedWaitSeconds">Server estimate; 0 when it has none.</param>
		/// <param name="reason">What the client is waiting for.</param>
		/// <remarks>
		/// State first, tree second, as <see cref="UITKReconnectDisplay"/> does: the values are
		/// kept here and written by <see cref="ApplyState"/>, which also runs from
		/// <see cref="OnAfterShow"/> and <see cref="OnAfterStarting"/>, so an update that arrives
		/// before the tree exists is not lost.
		/// </remarks>
		public void ShowWaiting(int position, int totalQueued, int estimatedWaitSeconds, WorldSceneQueueReason reason)
		{
			ShowWaiting(QueueKind.World, position, totalQueued, estimatedWaitSeconds, reason);
		}

		/// <summary>
		/// Shows, or updates, a wait in the login server's admission queue.
		/// </summary>
		/// <param name="position">1-based position in the queue.</param>
		/// <param name="totalQueued">Size of the queue.</param>
		/// <param name="estimatedWaitSeconds">Server estimate; 0 when it has none.</param>
		public void ShowLoginWaiting(int position, int totalQueued, int estimatedWaitSeconds)
		{
			ShowWaiting(QueueKind.Login, position, totalQueued, estimatedWaitSeconds, WorldSceneQueueReason.Capacity);
		}

		/// <summary>Shows, or updates, the waiting state of either queue.</summary>
		private void ShowWaiting(QueueKind kind, int position, int totalQueued, int estimatedWaitSeconds, WorldSceneQueueReason reason)
		{
			// A different queue is a different wait: the login queue ends before the world queue
			// can begin, and the two must not share a clock or a progress baseline.
			if (State != DisplayState.Waiting || Kind != kind)
			{
				// A new wait: its own clock, and its own measure of how far it has come.
				waitStartedAt = Time.unscaledTime;
				startPosition = Math.Max(1, position);
				shownElapsedSeconds = -1;
			}
			else
			{
				startPosition = Math.Max(startPosition, position);
			}

			shownPosition = position;
			shownTotal = totalQueued;
			shownEstimateSeconds = estimatedWaitSeconds;
			shownReason = reason;
			Kind = kind;
			State = DisplayState.Waiting;

			Show();
			// Already visible: Show() returned early and did not reach OnAfterShow.
			ApplyState();
		}

		/// <summary>
		/// Shows that the world server abandoned the wait, and offers to queue again or go back.
		/// </summary>
		/// <param name="reason">What the wait that ended was for.</param>
		public void ShowWaitEnded(WorldSceneQueueReason reason)
		{
			ShowWaitEnded(QueueKind.World, reason);
		}

		/// <summary>
		/// Shows that the login server abandoned the wait. The client is already back at the
		/// login screen; the panel explains why and closes.
		/// </summary>
		public void ShowLoginWaitEnded()
		{
			ShowWaitEnded(QueueKind.Login, WorldSceneQueueReason.Capacity);
		}

		private void ShowWaitEnded(QueueKind kind, WorldSceneQueueReason reason)
		{
			shownReason = reason;
			Kind = kind;
			State = DisplayState.Ended;

			Show();
			ApplyState();
			if (WorldQueuePresentation.OffersRetry(kind))
			{
				retryButton?.Focus();
			}
			else
			{
				leaveButton?.Focus();
			}
		}

		/// <summary>
		/// Takes the panel down without doing anything else: the wait is over because the client
		/// was routed, or something else now owns the screen.
		/// </summary>
		public void Dismiss()
		{
			State = DisplayState.Hidden;
			if (Visible)
			{
				Hide();
			}
		}

		/// <inheritdoc/>
		protected override void OnAfterShow()
		{
			base.OnAfterShow();
			ApplyState();
		}

		/// <summary>
		/// Re-applies the state to a tree that was replaced while the panel was showing.
		/// </summary>
		/// <remarks>
		/// Only while visible: at scene load the panel is hidden, and <see cref="ApplyState"/>
		/// claims the cursor.
		/// </remarks>
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();
			if (Visible)
			{
				ApplyState();
			}
		}

		/// <summary>
		/// Advances the elapsed time and breathes the lamp once a second while waiting, and keeps
		/// Try again unavailable until the old connection has actually closed.
		/// </summary>
		protected override void OnTick()
		{
			if (!Visible)
			{
				return;
			}

			if (State == DisplayState.Ended)
			{
				retryButton?.SetEnabled(CanRejoin());
				return;
			}

			if (State != DisplayState.Waiting)
			{
				return;
			}

			int elapsed = Mathf.Max(0, (int)(Time.unscaledTime - waitStartedAt));
			if (elapsed == shownElapsedSeconds)
			{
				return;
			}
			shownElapsedSeconds = elapsed;

			if (elapsedLabel != null)
			{
				elapsedLabel.text = WorldQueuePresentation.ElapsedText(elapsed);
			}
			lamp?.EnableInClassList(LampDimClass, (elapsed & 1) == 1);
		}

		/// <summary>
		/// Hides the panel, forgets its state and hands the cursor back.
		/// </summary>
		/// <remarks>
		/// Overrides <c>Hide(bool)</c> because the quit-to-login teardown calls that form directly;
		/// see <see cref="UITKReconnectDisplay.Hide(bool)"/> for why the cursor claim must be
		/// released on every path.
		/// </remarks>
		/// <param name="overrideIsAlwaysOpen">When true, the call is a no-op.</param>
		public override void Hide(bool overrideIsAlwaysOpen)
		{
			base.Hide(overrideIsAlwaysOpen);

			if (overrideIsAlwaysOpen || Document == null)
			{
				return;
			}

			State = DisplayState.Hidden;
			ReleasesCursor = false;
		}

		/// <summary>
		/// Writes the current state into the live tree. Idempotent, and tolerant of elements that
		/// do not exist yet.
		/// </summary>
		private void ApplyState()
		{
			if (!Visible || State == DisplayState.Hidden)
			{
				return;
			}

			bool ended = State == DisplayState.Ended;

			SetText(titleLabel, WorldQueuePresentation.Title(Kind, ended));
			SetText(headlineLabel, ended ? WorldQueuePresentation.EndedHeadline(Kind, shownReason) : WorldQueuePresentation.Headline(Kind, shownReason));
			SetText(detailLabel, ended ? WorldQueuePresentation.EndedDetail(Kind, shownReason) : WorldQueuePresentation.Detail(Kind, shownReason));
			SetText(noteLabel, WorldQueuePresentation.Note(Kind));

			// The numbers describe a live wait; once it has ended they are history, not status.
			SetShown(stats, !ended);
			SetShown(progress, !ended);
			SetShown(noteLabel, !ended);
			SetShown(retryButton, ended && WorldQueuePresentation.OffersRetry(Kind));

			if (leaveButton != null)
			{
				leaveButton.text = ended ? WorldQueuePresentation.EndedLeaveLabel(Kind) : "Leave queue";
			}

			if (lamp != null)
			{
				lamp.EnableInClassList(LampEndedClass, ended);
				if (ended)
				{
					lamp.RemoveFromClassList(LampDimClass);
				}
			}

			if (ended)
			{
				retryButton?.SetEnabled(CanRejoin());
			}
			else
			{
				SetText(positionLabel, WorldQueuePresentation.PositionText(shownPosition, shownTotal));
				SetText(estimateLabel, WorldQueuePresentation.EstimateText(shownEstimateSeconds));
				int elapsed = Mathf.Max(0, (int)(Time.unscaledTime - waitStartedAt));
				shownElapsedSeconds = elapsed;
				SetText(elapsedLabel, WorldQueuePresentation.ElapsedText(elapsed));
				if (progressFill != null)
				{
					progressFill.style.width = Length.Percent(100f * WorldQueuePresentation.Progress(startPosition, shownPosition));
				}
			}

			/* Claim the cursor for as long as the panel is up. A return through the world server
			 * after a zone change starts from gameplay with the cursor locked, and
			 * PlayerInputController.HandleAutoDismiss recaptures it on the next frame unless a
			 * VISIBLE panel claims it through ReleasesCursor — the same trap the reconnect
			 * display's Cancel button fell into. Released again in Hide. */
			if (!ReleasesCursor)
			{
				ReleasesCursor = true;
			}
			if (!PlayerInputController.MouseMode)
			{
				PlayerInputController.MouseMode = true;
			}
		}

		/// <summary>Whether the client can queue again now. See <see cref="Client.CanRejoinWorldQueue"/>.</summary>
		private bool CanRejoin() => Client == null || Client.CanRejoinWorldQueue;

		/// <summary>
		/// Leaves the queue, or once the wait has ended, goes back to the login screen. Either way
		/// the session ends: the login server has no route back to character select without
		/// signing in again.
		/// </summary>
		/// <remarks>
		/// Leaving the world queue tells the world server first (<see cref="Client.LeaveWorldQueue"/>),
		/// because it holds a place for a connection that merely dropped and a player who chose to
		/// go must not keep one. Once a world wait has ended the connection is already closed, so
		/// there is nobody to tell. An ended login wait has already returned to the login screen;
		/// its button only closes the panel.
		/// </remarks>
		private void OnLeaveClicked()
		{
			bool ended = State == DisplayState.Ended;
			QueueKind kind = Kind;
			Dismiss();

			if (kind == QueueKind.Login)
			{
				if (!ended)
				{
					Client?.QuitToLogin();
				}
				return;
			}

			if (ended)
			{
				Client?.QuitToLogin();
			}
			else
			{
				Client?.LeaveWorldQueue();
			}
		}

		/// <summary>
		/// Queues again through the world server this client was routed by. Only after the wait
		/// has ended, and only once the old connection has closed.
		/// </summary>
		private void OnRetryClicked()
		{
			if (State != DisplayState.Ended || !WorldQueuePresentation.OffersRetry(Kind) || !CanRejoin())
			{
				return;
			}

			Dismiss();
			Client?.RejoinWorldQueue();
		}

		/// <summary>Enter: try again once a world wait has ended; close an ended login wait.</summary>
		private void OnSubmitKey()
		{
			if (State != DisplayState.Ended)
			{
				return;
			}

			if (WorldQueuePresentation.OffersRetry(Kind))
			{
				OnRetryClicked();
			}
			else
			{
				OnLeaveClicked();
			}
		}

		/// <summary>Escape: back to login, once the wait has ended. Never while queued.</summary>
		private void OnCancelKey()
		{
			if (State == DisplayState.Ended)
			{
				OnLeaveClicked();
			}
		}

		/// <summary>
		/// A dropped connection arms a reconnect; the reconnect display and the loading overlay own
		/// the screen from here. If the reconnect lands, the panel comes back with the next position
		/// the world server sends, and the world server has held this account's place in the line
		/// for the meantime (see <see cref="Client.RejoinWorldQueue"/>).
		/// </summary>
		private void OnReconnectStarted()
		{
			Dismiss();
		}

		/// <summary>See <see cref="OnReconnectStarted"/>.</summary>
		private void OnReconnectAttempt(int attempts, int maxAttempts)
		{
			Dismiss();
		}

		private static void SetText(Label label, string text)
		{
			if (label != null)
			{
				label.text = text ?? string.Empty;
			}
		}

		private static void SetShown(VisualElement element, bool shown)
		{
			if (element != null)
			{
				element.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
			}
		}
	}
}
