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
			// Only enforce for non-development standalone builds.
			if (EditorUserBuildSettings.development)
				return;

			bool hasStreamingPins = StreamingAssetsConfigHasPins();
			bool hasCompilePins = ClientSecurityBootstrap.DefaultPinCount >= 2;

			if (!hasStreamingPins && !hasCompilePins)
			{
				throw new BuildFailedException(
					"Production build blocked: No TLS public key pins configured. " +
					"Add pins to StreamingAssets/client-security.json or set allowOnEmpty to true for development builds only.");
			}
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
				if (parsed?.pins == null)
				{
					return false;
				}

				int usable = 0;
				foreach (string raw in parsed.pins)
				{
					if (string.IsNullOrWhiteSpace(raw))
					{
						continue;
					}

					string pin = raw.Trim();
					// Match ClientSecurityBootstrap.IsPlaceholderPin — template values
					// must not satisfy the production pin gate.
					if (pin.StartsWith("REPLACE_ME", StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}

					usable++;
				}

				return usable >= 1;
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