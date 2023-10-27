using System;
using System.Reflection;

namespace FishMMO.Shared
{
	/// <summary>
	/// This simple script that can give you version control over your server and client.
	/// *IMPORTANT* This is not a secure method of verifying your client version with the server..
	/// A production level game should instead create checksums for all game files and check them
	/// against the servers game file checksums.
	/// </summary>
	public class AssemblyVersionTool
	{
		private static Version version;

		public static Version Version
		{
			get
			{
				if (version == null)
				{
					version = Assembly.GetExecutingAssembly().GetName().Version;
				}
				return version;
			}
		}
	}
}