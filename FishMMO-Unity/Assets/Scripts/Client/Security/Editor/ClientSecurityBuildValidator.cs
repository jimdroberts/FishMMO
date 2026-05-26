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
			if ((report.summary.options & BuildOptions.Development) != 0)
			{
				return;
			}

			if (ClientSecurityBootstrap.DefaultPinCount > 0 || StreamingAssetsConfigHasPins())
			{
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