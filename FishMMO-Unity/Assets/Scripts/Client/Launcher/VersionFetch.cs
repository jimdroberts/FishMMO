using System;

namespace FishMMO.Client
{
	/// <summary>
	/// Structure to parse the JSON response from the /latest_version endpoint.
	/// </summary>
	[Serializable]
	public struct VersionFetch
	{
		/// <summary>
		/// The latest version string returned by the /latest_version endpoint.
		/// </summary>
		public string latest_version;

		/// <summary>
		/// Optional: true when the server indicates the client is already at the latest
		/// version. Present only when the client sent a <c>from</c> query parameter.
		/// </summary>
		public bool up_to_date;

		/// <summary>
		/// Optional: true when the server has an applicable patch for the client's
		/// reported version. Present only when the client sent a <c>from</c> query parameter.
		/// </summary>
		public bool patch_available;

		/// <summary>
		/// Optional: lowercase hexadecimal SHA-256 of the patch zip the server will
		/// return for the reported client version. Present only when a patch is available.
		/// </summary>
		public string sha256;

		/// <summary>
		/// Optional: size in bytes of the patch zip the server will return for the
		/// reported client version. Present only when a patch is available.
		/// </summary>
		public long size;
	}
}