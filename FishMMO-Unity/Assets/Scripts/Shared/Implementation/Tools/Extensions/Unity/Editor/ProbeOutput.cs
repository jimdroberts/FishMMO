using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Where render and probe harnesses write their images and reports, so no harness carries a
	/// machine-specific absolute path.
	/// </summary>
	/// <remarks>
	/// The root is the <c>FISHMMO_PROBE_OUT</c> environment variable when set, else the checkout
	/// root (the folder that holds the Unity project). Output never lands under <c>Assets</c>, where
	/// Unity would import every file and write a .meta beside it.
	/// </remarks>
	public static class ProbeOutput
	{
		/// <summary>The environment variable that overrides <see cref="Root"/>.</summary>
		public const string RootEnvironmentVariable = "FISHMMO_PROBE_OUT";

		/// <summary>The folder every harness output path is resolved against.</summary>
		public static string Root
		{
			get
			{
				string overridden = Environment.GetEnvironmentVariable(RootEnvironmentVariable);
				return string.IsNullOrEmpty(overridden)
					? System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", ".."))
					: System.IO.Path.GetFullPath(overridden);
			}
		}

		/// <summary>
		/// <paramref name="parts"/> joined under <see cref="Root"/>, e.g.
		/// <c>ProbeOutput.Path("PanelRenders", "interface-scaling")</c>.
		/// </summary>
		public static string Path(params string[] parts)
		{
			string[] all = new string[parts.Length + 1];
			all[0] = Root;
			Array.Copy(parts, 0, all, 1, parts.Length);
			return System.IO.Path.Combine(all);
		}

		/// <summary>
		/// The value of environment variable <paramref name="name"/>, else <see cref="Path"/> of
		/// <paramref name="fallback"/>: for harnesses that already take their own output override.
		/// </summary>
		public static string FromEnvironment(string name, params string[] fallback)
		{
			string v = Environment.GetEnvironmentVariable(name);
			return string.IsNullOrEmpty(v) ? Path(fallback) : v;
		}
	}
}
