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
			base.OnStarting();

			AddressableLoadProcessor.OnProgressUpdate += OnProgressUpdate;

			LoadingImage.sprite = DefaultLoadingScreenSprite;
		}

		public override void OnDestroying()
		{
			base.OnDestroying();

			AddressableLoadProcessor.OnProgressUpdate -= OnProgressUpdate;
		}

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

		public void Client_OnReconnectAttempt(byte attempts, byte maxAttempts)
		{
			LoadingImage.sprite = DefaultLoadingScreenSprite;
			Show();
		}

		public void Client_OnReconnectFailed()
		{
			Hide();
		}

		#region Scene Events
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

		private void OnSceneProgressUpdate(SceneLoadPercentEventArgs percentEvent)
		{
			if (LoadingProgress != null)
			{
				LoadingProgress.value = percentEvent.Percent;
			}
		}

		private void OnSceneEndLoad(SceneLoadEndEventArgs endEvent)
		{
			Hide();
		}

		private void OnSceneStartUnload(SceneUnloadStartEventArgs startEvent)
		{
			LoadingImage.sprite = DefaultLoadingScreenSprite;
			Show();
		}

		private void OnSceneEndUnload(SceneUnloadEndEventArgs endEvent)
		{
			//Hide();
		}
		#endregion
	}
}