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

			Client.OnReconnectAttempt += Client_OnReconnectAttempt;
			Client.OnReconnectFailed += Client_OnReconnectFailed;
		}

		public override void OnDestroying()
		{
			Client.NetworkManager.SceneManager.OnLoadStart -= OnSceneStartLoad;
			Client.NetworkManager.SceneManager.OnLoadPercentChange -= OnSceneProgressUpdate;
			Client.NetworkManager.SceneManager.OnLoadEnd -= OnSceneEndLoad;

			Client.OnReconnectAttempt -= Client_OnReconnectAttempt;
			Client.OnReconnectFailed -= Client_OnReconnectFailed;
		}

		private void ShowLoadingScreen()
		{
			LoadingProgress.value = 0;
			LoadingImage.gameObject.SetActive(LoadingImage.sprite != null);
			Show();
		}

		public void Client_OnReconnectAttempt(byte attempts, byte maxAttempts)
		{
			LoadingImage.sprite = DefaultLoadingScreenSprite;
			ShowLoadingScreen();
		}

		public void Client_OnReconnectFailed()
		{
			Hide();
		}

		#region Scene Events
		private void OnSceneStartLoad(SceneLoadStartEventArgs startEvent)
		{
			ShowLoadingScreen();

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