using FishNet.Managing.Scened;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit implementation of the loading screen. Displays a transition image and a
	/// progress bar driven by addressable load progress and FishNet scene load events.
	/// </summary>
	public class UITKLoadingScreen : UITKControl
	{
		/// <summary>Draw order tier for this panel. See <see cref="UITKPanelLayer"/>.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.System;

		/// <summary>
		/// The name of the loading image VisualElement in the UI.
		/// </summary>
		private const string LOADING_IMAGE_NAME = "loading-image";
		/// <summary>
		/// The name of the loading progress fill VisualElement in the UI.
		/// </summary>
		private const string LOADING_PROGRESS_FILL_NAME = "loading-progress-fill";
		/// <summary>Name of the percentage label drawn on the progress bar.</summary>
		private const string LOADING_PROGRESS_TEXT_NAME = "loading-progress-text";
		/// <summary>Name of the hint line under the progress bar.</summary>
		private const string LOADING_HINT_NAME = "loading-hint";
		/// <summary>
		/// The name of the optional status Label in the UI.
		/// </summary>
		private const string LOADING_STATUS_NAME = "loading-status";
		/// <summary>Name of the container holding the escape hatch.</summary>
		private const string LOADING_ACTIONS_NAME = "loading-actions";
		/// <summary>Name of the label explaining why the escape hatch appeared.</summary>
		private const string LOADING_STUCK_NAME = "loading-stuck";
		/// <summary>Name of the "Return to Login" button.</summary>
		private const string LOADING_RETURN_BUTTON_NAME = "loading-return-btn";

		/// <summary>
		/// How long the overlay may hold the screen before the escape hatch is offered.
		/// </summary>
		/// <remarks>
		/// Longer than any honest transition. A world entry is three round trips and an
		/// Addressable preload; a scene load on a cold disk is seconds. A reconnect backoff can
		/// legitimately run for minutes, but that case raises <see cref="UITKReconnectDisplay"/>
		/// on top of this panel with a Cancel button of its own, so the player is not stuck
		/// there — this is the backstop for when nothing else is on screen.
		/// <para>
		/// Deliberately generous rather than tight: offering a way out is harmless, but offering
		/// it during a slow-but-working load invites the player to abandon a login that was about
		/// to succeed.
		/// </para>
		/// </remarks>
		private const float EscapeHatchDelaySeconds = 45.0f;

		/// <summary>
		/// Cache containing details for world scenes, including transition images.
		/// </summary>
		public WorldSceneDetailsCache Details;

		/// <summary>
		/// The default sprite to use for the loading screen.
		/// </summary>
		public Sprite DefaultLoadingScreenSprite;

		private VisualElement loadingImage;
		private VisualElement loadingProgressFill;
		/// <summary>Percentage label drawn on the progress bar.</summary>
		private Label loadingProgressText;
		/// <summary>Hint line under the progress bar.</summary>
		private Label loadingHint;
		/// <summary>
		/// Optional status line, used to explain a wait the player cannot otherwise see a
		/// reason for. Null when the document does not define one.
		/// </summary>
		private Label loadingStatus;
		/// <summary>Container holding the escape hatch; collapsed until it is offered.</summary>
		private VisualElement loadingActions;
		/// <summary>Explains why the escape hatch has appeared.</summary>
		private Label loadingStuck;
		/// <summary>The "Return to Login" button.</summary>
		private Button loadingReturnButton;
		/// <summary>
		/// The currently displayed loading screen sprite, or null if none is set.
		/// </summary>
		private Sprite currentSprite;

		/// <summary>
		/// Unscaled time at which the overlay last became visible, or -1 when it is not up.
		/// </summary>
		private float overlayShownAtUnscaled = -1.0f;

		/// <summary>
		/// True once the escape hatch has been offered for the current showing.
		/// </summary>
		/// <remarks>
		/// Latched rather than recomputed. Taking the button away again because a stalled load
		/// briefly ticked forward would be worse than leaving it: the player has already been told
		/// the transition is in trouble, and a control that appears and vanishes is not one anybody
		/// trusts enough to press.
		/// </remarks>
		private bool escapeHatchOffered;

		/// <summary>
		/// Resolves cached elements, subscribes to addressable progress updates, and seeds
		/// visibility from the processor's live state.
		/// </summary>
		/// <remarks>
		/// The seed matters whenever this control lives in a scene the processor is itself
		/// loading: every <see cref="AddressableLoadProcessor.OnProgressUpdate"/> raised
		/// before this Awake is lost, so the earliest tick it can observe arrives only once
		/// some later chained item finishes — leaving boot uncovered. Reading
		/// <see cref="AddressableLoadProcessor.IsLoading"/> starts it from the truth instead.
		/// Note this only holds when <c>StartOpen</c> is true, since
		/// <see cref="UITKControl.Awake"/> hides the control after this method returns
		/// otherwise.
		/// </remarks>
		public override void OnStarting()
		{
			base.OnStarting();

			if (Root != null)
			{
				loadingImage = Root.Q<VisualElement>(LOADING_IMAGE_NAME);
				loadingProgressFill = Root.Q<VisualElement>(LOADING_PROGRESS_FILL_NAME);
				loadingProgressText = Root.Q<Label>(LOADING_PROGRESS_TEXT_NAME);
				loadingHint = Root.Q<Label>(LOADING_HINT_NAME);
				loadingStatus = Root.Q<Label>(LOADING_STATUS_NAME);
				loadingActions = Root.Q<VisualElement>(LOADING_ACTIONS_NAME);
				loadingStuck = Root.Q<Label>(LOADING_STUCK_NAME);
				loadingReturnButton = Root.Q<Button>(LOADING_RETURN_BUTTON_NAME);

				/* Assigned, not accumulated: OnStarting runs again on every tree rebuild and the
				 * button is a new element each time. A rebuild that reused one would otherwise
				 * stack a second handler and quit to login twice on a single click. */
				if (loadingReturnButton != null)
				{
					loadingReturnButton.clicked += OnClick_ReturnToLogin;
				}
			}

			SetStatus(null);
			ApplyEscapeHatch();

			/* Unsubscribe first. OnStarting runs again on every visual tree rebuild — see the
			 * button wiring above, which is written as an assignment for the same reason — and
			 * this is a STATIC event, so unlike the elements it does not get thrown away with the
			 * tree. A bare += therefore added one more handler per hide/show, and this overlay is
			 * hidden and re-shown on every scene transition and every reconnect. OnDestroying
			 * removes exactly one, so the rest outlive the panel: each surviving copy re-runs
			 * RefreshVisibility and SetProgress on every progress tick for the rest of the
			 * session. The handler is a method group rather than a lambda so the -= matches. */
			AddressableLoadProcessor.OnProgressUpdate -= OnProgressUpdate;
			AddressableLoadProcessor.OnProgressUpdate += OnProgressUpdate;

			SetLoadingImage(DefaultLoadingScreenSprite);

			addressableLoadActive = AddressableLoadProcessor.IsLoading;
			RefreshVisibility(forceRefresh: true);
		}

		/// <summary>
		/// Unsubscribes from addressable progress updates when the control is destroyed.
		/// </summary>
		public override void OnDestroying()
		{
			base.OnDestroying();

			AddressableLoadProcessor.OnProgressUpdate -= OnProgressUpdate;
		}

		/// <summary>
		/// Subscribes to scene and reconnect events when the client is injected.
		/// </summary>
		public override void OnClientSet()
		{
			Client.NetworkManager.SceneManager.OnLoadStart += OnSceneStartLoad;
			Client.NetworkManager.SceneManager.OnLoadPercentChange += OnSceneProgressUpdate;
			Client.NetworkManager.SceneManager.OnLoadEnd += OnSceneEndLoad;
			Client.NetworkManager.SceneManager.OnUnloadStart += OnSceneStartUnload;
			Client.NetworkManager.SceneManager.OnUnloadEnd += OnSceneEndUnload;

			Client.OnReconnectPending += Client_OnReconnectPending;
			Client.OnReconnectAttempt += Client_OnReconnectAttempt;
			Client.OnReconnectFailed += Client_OnReconnectFailed;
			Client.OnEnterGameWorld += Client_OnEnterGameWorld;
		}

		/// <summary>
		/// Unsubscribes from scene and reconnect events when the client is cleared.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.SceneManager.OnLoadStart -= OnSceneStartLoad;
			Client.NetworkManager.SceneManager.OnLoadPercentChange -= OnSceneProgressUpdate;
			Client.NetworkManager.SceneManager.OnLoadEnd -= OnSceneEndLoad;
			Client.NetworkManager.SceneManager.OnUnloadStart -= OnSceneStartUnload;
			Client.NetworkManager.SceneManager.OnUnloadEnd -= OnSceneEndUnload;

			Client.OnReconnectPending -= Client_OnReconnectPending;
			Client.OnReconnectAttempt -= Client_OnReconnectAttempt;
			Client.OnReconnectFailed -= Client_OnReconnectFailed;
			Client.OnEnterGameWorld -= Client_OnEnterGameWorld;
		}

		/// <summary>
		/// True while an Addressable batch is driving the overlay.
		/// </summary>
		private bool addressableLoadActive;
		/// <summary>
		/// True while a FishNet scene load/unload or a reconnect attempt is driving the overlay.
		/// </summary>
		private bool sceneTransitionActive;
		/// <summary>
		/// True from the moment a reconnect is armed until the overlay is dismissed.
		/// </summary>
		/// <remarks>
		/// A scene-to-scene transfer is a deliberate disconnect: the scene server unloads the
		/// client's scene and drops it, and the client returns through the world server. The
		/// unload finishes first, so <see cref="OnSceneEndUnload"/> cleared the only active
		/// driver and the overlay came down over an emptied world — the player then watched
		/// nothing at all until the retry fired and raised it again.
		/// <para>
		/// Arming is the earliest honest signal that the transition is still running, so it gets
		/// its own driver rather than being folded into <see cref="sceneTransitionActive"/>,
		/// which the unload events own and will clear. Cleared only by <see cref="Hide"/> —
		/// reached on reconnect failure, and on world entry via
		/// <c>Client.DismissLoadingScreen</c> once the player character actually exists.
		/// </para>
		/// </remarks>
		private bool reconnectPendingActive;

		/// <summary>
		/// True from the moment the scene server accepts this client until the player character
		/// actually exists in the world.
		/// </summary>
		/// <remarks>
		/// World entry is three separate waits with gaps between them, and the other drivers
		/// only cover the waits. The FishNet scene load ends before the server has been told
		/// the client is in it, and the Addressable world preload ends before the server has
		/// spawned the character — so on each of those boundaries every driver was momentarily
		/// clear and the overlay came down over a half-built world with no player in it, for a
		/// full network round trip each time. The second gap is the worse of the two: the
		/// progress bar reaches 100%, the screen vanishes, and the player looks at an empty
		/// scene until the spawn lands.
		/// <para>
		/// <c>OnEnterGameWorld</c> is raised on SceneLoginSuccess, which precedes the scene
		/// load request, so this driver spans the whole entry. It is cleared only by
		/// <see cref="Hide"/> — reached from <c>Client.DismissLoadingScreen</c> once the local
		/// character starts, from the quit-to-login teardown, and on reconnect failure. Every
		/// way world entry can fail ends at one of those, and the servers bound the wait with
		/// their own scene-handshake and residency watchdogs.
		/// </para>
		/// </remarks>
		private bool worldEntryActive;

		/// <summary>
		/// Shows the overlay while any driver is active and hides it only once every driver
		/// has finished.
		/// </summary>
		/// <remarks>
		/// The two drivers are independent and overlap constantly — entering the world runs
		/// an Addressable preload and a FishNet scene load at the same time. Letting either
		/// one call Hide() directly meant whichever finished first pulled the overlay out
		/// from under the other, exposing a half-built scene.
		/// </remarks>
		/// <param name="forceRefresh">
		/// Applies the target state even when <see cref="Visible"/> already matches it. Needed
		/// once at startup so the panel's child elements are synced to the seeded state rather
		/// than left at their authored defaults.
		/// </param>
		private void RefreshVisibility(bool forceRefresh = false)
		{
			bool shouldShow = addressableLoadActive || sceneTransitionActive || reconnectPendingActive || worldEntryActive;

			if (shouldShow && (forceRefresh || !Visible))
			{
				Show();
			}
			else if (!shouldShow && (forceRefresh || Visible))
			{
				Hide();
			}
		}

		/// <summary>
		/// Updates the loading progress bar and tracks whether an Addressable load is running.
		/// </summary>
		/// <remarks>
		/// <see cref="AddressableLoadProcessor.OnProgressUpdate"/> is a global aggregate: it
		/// reports on every load in flight, not just the one this screen cares about. That
		/// is exactly what an overall loading bar wants, but it means completion here says
		/// "the queue is empty", not "the transition is done" — hence the driver flags.
		/// </remarks>
		/// <param name="progress">The current loading progress (0-1).</param>
		public void OnProgressUpdate(float progress)
		{
			/* Incidental background asset loading. Once the player is in the world this
			 * must not touch the overlay at all — not to show it, and not to hide it:
			 * an unrelated Addressable load finishing would otherwise pull the loading
			 * screen out from under a genuine scene transition that is still running. */
			if (Client.LoadingSuppressed) return;

			addressableLoadActive = progress < 1.0f;
			RefreshVisibility();

			SetProgress(progress);
		}

		/// <summary>
		/// Shows the loading screen and resets the progress bar.
		/// </summary>
		public override void Show()
		{
			/* No LoadingSuppressed check here. Scene transitions and reconnects call this
			 * directly and must always be able to show the overlay; only the incidental
			 * Addressable path in OnProgressUpdate is suppressed. */
			// Read before base.Show(), which sets Visible.
			bool wasVisible = Visible;

			base.Show();

			/* Start the escape-hatch clock on the transition into visibility, not on every call.
			 * The drivers call Show() repeatedly — every Addressable progress tick can reach it —
			 * and restarting the clock each time meant the deadline could never be reached by an
			 * overlay that was busily reporting progress on a transfer that was going nowhere. */
			if (!wasVisible)
			{
				this.overlayShownAtUnscaled = Time.unscaledTime;
				this.escapeHatchOffered = false;
			}

			SetProgress(0.0f);
			SetHint(null);
			ApplyEscapeHatch();
			if (loadingImage != null)
			{
				loadingImage.style.display = currentSprite != null ? DisplayStyle.Flex : DisplayStyle.None;
			}
		}

		/// <summary>
		/// Re-applies the escape hatch after the visual tree was rebuilt.
		/// </summary>
		/// <remarks>
		/// The overlay can be re-shown while the hatch is already offered — a reconnect attempt
		/// lands on an overlay that has been up for a minute — and the rebuilt tree carries the
		/// UXML's collapsed default, so without this the only control on the screen would silently
		/// disappear at the worst possible moment.
		/// </remarks>
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();
			ApplyEscapeHatch();
		}

		/// <inheritdoc/>
		protected override void OnAfterShow()
		{
			base.OnAfterShow();
			ApplyEscapeHatch();
		}

		/// <summary>
		/// Offers the escape hatch once the overlay has outlasted any honest transition, and
		/// keeps the deferred login notices moving.
		/// </summary>
		/// <remarks>
		/// This is the invariant this panel is responsible for: the loading overlay covers every
		/// other panel in the client, including the modals, so for as long as it is up it <b>is</b>
		/// the UI. Four independent drivers raise it and two of them —
		/// <see cref="worldEntryActive"/> and <see cref="reconnectPendingActive"/> — are cleared
		/// only by <see cref="Hide(bool)"/>, so anything that leaves one set holds the screen with
		/// nothing on it to press. Rather than enumerate the ways that can happen, the overlay
		/// gives itself a deadline and then offers a way out.
		/// </remarks>
		protected override void OnTick()
		{
			base.OnTick();

			// Login-flow notices are refused while another dialog is up; see LoginNotice.
			LoginNotice.Pump();

			/* Client null means boot: the overlay is covering the initial Addressable load, there
			 * is no session to quit and no login panel to return to yet. Offering the button there
			 * would be offering nothing. */
			if (!Visible || Client == null || this.escapeHatchOffered || this.overlayShownAtUnscaled < 0.0f)
			{
				return;
			}

			if (Time.unscaledTime - this.overlayShownAtUnscaled < EscapeHatchDelaySeconds)
			{
				return;
			}

			this.escapeHatchOffered = true;
			ApplyEscapeHatch();
		}

		/// <summary>
		/// Shows or collapses the escape hatch to match <see cref="escapeHatchOffered"/>.
		/// </summary>
		/// <remarks>
		/// Idempotent, and tolerates elements that are still null: it runs from
		/// <see cref="OnStarting"/>, <see cref="Show"/>, <see cref="OnAfterShow"/>,
		/// <see cref="OnAfterStarting"/> and <see cref="OnTick"/>, and on the very first show the
		/// visual tree does not exist yet.
		/// </remarks>
		private void ApplyEscapeHatch()
		{
			if (!this.authoredReleasesCursorCaptured)
			{
				this.authoredReleasesCursorCaptured = true;
				this.authoredReleasesCursor = ReleasesCursor;
			}

			if (loadingActions != null)
			{
				loadingActions.style.display = this.escapeHatchOffered ? DisplayStyle.Flex : DisplayStyle.None;
			}

			if (loadingStuck != null)
			{
				loadingStuck.text = this.escapeHatchOffered
					? "This is taking longer than it should."
					: string.Empty;
			}

			/* A button nobody can click is not an escape hatch. The overlay is normally shown over
			 * gameplay with the cursor locked, and the panel is authored ReleasesCursor: 0 because
			 * declaring it statically would release the cursor on every routine scene load.
			 *
			 * Setting MouseMode alone was not enough, and produced exactly the dead end the hatch
			 * exists to prevent: PlayerInputController.HandleAutoDismiss runs every frame and
			 * takes the cursor straight back whenever no VISIBLE panel claims it through
			 * ReleasesCursor. This overlay did not claim it, so the cursor was released here and
			 * recaptured on the very next frame, leaving a Return to Login button on screen that
			 * could never be clicked. Claiming the cursor for exactly as long as the hatch is
			 * offered resolves both halves; it is handed back in Hide(), which clears the flag
			 * along with the offer. UITKChat claims the cursor the same way while its input has
			 * focus. */
			bool wantsCursor = this.authoredReleasesCursor || this.escapeHatchOffered;
			if (ReleasesCursor != wantsCursor)
			{
				ReleasesCursor = wantsCursor;
			}

			if (this.escapeHatchOffered)
			{
				PlayerInputController.MouseMode = true;
			}
		}

		/// <summary>
		/// The <see cref="UITKControl.ReleasesCursor"/> value the scene authored, before the
		/// escape hatch started borrowing it.
		/// </summary>
		/// <remarks>
		/// Captured rather than assumed false, so a scene that deliberately gives this overlay the
		/// cursor keeps it once the hatch is withdrawn. Read on the first
		/// <see cref="ApplyEscapeHatch"/>, which runs from <see cref="OnStarting"/> before
		/// anything here has written to the flag.
		/// </remarks>
		private bool authoredReleasesCursor;

		/// <summary>True once <see cref="authoredReleasesCursor"/> holds the authored value.</summary>
		private bool authoredReleasesCursorCaptured;

		/// <summary>
		/// Abandons whatever the overlay was waiting for and returns the player to the login screen.
		/// </summary>
		/// <remarks>
		/// <c>QuitToLogin</c> rather than a bare <see cref="Hide(bool)"/>. Hiding alone would
		/// reveal whichever panel happened to be underneath — which, on every path that reaches
		/// this button, is none: the login panel has hidden itself, character select hides on
		/// Stopped, and the world is either half-built or gone. The teardown is what puts a panel
		/// back and it is the route every login panel already implements.
		/// </remarks>
		public void OnClick_ReturnToLogin()
		{
			// Clear the drivers first so the teardown's own RefreshVisibility cannot raise the
			// overlay straight back up over the login screen. See OnQuitToLogin.
			Hide();

			if (Client != null)
			{
				Client.QuitToLogin();
			}
		}

		/// <summary>
		/// Hides the loading screen and clears the driver flags.
		/// </summary>
		/// <remarks>
		/// Clearing here keeps the flags honest when something outside this class hides the
		/// overlay (<c>Client.DismissLoadingScreen</c> does, on local character start).
		/// Stale flags would otherwise let the next refresh pop the overlay back up over
		/// live gameplay.
		/// <para>
		/// <b>This must override <c>Hide(bool)</c>, not <c>Hide()</c>.</b> <c>Hide()</c> is
		/// <c>Hide(IsAlwaysOpen)</c>, and the quit-to-login teardown calls <c>Hide(false)</c>
		/// directly — so with the clearing on the parameterless overload the teardown took the
		/// panel down without clearing a single driver, and <see cref="OnQuitToLogin"/> then
		/// re-ran <see cref="RefreshVisibility"/>, saw <see cref="worldEntryActive"/> still set
		/// from the session that had just ended, and put the overlay straight back up. The result
		/// was a full-screen loading overlay covering the login screen with no driver left that
		/// could ever take it down and, before the escape hatch below, no button on it: quitting
		/// to login from the world was itself a route to the unrecoverable empty screen.
		/// </para>
		/// </remarks>
		/// <param name="overrideIsAlwaysOpen">When true, the call is a no-op.</param>
		public override void Hide(bool overrideIsAlwaysOpen)
		{
			if (overrideIsAlwaysOpen || Document == null)
			{
				// Refused; the overlay is still up, so its drivers still describe the truth.
				base.Hide(overrideIsAlwaysOpen);
				return;
			}

			addressableLoadActive = false;
			sceneTransitionActive = false;
			reconnectPendingActive = false;
			worldEntryActive = false;

			// The status line explains one specific wait; it must not survive into the next
			// one, which is usually an ordinary scene load with nothing to explain.
			SetStatus(null);

			// The next showing gets its own deadline and its own decision about the hatch.
			this.overlayShownAtUnscaled = -1.0f;
			this.escapeHatchOffered = false;
			ApplyEscapeHatch();

			base.Hide(overrideIsAlwaysOpen);
		}

		/// <summary>
		/// Writes the status line, hiding it entirely when there is nothing to say.
		/// </summary>
		/// <remarks>
		/// Optional by design: the label is looked up by name and older documents that predate
		/// it simply resolve to null. A missing label costs the explanation, not the overlay.
		/// </remarks>
		/// <param name="text">Message to display, or null/empty to hide the line.</param>
		private void SetStatus(string text)
		{
			if (loadingStatus == null)
			{
				return;
			}

			bool hasText = !string.IsNullOrEmpty(text);
			loadingStatus.text = hasText ? text : string.Empty;
			loadingStatus.style.display = hasText ? DisplayStyle.Flex : DisplayStyle.None;
		}

		/// <summary>
		/// Re-asserts the overlay's own state after the quit-to-login teardown.
		/// </summary>
		/// <remarks>
		/// The base handler forces visibility straight from <c>CloseOnQuitToMenu</c>, and it does
		/// so through a path that bypasses this control's <see cref="Show"/>/<see cref="Hide"/>
		/// overrides — so the driver flags are left exactly as they were while the panel is
		/// switched underneath them. Either half of that mismatch is a visible fault: flags left
		/// set behind a hidden panel pop the overlay back up on the next progress tick, and a
		/// panel forced visible with no driver set has nothing that will ever take it down.
		/// Re-running the normal decision leaves the two in agreement whichever way the flag is
		/// configured.
		/// </remarks>
		public override void OnQuitToLogin()
		{
			base.OnQuitToLogin();

			RefreshVisibility(forceRefresh: true);
		}

		/// <summary>
		/// Holds the overlay up for the whole of world entry.
		/// </summary>
		/// <remarks>See <see cref="worldEntryActive"/>.</remarks>
		public void Client_OnEnterGameWorld()
		{
			SetLoadingImage(DefaultLoadingScreenSprite);
			worldEntryActive = true;
			RefreshVisibility();
		}

		/// <summary>
		/// Raises the overlay as soon as a reconnect is armed, before its delay elapses.
		/// </summary>
		/// <remarks>See <see cref="reconnectPendingActive"/>.</remarks>
		public void Client_OnReconnectPending()
		{
			SetLoadingImage(DefaultLoadingScreenSprite);
			reconnectPendingActive = true;
			RefreshVisibility();

			/* Say what the overlay is waiting for. A lost connection holds this screen for the
			 * whole backoff — up to several minutes across ten attempts — with a static image
			 * and a progress bar that never moves, which is indistinguishable from a hang.
			 *
			 * A scene handoff arrives here too, because that is how a zone change, a channel
			 * switch and a cross-scene respawn are implemented, and it is not an outage — it
			 * retries within a quarter of a second. Labelling a routine teleport "connection
			 * lost" would be worse than saying nothing, so those stay silent and keep the plain
			 * loading overlay.
			 *
			 * The text is set after RefreshVisibility because Show() is what resets the panel. */
			SetStatus(IsSceneHandoff() ? null : "Connection lost. Reconnecting...");
		}

		/// <summary>
		/// Whether the reconnect currently running is a deliberate scene handoff rather than a
		/// dropped connection.
		/// </summary>
		/// <remarks>See <see cref="ClientConnectionManager.IsSceneHandoffReconnect"/>.</remarks>
		private bool IsSceneHandoff() => Client?.Connection?.IsSceneHandoffReconnect ?? false;

		/// <summary>
		/// Resets the loading image and shows the screen on a reconnect attempt.
		/// </summary>
		/// <param name="attempts">The current attempt number.</param>
		/// <param name="maxAttempts">The maximum number of allowed attempts.</param>
		public void Client_OnReconnectAttempt(int attempts, int maxAttempts)
		{
			SetLoadingImage(DefaultLoadingScreenSprite);
			reconnectPendingActive = true;
			sceneTransitionActive = true;
			RefreshVisibility();

			// A scene handoff succeeds on its first attempt; anything past that is a genuine
			// failure and worth naming even when the drop that started it was deliberate.
			if (IsSceneHandoff() && attempts <= 1)
			{
				SetStatus(null);
				return;
			}

			SetStatus($"Reconnecting... (attempt {attempts} of {maxAttempts})");
		}

		/// <summary>
		/// Hides the loading screen on reconnect failure.
		/// </summary>
		public void Client_OnReconnectFailed()
		{
			Hide();
		}

		/// <summary>
		/// Sets the current loading image sprite as the background of the image element.
		/// </summary>
		/// <param name="sprite">The sprite to display, or null to clear.</param>
		private void SetLoadingImage(Sprite sprite)
		{
			currentSprite = sprite;
			if (loadingImage != null)
			{
				loadingImage.style.backgroundImage = sprite != null ? new StyleBackground(sprite) : new StyleBackground();
				loadingImage.style.display = sprite != null ? DisplayStyle.Flex : DisplayStyle.None;
			}
		}

		/// <summary>
		/// Sets the hint line under the progress bar, hiding it when there is nothing to say.
		/// </summary>
		/// <param name="text">The hint, or null to hide the line.</param>
		/// <remarks>
		/// Collapsed rather than left blank so the block above it stays vertically centred on
		/// itself — an empty label still occupies a line and shifts everything off-centre.
		/// </remarks>
		private void SetHint(string text)
		{
			if (loadingHint == null)
			{
				return;
			}
			loadingHint.text = text ?? string.Empty;
			loadingHint.style.display = string.IsNullOrWhiteSpace(text) ? DisplayStyle.None : DisplayStyle.Flex;
		}

		/// <summary>
		/// Sets the progress bar fill width to the supplied normalised progress.
		/// </summary>
		/// <param name="progress">The progress in the range 0-1.</param>
		private void SetProgress(float progress)
		{
			float clamped = Mathf.Clamp01(progress);

			if (loadingProgressFill != null)
			{
				loadingProgressFill.style.width = Length.Percent(clamped * 100.0f);
			}

			if (loadingProgressText != null)
			{
				/* Rounded down, not to nearest. Showing "100%" while the bar is still moving is
				 * the one reading a loading screen must never give — a player who sees it and
				 * then waits assumes the client has hung. */
				loadingProgressText.text = $"{Mathf.FloorToInt(clamped * 100.0f)}%";
			}
		}

		#region Scene Events
		/// <summary>
		/// Updates the loading image based on scene details when a scene starts loading.
		/// </summary>
		/// <param name="startEvent">The event arguments for scene load start.</param>
		private void OnSceneStartLoad(SceneLoadStartEventArgs startEvent)
		{
			sceneTransitionActive = true;
			RefreshVisibility();

			SceneLookupData[] lookupData = startEvent.QueueData.SceneLoadData.SceneLookupDatas;

			if (lookupData == null ||
				lookupData.Length < 1)
			{
				return;
			}

			SceneLookupData sld = lookupData[0];
			if (sld == null)
			{
				return;
			}

			/* Naming the destination turns an anonymous wait into a legible one. The player
			 * already knows they clicked something; the useful fact is where they are going,
			 * and it costs nothing since the lookup data is in hand. */
			SetHint(string.IsNullOrWhiteSpace(sld.Name) ? null : $"Entering {sld.Name}");

			// Details is an Inspector reference and the overlay must survive it being unset:
			// an NRE here escapes into FishNet's SceneManager event invocation, which aborts
			// the remaining OnLoadStart subscribers mid-transition.
			if (Details != null &&
				Details.Scenes != null &&
				Details.Scenes.TryGetValue(sld.Name, out WorldSceneDetails details) &&
				details.SceneTransitionImage != null)
			{
				SetLoadingImage(details.SceneTransitionImage);
			}
		}

		/// <summary>
		/// Updates the loading progress bar on scene load progress.
		/// </summary>
		/// <param name="percentEvent">The event arguments for scene load percent change.</param>
		private void OnSceneProgressUpdate(SceneLoadPercentEventArgs percentEvent)
		{
			SetProgress(percentEvent.Percent);
		}

		/// <summary>
		/// Hides the loading screen when a scene finishes loading.
		/// </summary>
		/// <param name="endEvent">The event arguments for scene load end.</param>
		private void OnSceneEndLoad(SceneLoadEndEventArgs endEvent)
		{
			sceneTransitionActive = false;
			RefreshVisibility();
		}

		/// <summary>
		/// Resets the loading image and shows the screen when a scene starts unloading.
		/// </summary>
		/// <param name="startEvent">The event arguments for scene unload start.</param>
		private void OnSceneStartUnload(SceneUnloadStartEventArgs startEvent)
		{
			SetLoadingImage(DefaultLoadingScreenSprite);
			sceneTransitionActive = true;
			RefreshVisibility();
		}

		/// <summary>
		/// Handles scene unload completion.
		/// </summary>
		/// <param name="endEvent">The event arguments for scene unload end.</param>
		private void OnSceneEndUnload(SceneUnloadEndEventArgs endEvent)
		{
			sceneTransitionActive = false;
			RefreshVisibility();
		}
		#endregion
	}
}
