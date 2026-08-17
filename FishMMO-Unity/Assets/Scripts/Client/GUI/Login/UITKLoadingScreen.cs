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
		/// Resolves cached elements and subscribes to addressable progress updates.
		/// </summary>
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

			Client.OnReconnectAttempt -= Client_OnReconnectAttempt;
			Client.OnReconnectFailed -= Client_OnReconnectFailed;
		}

		/// <summary>
		/// Updates the loading progress bar and shows/hides the screen.
		/// </summary>
		/// <param name="progress">The current loading progress (0-1).</param>
		public void OnProgressUpdate(float progress)
		{
			// Suppressed after world entry: this fires for any addressable load (item
			// icons, minor prefabs, etc.), not just scene transitions, so without this
			// guard it would flash the full-screen overlay for incidental background
			// loads during normal gameplay. Genuine scene transitions (teleporters,
			// zone changes) go through OnSceneStartLoad/OnSceneStartUnload, which call
			// Show() directly and are never suppressed.
			if (Client.LoadingSuppressed) return;

			if (progress < 1.0f && !Visible)
			{
				Show();
			}
			else if (progress >= 1.0f)
			{
				Hide();
			}

			SetProgress(progress);
		}

		/// <summary>
		/// Shows the loading screen and resets the progress bar.
		/// </summary>
		public override void Show()
		{
			base.Show();

			SetProgress(0.0f);
			if (loadingImage != null)
			{
				loadingImage.style.display = currentSprite != null ? DisplayStyle.Flex : DisplayStyle.None;
			}
		}

		/// <summary>
		/// Resets the loading image and shows the screen on a reconnect attempt.
		/// </summary>
		/// <param name="attempts">The current attempt number.</param>
		/// <param name="maxAttempts">The maximum number of allowed attempts.</param>
		public void Client_OnReconnectAttempt(int attempts, int maxAttempts)
		{
			SetLoadingImage(DefaultLoadingScreenSprite);
			Show();
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
			Show();

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

			if (Details.Scenes.TryGetValue(sld.Name, out WorldSceneDetails details) &&
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
			Hide();
		}

		/// <summary>
		/// Resets the loading image and shows the screen when a scene starts unloading.
		/// </summary>
		/// <param name="startEvent">The event arguments for scene unload start.</param>
		private void OnSceneStartUnload(SceneUnloadStartEventArgs startEvent)
		{
			SetLoadingImage(DefaultLoadingScreenSprite);
			Show();
		}

		/// <summary>
		/// Handles scene unload completion. (No implementation)
		/// </summary>
		/// <param name="endEvent">The event arguments for scene unload end.</param>
		private void OnSceneEndUnload(SceneUnloadEndEventArgs endEvent)
		{
			Hide();
		}
		#endregion
	}
}
