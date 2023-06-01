using FishNet.Managing.Scened;
using UnityEngine;
using UnityEngine.UI;

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

			Client.NetworkManager.ClientManager.RegisterBroadcast<SceneWorldReconnectBroadcast>(OnServerChange);
		}

		public override void OnDestroying()
		{
			Client.NetworkManager.SceneManager.OnLoadStart -= OnSceneStartLoad;
			Client.NetworkManager.SceneManager.OnLoadPercentChange -= OnSceneProgressUpdate;
			Client.NetworkManager.SceneManager.OnLoadEnd -= OnSceneEndLoad;

			Client.NetworkManager.ClientManager.UnregisterBroadcast<SceneWorldReconnectBroadcast>(OnServerChange);
		}

		private void ShowLoadingScreen()
		{
			visible = true;
			LoadingProgress.value = 0;
			LoadingImage.gameObject.SetActive(LoadingImage.sprite != null);
		}

		#region Network Events
		public void OnServerChange(SceneWorldReconnectBroadcast reconnect)
		{
			if (reconnect.sceneName != null &&
				reconnect.teleporterName != null &&
				Details.scenes.TryGetValue(reconnect.sceneName, out WorldSceneDetails details) &&
				details.teleporters.TryGetValue(reconnect.teleporterName, out SceneTeleporterDetails teleporter))
			{
				LoadingImage.sprite = teleporter.sceneTransitionImage;
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
			visible = false;
		}
		#endregion
	}
}