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
		/// Unity Awake message. Sets the Addressables load path override when the object is initialized.
		/// </summary>
		void Awake()
		{
			SetAddressablesLoadPathOverride();
		}

		/// <summary>
		/// Sets the Addressables.ResourceManager.InternalIdTransformFunc to override remote asset load paths.
		/// </summary>
		private void SetAddressablesLoadPathOverride()
		{
			//Log.Debug($"Attempting to set Addressable Remote Load path to {RuntimeBaseUrl}");

			Addressables.ResourceManager.InternalIdTransformFunc = (IResourceLocation location) =>
			{
				//Log.Debug($"Current Addressable load path {location.InternalId}");

				// Modify remote asset load paths based on custom logic (e.g., IP discovery)
				// Ensure the location starts with a protocol to identify it as a remote asset
				if (location.InternalId.StartsWith("http://") || location.InternalId.StartsWith("https://"))
				{
					// Find the index of the third slash, which typically marks the end of the base URL
					// e.g., "http://domain.com/path/to/asset" -> third slash is after .com
					// For "http://127.0.0.1:8000/path/to/asset", the third slash is after 8000
					int startIndex = location.InternalId.IndexOf("://") + 3; // Start after "http://" or "https://"
					int thirdSlashIndex = location.InternalId.IndexOf('/', startIndex);

					// If a third slash exists, it means there's a path component after the base URL
					if (thirdSlashIndex != -1)
					{
						// Extract the part of the path that comes after the base URL
						string relativePath = location.InternalId.Substring(thirdSlashIndex + 1);
						string newPath = RuntimeBaseUrl + relativePath;

						//Log.Debug($"Original Path: {location.InternalId}");
						//Log.Debug($"Transformed Path: {newPath}");
						return newPath;
					}
					else
					{
						//Log.Warning($"Addressable InternalId '{location.InternalId}' starts with http/https but has no path component after the domain/port. Using runtimeBaseUrl directly.");
						return RuntimeBaseUrl;
					}
				}

				// Local assets (e.g., models) can remain untouched
				return location.InternalId;
			};
		}
	}
}