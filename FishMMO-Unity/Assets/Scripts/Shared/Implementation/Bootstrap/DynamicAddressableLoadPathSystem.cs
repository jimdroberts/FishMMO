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
	/// <para>
	/// Client builds with an empty or loopback RuntimeBaseUrl do not register a rewrite:
	/// Addressables.RuntimePath already resolves correctly (including WebGL, where
	/// StreamingAssets is an absolute http(s) URL derived from the page origin, e.g.
	/// https://eqbrowser.com/test/StreamingAssets/aa). Rewriting those IDs to a
	/// hardcoded host (or prefixing file://) breaks loads when the game is served from
	/// a different domain or subpath.
	/// </para>
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
		/// When left empty (or set to a loopback placeholder), client builds keep Addressables'
		/// default local StreamingAssets resolution and do not rewrite http(s) IDs.
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
		/// Builds a load-base URL for Addressables.RuntimePath / StreamingAssets.
		/// WebGL (and any platform where StreamingAssets is already http/https) must not
		/// receive a file:// prefix — that produces invalid IDs like
		/// file://https://host/StreamingAssets/aa/...
		/// </summary>
		public static string ToLoadBaseUrl(string path)
		{
			if (string.IsNullOrEmpty(path))
				return path;

			string normalized = path.Replace('\\', '/');
			if (normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
				normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
				normalized.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
			{
				return normalized.EndsWith("/") ? normalized : normalized + "/";
			}

			// Windows absolute paths (e.g. "C:/...") need three slashes: file:///C:/...
			bool isWindowsAbsolute = normalized.Length >= 2 && normalized[1] == ':';
			string fileUrl = (isWindowsAbsolute ? "file:///" : "file://") + normalized;
			return fileUrl.EndsWith("/") ? fileUrl : fileUrl + "/";
		}

		/// <summary>
		/// Unity Awake message. On server builds, overrides RuntimeBaseUrl to load from
		/// StreamingAssets/ServerData/ where the build pipeline places server-specific bundles.
		/// On client builds, registers a CDN rewrite only when RuntimeBaseUrl is a non-loopback
		/// address; otherwise leaves Addressables local StreamingAssets resolution alone.
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
			// pipeline places server-specific Addressable asset bundles.
			RuntimeBaseUrl = ToLoadBaseUrl(Application.streamingAssetsPath + "/ServerData");
			SetAddressablesLoadPathOverride();
#else
			// Client: a real CDN / public host rewrites remote http(s) catalog IDs.
			// Empty or loopback RuntimeBaseUrl means "use local StreamingAssets" — do not
			// register InternalIdTransformFunc. On WebGL, RuntimePath is already an absolute
			// URL under the page origin (e.g. https://eqbrowser.com/test/StreamingAssets/aa);
			// rewriting those IDs to a hardcoded host or file:// breaks loads.
			if (!string.IsNullOrEmpty(RuntimeBaseUrl) && !IsLoopbackPlaceholder(RuntimeBaseUrl))
			{
				Debug.Log($"DynamicAddressableLoadPathSystem: CDN rewrite active. RuntimeBaseUrl={RuntimeBaseUrl}", this);
				SetAddressablesLoadPathOverride();
			}
			else
			{
				RuntimeBaseUrl = ToLoadBaseUrl(Addressables.RuntimePath);
				Debug.Log($"DynamicAddressableLoadPathSystem: Local Addressables path (no URL rewrite). Base={RuntimeBaseUrl}", this);
			}
#endif
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
