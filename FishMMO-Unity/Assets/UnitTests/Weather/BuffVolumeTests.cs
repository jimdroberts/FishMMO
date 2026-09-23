using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared;
using FishMMO.Shared.Weather;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// Which region buffs a character standing at a point should have (N13).
	/// </summary>
	/// <remarks>
	/// <para>
	/// The verdict is a pure function of a position, and that is the whole design. A trigger would
	/// have been the obvious way to build region buffs and is the wrong one: trigger callbacks are
	/// raised outside the replicate so they cannot be predicted, are not rolled back so a reconcile
	/// leaves them stale, and re-fire Enter and Exit on every replayed tick — one reconcile of
	/// thirty ticks would apply and remove the same buff thirty times.
	/// </para>
	/// <para>
	/// So these tests ask the question the controller asks, the way it asks it: from a position and
	/// a weather sample, with no history of any kind. Anything that can be answered that way can be
	/// predicted, replayed and reconciled for free.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class BuffVolumeTests
	{
		private readonly List<Object> created = new List<Object>();
		private Scene scene;

		[SetUp]
		public void SetUp()
		{
			BuffVolumeRegistry.Clear();
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
			BuffVolumeRegistry.Clear();
		}

		private BaseBuffTemplate Buff(string name)
		{
			var buff = ScriptableObject.CreateInstance<StateBuffTemplate>();
			buff.name = name;
			buff.AddToCache(name);
			created.Add(buff);
			return buff;
		}

		/// <summary>A box volume centred on the origin, half-extent 5, granting that buff.</summary>
		private BuffVolume Volume(string name, BaseBuffTemplate buff, Vector3 centre = default, float halfExtent = 5f)
		{
			var host = new GameObject(name);
			created.Add(host);
			host.transform.position = centre;
			BoxCollider box = host.AddComponent<BoxCollider>();
			box.size = Vector3.one * (halfExtent * 2f);
			box.isTrigger = true;

			BuffVolume volume = host.AddComponent<BuffVolume>();
			volume.Buff = buff;
			/* Registered by hand, because edit mode does not run OnEnable on a component that is not
			 * [ExecuteAlways] — and BuffVolume deliberately is not one, since a volume has no
			 * business registering itself while somebody is only editing the scene. Register() is
			 * the whole body of OnEnable, so everything but that one call is still under test. */
			volume.Register();
			return volume;
		}

		private static WeatherSample Open()
		{
			// In the open, nothing falling.
			return new WeatherSample { Frame = WeatherFrame.Clear, Shelter = 0f };
		}

		private static WeatherSample Raining(float amount = 1f, float shelter = 0f)
		{
			var frame = new WeatherFrame();
			frame[WeatherChannel.Precipitation] = amount;
			frame[WeatherChannel.RainWeight] = 1f;
			return new WeatherSample { Frame = frame, Shelter = shelter };
		}

		private Dictionary<int, BuffVolume> At(Vector3 position, WeatherSample weather)
		{
			var into = new Dictionary<int, BuffVolume>();
			BuffVolumeRegistry.Applicable(scene, position, weather, into);
			return into;
		}

		[Test]
		public void AVolumeBuffsWhoeverIsStandingInItAndNobodyElse()
		{
			BaseBuffTemplate blessing = Buff("Blessing");
			BuffVolume shrine = Volume("Shrine", blessing);

			Assert.That(At(Vector3.zero, Open()).ContainsKey(blessing.ID), Is.True, "standing in it");
			Assert.That(At(new Vector3(4.9f, 0f, 0f), Open()).ContainsKey(blessing.ID), Is.True, "just inside the edge");
			Assert.That(At(new Vector3(5.5f, 0f, 0f), Open()), Is.Empty, "a step outside is nothing");
			Assert.That(At(new Vector3(0f, 200f, 0f), Open()), Is.Empty, "and height counts as much as distance");

			Assert.That(shrine.Contains(Vector3.zero), Is.True);
		}

		[Test]
		public void TheSamePositionAlwaysGivesTheSameAnswer()
		{
			/* The property the whole design rests on, stated plainly. Ask five hundred times, in any
			 * order, in and out and back in — the answer depends on the position and nothing else.
			 * That is what a replayed tick needs, and exactly what a trigger's Enter/Exit pair,
			 * which depends on where you were last tick, could not have given. */
			BaseBuffTemplate blessing = Buff("Repeatable");
			Volume("Shrine", blessing);

			var inside = new Vector3(1f, 0f, 1f);
			var outside = new Vector3(40f, 0f, 0f);

			for (int i = 0; i < 500; i++)
			{
				// Walk in and out repeatedly; each answer must depend only on where we are now.
				Vector3 here = (i % 3 == 0) ? outside : inside;
				bool expected = here == inside;
				Assert.That(At(here, Open()).ContainsKey(blessing.ID), Is.EqualTo(expected),
					$"step {i}: the verdict must come from the position and nothing else");
			}
		}

		[Test]
		public void TwoVolumesGrantingTheSameBuffCollapseToOne()
		{
			// Overlapping regions must not apply the same buff twice, and walking out of one while
			// still inside the other must keep it on.
			BaseBuffTemplate warmth = Buff("Warmth");
			Volume("FireA", warmth, new Vector3(-2f, 0f, 0f));
			Volume("FireB", warmth, new Vector3(2f, 0f, 0f));

			Dictionary<int, BuffVolume> both = At(Vector3.zero, Open());
			Assert.That(both.Count, Is.EqualTo(1), "one buff, not two");

			// Inside B only: still on.
			Assert.That(At(new Vector3(6f, 0f, 0f), Open()).ContainsKey(warmth.ID), Is.True);
			// Outside both: off.
			Assert.That(At(new Vector3(20f, 0f, 0f), Open()), Is.Empty);
		}

		[Test]
		public void TwoVolumesWithDifferentBuffsBothApply()
		{
			BaseBuffTemplate warmth = Buff("Warmth2");
			BaseBuffTemplate blessing = Buff("Blessing2");
			Volume("Fire", warmth);
			Volume("Shrine", blessing);

			Dictionary<int, BuffVolume> here = At(Vector3.zero, Open());
			Assert.That(here.Count, Is.EqualTo(2));
			Assert.That(here.ContainsKey(warmth.ID), Is.True);
			Assert.That(here.ContainsKey(blessing.ID), Is.True);
		}

		[Test]
		public void AWeatherGatedVolumeOnlyBuffsWhileTheWeatherIsDoingIt()
		{
			// A storm shelter that is only worth anything while it is actually raining.
			BaseBuffTemplate respite = Buff("Respite");
			BuffVolume shelter = Volume("Shelter", respite);
			shelter.RequiredWeather = WeatherKindMask.Rain;

			Assert.That(At(Vector3.zero, Open()), Is.Empty, "clear skies, nothing to shelter from");
			Assert.That(At(Vector3.zero, Raining()).ContainsKey(respite.ID), Is.True, "in the rain it means something");
			Assert.That(shelter.NeedsWeather, Is.True, "and it says so, so the controller knows to sample");
		}

		[Test]
		public void AnExposureGatedVolumeNeedsTheSkyAboveIt()
		{
			// A sun-worship circle: standing in it under a roof is not standing in the sun.
			BaseBuffTemplate sunlight = Buff("Sunlight");
			BuffVolume circle = Volume("Circle", sunlight);
			circle.MinimumExposure = 0.75f;

			Assert.That(At(Vector3.zero, Open()).ContainsKey(sunlight.ID), Is.True, "in the open");
			Assert.That(At(Vector3.zero, Raining(1f, shelter: 0.5f)), Is.Empty, "half under cover is not enough");
			Assert.That(At(Vector3.zero, Raining(1f, shelter: 1f)), Is.Empty, "and under a roof, certainly not");
			Assert.That(circle.NeedsWeather, Is.True);
		}

		[Test]
		public void AnUngatedVolumeNeverNeedsTheWeatherSampled()
		{
			/* What keeps this cheap. Sampling the weather is the expensive part of the tick, and a
			 * plain box with no gate on it does not need it at all — so a scene full of ordinary
			 * volumes costs nothing but the collider tests. */
			BaseBuffTemplate plain = Buff("Plain");
			BuffVolume volume = Volume("Plain", plain);

			Assert.That(volume.NeedsWeather, Is.False);
			Assert.That(BuffVolumeRegistry.AnyNeedsWeather(scene), Is.False);

			// One gated volume anywhere in the scene, and the sample is needed again.
			BuffVolume gated = Volume("Gated", Buff("Gated"));
			gated.RequiredWeather = WeatherKindMask.Fog;
			Assert.That(BuffVolumeRegistry.AnyNeedsWeather(scene), Is.True);
		}

		[Test]
		public void AVolumeWithNoBuffOrNoShapeIsInertRatherThanBroken()
		{
			// Half-authored content should do nothing, not throw in the middle of a replicate.
			BuffVolume noBuff = Volume("NoBuff", null);
			Assert.That(noBuff.AppliesAt(Vector3.zero, Open()), Is.False);

			BuffVolume noShape = Volume("NoShape", Buff("Orphan"));
			noShape.Shape = null;
			Assert.That(noShape.AppliesAt(Vector3.zero, Open()), Is.False);

			Assert.That(At(Vector3.zero, Open()), Is.Empty, "and neither reaches the result");
		}

		[Test]
		public void AVolumeThatLeavesTheRegistryTakesItsBuffWithIt()
		{
			// What OnDisable does, and what unloading the scene it belongs to does.
			BaseBuffTemplate blessing = Buff("Leaving");
			BuffVolume shrine = Volume("Shrine", blessing);
			Assert.That(At(Vector3.zero, Open()).ContainsKey(blessing.ID), Is.True);

			shrine.Unregister();
			Assert.That(At(Vector3.zero, Open()), Is.Empty, "an unregistered volume is not in the scene's list");

			shrine.Register();
			Assert.That(At(Vector3.zero, Open()).ContainsKey(blessing.ID), Is.True, "and comes back when it returns");

			// Unregistering twice is not an error; a scene teardown can easily do it.
			shrine.Unregister();
			shrine.Unregister();
			Assert.That(At(Vector3.zero, Open()), Is.Empty);
		}

		[Test]
		public void TheResultBufferIsClearedSoAStaleVolumeCannotSurviveIntoTheNextTick()
		{
			/* The controller reuses one dictionary every tick to avoid allocating. That is only safe
			 * because the pass clears it first — otherwise a buff would be applied once and then
			 * never let go, since it would still be in the "wanted" set long after the character
			 * walked out. */
			BaseBuffTemplate blessing = Buff("Reused");
			Volume("Shrine", blessing);

			var reused = new Dictionary<int, BuffVolume>();
			BuffVolumeRegistry.Applicable(scene, Vector3.zero, Open(), reused);
			Assert.That(reused.Count, Is.EqualTo(1));

			BuffVolumeRegistry.Applicable(scene, new Vector3(50f, 0f, 0f), Open(), reused);
			Assert.That(reused, Is.Empty, "the same buffer, refilled from scratch");
		}
	}
}
