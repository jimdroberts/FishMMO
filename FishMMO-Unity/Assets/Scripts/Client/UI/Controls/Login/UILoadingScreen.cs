using FishNet.Managing.Scened;
using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;

namespace FishMMO.Client
{
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
		/// Called when the UI is starting. Subscribes to progress updates and sets the default loading image.
		/// </summary>
		public override void OnStarting()
		{
			base.OnStarting();

			AddressableLoadProcessor.OnProgressUpdate += OnProgressUpdate;

			LoadingImage.sprite = DefaultLoadingScreenSprite;
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
		/// Event handler for progress updates. Updates the loading progress bar and shows/hides the screen.
		/// </summary>
		/// <param name="progress">The current loading progress (0-1).</param>
		public void OnProgressUpdate(float progress)
		{
			if (progress < 1.0f && !Visible)
			{
				Show();
			}
			else if (progress >= 1.0f)
			{
				Hide();
			}

			if (LoadingProgress != null)
			{
				LoadingProgress.value = progress;
			}
		}

		/// <summary>
		/// Shows the loading screen and resets the progress bar.
		/// </summary>
		public override void Show()
		{
			base.Show();

			if (LoadingProgress == null)
			{
				return;
			}
			LoadingProgress.value = 0;
			LoadingImage.gameObject.SetActive(LoadingImage.sprite != null);
		}

		/// <summary>
		/// Event handler for reconnect attempts. Resets the loading image and shows the screen.
		/// </summary>
		/// <param name="attempts">The current attempt number.</param>
		/// <param name="maxAttempts">The maximum number of allowed attempts.</param>
		public void Client_OnReconnectAttempt(byte attempts, byte maxAttempts)
		{
			LoadingImage.sprite = DefaultLoadingScreenSprite;
			Show();
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
				LoadingImage.sprite = details.SceneTransitionImage;
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
			Hide();
		}

		/// <summary>
		/// Event handler for when a scene starts unloading. Resets the loading image and shows the screen.
		/// </summary>
		/// <param name="startEvent">The event arguments for scene unload start.</param>
		private void OnSceneStartUnload(SceneUnloadStartEventArgs startEvent)
		{
			LoadingImage.sprite = DefaultLoadingScreenSprite;
			Show();
		}

		/// <summary>
		/// Event handler for when a scene finishes unloading. (No implementation)
		/// </summary>
		/// <param name="endEvent">The event arguments for scene unload end.</param>
		private void OnSceneEndUnload(SceneUnloadEndEventArgs endEvent)
		{
			//Hide();
		}
		#endregion
	}
}