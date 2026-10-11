using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Weather;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// The hot ground (GeothermalVents): where its sites are, and each geyser's schedule on the shared
	/// world clock — one eruption an interval, scattered about it, the steam gone before the next.
	/// </summary>
	[TestFixture]
	public class GeothermalVentsTests
	{
		private static GeothermalSite Geyser(float size, uint seed)
		{
			return new GeothermalSite { Position = new Vector2(100f, 200f), Kind = GeothermalKind.Geyser, Size = size, Seed = seed };
		}

		/// <summary>The starts of every eruption between two moments, read a second at a time.</summary>
		private static List<double> Starts(in GeothermalSite site, double from, double to)
		{
			var starts = new List<double>();
			long last = long.MinValue;
			for (double t = from; t < to; t += 1.0)
			{
				GeyserMoment m = GeothermalVents.Moment(site, t);
				if (m.Erupting && m.Eruption != last)
				{
					last = m.Eruption;
					starts.Add(t - m.Since);
				}
			}
			return starts;
		}

		[Test]
		public void TheSchedule_IsAPureFunctionOfTheClock()
		{
			GeothermalSite site = Geyser(0.5f, 12345u);
			foreach (double t in new[] { 0.0, 1234.5, 1.7e9 + 321.25 })
			{
				GeyserMoment a = GeothermalVents.Moment(site, t);
				GeyserMoment b = GeothermalVents.Moment(site, t);
				Assert.That(a.Eruption, Is.EqualTo(b.Eruption));
				Assert.That(a.Since, Is.EqualTo(b.Since));
				Assert.That(a.Column, Is.EqualTo(b.Column));
			}
		}

		[Test]
		public void AGeyser_EruptsOnceAnInterval_ScatteredAboutIt()
		{
			foreach (float size in new[] { 0.05f, 0.5f, 1f })
			{
				GeothermalSite site = Geyser(size, 777u + (uint)(size * 100f));
				double interval = GeothermalVents.IntervalSeconds(site);
				// Far from the epoch, where a float clock would long since have lost the second.
				double from = 1.6e9;
				List<double> starts = Starts(site, from, from + 12 * interval);
				Assert.That(starts.Count, Is.InRange(11, 13), $"size {size}: one an interval");
				for (int i = 1; i < starts.Count; i++)
				{
					double gap = starts[i] - starts[i - 1];
					Assert.That(gap, Is.InRange(interval * (1.0 - GeothermalVents.StartScatter) - 1.0, interval * (1.0 + GeothermalVents.StartScatter) + 1.0),
						$"size {size}: a gap of {gap:0} s against {interval:0}");
				}
			}
		}

		[Test]
		public void AnEruption_LastsAboutItsOwnLength_AndTheSteamClearsBeforeTheNext()
		{
			GeothermalSite site = Geyser(0.6f, 4242u);
			double interval = GeothermalVents.IntervalSeconds(site);
			float own = GeothermalVents.DurationSeconds(site);
			float steam = GeothermalVents.SteamSeconds(site);
			double from = 3.0e8;
			List<double> starts = Starts(site, from, from + 6 * interval);
			Assert.That(starts.Count, Is.GreaterThanOrEqualTo(5));
			for (int i = 0; i < starts.Count - 1; i++)
			{
				GeyserMoment during = GeothermalVents.Moment(site, starts[i] + 1.0);
				Assert.That(during.Duration, Is.InRange(own * (1f - GeothermalVents.DurationScatter) - 0.01f, own * (1f + GeothermalVents.DurationScatter) + 0.01f));
				Assert.That(during.Steam, Is.EqualTo(1f));
				GeyserMoment after = GeothermalVents.Moment(site, starts[i] + during.Duration + 0.5 * steam);
				Assert.That(after.Column, Is.EqualTo(0f), "the water has stopped");
				Assert.That(after.Steam, Is.GreaterThan(0f).And.LessThan(1f), "the steam drifts off");
				GeyserMoment quiet = GeothermalVents.Moment(site, starts[i] + during.Duration + steam + 1.0);
				Assert.That(quiet.Steam, Is.EqualTo(0f), "and is gone");
				Assert.That(starts[i] + during.Duration + steam, Is.LessThan(starts[i + 1]), "before the next eruption");
			}
		}

		[Test]
		public void TheColumn_RisesStandsAndSinks()
		{
			const float duration = 60f;
			Assert.That(GeothermalVents.ColumnEnvelope(-1f, duration), Is.EqualTo(0f));
			Assert.That(GeothermalVents.ColumnEnvelope(1f, duration), Is.GreaterThan(0f).And.LessThan(0.5f), "rising");
			Assert.That(GeothermalVents.ColumnEnvelope(30f, duration), Is.EqualTo(1f), "standing");
			Assert.That(GeothermalVents.ColumnEnvelope(55f, duration), Is.GreaterThan(0f).And.LessThan(0.5f), "sinking");
			Assert.That(GeothermalVents.ColumnEnvelope(duration, duration), Is.EqualTo(0f));
		}

		[Test]
		public void TheSameGeyser_ThrowsHigherUnderWeakerGravity()
		{
			GeothermalSite site = Geyser(0.8f, 9u);
			float here = GeothermalVents.ColumnMetres(site, 9.81f);
			Assert.That(here, Is.InRange(GeothermalVents.LowestColumn, GeothermalVents.HighestColumn + 1f));
			Assert.That(GeothermalVents.ColumnMetres(site, 3.7f), Is.EqualTo(here * 9.81f / 3.7f).Within(here * 0.01f));
		}

		[Test]
		public void BiggerGeysers_WaitLongerAndPlayLonger()
		{
			GeothermalSite small = Geyser(0.1f, 5u), big = Geyser(0.95f, 5u);
			Assert.That(GeothermalVents.IntervalSeconds(big), Is.GreaterThan(GeothermalVents.IntervalSeconds(small)));
			Assert.That(GeothermalVents.DurationSeconds(big), Is.GreaterThan(GeothermalVents.DurationSeconds(small)));
			Assert.That(GeothermalVents.IntervalSeconds(small), Is.GreaterThanOrEqualTo(GeothermalVents.ShortestInterval * 0.8f));
		}

		[Test]
		public void APoolSteams_CoolerThanAVent()
		{
			var vent = new GeothermalSite { Kind = GeothermalKind.Fumarole, Size = 0.5f };
			var pool = new GeothermalSite { Kind = GeothermalKind.HotSpring, Size = 0.5f };
			Assert.That(GeothermalVents.SourceC(vent, 95f), Is.EqualTo(95f));
			Assert.That(GeothermalVents.SourceC(pool, 95f), Is.InRange(65f, 90f));
			Assert.That(GeothermalVents.SourceRadius(pool), Is.GreaterThan(GeothermalVents.SourceRadius(vent)));
			Assert.That(GeothermalVents.RiseSpeed(pool), Is.LessThan(GeothermalVents.RiseSpeed(vent)));
		}

		[Test]
		public void TheSites_AreTheSameWhateverAreaIsAsked()
		{
			var steam = ScriptableObject.CreateInstance<WeatherSubstance>();
			var basin = ScriptableObject.CreateInstance<BiomeTemplate>();
			try
			{
				steam.Vapour = true;
				basin.Emits = steam;
				basin.EmissionRate = 0.5f;
				var origin = new Vector2(-1000f, -1000f);
				var whole = new List<GeothermalSite>();
				var part = new List<GeothermalSite>();
				GeothermalVents.Find(origin, new Rect(-1000f, -1000f, 2000f, 2000f), 99u, _ => basin, whole);
				GeothermalVents.Find(origin, new Rect(-300f, -200f, 500f, 400f), 99u, _ => basin, part);
				Assert.That(whole.Count, Is.GreaterThan(100), "a basin is thick with springs");
				Assert.That(part.Count, Is.GreaterThan(0));
				foreach (GeothermalSite p in part)
				{
					Assert.That(whole.Exists(w => w.Position == p.Position && w.Kind == p.Kind && w.Seed == p.Seed), Is.True, "a smaller search finds the same sites");
				}
				int geysers = whole.FindAll(s => s.Kind == GeothermalKind.Geyser).Count;
				Assert.That(geysers, Is.GreaterThan(0).And.LessThan(whole.Count / 2), "geysers among springs and vents");

				// Ground that does not steam has none; volcanic ground has fumaroles only.
				GeothermalVents.Find(origin, new Rect(-1000f, -1000f, 2000f, 2000f), 99u, _ => null, part);
				Assert.That(part.Count, Is.EqualTo(0));
			}
			finally
			{
				Object.DestroyImmediate(basin);
				Object.DestroyImmediate(steam);
			}
		}

		[Test]
		public void VolcanicGround_SteamsFromFumarolesOnly()
		{
			var ash = ScriptableObject.CreateInstance<WeatherSubstance>();
			var volcano = ScriptableObject.CreateInstance<BiomeTemplate>();
			try
			{
				ash.Vented = true;
				volcano.Emits = ash;
				volcano.EmissionRate = 0.15f;
				var sites = new List<GeothermalSite>();
				GeothermalVents.Find(Vector2.zero, new Rect(0f, 0f, 3000f, 3000f), 7u, _ => volcano, sites);
				Assert.That(sites.Count, Is.GreaterThan(0));
				Assert.That(sites.TrueForAll(s => s.Kind == GeothermalKind.Fumarole && s.Substance == null), Is.True);
			}
			finally
			{
				Object.DestroyImmediate(volcano);
				Object.DestroyImmediate(ash);
			}
		}

		[Test]
		public void Steam_IsNotWeather()
		{
			var steam = ScriptableObject.CreateInstance<WeatherSubstance>();
			var basin = ScriptableObject.CreateInstance<BiomeTemplate>();
			try
			{
				steam.Vapour = true;
				basin.Emits = steam;
				basin.EmissionRate = 0.5f;
				GroundTraits ground = GroundTraits.Of(basin, false);
				Assert.That(ground.Emits, Is.Null, "nothing falls out of it");
				Assert.That(ground.EmissionRate, Is.EqualTo(0f), "and it raises no eruption");
				Assert.That(VolcanicVents.IsVented(basin), Is.False, "nor a volcanic vent's plume");
			}
			finally
			{
				Object.DestroyImmediate(basin);
				Object.DestroyImmediate(steam);
			}
		}
	}
}
