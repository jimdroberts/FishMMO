using System;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace FishMMO.Shared
{
	/// <summary>
	/// Dynamically overrides the Addressables remote load path at runtime.
	/// Registers a persistent InternalIdTransformFunc on the Addressables ResourceManager
	/// that rewrites remote asset URLs to use RuntimeBaseUrl. Only one instance should
	/// exist in the scene — the last one to Awake wins.
	/// </summary>
	public class DynamicAddressableLoadPathSystem : MonoBehaviour
	{
		/// <summary>
		/// Backing field for RuntimeBaseUrl. Serialized so Inspector values persist.
		/// Unity deserializes this directly, bypassing the property setter — trailing-slash
		/// normalization for Inspector values happens in Awake().
		/// </summary>
		[SerializeField]
		private string runtimeBaseUrl;

		/// <summary>
		/// The base URL to use for remote Addressables asset loading at runtime.
		/// When set to a real CDN URL on client builds, overrides the profile's remote load path.
		/// When left empty (or set to a loopback placeholder), client builds fall back to local StreamingAssets.
		/// Trailing slash is normalized automatically on set.
		/// Cannot be set to null or empty after the transform is registered.
		/// </summary>
		public string RuntimeBaseUrl
		{
			get => runtimeBaseUrl;
			set
			{
				if (transformRegistered && string.IsNullOrEmpty(value))
				{
					Debug.LogWarning("DynamicAddressableLoadPathSystem: Cannot clear RuntimeBaseUrl while the Addressables transform is active. Ignoring.");
					return;
				}

				runtimeBaseUrl = value;
				if (!string.IsNullOrEmpty(runtimeBaseUrl) && !runtimeBaseUrl.EndsWith("/"))
					runtimeBaseUrl += "/";
			}
		}

		/// <summary>
		/// Set to true once the transform is registered on the Addressables ResourceManager.
		/// Guards against clearing RuntimeBaseUrl after registration.
		/// </summary>
		private bool transformRegistered;

		/// <summary>
		/// Tracks how many instances of this component are alive. Used to detect
		/// duplicate instances which would silently overwrite each other's transform.
		/// </summary>
		private static int instanceCount;

		/// <summary>
		/// Returns true if the given URL is a loopback/localhost placeholder address
		/// that should be replaced with a local path on client builds.
		/// Uses System.Uri for exact host comparison to avoid substring false-positives.
		/// </summary>
		/// <param name="url">The URL to check.</param>
		/// <returns>True if the URL's host is a loopback address.</returns>
		public static bool IsLoopbackPlaceholder(string url)
		{
			if (string.IsNullOrEmpty(url))
				return false;

			// Parse with Uri for exact host comparison — avoids false-positives
			// from hostnames containing "localhost" or "127.0.0.1" as substrings.
			if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
				return uri.Host == "127.0.0.1" || uri.Host == "localhost" || uri.Host == "::1";

			// Fallback for non-absolute URLs (bare IPs, hostnames without a scheme).
			return url == "127.0.0.1" || url == "localhost" || url == "::1" || url == "[::1]";
		}

		/// <summary>
		/// Unity Awake message. On server builds, overrides RuntimeBaseUrl to load from
		/// StreamingAssets/ServerData/ where the build pipeline places server-specific bundles.
		/// On client builds, falls back to local StreamingAssets when no real CDN URL is configured,
		/// and applies a CDN override when RuntimeBaseUrl is set to a non-loopback address.
		/// Then applies the Addressables load path override.
		/// </summary>
		private void Awake()
		{
			instanceCount++;
			if (instanceCount > 1)
			{
				Debug.LogWarning($"DynamicAddressableLoadPathSystem: {instanceCount} instances are active. " +
					"The most recent Awake() will control the Addressables load path transform.", this);
			}

			// Normalize trailing slash on any Inspector-set value. Unity deserializes
			// [SerializeField] backing fields directly, bypassing the property setter,
			// so an Inspector value without a trailing slash would otherwise be missed.
			if (!string.IsNullOrEmpty(runtimeBaseUrl) && !runtimeBaseUrl.EndsWith("/"))
				runtimeBaseUrl += "/";

#if UNITY_SERVER
			// Server builds load from a dedicated ServerData subfolder where the build
			// pipeline places server-specific Addressables asset bundles.
			RuntimeBaseUrl = "file://" + Application.streamingAssetsPath + "/ServerData/";
#else
			// Client builds load from Addressables.RuntimePath, the canonical
			// platform-agnostic path Unity uses for local bundle resolution
			// ({StreamingAssetsPath}/aa/ on standalone). A real CDN URL overrides
			// this when RuntimeBaseUrl is set to a non-loopback address.
			if (string.IsNullOrEmpty(RuntimeBaseUrl) || IsLoopbackPlaceholder(RuntimeBaseUrl))
				RuntimeBaseUrl = "file://" + Addressables.RuntimePath;
#endif
			SetAddressablesLoadPathOverride();
		}

		/// <summary>
		/// Unity OnDestroy message. Decrements the instance counter so duplicate-instance
		/// warnings are accurate across scene loads.
		/// </summary>
		private void OnDestroy()
		{
			instanceCount--;
		}

		/// <summary>
		/// Registers an InternalIdTransformFunc on the Addressables ResourceManager that
		/// rewrites remote asset URLs to use RuntimeBaseUrl. Uses System.Uri for robust
		/// URL parsing.
		/// </summary>
		private void SetAddressablesLoadPathOverride()
		{
			Addressables.ResourceManager.InternalIdTransformFunc = (IResourceLocation location) =>
			{
				string id = location.InternalId;

				// Only transform remote URLs; local assets remain untouched.
				if (!id.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
					!id.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
					return id;

				// Parse with System.Uri for robust host/path/query separation.
				if (!Uri.TryCreate(id, UriKind.Absolute, out var uri))
					return id;

				string pathAndQuery = uri.PathAndQuery.TrimStart('/');
				return string.IsNullOrEmpty(pathAndQuery)
					? RuntimeBaseUrl
					: RuntimeBaseUrl + pathAndQuery;
			};

			transformRegistered = true;
		}
	}
}