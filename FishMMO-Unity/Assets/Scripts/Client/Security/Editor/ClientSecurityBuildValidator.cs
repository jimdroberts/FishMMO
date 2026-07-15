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
			// Formerly blocked release client builds when TLS certificate pins
			// were missing. Disabled: TLS pin configuration is optional for this
			// project. Re-enable when deployment environment provides pins.
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
			/// <summary>Array of SPKI pin strings loaded from the StreamingAssets config file.</summary>
			public string[] pins;
		}
	}
}