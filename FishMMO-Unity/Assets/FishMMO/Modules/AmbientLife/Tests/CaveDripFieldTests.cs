using FishMMO.Client;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The client's numbers for the cave drips' shader (<see cref="CaveDripField.Fill"/>) agree with the drop's beat
	/// (<see cref="SceneDrip"/>). Split from CaveDripsTests (the World Design module's), Phase 4, 2026-11-18.
	/// </summary>
	[TestFixture]
	public class CaveDripFieldTests
	{
		[Test]
		public void TheShadersNumbers_AreTheBeatsOwn()
		{
			uint seed = SceneDrip.Seed(0x321u, 3.1f, 0.55f, SceneDripLanding.Puddle);
			var e = new SceneEmitter { Position = new Vector3(4f, -20f, 9f), Kind = SceneEmitterKind.Drip, Seed = seed, Reach = 7.5f };
			double t = 1.9e9 + 41.25;
			CaveDripField.Fill(e, t, 0.8f, out Vector4 source, out Vector4 shape, out Vector4 time, out Vector4 extra);
			Assert.That((Vector3)source, Is.EqualTo(e.Position));
			Assert.That(source.w, Is.EqualTo(7.5f));
			Assert.That(shape.x, Is.EqualTo(0.55f).Within(1e-4f));
			Assert.That(shape.y, Is.EqualTo(SceneDrip.FallSeconds(7.5f)).Within(1e-5f));
			Assert.That(shape.z, Is.EqualTo(0.8f));
			Assert.That(shape.w, Is.EqualTo(SceneDrip.RingLifeOf(seed)).Within(1e-5f));
			SceneDrip.ClockAt(t, seed, out long cycle, out float into);
			Assert.That(time.x, Is.LessThanOrEqualTo(0f), "drop −1 has landed");
			Assert.That(time.x, Is.LessThan(time.y));
			Assert.That(time.y, Is.LessThan(time.z));
			Assert.That(time.z, Is.LessThan(time.w));
			Assert.That(time.z, Is.EqualTo(SceneDrip.SecondsToLanding(seed, cycle, into, 1)).Within(1e-6f));
			Assert.That(extra.x, Is.EqualTo(cycle % 4096));
			Assert.That(extra.y, Is.EqualTo((float)SceneDripLanding.Puddle));
		}
	}
}
