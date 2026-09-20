using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Celestial
{
	/// <summary>
	/// The whole solar system as data: its stars, planets and moons, the home world and the
	/// world calendar. Loaded on server and client alike, so both agree about every sunrise.
	/// </summary>
	/// <remarks>
	/// Comets are bodies (<see cref="CometBody"/>) in <see cref="Bodies"/>. Meteor showers and
	/// asteroid belts are plain data here; the sky renderer draws them, within <see cref="Limits"/>.
	/// </remarks>
	[CreateAssetMenu(fileName = "Solar System", menuName = "FishMMO/World/Solar System", order = 9)]
	public class SolarSystemProfile : CachedScriptableObject<SolarSystemProfile>, ICachedObject
	{
		[Tooltip("Stars, planets and moons. The first star is the primary.")]
		public List<CelestialBody> Bodies = new List<CelestialBody>();
		[Tooltip("The world the calendar belongs to.")]
		public WorldBody HomeWorld;
		[Tooltip("The seed the world's weather grows from. Every high, low and front is a function of this and the world clock, so the same seed gives the same weather on the server and on every client, and the same weather again after a restart. Changing it gives the world a different climate history.")]
		public uint WeatherSeed = 238;
		[Tooltip("The seed the night sky is scattered from. The stars belong to the system: every world and moon in it sees these same constellations, each from the angle its own axis gives it. Change it and the whole system gets a different sky; two systems with different seeds have different skies.")]
		public uint StarSeed = 238;
		[Tooltip("Roughly how long one weather system takes to pass, in world hours, at an ordinary wind. Judge it against the day length rather than against a real day: at 6 hours a day, five hours a system means the weather turns over about once a day. Lower it and the sky changes its mind several times a day; raise it for long settled spells. It sets how big the systems are, not how hard the wind blows, so the clouds keep moving at the same speed either way.")]
		[Min(0.25f)] public float WeatherSystemHours = 5f;
		public CalendarProfile Calendar;
		[Header("Small bodies")]
		public List<MeteorShower> MeteorShowers = new List<MeteorShower>();
		[Tooltip("Meteors per hour on any night, outside the showers.")]
		[Min(0f)] public float SporadicMeteorsPerHour = 6f;
		public List<AsteroidBelt> AsteroidBelts = new List<AsteroidBelt>();

		[Header("Sky")]
		public SkyLimits Limits = new SkyLimits();
		[Tooltip("Share of the visible sky, in percent, above which a body is drawn with its texture.")]
		[Range(0.001f, 5f)] public float TextureAboveSkyPercent = 0.1f;

		/// <summary>The loaded profile, if any. There is one per world.</summary>
		public static SolarSystemProfile Active => GetFirst<SolarSystemProfile>();

		/// <summary>The first star in <see cref="Bodies"/>, or null.</summary>
		public StarBody PrimaryStar
		{
			get
			{
				foreach (CelestialBody body in Bodies)
				{
					if (body is StarBody star)
					{
						return star;
					}
				}
				return null;
			}
		}

		public int DaysPerYear => Calendar != null ? Mathf.Max(1, Calendar.DaysPerYear) : 365;

		public long EpochUnixSeconds => Calendar != null ? Calendar.WorldEpochUnixSeconds : CalendarProfile.DefaultEpochUnixSeconds;
	}
}
