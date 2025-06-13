using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.ResourceLocations;

public class DynamicAddressableLoadPathSystem : MonoBehaviour
{
	public string RuntimeBaseUrl;

	void Awake()
	{
		SetAddressablesLoadPathOverride();
	}

	private void SetAddressablesLoadPathOverride()
	{
		//Debug.Log($"Attempting to set Addressable Remote Load path to {RuntimeBaseUrl}");

		Addressables.ResourceManager.InternalIdTransformFunc = (IResourceLocation location) =>
		{
			//Debug.Log($"Current Addressable load path {location.InternalId}");

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

					//Debug.Log($"Original Path: {location.InternalId}");
					//Debug.Log($"Transformed Path: {newPath}");
					return newPath;
				}
				else
				{
					//Debug.LogWarning($"Addressable InternalId '{location.InternalId}' starts with http/https but has no path component after the domain/port. Using runtimeBaseUrl directly.");
					return RuntimeBaseUrl;
				}
			}

			// Local assets (e.g., models) can remain untouched
			return location.InternalId;
		};
	}
}