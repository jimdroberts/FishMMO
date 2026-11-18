using System;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins that forking an ability object actually changes where it goes, and that the cone the new
	/// heading is drawn from is a cone.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Neither property held before. <c>AbilityForkHitAction</c> assigned <c>Transform.rotation</c>,
	/// but an ability object's position is a closed form over its spawn pose and integer tick count
	/// (<see cref="AbilityMoveTransformAction"/>), so the next tick recomputed the position from the
	/// ORIGINAL spawn line and discarded the turn — fork was a no-op that only span the mesh (and the
	/// direction <c>KnockbackHitAction</c> reads off it). Underneath, the cone helper fed a world
	/// POSITION into <c>Quaternion.Euler</c> as degrees, so the "spread" was a function of the
	/// object's map coordinates.
	/// </para>
	/// <para>
	/// The trajectory assertions go through <see cref="AbilityObject.Redirect"/> and the closed form
	/// directly rather than through the action, so they need no ability template, caster or
	/// TimeManager — the properties under test are geometric.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class AbilityForkTests
	{
		/// <summary>The closed form, exactly as <see cref="AbilityMoveTransformAction"/> evaluates it.</summary>
		private static Vector3 ClosedFormPosition(AbilityObject o, Vector3 moveDirection, float speed, float elapsedSeconds)
			=> o.SpawnPosition + (o.SpawnRotation * moveDirection * (speed * elapsedSeconds));

		/// <summary>
		/// A redirect must move the closed form's own inputs, not just the transform.
		/// </summary>
		[Test]
		public void Redirect_ChangesTheClosedFormTrajectory_NotJustTheTransform()
		{
			GameObject go = new GameObject("ForkObject");
			try
			{
				AbilityObject o = go.AddComponent<AbilityObject>();
				o.SpawnPosition = Vector3.zero;
				o.SpawnRotation = Quaternion.LookRotation(Vector3.forward);
				o.ElapsedTicks = 10;
				go.transform.position = new Vector3(0f, 0f, 10f);

				Quaternion turned = Quaternion.LookRotation(Vector3.right);
				o.Redirect(turned);

				LogAssert.AreEqual(0u, o.ElapsedTicks,
					"The new leg starts now. Leaving the tick count alone would have the closed form " +
					"project the whole of the previous leg's travel along the NEW heading in one step.");
				LogAssert.IsTrue(o.SpawnPosition == new Vector3(0f, 0f, 10f),
					"The leg must start where the object actually is, not back at the original spawn.");
				LogAssert.IsTrue(o.SpawnRotation == turned,
					"SpawnRotation is what the closed form reads; writing only Transform.rotation is " +
					"exactly the no-op this fixture exists to prevent.");

				// One second of travel at 5 m/s must now go along +X from the turn, not along +Z.
				Vector3 after = ClosedFormPosition(o, Vector3.forward, speed: 5f, elapsedSeconds: 1f);
				LogAssert.IsTrue(after.x > 4.9f && after.x < 5.1f,
					$"The object must travel along the new heading; it went to {after}.");
				LogAssert.IsTrue(Mathf.Abs(after.z - 10f) < 0.001f,
					$"And must not keep advancing along the old one; it went to {after}.");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		/// <summary>
		/// The heading must depend on the object's direction, never on where it is in the world.
		/// </summary>
		/// <remarks>
		/// The previous helper computed <c>Quaternion.Euler(startPosition - ...)</c>, so the same
		/// ability fired along the same heading produced unrelated spreads at different map positions.
		/// Two generators seeded identically are the control.
		/// </remarks>
		[Test]
		public void ConicalDirection_DependsOnHeadingOnly_NotOnWorldPosition()
		{
			Quaternion a = Vector3.forward.GetRandomConicalDirection(60f, new DeterministicRNG(1234));
			Quaternion b = Vector3.forward.GetRandomConicalDirection(60f, new DeterministicRNG(1234));

			LogAssert.IsTrue(Quaternion.Angle(a, b) < 0.001f,
				"Same heading, same generator state, same answer — that is what lets the server and " +
				"the caster's owner fork the same way for the same hit.");
		}

		/// <summary>Every sampled direction must lie inside the authored arc.</summary>
		[Test]
		public void ConicalDirection_StaysWithinTheAuthoredArc()
		{
			const float arcDegrees = 40f;
			DeterministicRNG rng = new DeterministicRNG(9876);
			Vector3 axis = new Vector3(0.3f, 0.1f, 1f).normalized;

			float widest = 0f;
			for (int i = 0; i < 512; ++i)
			{
				Vector3 sampled = axis.GetRandomConicalDirection(arcDegrees, rng) * Vector3.forward;
				LogAssert.IsTrue(Mathf.Abs(sampled.magnitude - 1f) < 0.01f,
					$"A heading must be a unit direction; got magnitude {sampled.magnitude}.");
				widest = Mathf.Max(widest, Vector3.Angle(axis, sampled));
			}

			LogAssert.IsTrue(widest <= (arcDegrees * 0.5f) + 0.5f,
				$"A {arcDegrees} degree arc is {arcDegrees * 0.5f} degrees either side of the heading; " +
				$"the widest sample was {widest}.");
			LogAssert.IsTrue(widest > arcDegrees * 0.2f,
				$"The arc must actually be used rather than collapsing onto the axis; widest was {widest}.");
		}

		/// <summary>A zero arc leaves the heading alone.</summary>
		[Test]
		public void ConicalDirection_ZeroArc_KeepsTheHeading()
		{
			Vector3 axis = new Vector3(1f, 0f, 1f).normalized;
			Vector3 result = axis.GetRandomConicalDirection(0f, new DeterministicRNG(5)) * Vector3.forward;

			LogAssert.IsTrue(Vector3.Angle(axis, result) < 0.01f,
				"An unconfigured arc must not deflect the projectile at all.");
		}

		/// <summary>
		/// The unit-sphere sampler must return unit vectors.
		/// </summary>
		/// <remarks>
		/// It used <c>Unsafe.As&lt;double, float&gt;</c> and called it a fast conversion, but that
		/// REINTERPRETS the double's first four bytes rather than converting its value: 0.5 came back
		/// as 0f and 0.9999 as 476472.94f. Every vector it produced was garbage.
		/// </remarks>
		[Test]
		public void RandomOnUnitSphere_ReturnsUnitVectors()
		{
			DeterministicRNG rng = new DeterministicRNG(4242);
			for (int i = 0; i < 256; ++i)
			{
				Vector3 v = Vector3Extensions.RandomOnUnitSphere(rng);
				LogAssert.IsTrue(Mathf.Abs(v.magnitude - 1f) < 0.001f,
					$"Sample {i} had magnitude {v.magnitude}; a point on the unit sphere has magnitude 1.");
			}
		}
	}
}
