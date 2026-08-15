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

			Client.OnReconnectAttempt += Client_OnReconnectAttempt;
			Client.OnReconnectFailed += Client_OnReconnectFailed;
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
			bool shouldShow = addressableLoadActive || sceneTransitionActive;

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

			base.Hide();
		}

		/// <summary>
		/// Event handler for reconnect attempts. Resets the loading image and shows the screen.
		/// </summary>
		/// <param name="attempts">The current attempt number.</param>
		/// <param name="maxAttempts">The maximum number of allowed attempts.</param>
		public void Client_OnReconnectAttempt(int attempts, int maxAttempts)
		{
			SetLoadingImage(DefaultLoadingScreenSprite);
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