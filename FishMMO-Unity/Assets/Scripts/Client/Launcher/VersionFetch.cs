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
	}
}