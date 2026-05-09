#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishMMO.Logging;
using UnityEditor;
using UnityEditor.Build;

namespace FishMMO.Shared
{
	/// <summary>
	/// Build-time assembly filter that strips client-only assemblies from headless/server
	/// player builds and server-only assemblies from client player builds.
	///
	/// <para>
	/// Unity invokes <see cref="IFilterBuildAssemblies"/> implementations during player build
	/// to allow the project to remove managed assemblies from the final binary. Removing
	/// assemblies that contain code, data, or secrets that should never ship to the other
	/// side of the trust boundary reduces the attack surface and prevents accidental leakage
	/// of server logic to clients (and vice versa).
	/// </para>
	///
	/// <para>
	/// Matching is performed against the assembly file name only (no directory components,
	/// no extension) using a case-insensitive substring check, so an assembly path such as
	/// <c>Library/ScriptAssemblies/FishMMO.Server.dll</c> is matched as
	/// <c>FishMMO.Server</c> and a client-side build directory like <c>ClientBuilds/</c>
	/// in the path will not produce false positives.
	/// </para>
	/// </summary>
	public sealed class SecurityAssemblyFilter : IFilterBuildAssemblies
	{
		private const string LogContext = nameof(SecurityAssemblyFilter);

		/// <summary>
		/// Substring matched (case-insensitive) against the assembly file name to identify
		/// client-only assemblies that must not ship in a server build.
		/// </summary>
		private const string ClientToken = "Client";

		/// <summary>
		/// Substring matched (case-insensitive) against the assembly file name to identify
		/// server-only assemblies that must not ship in a client build.
		/// </summary>
		private const string ServerToken = "Server";

		/// <summary>
		/// Callback order. Lower runs earlier; <c>0</c> is fine since this filter does not
		/// depend on other <see cref="IFilterBuildAssemblies"/> implementations.
		/// </summary>
		public int callbackOrder => 0;

		/// <summary>
		/// Called by Unity during the player build process. Returns the filtered set of
		/// assemblies that should be included in the resulting player.
		/// </summary>
		/// <param name="buildOptions">Active <see cref="BuildOptions"/> for this build.</param>
		/// <param name="assemblies">Full paths of assemblies Unity is about to package.</param>
		/// <returns>Filtered array of assembly paths to include in the build.</returns>
		public string[] OnFilterAssemblies(BuildOptions buildOptions, string[] assemblies)
		{
			if (assemblies == null || assemblies.Length == 0)
			{
				return assemblies;
			}

			bool isServerBuild =
#pragma warning disable CS0618 // EnableHeadlessMode is obsolete in newer Unity but still set on legacy build profiles.
				buildOptions.HasFlag(BuildOptions.EnableHeadlessMode) ||
#pragma warning restore CS0618
				EditorUserBuildSettings.standaloneBuildSubtarget == StandaloneBuildSubtarget.Server;

			string strippedToken = isServerBuild ? ClientToken : ServerToken;

			List<string> filtered = new List<string>(assemblies.Length);
			List<string> removed = new List<string>();

			foreach (string assemblyPath in assemblies)
			{
				if (string.IsNullOrEmpty(assemblyPath))
				{
					continue;
				}

				string fileName = Path.GetFileNameWithoutExtension(assemblyPath);
				if (fileName.IndexOf(strippedToken, System.StringComparison.OrdinalIgnoreCase) >= 0)
				{
					removed.Add(fileName);
					continue;
				}

				filtered.Add(assemblyPath);
			}

			if (removed.Count > 0)
			{
				string buildKind = isServerBuild ? "Server" : "Client";
				string strippedKind = isServerBuild ? "Client" : "Server";
				Log.Info(
					LogContext,
					$"Stripped {removed.Count} {strippedKind}-only assemblies from {buildKind} build: " +
					string.Join(", ", removed));
			}

			return filtered.ToArray();
		}
	}
}
#endif