using System;

namespace FishMMO.Client
{
	/// <summary>
	/// Structure to parse the JSON response from the /latest_version endpoint.
	/// </summary>
	[Serializable]
	public struct VersionFetch
	{
		public string latest_version;
	}
}