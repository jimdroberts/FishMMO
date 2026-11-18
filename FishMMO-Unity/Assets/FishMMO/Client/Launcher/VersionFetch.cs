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

		/// <summary>
		/// Base64 Ed25519 signature over this document.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>S4.</b> Every other field here is trusted absolutely — <c>sha256</c> in particular
		/// is the ONLY integrity check applied to the patch archive, so whoever writes this
		/// document chooses which bytes the updater installs. It travelled over pinned TLS and
		/// nothing more, which the project's own TODO called out: TLS authenticates the
		/// transport, and says nothing about a compromised gateway, a mis-issued certificate or
		/// a hostile CDN edge.
		/// </para>
		/// <para>
		/// The signed form is this document with the value of <c>signature</c> replaced by an
		/// empty string, with the base64 signature appended — byte-identical handling to the pin
		/// manifest, so one signing tool serves both. See
		/// <c>FishMMO.Client.Security.Ed25519ManifestVerifier</c>.
		/// </para>
		/// <para>
		/// Absent when the deployment has not configured signing. The client reports that
		/// loudly rather than treating it as normal; once a verification key is embedded, an
		/// unsigned or wrongly-signed manifest is refused.
		/// </para>
		/// </remarks>
		public string signature;
	}
}