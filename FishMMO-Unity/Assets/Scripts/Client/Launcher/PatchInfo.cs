using System;

namespace FishMMO.Client
{
	/// <summary>
	/// Metadata describing the patch the server has for a given client version.
	/// Returned alongside the parsed latest <see cref="VersionConfig"/> from
	/// <see cref="IPatchServerService.GetLatestVersion"/>.
	/// </summary>
	[Serializable]
	public struct PatchInfo
	{
		/// <summary>
		/// True when the server indicated the client is already at the latest version
		/// (no patch needed).
		/// </summary>
		public bool UpToDate;

		/// <summary>
		/// True when the server has an applicable patch for the supplied client version.
		/// </summary>
		public bool PatchAvailable;

		/// <summary>
		/// Lowercase hexadecimal SHA-256 of the patch zip the server will return for
		/// the supplied client version, or null/empty if no patch info was returned.
		/// </summary>
		public string sha256;

		/// <summary>
		/// Size in bytes of the patch zip the server will return for the supplied
		/// client version, or 0 if not provided.
		/// </summary>
		public long size;
	}
}