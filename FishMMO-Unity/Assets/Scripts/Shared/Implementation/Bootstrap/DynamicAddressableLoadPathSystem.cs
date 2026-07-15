using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace FishMMO.Shared
{
	/// <summary>
	/// Dynamically overrides the Addressables remote load path at runtime.
	/// Useful for changing asset server URLs based on runtime configuration (e.g., IP discovery).
	/// </summary>
	public class DynamicAddressableLoadPathSystem : MonoBehaviour
	{
		/// <summary>
		/// The base URL to use for remote Addressables asset loading at runtime.
		/// </summary>
		public string RuntimeBaseUrl;

		/// <summary>
		/// Unity Awake message. On server builds, overrides RuntimeBaseUrl to load from local StreamingAssets.
		/// On client builds, RuntimeBaseUrl is expected to be set externally (e.g., CDN URL from config).
		/// Then applies the Addressables load path override.
		/// </summary>
		void Awake()
		{
#if UNITY_SERVER
			RuntimeBaseUrl = "file://" + Application.streamingAssetsPath + "/ServerData/";
#endif
			// Ensure the base URL ends with a slash so concatenation works every time
			if (!string.IsNullOrEmpty(RuntimeBaseUrl) && !RuntimeBaseUrl.EndsWith("/"))
			{
				RuntimeBaseUrl += "/";
			}
			SetAddressablesLoadPathOverride();
		}

		/// <summary>
		/// Sets the Addressables.ResourceManager.InternalIdTransformFunc to override remote asset load paths.
		/// </summary>
		private void SetAddressablesLoadPathOverride()
		{
			//Log.Debug($"Attempting to set Addressable Remote Load path to {RuntimeBaseUrl}");

			// Do not register a transform if no base URL is configured. An empty
			// RuntimeBaseUrl on client builds using only Local paths or before a CDN
			// base is set in the Inspector would otherwise strip the path prefix
			// and return a root-relative URL that fails to load.
			if (string.IsNullOrEmpty(RuntimeBaseUrl))
				return;

			Addressables.ResourceManager.InternalIdTransformFunc = (IResourceLocation location) =>
			{
				if (location.InternalId.StartsWith("http://") || location.InternalId.StartsWith("https://"))
				{
					int startIndex = location.InternalId.IndexOf("://") + 3;
					int thirdSlashIndex = location.InternalId.IndexOf('/', startIndex);

					if (thirdSlashIndex != -1)
					{
						string relativePath = location.InternalId.Substring(thirdSlashIndex + 1);
						return RuntimeBaseUrl + relativePath;
					}
					return RuntimeBaseUrl;
				}

				// Local assets remain untouched
				return location.InternalId;
			};
		}
	}
}