using System;
using UnityEngine;

namespace FishMMO.Shared.Celestial
{
	/// <summary>What a world body is. Scenes can sit on any of them.</summary>
	public enum WorldBodyKind : byte
	{
		Planet = 0,
		Moon = 1,
		DwarfPlanet = 2,

		/// <summary>
		/// A world with no solid ground. It has no heightmap, no coastline and no scene can stand
		/// on it; what looks like a surface is a cloud deck at whatever depth the pressure makes one.
		/// </summary>
		/// <remarks>
		/// Appended rather than inserted: these values are serialized on every body asset, so
		/// renumbering would silently turn planets into moons.
		/// </remarks>
		GasGiant = 3,
	}

	/// <summary>How thick a body's air is. <see cref="None"/> means no weather, no meteors and a black day sky.</summary>
	public enum AtmosphereKind : byte
	{
		None = 0,
		Thin = 1,
		Standard = 2,
		Thick = 3,
	}

	/// <summary>Where an orbital period comes from.</summary>
	public enum OrbitPeriodMode : byte
	{
		/// <summary>Kepler's third law from the distance, scaled to the home world's year. Planets only.</summary>
		Kepler = 0,
		/// <summary><see cref="OrbitSettings.PeriodDays"/>, in home solar days.</summary>
		Authored = 1,
	}

	/// <summary>How a body's atlas radius is chosen.</summary>
	public enum AtlasRadiusMode : byte
	{
		/// <summary>Grows to fit the scenes placed on the body, never below the minimum.</summary>
		Auto = 0,
		/// <summary>Fixed; scenes that do not fit are reported.</summary>
		Manual = 1,
	}

	/// <summary>Whether a scene follows the world clock or shows one fixed time.</summary>
	public enum SceneTimeMode : byte
	{
		/// <summary>Local time from the body's rotation, the sun and the scene's longitude.</summary>
		World = 0,
		/// <summary>Always <see cref="WorldSceneSettings.FixedTimeOfDay01"/>. Developer-authored only.</summary>
		Fixed = 1,
	}

	/// <summary>
	/// Where a body is on its orbit.
	/// </summary>
	/// <remarks>
	/// Planets orbit the primary star and measure <see cref="Distance"/> in AU; moons orbit their
	/// parent and measure it in thousands of km. Moon periods are always authored: a gameplay moon's
	/// period is a design choice, and Kepler's law with invented masses would only obscure it.
	/// </remarks>
	[Serializable]
	public struct OrbitSettings
	{
		[Tooltip("Planets: AU from the star. Moons: thousands of km from the planet.")]
		[Min(0.0001f)] public float Distance;
		[Tooltip("0 is a circle. Planets and comets; comets go up to 0.99.")]
		[Range(0f, 0.99f)] public float Eccentricity;
		[Tooltip("Tilt of the orbit against the home world's orbit, in degrees.")]
		[Range(-90f, 90f)] public float InclinationDegrees;
		[Tooltip("Where the body is along its orbit at the world epoch, in degrees.")]
		[Range(0f, 360f)] public float OffsetDegrees;
		public OrbitPeriodMode PeriodMode;
		[Tooltip("Orbital period in home solar days, when authored. Always used for moons.")]
		[Min(0.01f)] public float PeriodDays;

		public static OrbitSettings Default => new OrbitSettings { Distance = 1f, PeriodDays = 27.3f };
	}

	/// <summary>A minimal double-precision vector for orbital maths, so nothing drifts over years of uptime.</summary>
	public readonly struct Vector3d
	{
		public readonly double X, Y, Z;

		public Vector3d(double x, double y, double z) { X = x; Y = y; Z = z; }

		public static Vector3d operator +(Vector3d a, Vector3d b) => new Vector3d(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
		public static Vector3d operator -(Vector3d a, Vector3d b) => new Vector3d(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
		public static Vector3d operator *(Vector3d a, double s) => new Vector3d(a.X * s, a.Y * s, a.Z * s);

		public double Magnitude => Math.Sqrt(X * X + Y * Y + Z * Z);
		public static double Dot(Vector3d a, Vector3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
		public static Vector3d Cross(Vector3d a, Vector3d b) => new Vector3d(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
		public Vector3 ToVector3() => new Vector3((float)X, (float)Y, (float)Z);

		public Vector3d Normalized
		{
			get
			{
				double m = Magnitude;
				return m > 1e-300 ? new Vector3d(X / m, Y / m, Z / m) : new Vector3d(0, 0, 0);
			}
		}

		public override string ToString() => $"({X:0.######}, {Y:0.######}, {Z:0.######})";
	}
}
