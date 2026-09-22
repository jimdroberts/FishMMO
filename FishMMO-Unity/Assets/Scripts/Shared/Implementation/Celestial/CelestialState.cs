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

	/// <summary>What stage a solar eclipse is at.</summary>
	public enum SolarEclipsePhase : byte
	{
		None = 0,
		/// <summary>The covering body has taken a bite out of the sun.</summary>
		Partial = 1,
		/// <summary>The covering body stands wholly inside the sun's disc, leaving a ring of it: too small to darken the day.</summary>
		Annular = 2,
		/// <summary>The sun is wholly covered: night in the middle of the day, the corona, the horizon lit all round.</summary>
		Total = 3,
	}

	/// <summary>What stage a lunar eclipse is at.</summary>
	public enum LunarEclipsePhase : byte
	{
		None = 0,
		/// <summary>The moon is in the planet's penumbra only: a little dimmer, hard to notice.</summary>
		Penumbral = 1,
		/// <summary>The umbra has taken a bite out of the moon.</summary>
		Partial = 2,
		/// <summary>The whole moon is in the umbra: dark red.</summary>
		Total = 3,
	}

	/// <summary>
	/// A solar eclipse in the terms the sky needs: how much is covered, how much of the diameter, what
	/// phase, and how dark it should look.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The whole sky used to darken in step with the covered AREA. That is the physics of the light,
	/// and it is why an eclipse looked like a switch: a camera at fixed exposure shows half the light
	/// as half the brightness, so the day dimmed visibly from first contact and was well on its way
	/// to dark long before totality, which then had nothing left to add. An eye does not see it that
	/// way, and neither does anyone who has stood under one: the day looks quite ordinary until the
	/// sun is nine tenths gone, strange in the last few minutes, and then, in a moment, night — the
	/// eye adapts, over about two and a half decades of light. <see cref="Darkness"/> is that
	/// response, and is what the sky, the ambient and the lights are dimmed by. Totality is its own
	/// thing on top, keyed to the last few per cent: the corona, the twilight sky, the stars.
	/// </para>
	/// </remarks>
	public struct SolarEclipseInfo
	{
		/// <summary>The share of the sun's area that is covered, 0..1.</summary>
		public float Obscuration;
		/// <summary>The share of the sun's DIAMETER that is covered, 0..1 (past 1 when the cover is bigger than the sun).</summary>
		public float Magnitude;
		public SolarEclipsePhase Phase;
		/// <summary>0..1 over the last few per cent of a total eclipse: how much of totality's own look is on.</summary>
		public float Totality;
		/// <summary>0..1: how dark the day looks, as an adapted eye would have it.</summary>
		public float Darkness;
		/// <summary>The body in front of the sun, or null.</summary>
		public CelestialBody Covering;

		public static readonly SolarEclipseInfo None = default;

		/// <summary>How dark a covered share of the sun looks: a log response over 2.5 decades, 1 at totality.</summary>
		public static float DarknessOf(float obscuration)
		{
			float uncovered = Mathf.Max(1f - Mathf.Clamp01(obscuration), 1e-4f);
			return Mathf.Clamp01(-Mathf.Log10(uncovered) / 2.5f);
		}
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
		/// <summary>Which stage of a lunar eclipse this moon is in.</summary>
		public LunarEclipsePhase ShadowPhase;
		/// <summary>Where its planet's shadow falls at its distance: the umbra's angular radius from the observer (rad), and the penumbra's.</summary>
		public float UmbraRadius, PenumbraRadius;
		/// <summary>Direction from the observer to the centre of that shadow at the moon's distance.</summary>
		public Vector3 ShadowDirection;
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

		/// <summary>
		/// Takes a direction in the SYSTEM's fixed frame — the ecliptic, which is where the stars are
		/// pinned — into scene space. Its columns are where the ecliptic's X, Y and pole point.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The stars belong to the system, not to the world under your feet. They are far enough off
		/// that going from one planet or moon of a system to another moves none of them by a pixel, so
		/// the constellations are the same from every body in it. What is not the same is which way
		/// that body's axis points among them: a world tilted twenty-three degrees has its pole star
		/// twenty-three degrees from the ecliptic's pole, a world tilted sixty has it sixty away, and
		/// an upright moon has the ecliptic's pole for its own.
		/// </para>
		/// <para>
		/// The star field used to be turned by <see cref="EquatorialToScene"/>, which is the observer's
		/// own equatorial frame. That frame is already tilted with the body, so the tilt cancelled:
		/// every world in the system had the same star over its pole and the same constellations at
		/// the same height — the sky of one world, worn by all of them. This goes through the same
		/// rotation out of the ecliptic that every planet and moon in the sky already goes through,
		/// so the stars and the bodies among them now agree about where the ecliptic is.
		/// </para>
		/// <para>Like <see cref="EquatorialToScene"/>, a reflection-rotation whose transpose is its inverse.</para>
		/// </remarks>
		public Matrix4x4 StarsToScene { get; private set; } = Matrix4x4.identity;

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

		/// <summary>0 … 1: how much of the primary sun's AREA is covered by another body, with the discs life-size.</summary>
		public float SolarEclipse => Solar.Obscuration;
		/// <summary>The body covering the sun, when <see cref="SolarEclipse"/> is above 0.</summary>
		public CelestialBody EclipsingBody => Solar.Covering;
		/// <summary>The solar eclipse in full, with the discs life-size: phase, magnitude, how dark it looks.</summary>
		public SolarEclipseInfo Solar { get; private set; }
		/// <summary>0 … 1: how much of the biggest moon's disc is in its planet's umbra.</summary>
		public float LunarEclipse => Moon >= 0 ? Bodies[Moon].Shadowed : 0f;
		public LunarEclipsePhase LunarPhase => Moon >= 0 ? Bodies[Moon].ShadowPhase : LunarEclipsePhase.None;

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
			CelestialMath.ToEquatorial(direction, Observer, out double ra, out double dec);
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
			Solar = SolarEclipseInfo.None;
			MeteorRate = 0f;

			if (system == null || observer == null)
			{
				IsDaylight = true;
				LocalTime01 = 0.5;
				EquatorialToScene = Matrix4x4.identity;
				StarsToScene = Matrix4x4.identity;
				SkyRotation = Quaternion.identity;
				return;
			}

			// The fixed stars: where the equatorial axes point in the scene.
			Vector3 x = EquatorialDirection(0.0, 0.0);
			Vector3 y = EquatorialDirection(Math.PI * 0.5, 0.0);
			Vector3 z = EquatorialDirection(0.0, Math.PI * 0.5);
			EquatorialToScene = Basis(x, y, z);
			// And the system's own axes, carried out of the ecliptic by this body's tilt like anything
			// else in the sky.
			StarsToScene = Basis(
				HeliocentricDirection(new Vector3d(1.0, 0.0, 0.0)),
				HeliocentricDirection(new Vector3d(0.0, 1.0, 0.0)),
				HeliocentricDirection(new Vector3d(0.0, 0.0, 1.0)));
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
				CelestialMath.ToEquatorial(toBody, observer, out double ra, out double dec);
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
					state.Shadowed = ShadowDepth(system, body, hours, starPosition, out state.ShadowPhase);
					if (IsMoon(body) && state.ShadowPhase != LunarEclipsePhase.None)
					{
						// Where the shadow falls, for the moon to be darkened against pixel by pixel: the
						// shadow's centre is on the axis from the sun through the planet, at the moon's
						// distance along it. Seen from the observer, who stands a planet's radius off that
						// axis, it is NOT exactly opposite the sun — the difference is about a degree at
						// our own moon, which is more than the umbra's radius there.
						PlanetShadowAt(system, body, hours, starPosition, out double umbraKm, out double penumbraKm, out _, out double alongKm);
						Vector3d planetPosition = CelestialMath.Position(system, body.Parent, hours);
						Vector3d axis = (planetPosition - starPosition).Normalized;
						Vector3d shadowCentre = planetPosition + axis * (alongKm / CelestialMath.AuKm);
						state.ShadowDirection = HeliocentricDirection(shadowCentre - observerPosition).normalized;
						double toShadowKm = (shadowCentre - observerPosition).Magnitude * CelestialMath.AuKm;
						state.UmbraRadius = (float)Math.Atan2(umbraKm, Math.Max(1.0, toShadowKm));
						state.PenumbraRadius = (float)Math.Atan2(penumbraKm, Math.Max(1.0, toShadowKm));
					}
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

		/// <summary>The solar eclipse with the discs life-size: the true one, for the world.</summary>
		private void ComputeSolarEclipse()
		{
			Solar = SolarEclipseAsDrawn(1f, 1f);
		}

		/// <summary>
		/// The solar eclipse as the discs are drawn: the sun's scaled by <paramref name="sunScale"/>,
		/// everything else's by <paramref name="bodyScale"/>. Life-size at 1 and 1.
		/// </summary>
		/// <remarks>
		/// A sky that flatters its bodies draws them bigger about the same centres, so on the screen
		/// they meet before the true discs do and part after; everything that makes an eclipse look
		/// like one has to be asked about the discs that are drawn, or the sun shines through the
		/// body in front of it for the whole of that stretch.
		/// </remarks>
		public SolarEclipseInfo SolarEclipseAsDrawn(float sunScale, float bodyScale)
		{
			var info = SolarEclipseInfo.None;
			if (Sun < 0 || Sun >= Bodies.Count)
			{
				return info;
			}
			SkyBodyState sun = Bodies[Sun];
			double a = sun.AngularRadius * Math.Max(0.01f, sunScale);
			double sunArea = Math.PI * a * a;
			if (sunArea <= 0.0)
			{
				return info;
			}
			for (int i = 0; i < Bodies.Count; i++)
			{
				if (i == Sun || Bodies[i].Kind == SkyBodyKind.Star || Bodies[i].Kind == SkyBodyKind.Comet || Bodies[i].DistanceKm >= sun.DistanceKm)
				{
					continue;
				}
				double b = Bodies[i].AngularRadius * Math.Max(0.01f, bodyScale);
				double d = Vector3.Angle(Bodies[i].Direction, sun.Direction) * CelestialMath.Deg2Rad;
				float covered = (float)Math.Min(1.0, CircleOverlap(a, b, d) / sunArea);
				if (covered <= info.Obscuration)
				{
					continue;
				}
				info.Obscuration = covered;
				info.Covering = Bodies[i].Body;
				info.Magnitude = (float)Math.Max(0.0, (a + b - d) / (2.0 * a));
				// Wholly inside the sun and smaller than it: a ring of sun is left, and that is never
				// dark. Wholly covering it: total. Anything else with a bite out of it: partial.
				bool inside = d <= Math.Abs(a - b);
				info.Phase = !inside ? SolarEclipsePhase.Partial : b < a ? SolarEclipsePhase.Annular : SolarEclipsePhase.Total;
				// Totality comes on over the last few per cent, and only where the cover CAN cover
				// the sun: an annular eclipse peaks at 96% and stays broad daylight.
				info.Totality = b >= a * 0.98 ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.93f, 1f, covered)) : 0f;
				info.Darkness = SolarEclipseInfo.DarknessOf(covered);
			}
			return info;
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
		/// Where a planet's shadow falls at one of its moons: the umbra's and the penumbra's radii there,
		/// in kilometres, and how far the moon's centre stands from the shadow's axis.
		/// </summary>
		/// <remarks>
		/// The umbra narrows with distance from the planet (the sun is bigger than the planet) and the
		/// penumbra widens; at our own moon they come to about 0.72 and 1.28 of the planet's radius,
		/// which is what these give. The moon used to be treated as a point with a made-up shadow
		/// profile between 0.75 and 1.35 radii, so the whole disc reddened a little as soon as it
		/// touched the penumbra — where a real eclipse is nearly invisible — and there was never a bite.
		/// </remarks>
		public static void PlanetShadowAt(SolarSystemProfile system, CelestialBody moon, double hours, Vector3d starPosition, out double umbraKm, out double penumbraKm, out double offAxisKm, out double alongKm)
		{
			CelestialBody planet = moon.Parent;
			Vector3d planetPosition = CelestialMath.Position(system, planet, hours);
			Vector3d toPlanet = planetPosition - starPosition;
			double sunDistanceKm = toPlanet.Magnitude * CelestialMath.AuKm;
			Vector3d axis = toPlanet.Normalized;
			Vector3d offset = CelestialMath.Position(system, moon, hours) - planetPosition;
			alongKm = Vector3d.Dot(offset, axis) * CelestialMath.AuKm;
			offAxisKm = (offset - axis * (alongKm / CelestialMath.AuKm)).Magnitude * CelestialMath.AuKm;
			double sunRadiusKm = system != null && system.PrimaryStar != null ? system.PrimaryStar.SkyRadiusKm : 696000.0;
			double planetRadiusKm = planet.SkyRadiusKm;
			double reach = Math.Max(0.0, alongKm) / Math.Max(1.0, sunDistanceKm);
			umbraKm = Math.Max(0.0, planetRadiusKm - reach * (sunRadiusKm - planetRadiusKm));
			penumbraKm = planetRadiusKm + reach * (sunRadiusKm + planetRadiusKm);
		}

		/// <summary>
		/// How much of a moon's disc is in its planet's umbra, 0..1, and which phase that is. Planets
		/// are never shadowed here.
		/// </summary>
		public static float ShadowDepth(SolarSystemProfile system, CelestialBody body, double hours, Vector3d starPosition, out LunarEclipsePhase phase)
		{
			phase = LunarEclipsePhase.None;
			if (!IsMoon(body))
			{
				return 0f;
			}
			PlanetShadowAt(system, body, hours, starPosition, out double umbra, out double penumbra, out double off, out double along);
			if (along <= 0.0)
			{
				return 0f;
			}
			double moonRadius = Math.Max(1.0, body.SkyRadiusKm);
			double moonArea = Math.PI * moonRadius * moonRadius;
			float inUmbra = (float)(CircleOverlap(umbra, moonRadius, off) / moonArea);
			float inPenumbra = (float)(CircleOverlap(penumbra, moonRadius, off) / moonArea);
			if (inUmbra >= 0.999f)
			{
				phase = LunarEclipsePhase.Total;
			}
			else if (inUmbra > 0f)
			{
				phase = LunarEclipsePhase.Partial;
			}
			else if (inPenumbra > 0f)
			{
				phase = LunarEclipsePhase.Penumbral;
			}
			return Mathf.Clamp01(inUmbra);
		}
	}

}
