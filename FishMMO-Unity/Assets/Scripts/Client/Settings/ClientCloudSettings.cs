using System;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// Whether the volumetric clouds are drawn: the player's switch, in the options' graphics rows.
	/// </summary>
	/// <remarks>
	/// A performance control. Off, the cloud march, its steadying and composite, the god rays, the cloud shadows on the
	/// ground and the light volume all stop (FishCloudsFeature, SkySystem); the sky, the weather's light, rain and fog
	/// go on as they are. The clouds were most of a fair sky's frame (ScenePerfProbe, 2026-10-07).
	/// </remarks>
	public static class ClientCloudSettings
	{
		/// <summary>Raised when the switch changes.</summary>
		public static event Action OnChanged;

		/// <summary>Whether the clouds are drawn; on for a fresh install.</summary>
		public static bool Enabled => ClientSettings.GetBool(ClientSettings.CloudsEnabledKey, true);

		/// <summary>Writes the switch and tells whoever listens.</summary>
		public static void SetEnabled(bool value)
		{
			ClientSettings.Set(ClientSettings.CloudsEnabledKey, value);
			try
			{
				OnChanged?.Invoke();
			}
			catch (Exception ex)
			{
				Log.Error("ClientCloudSettings", "A cloud-settings subscriber threw.", ex);
			}
		}
	}
}
