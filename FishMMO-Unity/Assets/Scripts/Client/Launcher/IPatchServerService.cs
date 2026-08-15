using System;
using System.Collections;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// Contract for version lookups and patch binary downloads.
	/// All methods use a single API host URL (NGINX routes by path to the correct backend).
	/// </summary>
	public interface IPatchServerService
	{
		/// <summary>
		/// Asynchronously retrieves the latest client version from the API gateway.
		/// When <paramref name="clientVersion"/> is non-empty, the server may also return
		/// patch metadata (SHA-256, size, availability) for that specific client version.
		/// </summary>
		/// <param name="apiHost">The unified API host URL.</param>
		/// <param name="clientVersion">The client's current version string, or null/empty to request only the latest version.</param>
		/// <param name="onComplete">Callback invoked with the latest <see cref="VersionConfig"/> and per-version <see cref="PatchInfo"/> upon success.</param>
		/// <param name="onError">Callback invoked with an error message upon failure.</param>
		/// <returns>An IEnumerator for use in a Unity Coroutine.</returns>
		IEnumerator GetLatestVersion(string apiHost, string clientVersion, Action<VersionConfig, PatchInfo> onComplete, Action<string> onError);

		/// <summary>
		/// Asynchronously downloads a patch file from the server. When
		/// <paramref name="expectedSha256"/> is provided, the downloaded file is hashed
		/// after download and the operation fails (with <paramref name="onError"/>) if
		/// the digest does not match.
		/// </summary>
		/// <param name="patchUrl">The full URL of the patch file to download.</param>
		/// <param name="destinationFilePath">Full path (including file name) the patch archive is written to. Parent directories are created if missing.</param>
		/// <param name="expectedSha256">Lowercase hex SHA-256 the downloaded file must match, or null/empty to skip verification.</param>
		/// <param name="onComplete">
		/// Callback invoked upon successful download (and verification, if requested).
		/// The argument is <c>true</c> when a patch archive was written to
		/// <paramref name="destinationFilePath"/>, and <c>false</c> when the server
		/// reported the client is already up to date and there is nothing to apply.
		/// </param>
		/// <param name="onError">Callback invoked with an error message upon failure.</param>
		/// <param name="onProgress">Callback invoked periodically with download progress (0.0 to 1.0) and a formatted string.</param>
		/// <returns>An IEnumerator for use in a Unity Coroutine.</returns>
		IEnumerator DownloadPatch(string patchUrl, string destinationFilePath, string expectedSha256, Action<bool> onComplete, Action<string> onError, Action<float, string> onProgress);
	}
}