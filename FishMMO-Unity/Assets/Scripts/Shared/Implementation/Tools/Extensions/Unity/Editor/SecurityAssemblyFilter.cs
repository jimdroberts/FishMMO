#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
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
	/// to allow the project to remove managed assemblies (both compiled <c>asmdef</c> output
	/// and precompiled DLLs under <c>Assets/Dependencies/</c>) from the final binary. Removing
	/// assemblies that contain code, data, or secrets that should never ship to the other
	/// side of the trust boundary reduces the attack surface and prevents accidental leakage
	/// of server logic to clients (and vice versa).
	/// </para>
	///
	/// <para>
	/// Two complementary mechanisms are applied:
	/// </para>
	/// <list type="number">
	///   <item>
	///     <description>A curated list of third-party dependency name prefixes that are
	///     known to be exclusively server-side (EF Core, Npgsql,
	///     OTP/2FA, OpenAI, etc.). These are stripped from client builds only.</description>
	///   </item>
	///   <item>
	///     <description>A case-insensitive substring fallback that strips assembly names
	///     containing <c>"Server"</c> from client builds and <c>"Client"</c> from server
	///     builds. This covers FishMMO-internal assemblies whose role is encoded in the
	///     name (e.g. <c>FishMMO.Server</c>, <c>FishMMO-ClientAuth</c>).</description>
	///   </item>
	/// </list>
	///
	/// <para>
	/// Matching is performed against the assembly file name only (no directory components,
	/// no extension), so a build path containing the words <c>"Client"</c> or <c>"Server"</c>
	/// (e.g. <c>ClientBuilds/</c>) cannot produce false positives.
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
		/// Curated list of assembly file name prefixes (case-insensitive) for third-party
		/// dependencies that are exclusively used by server-side code paths in this project
		/// (database, EF Core tooling, server TOTP, AI integrations, etc.). Stripped
		/// from client builds.
		///
		/// <para>
		/// Verified via grep against <c>Assets/Scripts/{Client,Shared}/**/*.cs</c>: none of
		/// these prefixes are referenced from client or shared code in this project. If a
		/// future client feature legitimately needs one of these libraries, remove the
		/// matching entry from this list.
		/// </para>
		///
		/// <para>
		/// Prefix matching is intentional so that family packages
		/// (e.g. <c>Microsoft.EntityFrameworkCore.Abstractions</c>,
		/// <c>Microsoft.EntityFrameworkCore.Relational</c>) all match a single entry.
		/// </para>
		/// </summary>
		private static readonly string[] ServerOnlyAssemblyPrefixes = new[]
		{
			// Database
			"Microsoft.EntityFrameworkCore",
			"EFCore.NamingConventions",
			"Npgsql",
			"Humanizer", // EF Core design-time dependency

			// Server-side dependency injection / configuration / logging plumbing
			// (FishMMO clients use FishMMO-Logger and Unity's own infrastructure instead).
			//
			// NOTE: Microsoft.Extensions.* and Microsoft.Bcl.AsyncInterfaces are intentionally
			// NOT stripped — although the FishMMO client never uses Microsoft.Extensions DI/
			// configuration/logging APIs at runtime, the netstandard 2.1 BCL polyfill
			// System.Text.Json.dll (shipped under Assets/Dependencies/) IL-references types
			// in Microsoft.Bcl.AsyncInterfaces, and several Microsoft.Extensions.* assemblies
			// have similar inter-dependencies. Stripping any of them breaks IL2CPP managed-
			// stripping with IL1005 / unresolved assembly errors on the client. If a
			// genuinely server-only Microsoft.Extensions.* sub-package is identified later,
			// add a more specific prefix (e.g. "Microsoft.Extensions.Configuration.Json")
			// rather than the family root.

			// AI integrations are server-side only.
			"OpenAI_API",

			// FishMMO server-only modules.
			"FishMMO-DB",
			"FishMMO-ServerAuth",
		};

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
			List<string> removedByToken = new List<string>();
			List<string> removedByCuratedList = new List<string>();

			foreach (string assemblyPath in assemblies)
			{
				if (string.IsNullOrEmpty(assemblyPath))
				{
					continue;
				}

				string fileName = Path.GetFileNameWithoutExtension(assemblyPath);

				// Curated server-only dependency list — applies only to client builds.
				if (!isServerBuild && IsServerOnlyDependency(fileName))
				{
					removedByCuratedList.Add(fileName);
					continue;
				}

				// Token-based fallback (Client/Server) for FishMMO-named assemblies.
				if (fileName.IndexOf(strippedToken, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					removedByToken.Add(fileName);
					continue;
				}

				filtered.Add(assemblyPath);
			}

			LogRemovals(isServerBuild, removedByToken, removedByCuratedList);

			return filtered.ToArray();
		}

		/// <summary>
		/// Returns <c>true</c> if <paramref name="fileName"/> begins (case-insensitively)
		/// with any entry in <see cref="ServerOnlyAssemblyPrefixes"/>.
		/// </summary>
		/// <param name="fileName">The assembly file name without directory or extension.</param>
		private static bool IsServerOnlyDependency(string fileName)
		{
			for (int i = 0; i < ServerOnlyAssemblyPrefixes.Length; i++)
			{
				if (fileName.StartsWith(ServerOnlyAssemblyPrefixes[i], StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Emits a single <see cref="Log.Info"/> entry summarizing all assemblies stripped
		/// during the current build, grouped by removal reason.
		/// </summary>
		private static void LogRemovals(bool isServerBuild, List<string> removedByToken, List<string> removedByCuratedList)
		{
			if (removedByToken.Count == 0 && removedByCuratedList.Count == 0)
			{
				return;
			}

			string buildKind = isServerBuild ? "Server" : "Client";

			if (removedByCuratedList.Count > 0)
			{
				Log.Info(
					LogContext,
					$"Stripped {removedByCuratedList.Count} server-only dependency assemblies from {buildKind} build (curated list): " +
					string.Join(", ", removedByCuratedList));
			}

			if (removedByToken.Count > 0)
			{
				string strippedKind = isServerBuild ? "Client" : "Server";
				Log.Info(
					LogContext,
					$"Stripped {removedByToken.Count} {strippedKind}-named assemblies from {buildKind} build (name token): " +
					string.Join(", ", removedByToken));
			}
		}
	}
}
#endif
