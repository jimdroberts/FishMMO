using FishNet.Transporting;
using FishNet.Managing.Scened;
using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UILoadingScreen : UIControl
	{
		[Header("Loading Screen Parameters")]
		public Slider LoadingProgress;
		public Image LoadingImage;
		public WorldSceneDetailsCache Details;
		public Sprite DefaultLoadingScreenSprite;

		public override void OnStarting()
		{
			Client.NetworkManager.SceneManager.OnLoadStart += OnSceneStartLoad;
			Client.NetworkManager.SceneManager.OnLoadPercentChange += OnSceneProgressUpdate;
			Client.NetworkManager.SceneManager.OnLoadEnd += OnSceneEndLoad;

			Client.NetworkManager.ClientManager.RegisterBroadcast<SceneWorldReconnectBroadcast>(OnClientSceneWorldReconnectBroadcastReceived);
		}

		public override void OnDestroying()
		{
			Client.NetworkManager.SceneManager.OnLoadStart -= OnSceneStartLoad;
			Client.NetworkManager.SceneManager.OnLoadPercentChange -= OnSceneProgressUpdate;
			Client.NetworkManager.SceneManager.OnLoadEnd -= OnSceneEndLoad;

			Client.NetworkManager.ClientManager.UnregisterBroadcast<SceneWorldReconnectBroadcast>(OnClientSceneWorldReconnectBroadcastReceived);
		}

		private void ShowLoadingScreen()
		{
			LoadingProgress.value = 0;
			LoadingImage.gameObject.SetActive(LoadingImage.sprite != null);
			Show();
		}

		#region Network Events
		public void OnClientSceneWorldReconnectBroadcastReceived(SceneWorldReconnectBroadcast reconnect, Channel channel)
		{
			if (!string.IsNullOrWhiteSpace(reconnect.sceneName) &&
				!string.IsNullOrWhiteSpace(reconnect.teleporterName) &&
				Details.Scenes.TryGetValue(reconnect.sceneName, out WorldSceneDetails details) &&
				details.Teleporters.TryGetValue(reconnect.teleporterName, out SceneTeleporterDetails teleporter))
			{
				LoadingImage.sprite = teleporter.SceneTransitionImage;
			}
			else
			{
				LoadingImage.sprite = DefaultLoadingScreenSprite;
			}

			ShowLoadingScreen();
		}
		#endregion

		#region Scene Events
		private void OnSceneStartLoad(SceneLoadStartEventArgs startEvent)
		{
			ShowLoadingScreen();
		}

		private void OnSceneProgressUpdate(SceneLoadPercentEventArgs percentEvent)
		{
			LoadingProgress.value = percentEvent.Percent;
		}

		private void OnSceneEndLoad(SceneLoadEndEventArgs endEvent)
		{
			Hide();
		}
		#endregion
	}
}