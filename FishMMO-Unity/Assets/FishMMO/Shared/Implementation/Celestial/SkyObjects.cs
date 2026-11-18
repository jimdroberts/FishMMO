using System;
using UnityEngine;

namespace FishMMO.Shared.Celestial
{
	/// <summary>
	/// A yearly meteor shower: a stream of meteors from one point in the sky, peaking on one day
	/// of the calendar year. Seen only from bodies with an atmosphere.
	/// </summary>
	[Serializable]
	public class MeteorShower
	{
		public string Name = "Shower";
		[Tooltip("Home calendar day of the peak, 1-based.")]
		[Min(1)] public int PeakDayOfYear = 225;
		[Tooltip("Days either side of the peak with any activity.")]
		[Min(0.1f)] public float HalfWidthDays = 3f;
		[Tooltip("Meteors per hour at the peak, for one observer under a dark, clear sky.")]
		[Min(0f)] public float PeakPerHour = 60f;
		[Tooltip("Where the meteors seem to come from, in the home world's sky (degrees).")]
		[Range(0f, 360f)] public float RadiantRightAscension = 48f;
		[Range(-90f, 90f)] public float RadiantDeclination = 58f;
		public Color Color = new Color(1f, 0.95f, 0.8f, 1f);

		/// <summary>Meteors per hour on a (fractional) day of the year: a smooth bump around the peak.</summary>
		public float RateOn(double dayOfYear, int daysPerYear)
		{
			double d = dayOfYear - (PeakDayOfYear - 1);
			double year = Math.Max(1, daysPerYear);
			d -= Math.Round(d / year) * year;
			double x = d / Math.Max(0.1, HalfWidthDays);
			return Math.Abs(x) >= 1.0 ? 0f : (float)(PeakPerHour * (1.0 - x * x) * (1.0 - x * x));
		}
	}

	/// <summary>A ring of asteroids around the primary star, drawn as moving points.</summary>
	[Serializable]
	public class AsteroidBelt
	{
		public string Name = "Belt";
		[Tooltip("Inner edge in AU.")]
		[Min(0.01f)] public float InnerAU = 2.2f;
		[Tooltip("Outer edge in AU.")]
		[Min(0.01f)] public float OuterAU = 3.3f;
		[Tooltip("How many asteroids exist. The sky shows only the ones bright enough to see.")]
		[Range(0, 20000)] public int Count = 2000;
		[Tooltip("Spread above and below the plane, in degrees.")]
		[Range(0f, 30f)] public float ThicknessDegrees = 6f;
		public int Seed = 1;
		public Color Color = new Color(0.8f, 0.75f, 0.7f, 1f);
	}

	/// <summary>
	/// How much the sky may draw. The designer's cost panel compares a system against these; the
	/// sky renderer never draws more.
	/// </summary>
	[Serializable]
	public class SkyLimits
	{
		[Range(1, 4)] public int Suns = 4;
		[Range(0, 64)] public int Moons = 64;
		[Range(0, 256)] public int Planets = 256;
		[Range(0, 32)] public int Comets = 32;
		[Range(0, 2000)] public int VisibleAsteroids = 2000;
		[Range(0, 512)] public int Meteors = 512;
	}
}
