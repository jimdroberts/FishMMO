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
		/// <summary>
		/// The name of the loading image VisualElement in the UI.
		/// </summary>
		private const string LOADING_IMAGE_NAME = "loading-image";
		/// <summary>
		/// The name of the loading progress fill VisualElement in the UI.
		/// </summary>
		private const string LOADING_PROGRESS_FILL_NAME = "loading-progress-fill";

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
		/// <summary>
		/// The currently displayed loading screen sprite, or null if none is set.
		/// </summary>
		private Sprite currentSprite;

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
			}

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
			bool shouldShow = addressableLoadActive || sceneTransitionActive || reconnectPendingActive;

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
			base.Show();

			SetProgress(0.0f);
			if (loadingImage != null)
			{
				loadingImage.style.display = currentSprite != null ? DisplayStyle.Flex : DisplayStyle.None;
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
		/// </remarks>
		public override void Hide()
		{
			addressableLoadActive = false;
			sceneTransitionActive = false;
			reconnectPendingActive = false;

			base.Hide();
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
		/// Raises the overlay as soon as a reconnect is armed, before its delay elapses.
		/// </summary>
		/// <remarks>See <see cref="reconnectPendingActive"/>.</remarks>
		public void Client_OnReconnectPending()
		{
			SetLoadingImage(DefaultLoadingScreenSprite);
			reconnectPendingActive = true;
			RefreshVisibility();
		}

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
		/// Sets the progress bar fill width to the supplied normalised progress.
		/// </summary>
		/// <param name="progress">The progress in the range 0-1.</param>
		private void SetProgress(float progress)
		{
			if (loadingProgressFill != null)
			{
				loadingProgressFill.style.width = Length.Percent(Mathf.Clamp01(progress) * 100.0f);
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

			// Details is an Inspector reference and the overlay must survive it being unset:
			// an NRE here escapes into FishNet's SceneManager event invocation, which aborts
			// the remaining OnLoadStart subscribers mid-transition. The uGUI UILoadingScreen
			// already guards the same lookup.
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
