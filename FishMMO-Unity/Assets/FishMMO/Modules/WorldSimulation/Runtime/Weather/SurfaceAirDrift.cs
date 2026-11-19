using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// How far the air near the ground has travelled by a moment: a share of the scene's ten-metre wind summed exactly
	/// from the calendar epoch (<see cref="AirDrift.GroundCarried"/>) — the wind the weather reports, with what the
	/// scene's author and its runtime air add.
	/// </summary>
	/// <remarks>
	/// <para>
	/// What the fog banks, the mist, the motes and the water's suspended matter ride. They were carried by adding up each
	/// frame's wind on each client, so a bank stood wherever that client's frame history had left it: two players saw
	/// their fog in different places, and a rejoin moved it. This is a pure function of the world clock, so every machine
	/// puts it in the same place, and still follows the wind — it creeps in a calm and runs in a gale.
	/// </para>
	/// <para>
	/// The same sum the clouds and the weather field ride (<see cref="AirDrift"/>, 2026-10-15: "Route them through the
	/// same code, everything needs to be deterministic and properly driven by WorldTime"): it used to sum the reported
	/// wind alone, so an admin's or the bed's added wind ran the clouds and left the fog and the motes where they were.
	/// The ground's share, not the steering level's, and with no wander: the surface air is the slow layer under the
	/// clouds, carried the same way.
	/// </para>
	/// </remarks>
	public static class SurfaceAirDrift
	{
		/// <summary>World seconds between the wind's knots (<see cref="AirDrift.KnotSeconds"/>).</summary>
		public const double KnotSeconds = AirDrift.KnotSeconds;
		/// <summary>The wind at which the extra stirring <see cref="Stirred"/> counts is full, m/s.</summary>
		public const float StirWind = 8f;

		/// <summary>
		/// Where the surface air of a scene has got to by <paramref name="worldSeconds"/>, m (x east, y north), at
		/// <paramref name="share"/> of its wind; and <paramref name="stirred"/>, the seconds of a clock that runs up to
		/// twice as fast in wind (<see cref="Stirred"/>).
		/// </summary>
		public static void At(in AirDrift.Air air, float share, double worldSeconds, out double x, out double y, out double stirred)
		{
			double distance = Mathf.Max(0f, share) * AirDrift.GroundCarried(air, worldSeconds);
			Vector2 direction = WeatherDriver.PrevailingWind(air.Latitude, air.Belts);
			x = direction.x * distance;
			y = direction.y * distance;
			stirred = System.Math.Max(0.0, worldSeconds) + AirDrift.StirredBeyond(air, Mathf.Max(0f, share), worldSeconds);
		}

		/// <summary>The world's own surface air, with nothing a scene adds: for callers with no scene, and the tests.</summary>
		public static void At(uint seed, float latitudeDegrees, in WindBelts belts, float share, double worldSeconds,
			out double x, out double y, out double stirred)
		{
			var air = new AirDrift.Air { Seed = seed, Latitude = latitudeDegrees, Belts = belts, Driver = true };
			At(air, share, worldSeconds, out x, out y, out stirred);
		}

		/// <summary>How much faster than the clock a bank is stirred at this wind: 0 in a calm, 1 at <see cref="StirWind"/>.</summary>
		public static float Stirred(float speed) => Mathf.Clamp01(speed / StirWind);

		/// <summary>Forgets every running sum (tests: the next query walks from the epoch again).</summary>
		public static void ResetCache() => AirDrift.ResetCache();
	}
}
