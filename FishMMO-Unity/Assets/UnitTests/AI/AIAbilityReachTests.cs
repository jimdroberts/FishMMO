using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Pins the reach the AI plans with for abilities whose object does not travel.
	/// </summary>
	/// <remarks>
	/// Issue #220: <c>Ability.Range</c> is Speed × LifeTime, so Punch and Lesser Fireball (Speed 0)
	/// reported a range of zero and the planner never reached the Attack intent. Orcs walked up to
	/// their targets and stood there; the mage held 22 m and never cast.
	/// </remarks>
	[TestFixture]
	public class AIAbilityReachTests
	{
		[Test]
		public void StationaryForwardAbility_ReachesPastTheCasterAndItsOwnObject()
		{
			// Punch: 2 m box at half scale = 0.5 m half-extent, orc radius 0.5.
			float reach = AIAbilityReach.ResolveFromExtents(AbilitySpawnTarget.Forward, 0.5f, 0.5f);

			Assert.That(reach, Is.EqualTo(0.5f + 1.0f + AIAbilityReach.REACH_SLACK).Within(1e-5f));
			Assert.That(reach, Is.GreaterThan(0f), "a zero reach is unplannable");
		}

		[Test]
		public void StationaryAbility_NeverReportsLessThanTheMinimum()
		{
			float reach = AIAbilityReach.ResolveFromExtents(AbilitySpawnTarget.Self, 0f, 0f);
			Assert.That(reach, Is.EqualTo(AIAbilityReach.MIN_REACH));
		}

		[Test]
		public void TargetedStationaryAbility_UsesTheCastRangeDefault()
		{
			float reach = AIAbilityReach.ResolveFromExtents(AbilitySpawnTarget.Target, 0.5f, 0.25f);
			Assert.That(reach, Is.EqualTo(AIAbilityReach.DEFAULT_TARGETED_REACH));
		}

		[Test]
		public void PrefabHalfExtent_ReadsTheShape_NotTheBounds()
		{
			// A collider on an object that has never been simulated has empty bounds; the shape
			// still knows its size. Scale must be honoured, as the ability prefabs are half-scale.
			GameObject go = new GameObject("reach-probe");
			try
			{
				go.transform.localScale = Vector3.one * 0.5f;
				BoxCollider box = go.AddComponent<BoxCollider>();
				box.size = new Vector3(2f, 2f, 2f);

				Assert.That(AIAbilityReach.ResolvePrefabHalfExtent(box), Is.EqualTo(0.5f).Within(1e-5f));

				SphereCollider sphere = go.AddComponent<SphereCollider>();
				sphere.radius = 1f;
				Assert.That(AIAbilityReach.ResolvePrefabHalfExtent(sphere), Is.EqualTo(0.5f).Within(1e-5f));
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void NullCollider_ContributesNothing()
		{
			Assert.That(AIAbilityReach.ResolvePrefabHalfExtent(null), Is.EqualTo(0f));
		}
	}
}
