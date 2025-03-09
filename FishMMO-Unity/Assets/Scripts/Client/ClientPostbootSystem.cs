using FishMMO.Shared;

namespace FishMMO.Client
{
	public class ClientPostbootSystem : BootstrapSystem
	{
		public string UILoadingScreenKey = "UILoadingScreen";

		public override void OnPreload()
		{
			// Set up the UI Loading Screen for the current AddressableLoadProcessor.
			if (UIManager.TryGet(UILoadingScreenKey, out UILoadingScreen loadingScreen))
			{
				AddressableLoadProcessor.OnProgressUpdate += loadingScreen.OnProgressUpdate;
				loadingScreen.Show();
			}

			// Load Template Cache
			AddressableLoadProcessor.OnAddressableLoaded += AddressableLoadProcessor_OnAddressableLoaded;
			AddressableLoadProcessor.OnAddressableUnloaded += AddressableLoadProcessor_OnAddressableUnloaded;
			AddressableLoadProcessor.EnqueueLoad(Constants.TemplateTypeCache);
		}

		public override void OnCompleteProcessing()
		{
			if (UIManager.TryGet(UILoadingScreenKey, out UILoadingScreen loadingScreen))
			{
				AddressableLoadProcessor.OnProgressUpdate -= loadingScreen.OnProgressUpdate;
				loadingScreen.Hide();
			}
		}

		public override void OnDestroying()
		{
			AddressableLoadProcessor.OnAddressableLoaded -= AddressableLoadProcessor_OnAddressableLoaded;
			AddressableLoadProcessor.OnAddressableUnloaded -= AddressableLoadProcessor_OnAddressableUnloaded;
		}

		public void AddressableLoadProcessor_OnAddressableLoaded(UnityEngine.Object addressable)
		{
			ICachedObject cachedObject = addressable as ICachedObject;
			if (cachedObject != null)
			{
				cachedObject.AddToCache(addressable.name);
			}
		}

		public void AddressableLoadProcessor_OnAddressableUnloaded(UnityEngine.Object addressable)
		{
			ICachedObject cachedObject = addressable as ICachedObject;
			if (cachedObject != null)
			{
				cachedObject.RemoveFromCache();
			}
		}
	}
}