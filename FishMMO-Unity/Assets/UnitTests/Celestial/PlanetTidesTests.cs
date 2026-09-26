using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Celestial;
using FishMMO.Water;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests.Celestial
{
	/// <summary>
	/// The tide: how high a close moon lifts the sea, and how a scene caps that without the sea lurching.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Built to the shape of the Soluris system, where the problem was found (2026-09-26): Arthis is a
	/// small world with a six-hour day, and its moon Helis orbits under fourteen of its radii out. The
	/// equilibrium tide that raises is ninety metres. The water clamped that to ±2.5 m, so the sea sat
	/// at one limit for hours and fell five metres almost at once — the whole sea visibly jumping with
	/// the moon. It is now scaled down whole instead (WaterEnvironment.ScaledTide).
	/// </para>
	/// </remarks>
	[TestFixture]
	public class PlanetTidesTests
	{
		private readonly List<ScriptableObject> created = new List<ScriptableObject>();
		private SolarSystemProfile system;
		private WorldBody home;

		[SetUp]
		public void BuildSystem()
		{
			// Soluris: a dim orange star. Mass comes from luminosity (L^(1/3.5)).
			StarBody sun = Make<StarBody>("Soluris");
			sun.SkyRadiusKm = 634786f;
			sun.Luminosity = 0.45028606f;

			home = Make<WorldBody>("Arthis");
			home.Parent = sun;
			home.SkyRadiusKm = 3583.1504f;
			home.RotationHours = 6f;
			home.AxialTiltDegrees = 23.4f;
			home.Orbit = new OrbitSettings { Distance = 0.7077031f, PeriodDays = 365f };

			system = Make<SolarSystemProfile>("Soluris System");
			system.Bodies.Add(sun);
			system.Bodies.Add(home);
			system.HomeWorld = home;

			// Moon distances are thousands of kilometres; periods home solar days, always authored.
			Moon("Helis", 48.884457f, 6.85f);
			Moon("Orirvar", 112.34743f, -4.75f);
		}

		[TearDown]
		public void TearDown()
		{
			foreach (ScriptableObject asset in created)
			{
				Object.DestroyImmediate(asset);
			}
			created.Clear();
		}

		private T Make<T>(string name) where T : ScriptableObject
		{
			T asset = ScriptableObject.CreateInstance<T>();
			asset.name = name;
			created.Add(asset);
			return asset;
		}

		private WorldBody Moon(string name, float thousandsOfKm, float inclination)
		{
			WorldBody moon = Make<WorldBody>(name);
			moon.Parent = home;
			moon.SkyRadiusKm = 1254.1027f;
			moon.Orbit = new OrbitSettings { Distance = thousandsOfKm, PeriodDays = 1.5f, InclinationDegrees = inclination };
			system.Bodies.Add(moon);
			return moon;
		}

		[Test]
		public void ACloseMoonRaisesATideOfTensOfMetres()
		{
			/* h = 1.5 · (M_moon / M_world) · R⁴ / d³, masses from radius at constant density. For Helis
			 * over Arthis that is 90.75 m — the physics is right; it is the size a moon that close
			 * raises, and why a scene must be able to cap it without clipping it. */
			CelestialBody helis = system.Bodies.Find(b => b.name == "Helis");
			double bulge = PlanetTides.BulgeMetres(system, home, helis, 0.0);
			LogAssert.IsTrue(Math.Abs(bulge - 90.75) < 0.9, $"Helis should raise a 90.75 m bulge on Arthis, got {bulge:0.00} m");
		}

		[Test]
		public void TheReachBoundsTheTideAtEveryLatitude()
		{
			foreach (double latitude in new[] { 0.0, 25.0, 50.0, 75.0, -40.0 })
			{
				for (double hours = 0.0; hours < 72.0; hours += 0.05)
				{
					double height = PlanetTides.HeightMetres(system, home, hours, latitude, 30.0, out double reach);
					LogAssert.IsTrue(Math.Abs(height) <= reach * (1.0 + 1e-9) + 1e-9,
						$"at {latitude}° and {hours:0.00} h the tide {height:0.000} m passed its reach {reach:0.000} m");
				}
			}
		}

		[Test]
		public void AHugeTideIsScaledWholeNotClipped()
		{
			const float Amplification = 2.5f, Maximum = 2.5f;
			const double Latitude = 25.0, Longitude = 30.0;
			float highest = float.MinValue, lowest = float.MaxValue, steepest = 0f, clampedSteepest = 0f;
			float previous = 0f, previousClamped = 0f;
			bool first = true;
			// Three days of world time at one-minute steps.
			for (int minute = 0; minute < 72 * 60; minute++)
			{
				double hours = minute / 60.0;
				double equilibrium = PlanetTides.HeightMetres(system, home, hours, Latitude, Longitude, out double reach);
				float tide = WaterEnvironment.ScaledTide(equilibrium, reach, Amplification, Maximum);
				// What the water used to do with the same tide: amplify, then clamp.
				float clamped = Mathf.Clamp((float)equilibrium * Amplification, -Maximum, Maximum);
				if (!first)
				{
					steepest = Mathf.Max(steepest, Mathf.Abs(tide - previous));
					clampedSteepest = Mathf.Max(clampedSteepest, Mathf.Abs(clamped - previousClamped));
				}
				first = false;
				previous = tide;
				previousClamped = clamped;
				highest = Mathf.Max(highest, tide);
				lowest = Mathf.Min(lowest, tide);
			}

			LogAssert.IsTrue(highest <= Maximum + 1e-4f && lowest >= -Maximum - 1e-4f,
				$"the tide must stay within ±{Maximum} m, went {lowest:0.00} to {highest:0.00}");
			// Large tides are wanted: the highest high water reaches most of the limit.
			LogAssert.IsTrue(highest >= 0.8f * Maximum, $"high water only reached {highest:0.00} m of {Maximum} m");
			LogAssert.IsTrue(highest - lowest >= Maximum, $"the range was only {highest - lowest:0.00} m");
			/* Smooth: a tide comes in and goes out. A few centimetres a world minute at most — about five
			 * here, where Helis brings high water every nine hours; the clamp, on the same tide, moved
			 * the whole sea by three metres in a minute (both measured with an independent model). */
			LogAssert.IsTrue(steepest <= 0.05f * Maximum, $"the tide moved {steepest:0.000} m in one minute");
			LogAssert.IsTrue(clampedSteepest > 0.5f, $"control: the old clamp should jump, but moved at most {clampedSteepest:0.000} m a minute");
		}
	}
}
