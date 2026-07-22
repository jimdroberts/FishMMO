using System;

// NOTE: All fields in this file use snake_case naming as a deliberate JSON contract exception.
// JsonUtility.FromJson maps directly to field names; PascalCase would break deserialization
// of the server response (which is produced by a non-.NET backend following JSON conventions).
// This is the ONLY place in the codebase where snake_case is intentionally used.

namespace FishMMO.Client
{
	/// <summary>
	/// Structure to parse the JSON response from the /latest_version endpoint.
	///
	/// NOTE: The fields below use snake_case naming as a deliberate JSON contract
	/// exception. JsonUtility.FromJson maps directly to these field names; renaming
	/// them to PascalCase would break deserialization of the server response.
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