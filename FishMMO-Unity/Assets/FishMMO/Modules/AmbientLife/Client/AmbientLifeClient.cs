using System.Globalization;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// Plugs the Ambient Life module into the client. Its systems start themselves (each hooks the render pipeline after the
	/// first scene loads); this adds what the core offers a seam for: the air motes drawn with the weather
	/// (<see cref="WeatherOverlays"/>), the test beds' switches (<see cref="RenderSwitches"/>) and the perf probe's report
	/// (<see cref="RenderReports"/>).
	/// </summary>
	public static class AmbientLifeClient
	{
		/// <summary>The air motes' overlay id.</summary>
		public const string AirMotesOverlayId = "ambient-life.air-motes";

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		public static void Install()
		{
			WeatherOverlays.Register(AirMotesOverlayId, () => new AirMotesField());
			RenderSwitches.Register("ambientLife", value => WithSettings(s => s.Enabled = value == "1"));
			RenderSwitches.Register("ambientLifeMax", value => WithSettings(s => s.MaxVisible = int.Parse(value, CultureInfo.InvariantCulture)));
			// Every animal magenta and value times its size ("0" off): what a frame-by-frame pixel count of the animals reads.
			RenderSwitches.Register("ambientHighlight", value => WithSettings(s =>
			{
				float scale = float.Parse(value, CultureInfo.InvariantCulture);
				s.DebugHighlight = scale > 0f;
				if (scale > 0f)
				{
					s.DebugScale = scale;
				}
			}));
			RenderReports.Register("ambient life", AmbientLifeSystem.DescribeWork);
		}

#if UNITY_EDITOR
		[UnityEditor.InitializeOnLoadMethod]
		private static void InstallInEditor() => Install();
#endif

		private static void WithSettings(System.Action<AmbientLifeSettings> change)
		{
			AmbientLifeRenderProfile profile = AmbientLifeRenderProfile.Active;
			if (profile != null && profile.AmbientLife != null)
			{
				change(profile.AmbientLife);
			}
		}
	}
}
