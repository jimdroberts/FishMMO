using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FishMMO.Client.Security.Editor
{
	/// <summary>
	/// Blocks release client builds when TLS certificate pins are not configured.
	/// </summary>
	public sealed class ClientSecurityBuildValidator : IPreprocessBuildWithReport
	{
		/// <inheritdoc/>
		public int callbackOrder => 0;

		/// <inheritdoc/>
		public void OnPreprocessBuild(BuildReport report)
		{
			bool isDevelopmentBuild = (report.summary.options & BuildOptions.Development) != 0;

			bool hasPins = ClientSecurityBootstrap.DefaultPinCount > 0 || StreamingAssetsConfigHasPins();
			if (hasPins)
			{
				return;
			}

			if (isDevelopmentBuild)
			{
				// Dev/editor builds without pins still risk being
				// promoted to QA or shared internally. A loud build-time warning forces
				// the issue into the build report rather than letting it surface only at
				// runtime as a Log.Warning in player.log that nobody reads.
				Debug.LogWarning(
					"[ClientSecurityBuildValidator] Development build has NO TLS certificate pins configured. " +
					"This is acceptable for local development against unsigned/localhost endpoints, but the resulting " +
					"player is MITM-vulnerable on any non-loopback HTTPS. Add SPKI pins to Assets/StreamingAssets/" +
					ClientSecurityBootstrap.StreamingAssetsConfigFileName +
					" or populate ClientSecurityBootstrap default pins before sharing this build.");
				return;
			}

			throw new BuildFailedException(
				"Release build blocked: no TLS certificate pins configured. Add at least two SPKI pins to " +
				"Assets/StreamingAssets/" + ClientSecurityBootstrap.StreamingAssetsConfigFileName +
				" or populate ClientSecurityBootstrap default pins.");
		}

		private static bool StreamingAssetsConfigHasPins()
		{
			string path = Path.Combine(Application.streamingAssetsPath, ClientSecurityBootstrap.StreamingAssetsConfigFileName);
			if (!File.Exists(path))
			{
				return false;
			}

			try
			{
				var parsed = JsonUtility.FromJson<PinConfigPayload>(File.ReadAllText(path));
				return parsed?.pins != null && parsed.pins.Length >= 2;
			}
			catch (Exception)
			{
				return false;
			}
		}

		[Serializable]
		private sealed class PinConfigPayload
		{
			public string[] pins;
		}
	}
}