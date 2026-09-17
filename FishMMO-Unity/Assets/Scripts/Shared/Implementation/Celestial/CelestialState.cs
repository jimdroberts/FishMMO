using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Celestial
{
	public enum SkyBodyKind : byte
	{
		Star = 0,
		Planet = 1,
		Moon = 2,
		Comet = 3,
	}

	/// <summary>One body in a scene's sky, in scene space.</summary>
	public struct SkyBodyState
	{
		public CelestialBody Body;
		public SkyBodyKind Kind;
		/// <summary>Unit vector from the viewer toward the body, in scene space (+Y up).</summary>
		public Vector3 Direction;
		/// <summary>Unit vector from the body toward the primary star, in scene space: the lit side.</summary>
		public Vector3 LightDirection;
		/// <summary>Angular radius in radians.</summary>
		public float AngularRadius;
		/// <summary>Lit fraction, 0 new … 1 full; 1 for stars.</summary>
		public float Illumination;
		/// <summary>0 … 1: how deep the body is in another body's shadow (a lunar eclipse).</summary>
		public float Shadowed;
		public float AltitudeDegrees;
		public bool Textured;
		/// <summary>Comets: tail direction in scene space and its angular length in radians.</summary>
		public Vector3 TailDirection;
		public float TailLength;
		/// <summary>Comets: brightness relative to their authored 1 AU value.</summary>
		public float Brightness;
		public double DistanceKm;

		public bool AboveHorizon => AltitudeDegrees > -2f;
	}

	/// <summary>
	/// Everything in one scene's sky at one moment: the suns, moons, planets and comets as scene
	/// directions, the day/night state, eclipses, and the turn of the fixed stars.
	/// </summary>
	/// <remarks>
	/// The scene frame follows the world atlas: at heading 0 the scene's +Z is north and +X east;
	/// a scene's heading turns the sky with it. Server and client compute the same state from
	/// the same world time. Double precision throughout; only the resulting directions are floats.
	/// </remarks>
	public sealed class CelestialState
	{
		public readonly List<SkyBodyState> Bodies = new List<SkyBodyState>();

		public SolarSystemProfile System { get; private set; }
		public WorldBody Observer { get; private set; }
		public double Hours { get; private set; }
		public double Latitude { get; private set; }
		public double Longitude { get; private set; }
		public float HeadingDegrees { get; private set; }

		/// <summary>
		/// Takes a direction in the observer's equatorial (fixed-star) frame into scene space. Its
		/// columns are where RA 0, RA 90° and the celestial pole point. The scene frame (east, up,
		/// north) has the other handedness, so this is a reflection-rotation, not a quaternion;
		/// its transpose is its inverse.
		/// </summary>
		public Matrix4x4 EquatorialToScene { get; private set; } = Matrix4x4.identity;

		/// <summary>A proper rotation that turns with the stars, for objects that follow the sky.</summary>
		public Quaternion SkyRotation { get; private set; } = Quaternion.identity;

		/// <summary>Index of the primary star in <see cref="Bodies"/>, or -1.</summary>
		public int Sun { get; private set; } = -1;
		/// <summary>Index of the biggest moon in the sky, or -1.</summary>
		public int Moon { get; private set; } = -1;

		public Vector3 SunDirection => Sun >= 0 ? Bodies[Sun].Direction : Vector3.down;
		public float SunAltitude => Sun >= 0 ? Bodies[Sun].AltitudeDegrees : -90f;
		public bool IsDaylight { get; private set; }
		public double LocalTime01 { get; private set; }

		/// <summary>0 … 1: how much of the primary sun is covered by another body.</summary>
		public float SolarEclipse { get; private set; }
		/// <summary>The body covering the sun, when <see cref="SolarEclipse"/> is above 0.</summary>
		public CelestialBody EclipsingBody { get; private set; }
		/// <summary>0 … 1: how deep the biggest moon is in its planet's shadow.</summary>
		public float LunarEclipse => Moon >= 0 ? Bodies[Moon].Shadowed : 0f;

		/// <summary>Meteors per hour right now (showers plus sporadic), 0 without air.</summary>
		public float MeteorRate { get; private set; }

		/// <summary>The scene-space direction of an altitude and azimuth (degrees) under a scene heading.</summary>
		public static Vector3 SceneDirection(double altitudeDegrees, double azimuthDegrees, double headingDegrees)
		{
			double alt = altitudeDegrees * CelestialMath.Deg2Rad;
			double az = (azimuthDegrees - headingDegrees) * CelestialMath.Deg2Rad;
			double c = Math.Cos(alt);
			return new Vector3((float)(Math.Sin(az) * c), (float)Math.Sin(alt), (float)(Math.Cos(az) * c));
		}

		/// <summary>An equatorial direction (right ascension and declination, radians) in scene space.</summary>
		public Vector3 EquatorialDirection(double rightAscension, double declination)
		{
			double hourAngle = CelestialSky.MeridianRightAscension(System, Observer, Hours, Longitude) - rightAscension;
			CelestialSky.Horizontal(Latitude, declination, CelestialMath.WrapPi(hourAngle), out double alt, out double az);
			return SceneDirection(alt, az, HeadingDegrees);
		}

		/// <summary>A heliocentric direction (AU frame) as seen from the observer, in scene space.</summary>
		public Vector3 HeliocentricDirection(Vector3d direction)
		{
			CelestialMath.ToEquatorial(direction, (Observer != null ? Observer.AxialTiltDegrees : 0.0) * CelestialMath.Deg2Rad, out double ra, out double dec);
			return EquatorialDirection(ra, dec);
		}

		/// <summary>Computes the sky. Without a system or observer the sky is empty and it is day.</summary>
		public void Compute(SolarSystemProfile system, WorldBody observer, double hours, double latitude, double longitude, float headingDegrees)
		{
			System = system;
			Observer = observer;
			Hours = hours;
			Latitude = latitude;
			Longitude = longitude;
			HeadingDegrees = headingDegrees;
			Bodies.Clear();
			Sun = -1;
			Moon = -1;
			SolarEclipse = 0f;
			EclipsingBody = null;
			MeteorRate = 0f;

			if (system == null || observer == null)
			{
				IsDaylight = true;
				LocalTime01 = 0.5;
				EquatorialToScene = Matrix4x4.identity;
				SkyRotation = Quaternion.identity;
				return;
			}

			// The fixed stars: where the equatorial axes point in the scene.
			Vector3 x = EquatorialDirection(0.0, 0.0);
			Vector3 y = EquatorialDirection(Math.PI * 0.5, 0.0);
			Vector3 z = EquatorialDirection(0.0, Math.PI * 0.5);
			EquatorialToScene = Basis(x, y, z);
			SkyRotation = Basis(x, y, -z).rotation;

			Vector3d observerPosition = CelestialMath.Position(system, observer, hours);
			CelestialBody primary = system.PrimaryStar;
			Vector3d starPosition = CelestialMath.Position(system, primary, hours);
			float biggestMoon = 0f;

			foreach (CelestialBody body in system.Bodies)
			{
				if (body == null || body == observer)
				{
					continue;
				}
				Vector3d position = CelestialMath.Position(system, body, hours);
				Vector3d toBody = position - observerPosition;
				double km = Math.Max(1.0, toBody.Magnitude * CelestialMath.AuKm);
				CelestialMath.ToEquatorial(toBody, observer.AxialTiltDegrees * CelestialMath.Deg2Rad, out double ra, out double dec);
				double hourAngle = CelestialMath.HourAngle(system, observer, hours, longitude, ra);
				CelestialSky.Horizontal(latitude, dec, hourAngle, out double alt, out double az);
				double angularDiameter = CelestialMath.AngularDiameter(body.SkyRadiusKm, km);

				var state = new SkyBodyState
				{
					Body = body,
					Kind = body is StarBody ? SkyBodyKind.Star : body is CometBody ? SkyBodyKind.Comet : IsMoon(body) ? SkyBodyKind.Moon : SkyBodyKind.Planet,
					Direction = SceneDirection(alt, az, headingDegrees),
					AngularRadius = (float)(angularDiameter * 0.5),
					AltitudeDegrees = (float)alt,
					DistanceKm = km,
					Illumination = 1f,
					LightDirection = Vector3.up,
				};

				if (!(body is StarBody))
				{
					state.Illumination = (float)CelestialMath.Illumination(system, observer, body, hours);
					state.LightDirection = HeliocentricDirection(starPosition - position).normalized;
					state.Textured = CelestialMath.SkyFraction(angularDiameter) * 100.0 >= system.TextureAboveSkyPercent;
					state.Shadowed = ShadowDepth(system, body, hours, starPosition);
				}
				if (body is CometBody comet)
				{
					Vector3d tip = CelestialSky.CometTailTip(system, comet, hours);
					Vector3 tipDirection = HeliocentricDirection(tip - observerPosition);
					state.TailDirection = (tipDirection - state.Direction).normalized;
					state.TailLength = Vector3.Angle(tipDirection, state.Direction) * Mathf.Deg2Rad;
					state.Brightness = (float)CelestialSky.CometBrightness(system, comet, hours);
				}

				if (body == primary)
				{
					Sun = Bodies.Count;
				}
				else if (state.Kind == SkyBodyKind.Moon && state.AngularRadius > biggestMoon)
				{
					biggestMoon = state.AngularRadius;
					Moon = Bodies.Count;
				}
				Bodies.Add(state);
			}

			IsDaylight = CelestialMath.IsDaylight(system, observer, hours, latitude, longitude);
			LocalTime01 = CelestialMath.LocalTime01(system, observer, hours, longitude);
			ComputeSolarEclipse();

			if (observer.HasWeather)
			{
				double day = hours / CelestialMath.HomeSolarDayHours(system);
				double dayOfYear = day - Math.Floor(day / system.DaysPerYear) * system.DaysPerYear;
				MeteorRate = CelestialSky.MeteorRate(system, dayOfYear);
			}
		}

		private static bool IsMoon(CelestialBody body) => body.Parent != null && !(body.Parent is StarBody);

		/// <summary>The matrix whose columns are the three given axes.</summary>
		public static Matrix4x4 Basis(Vector3 x, Vector3 y, Vector3 z)
		{
			var m = new Matrix4x4();
			m.SetColumn(0, new Vector4(x.x, x.y, x.z, 0f));
			m.SetColumn(1, new Vector4(y.x, y.y, y.z, 0f));
			m.SetColumn(2, new Vector4(z.x, z.y, z.z, 0f));
			m.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));
			return m;
		}

		/// <summary>How much of the primary sun's disc other bodies cover.</summary>
		private void ComputeSolarEclipse()
		{
			if (Sun < 0)
			{
				return;
			}
			SkyBodyState sun = Bodies[Sun];
			double sunArea = Math.PI * sun.AngularRadius * sun.AngularRadius;
			if (sunArea <= 0.0)
			{
				return;
			}
			for (int i = 0; i < Bodies.Count; i++)
			{
				if (i == Sun || Bodies[i].Kind == SkyBodyKind.Star || Bodies[i].Kind == SkyBodyKind.Comet || Bodies[i].DistanceKm >= sun.DistanceKm)
				{
					continue;
				}
				double separation = Vector3.Angle(Bodies[i].Direction, sun.Direction) * CelestialMath.Deg2Rad;
				double covered = CircleOverlap(sun.AngularRadius, Bodies[i].AngularRadius, separation) / sunArea;
				if (covered > SolarEclipse)
				{
					SolarEclipse = (float)Math.Min(1.0, covered);
					EclipsingBody = Bodies[i].Body;
				}
			}
		}

		/// <summary>Area of the intersection of two circles with radii a and b whose centres are d apart.</summary>
		public static double CircleOverlap(double a, double b, double d)
		{
			if (d >= a + b)
			{
				return 0.0;
			}
			if (d <= Math.Abs(a - b))
			{
				double r = Math.Min(a, b);
				return Math.PI * r * r;
			}
			double alpha = Math.Acos(Math.Max(-1.0, Math.Min(1.0, (d * d + a * a - b * b) / (2.0 * d * a))));
			double beta = Math.Acos(Math.Max(-1.0, Math.Min(1.0, (d * d + b * b - a * a) / (2.0 * d * b))));
			return a * a * (alpha - Math.Sin(2.0 * alpha) * 0.5) + b * b * (beta - Math.Sin(2.0 * beta) * 0.5);
		}

		/// <summary>
		/// How deep a moon is in its planet's shadow: 1 in the umbra, fading to 0 at the edge of
		/// the penumbra. Planets are never shadowed here.
		/// </summary>
		public static float ShadowDepth(SolarSystemProfile system, CelestialBody body, double hours, Vector3d starPosition)
		{
			if (!IsMoon(body))
			{
				return 0f;
			}
			CelestialBody planet = body.Parent;
			Vector3d planetPosition = CelestialMath.Position(system, planet, hours);
			Vector3d axis = (planetPosition - starPosition).Normalized;
			Vector3d offset = CelestialMath.Position(system, body, hours) - planetPosition;
			double along = Vector3d.Dot(offset, axis);
			if (along <= 0.0)
			{
				return 0f;
			}
			double perpendicular = (offset - axis * along).Magnitude * CelestialMath.AuKm;
			double radius = planet.SkyRadiusKm;
			double inner = radius * 0.75, outer = radius * 1.35;
			double depth = 1.0 - (perpendicular - inner) / (outer - inner);
			return (float)Math.Max(0.0, Math.Min(1.0, depth));
		}
	}
}
