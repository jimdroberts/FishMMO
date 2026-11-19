using UnityEngine;

namespace FishMMO.Shared.Celestial
{
	/// <summary>
	/// A comet: a small body on a long, eccentric orbit around a star. Its tails point away from
	/// the star and grow as it nears perihelion.
	/// </summary>
	/// <remarks>
	/// Orbits use <see cref="OrbitSettings"/> like a planet's, with <see cref="OrbitSettings.Distance"/>
	/// as the semi-major axis in AU and an eccentricity near 1. Scenes never sit on a comet.
	/// </remarks>
	[CreateAssetMenu(fileName = "New Comet", menuName = "FishMMO/World/Comet", order = 13)]
	public class CometBody : CelestialBody
	{
		[Tooltip("Tail length at 1 AU from the star, in millions of km. Scales with 1/distance².")]
		[Min(0f)] public float TailLengthMillionKm = 20f;
		[Tooltip("Brightness at 1 AU, as a multiplier. 1 is a bright naked-eye comet.")]
		[Min(0f)] public float Brightness = 1f;
		[Tooltip("Colour of the ion tail. The dust tail uses the tint.")]
		public Color IonTailColor = new Color(0.55f, 0.75f, 1f, 1f);

		private void Reset()
		{
			SkyRadiusKm = 10f;
			Orbit = new OrbitSettings { Distance = 17.8f, Eccentricity = 0.967f, InclinationDegrees = 18f, PeriodMode = OrbitPeriodMode.Kepler, PeriodDays = 27.3f };
			Tint = new Color(1f, 0.95f, 0.85f, 1f);
		}
	}
}
