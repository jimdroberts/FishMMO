using FishNet.Managing.Scened;
using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// UI control for displaying loading progress during scene transitions.
	/// Manages a progress bar, scene transition images, and subscribes to
	/// scene load/unload events and reconnect attempt events.
	/// </summary>
	public class UILoadingScreen : UIControl
	{
		[Header("Loading Screen Parameters")]
		/// <summary>
		/// The slider UI element representing the loading progress.
		/// </summary>
		public Slider LoadingProgress;
		/// <summary>
		/// The image UI element for the loading screen background.
		/// </summary>
		public Image LoadingImage;
		/// <summary>
		/// Cache containing details for world scenes, including transition images.
		/// </summary>
		public WorldSceneDetailsCache Details;
		/// <summary>
		/// The default sprite to use for the loading screen.
		/// </summary>
		public Sprite DefaultLoadingScreenSprite;

		/// <summary>
		/// Called when the UI is starting. Subscribes to progress updates, sets the default
		/// loading image, and seeds visibility from the processor's live state.
		/// </summary>
		/// <remarks>
		/// The seed is what makes the overlay cover boot at all. This control lives in
		/// ClientPreboot, which is itself loaded by the drain it is meant to be covering, so
		/// every <see cref="AddressableLoadProcessor.OnProgressUpdate"/> raised before this
		/// Awake is lost — the earliest one it can observe arrives only once some later
		/// chained item finishes. Reading
		/// <see cref="AddressableLoadProcessor.IsLoading"/> directly means the overlay is
		/// already up the moment ClientPreboot activates and stays up until the drain raises
		/// its terminal 1, which lands after the whole ClientPostboot chain has settled.
		/// <para>This makes <c>StartOpen</c> irrelevant for this control — the real loading
		/// state decides. Note it must be left true in the scene regardless, because
		/// <see cref="UIControl.Awake"/> hides the control after this method returns when it
		/// is false, which would undo the <see cref="Show"/> below.</para>
		/// </remarks>
		public override void OnStarting()
		{
			base.OnStarting();

			AddressableLoadProcessor.OnProgressUpdate += OnProgressUpdate;

			SetLoadingImage(DefaultLoadingScreenSprite);

			addressableLoadActive = AddressableLoadProcessor.IsLoading;
			RefreshVisibility(forceRefresh: true);
		}

		/// <summary>
		/// Called when the UI is being destroyed. Unsubscribes from progress updates.
		/// </summary>
		public override void OnDestroying()
		{
			base.OnDestroying();

			AddressableLoadProcessor.OnProgressUpdate -= OnProgressUpdate;
		}

		/// <summary>
		/// Called when the client is set. Subscribes to scene and reconnect events.
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
		/// Called when the client is unset. Unsubscribes from scene and reconnect events.
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
		/// once at startup: the control's GameObject is active in the scene but its child
		/// elements are not, so the no-op path would leave <see cref="LoadingImage"/> switched
		/// off behind a technically-visible overlay.
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
		/// Event handler for aggregate Addressable progress. Updates the bar and tracks
		/// whether a load is still running.
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

			/* Each element is driven independently. Bailing out on a missing slider used to
			 * skip the image as well, so one unwired reference blanked the whole overlay. */
			SetProgress(0.0f);
			if (LoadingImage != null)
			{
				LoadingImage.gameObject.SetActive(LoadingImage.sprite != null);
			}
		}

		/// <summary>
		/// Sets the current loading image sprite, switching the element off when there is none.
		/// </summary>
		/// <param name="sprite">The sprite to display, or null to clear.</param>
		private void SetLoadingImage(Sprite sprite)
		{
			if (LoadingImage == null)
			{
				return;
			}
			LoadingImage.sprite = sprite;
			LoadingImage.gameObject.SetActive(sprite != null);
		}

		/// <summary>
		/// Sets the progress bar to the supplied normalised progress.
		/// </summary>
		/// <param name="progress">The progress in the range 0-1.</param>
		private void SetProgress(float progress)
		{
			if (LoadingProgress != null)
			{
				LoadingProgress.value = Mathf.Clamp01(progress);
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
			worldEntryActive = false;

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
		}

		/// <summary>
		/// Event handler for reconnect attempts. Resets the loading image and shows the screen.
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
		/// Event handler for reconnect failure. Hides the loading screen.
		/// </summary>
		public void Client_OnReconnectFailed()
		{
			Hide();
		}

		#region Scene Events
		/// <summary>
		/// Event handler for when a scene starts loading. Updates the loading image based on scene details.
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

			if (Details != null &&
				Details.Scenes != null &&
				Details.Scenes.TryGetValue(sld.Name, out WorldSceneDetails details) &&
				details.SceneTransitionImage != null)
			{
				SetLoadingImage(details.SceneTransitionImage);
			}
		}

		/// <summary>
		/// Event handler for scene load progress updates. Updates the loading progress bar.
		/// </summary>
		/// <param name="percentEvent">The event arguments for scene load percent change.</param>
		private void OnSceneProgressUpdate(SceneLoadPercentEventArgs percentEvent)
		{
			if (LoadingProgress != null)
			{
				LoadingProgress.value = percentEvent.Percent;
			}
		}

		/// <summary>
		/// Event handler for when a scene finishes loading. Hides the loading screen.
		/// </summary>
		/// <param name="endEvent">The event arguments for scene load end.</param>
		private void OnSceneEndLoad(SceneLoadEndEventArgs endEvent)
		{
			sceneTransitionActive = false;
			RefreshVisibility();
		}

		/// <summary>
		/// Event handler for when a scene starts unloading. Resets the loading image and shows the screen.
		/// </summary>
		/// <param name="startEvent">The event arguments for scene unload start.</param>
		private void OnSceneStartUnload(SceneUnloadStartEventArgs startEvent)
		{
			SetLoadingImage(DefaultLoadingScreenSprite);
			sceneTransitionActive = true;
			RefreshVisibility();
		}

		/// <summary>
		/// Event handler for when a scene finishes unloading.
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