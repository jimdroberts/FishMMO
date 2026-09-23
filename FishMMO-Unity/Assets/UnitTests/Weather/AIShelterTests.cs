using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Weather;
using FishMMO.Server.Implementation.World.SceneServer.AI;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// Whether an NPC goes and stands out of the weather, and which cover it picks (Q15).
	/// </summary>
	/// <remarks>
	/// The decision is pure — a weather sample and an archetype's settings in, a yes or no out — so
	/// it answers the same way on a tick, in a test, and in whatever tool eventually shows a
	/// designer what an archetype will do.
	/// </remarks>
	[TestFixture]
	public class AIShelterTests
	{
		private readonly List<Object> created = new List<Object>();
		private Scene scene;

		[SetUp]
		public void SetUp()
		{
			WeatherVolumeRegistry.Clear();
			scene = SceneManager.GetActiveScene();
		}

		[TearDown]
		public void TearDown()
		{
			foreach (Object o in created)
			{
				Object.DestroyImmediate(o);
			}
			created.Clear();
			WeatherVolumeRegistry.Clear();
		}

		private static AIShelterSettings Sheltering()
		{
			return new AIShelterSettings
			{
				Enabled = true,
				ShelterFrom = WeatherKindMask.Precipitation,
				MinimumExposure = 0.5f,
				MinimumSeverity = 0.35f,
				SearchRadius = 45f,
				MinimumShelterStrength = 0.5f,
			};
		}

		private static WeatherSample Weather(float precipitation, float shelter = 0f, float wind = 0f, float lightning = 0f)
		{
			var frame = new WeatherFrame();
			frame[WeatherChannel.Precipitation] = precipitation;
			frame[WeatherChannel.RainWeight] = precipitation > 0f ? 1f : 0f;
			frame[WeatherChannel.WindSpeed] = wind;
			frame[WeatherChannel.LightningRate] = lightning;
			return new WeatherSample { Frame = frame, Shelter = shelter };
		}

		private WeatherVolume Shelter(string name, Vector3 centre, float strength = 1f, float halfExtent = 4f)
		{
			var host = new GameObject(name);
			created.Add(host);
			host.transform.position = centre;
			BoxCollider box = host.AddComponent<BoxCollider>();
			box.size = Vector3.one * (halfExtent * 2f);
			box.isTrigger = true;

			WeatherVolume volume = host.AddComponent<WeatherVolume>();
			volume.Kind = WeatherVolumeKind.Shelter;
			volume.ShelterStrength = strength;
			volume.Shape = box;
			// Edit mode does not run OnEnable on a component that is not [ExecuteAlways], so the
			// registration the scene would do at load is done here by hand.
			WeatherVolumeRegistry.Add(host.scene.handle, volume);
			return volume;
		}

		[Test]
		public void AnArchetypeThatSaysNothingAboutWeatherNeverShelters()
		{
			/* The default, and the one that matters most (Q15). Every archetype already in the
			 * project gets this struct at its defaults, and weather covers whole regions — so a
			 * default of "on" would have had every NPC on the continent leave its post the first
			 * time a front came through. Patrols off their routes, merchants away from their stalls,
			 * quest targets nowhere to be found, all from a field nobody had set. */
			var untouched = new AIShelterSettings();

			Assert.That(untouched.Enabled, Is.False, "off unless an archetype asks for it");
			Assert.That(untouched.WantsShelter(Weather(1f)), Is.False, "and a downpour does not change that");
			Assert.That(untouched.WouldStay(Weather(1f)), Is.False);
		}

		[Test]
		public void AnNpcInTheOpenInHeavyRainGoesLookingForCover()
		{
			AIShelterSettings settings = Sheltering();

			Assert.That(settings.WantsShelter(Weather(0f)), Is.False, "a clear day");
			Assert.That(settings.WantsShelter(Weather(0.2f)), Is.False, "a drizzle is not worth the walk");
			Assert.That(settings.WantsShelter(Weather(0.9f)), Is.True, "a downpour is");
		}

		[Test]
		public void AnNpcAlreadyUnderSomethingHasNothingToWalkAwayFrom()
		{
			AIShelterSettings settings = Sheltering();   // MinimumExposure 0.5

			Assert.That(settings.WantsShelter(Weather(1f, shelter: 0f)), Is.True, "standing in the open");
			Assert.That(settings.WantsShelter(Weather(1f, shelter: 0.6f)), Is.False, "mostly under cover already");
			Assert.That(settings.WantsShelter(Weather(1f, shelter: 1f)), Is.False, "and indoors, certainly not");
		}

		[Test]
		public void SeverityIsReadFromTheKindsTheArchetypeActuallyCaresAbout()
		{
			/* An archetype that shelters from lightning and nothing else must not be driven indoors
			 * by heavy rain it is perfectly happy in — and would be, if this measured precipitation
			 * regardless of what was chosen. */
			AIShelterSettings stormOnly = Sheltering();
			stormOnly.ShelterFrom = WeatherKindMask.Lightning;

			Assert.That(stormOnly.WantsShelter(Weather(1f)), Is.False, "torrential rain, no lightning: unbothered");
			Assert.That(stormOnly.WantsShelter(Weather(0f, lightning: 0.8f)), Is.True, "a dry thunderstorm sends it in");

			// And one that cares about wind reads the wind.
			AIShelterSettings windOnly = Sheltering();
			windOnly.ShelterFrom = WeatherKindMask.Wind;
			Assert.That(windOnly.WantsShelter(Weather(0f, wind: 0.9f)), Is.True);
			Assert.That(windOnly.WantsShelter(Weather(1f)), Is.False, "rain is not wind");
		}

		[Test]
		public void ItTakesMoreToGoInThanToStayPut()
		{
			/* Two thresholds, for the same reason as everywhere else in exposure: with one, weather
			 * hovering on it would send the NPC out of the doorway and back in again for as long as
			 * it held there — a creature visibly pacing in and out of a barn. */
			AIShelterSettings settings = Sheltering();   // MinimumSeverity 0.35, stay at 0.6x = 0.21

			WeatherSample between = Weather(0.28f);
			Assert.That(settings.WantsShelter(between), Is.False, "not enough to come in for");
			Assert.That(settings.WouldStay(between), Is.True, "but enough to stay for, once in");

			WeatherSample clearing = Weather(0.1f);
			Assert.That(settings.WouldStay(clearing), Is.False, "and when it really eases off, back out");
		}

		[Test]
		public void TheNearestRealShelterIsTheOneItPicks()
		{
			Shelter("Far barn", new Vector3(30f, 0f, 0f));
			WeatherVolume near = Shelter("Near barn", new Vector3(8f, 0f, 0f));

			WeatherVolume chosen = WeatherVolumeRegistry.NearestShelter(scene, Vector3.zero, 45f, 0.5f);
			Assert.That(chosen, Is.SameAs(near));
		}

		[Test]
		public void AThinCanopyIsNotARoofAndAFarOneIsNotWorthTheWalk()
		{
			// A weak volume nearby and a real one further off: the real one wins, because crossing a
			// field to stand under a thin canopy is worse than getting rained on.
			Shelter("Canopy", new Vector3(3f, 0f, 0f), strength: 0.2f);
			WeatherVolume barn = Shelter("Barn", new Vector3(25f, 0f, 0f), strength: 1f);

			Assert.That(WeatherVolumeRegistry.NearestShelter(scene, Vector3.zero, 45f, 0.5f), Is.SameAs(barn));

			// And nothing at all when the only real one is out of range.
			Assert.That(WeatherVolumeRegistry.NearestShelter(scene, Vector3.zero, 10f, 0.5f), Is.Null,
				"out of reach is the same as not there");

			// Lower the bar and the canopy becomes an option again.
			Assert.That(WeatherVolumeRegistry.NearestShelter(scene, Vector3.zero, 10f, 0.1f), Is.Not.Null);
		}

		[Test]
		public void AVolumeThatIsNotShelterIsNeverMistakenForIt()
		{
			// An Override volume — a crater that is always ashfall — is emphatically not somewhere to
			// go and stand in a storm.
			var host = new GameObject("Crater");
			created.Add(host);
			BoxCollider box = host.AddComponent<BoxCollider>();
			box.size = Vector3.one * 8f;
			WeatherVolume crater = host.AddComponent<WeatherVolume>();
			crater.Kind = WeatherVolumeKind.Override;
			crater.ShelterStrength = 1f;     // set, and must still be ignored
			crater.Shape = box;
			WeatherVolumeRegistry.Add(host.scene.handle, crater);

			Assert.That(WeatherVolumeRegistry.NearestShelter(scene, Vector3.zero, 45f, 0.5f), Is.Null);
		}

		[Test]
		public void ShelteringFromNothingIsTheSameAsNotShelteringAtAll()
		{
			AIShelterSettings settings = Sheltering();
			settings.ShelterFrom = WeatherKindMask.None;

			Assert.That(settings.WantsShelter(Weather(1f)), Is.False, "enabled, but with nothing to shelter from");
			Assert.That(settings.WouldStay(Weather(1f)), Is.False);
		}
	}
}
