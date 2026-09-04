using FishMMO.Shared;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs for the rule that releases a pinned target. The pin is a promise the target frame
	/// makes — "this card stays up until you let go" — so the truth table of what may break that
	/// promise is written down here: destruction, despawn, death and distance, and nothing else.
	/// </summary>
	[TestFixture]
	public class PinnedTargetRulesTests
	{
		private const float Release = PinnedTargetRules.RELEASE_DISTANCE;

		[Test]
		public void LiveSpawnedTargetInRange_Holds()
		{
			Assert.IsFalse(PinnedTargetRules.ShouldRelease(
				isDestroyed: false, isSpawned: true, isAlive: true,
				sqrDistance: 10.0f * 10.0f, releaseDistance: Release));
		}

		[Test]
		public void DestroyedTarget_Releases()
		{
			Assert.IsTrue(PinnedTargetRules.ShouldRelease(
				isDestroyed: true, isSpawned: true, isAlive: true,
				sqrDistance: 0.0f, releaseDistance: Release));
		}

		[Test]
		public void DespawnedTarget_Releases()
		{
			Assert.IsTrue(PinnedTargetRules.ShouldRelease(
				isDestroyed: false, isSpawned: false, isAlive: true,
				sqrDistance: 0.0f, releaseDistance: Release));
		}

		[Test]
		public void DeadTarget_Releases()
		{
			Assert.IsTrue(PinnedTargetRules.ShouldRelease(
				isDestroyed: false, isSpawned: true, isAlive: false,
				sqrDistance: 0.0f, releaseDistance: Release));
		}

		[Test]
		public void TargetBeyondReleaseDistance_Releases()
		{
			float beyond = Release + 1.0f;
			Assert.IsTrue(PinnedTargetRules.ShouldRelease(
				isDestroyed: false, isSpawned: true, isAlive: true,
				sqrDistance: beyond * beyond, releaseDistance: Release));
		}

		[Test]
		public void TargetExactlyAtReleaseDistance_Holds()
		{
			// The boundary is inclusive: a target sitting on the line is still followed.
			Assert.IsFalse(PinnedTargetRules.ShouldRelease(
				isDestroyed: false, isSpawned: true, isAlive: true,
				sqrDistance: Release * Release, releaseDistance: Release));
		}

		[Test]
		public void ReleaseDistanceIsWiderThanAcquisition()
		{
			/* The hysteresis the rule depends on. A target pinned at the edge of the hover
			 * acquisition range and drifting one step further away must keep its card. */
			Assert.Greater(PinnedTargetRules.RELEASE_DISTANCE, TargetController.MAX_TARGET_DISTANCE);
		}

		[TestCase(0.0f)]
		[TestCase(-1.0f)]
		[TestCase(float.NaN)]
		public void NoDistanceLimit_HoldsAtAnyRange(float limit)
		{
			Assert.IsFalse(PinnedTargetRules.ShouldRelease(
				isDestroyed: false, isSpawned: true, isAlive: true,
				sqrDistance: float.MaxValue, releaseDistance: limit));
		}
	}
}
