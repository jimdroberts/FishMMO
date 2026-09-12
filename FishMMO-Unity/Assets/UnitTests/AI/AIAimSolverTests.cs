using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Pins the aim arithmetic behind issue #274, including the regression that made every NPC miss.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The reported bug was not a tuning problem: the aim <em>direction</em> was solved from the NPC's
	/// feet while the projectile left its eye, and only the direction is replicated, so the shot was
	/// displaced upward by the eye height at every range — over a player's head, permanently. The two
	/// tests at the end of this fixture state that identity directly, and are the ones that must fail
	/// if anyone ever solves an aim from <c>transform.position</c> again.
	/// </para>
	/// <para>
	/// EditMode only. The collider cases build real colliders because <see cref="Collider.bounds"/> is
	/// the whole point — the aim point used to be cached against the target's identity and latched at
	/// zero when the collider was not yet resolvable, so a fixture that stubbed the bounds out would
	/// not be testing the thing that broke.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class AIAimSolverTests
	{
		/// <summary>A 1.8 m capsule root, at the height a character prefab stands.</summary>
		private const float CapsuleRootHeight = 0.9f;

		/// <summary>Everything created by a test, torn down afterwards.</summary>
		private GameObject root;

		[SetUp]
		public void SetUp()
		{
			root = new GameObject("AIAimSolverTests");
		}

		[TearDown]
		public void TearDown()
		{
			if (root != null)
			{
				Object.DestroyImmediate(root);
			}
		}

		/// <summary>
		/// Builds a child capsule collider matching a standing character: 1.8 m tall, 0.4 m radius,
		/// its feet on the ground plane.
		/// </summary>
		/// <param name="offset">Where to place the collider's root.</param>
		/// <returns>The collider.</returns>
		private CapsuleCollider CreateCharacterCollider(Vector3 offset)
		{
			GameObject go = new GameObject("Target");
			go.transform.SetParent(root.transform, false);
			go.transform.position = offset;

			CapsuleCollider capsule = go.AddComponent<CapsuleCollider>();
			capsule.height = 1.8f;
			capsule.radius = 0.4f;

			/* Collider.bounds is served from the transform state the physics engine has cached, not
			 * from the Transform directly. No FixedUpdate runs in an EditMode test, so without this
			 * the bounds are those of a collider that has never been moved. */
			Physics.SyncTransforms();
			return capsule;
		}

		// --- The aim point ----------------------------------------------------------------------

		[Test]
		public void Center_IsTheColliderCentre()
		{
			CapsuleCollider capsule = CreateCharacterCollider(new Vector3(10f, CapsuleRootHeight, 0f));

			Vector3 point = AIAimSolver.ResolveAimPoint(capsule.transform.position, capsule, AIAimPoint.Center);

			Assert.That(point.y, Is.EqualTo(CapsuleRootHeight).Within(1e-4f),
				"the body centre of a 1.8 m capsule rooted at 0.9 sits at 0.9");
		}

		[Test]
		public void Head_SitsJustBelowTheTopOfTheCollider()
		{
			CapsuleCollider capsule = CreateCharacterCollider(new Vector3(10f, CapsuleRootHeight, 0f));

			Vector3 point = AIAimSolver.ResolveAimPoint(capsule.transform.position, capsule, AIAimPoint.Head);

			Assert.That(point.y, Is.EqualTo(1.65f).Within(1e-3f), "1.8 m top less the 0.15 m eye inset");
			Assert.That(point.y, Is.LessThan(capsule.bounds.max.y), "the head is inside the collider");
		}

		[Test]
		public void Feet_IsTheBottomOfTheCollider()
		{
			CapsuleCollider capsule = CreateCharacterCollider(new Vector3(10f, CapsuleRootHeight, 0f));

			Vector3 point = AIAimSolver.ResolveAimPoint(capsule.transform.position, capsule, AIAimPoint.Feet);

			Assert.That(point.y, Is.EqualTo(0f).Within(1e-3f));
		}

		[Test]
		public void Head_OnAColliderShorterThanTheInset_DoesNotFallBelowItsOwnFeet()
		{
			GameObject go = new GameObject("Critter");
			go.transform.SetParent(root.transform, false);
			go.transform.position = Vector3.zero;
			SphereCollider sphere = go.AddComponent<SphereCollider>();
			sphere.radius = 0.05f;
			Physics.SyncTransforms();

			Vector3 point = AIAimSolver.ResolveAimPoint(go.transform.position, sphere, AIAimPoint.Head);

			Assert.That(point.y, Is.GreaterThanOrEqualTo(sphere.bounds.min.y),
				"a prone or tiny target must not be aimed at below its own collider");
		}

		[Test]
		public void NoCollider_FallsBackPerAimPoint()
		{
			Vector3 feet = new Vector3(3f, 0f, 7f);

			Assert.That(AIAimSolver.ResolveAimPoint(feet, null, AIAimPoint.Feet).y, Is.EqualTo(0f).Within(1e-5f));
			Assert.That(AIAimSolver.ResolveAimPoint(feet, null, AIAimPoint.Center).y, Is.EqualTo(0.9f).Within(1e-5f));
			Assert.That(AIAimSolver.ResolveAimPoint(feet, null, AIAimPoint.Head).y, Is.EqualTo(1.45f).Within(1e-5f));
		}

		[Test]
		public void NoCollider_HeadFallback_AgreesWithTheAimOriginFallback()
		{
			/* Both constants are private and both must be 1.45 m — an NPC aiming at another NPC's head
			 * and an NPC aiming from its own eye are the same height on the same rig. Asserting the two
			 * functions agree pins the equality without widening either constant's visibility. */
			GameObject rig = new GameObject("Rig");
			rig.transform.SetParent(root.transform, false);
			rig.transform.position = new Vector3(-4f, 1.2f, 2f);

			Vector3 head = AIAimSolver.ResolveAimPoint(rig.transform.position, null, AIAimPoint.Head);
			Vector3 eye = CharacterAimOrigin.Resolve(null, rig.transform);

			Assert.That(Vector3.Distance(head, eye), Is.LessThan(1e-5f));
		}

		// --- Accuracy ---------------------------------------------------------------------------

		[Test]
		public void Accuracy_WithNoRamp_IsTheCeilingImmediately()
		{
			Assert.That(AIAimSolver.ResolveAccuracy(0f, 0f, 0.7f), Is.EqualTo(0.7f).Within(1e-5f));
			Assert.That(AIAimSolver.ResolveAccuracy(100f, 0f, 0.7f), Is.EqualTo(0.7f).Within(1e-5f));
		}

		[Test]
		public void Accuracy_RampsInAndOut_AndReachesTheCeiling()
		{
			// Smoothstep: 0.5 of the way through time is 0.5 of the way through accuracy.
			Assert.That(AIAimSolver.ResolveAccuracy(0f, 2f, 1f), Is.EqualTo(0f).Within(1e-5f));
			Assert.That(AIAimSolver.ResolveAccuracy(1f, 2f, 1f), Is.EqualTo(0.5f).Within(1e-5f));
			Assert.That(AIAimSolver.ResolveAccuracy(2f, 2f, 1f), Is.EqualTo(1f).Within(1e-5f));

			// Eases in: a quarter of the way through time is well under a quarter of the accuracy.
			Assert.That(AIAimSolver.ResolveAccuracy(0.5f, 2f, 1f), Is.LessThan(0.25f),
				"a just-acquired target must be meaningfully hard to hit, not nearly settled");

			// Eases out, so the ramp does not visibly snap from imperfect to perfect.
			Assert.That(AIAimSolver.ResolveAccuracy(1.5f, 2f, 1f), Is.GreaterThan(0.75f));
		}

		[Test]
		public void Accuracy_IsClampedAtBothEnds()
		{
			Assert.That(AIAimSolver.ResolveAccuracy(100f, 2f, 1f), Is.EqualTo(1f).Within(1e-5f));
			Assert.That(AIAimSolver.ResolveAccuracy(0f, 2f, 0f), Is.EqualTo(0f).Within(1e-5f));
			Assert.That(AIAimSolver.ResolveAccuracy(1f, 2f, 4f), Is.LessThanOrEqualTo(1f));
		}

		[Test]
		public void Spread_ScalesInverselyWithAccuracy()
		{
			Assert.That(AIAimSolver.ResolveSpread(8f, 1f), Is.EqualTo(0f).Within(1e-5f));
			Assert.That(AIAimSolver.ResolveSpread(8f, 0f), Is.EqualTo(8f).Within(1e-5f));
			Assert.That(AIAimSolver.ResolveSpread(8f, 0.75f), Is.EqualTo(2f).Within(1e-5f));
			Assert.That(AIAimSolver.ResolveSpread(0f, 0f), Is.EqualTo(0f).Within(1e-5f));
		}

		// --- Lead -------------------------------------------------------------------------------

		[Test]
		public void LeadTime_ForAnInstantAbility_IsZero()
		{
			Assert.That(AIAimSolver.ResolveLeadTime(20f, 0f, 1f), Is.EqualTo(0f),
				"a melee swing has no flight time; dividing by its zero speed would produce an infinite lead");
			Assert.That(AIAimSolver.ResolveLeadTime(0f, 30f, 1f), Is.EqualTo(0f));
		}

		[Test]
		public void LeadTime_IsFlightTimeScaledByTheAccuracyDial()
		{
			// 30 m at 30 m/s is one second of flight.
			Assert.That(AIAimSolver.ResolveLeadTime(30f, 30f, 1f), Is.EqualTo(1f).Within(1e-5f));
			Assert.That(AIAimSolver.ResolveLeadTime(30f, 30f, 0.5f), Is.EqualTo(0.5f).Within(1e-5f));
			Assert.That(AIAimSolver.ResolveLeadTime(30f, 30f, 0f), Is.EqualTo(0f).Within(1e-5f));
		}

		[Test]
		public void LeadOffset_IsVelocityTimesLead()
		{
			Vector3 offset = AIAimSolver.ResolveLeadOffset(new Vector3(4f, 0f, 0f), 0.5f);

			Assert.That(Vector3.Distance(offset, new Vector3(2f, 0f, 0f)), Is.LessThan(1e-5f));
			Assert.That(AIAimSolver.ResolveLeadOffset(new Vector3(4f, 0f, 0f), 0f), Is.EqualTo(Vector3.zero));
		}

		// --- Scatter ---------------------------------------------------------------------------

		[Test]
		public void Scatter_WithNoSpread_IsIdentity()
		{
			Assert.That(Quaternion.Angle(AIAimSolver.RollScatter(0f, 1f, 1f), Quaternion.identity), Is.EqualTo(0f));
			Assert.That(Quaternion.Angle(AIAimSolver.RollScatter(4f, 0f, 0f), Quaternion.identity), Is.EqualTo(0f));
		}

		[Test]
		public void Scatter_IsDeterministic()
		{
			Quaternion a = AIAimSolver.RollScatter(5f, 0.3f, -0.8f);
			Quaternion b = AIAimSolver.RollScatter(5f, 0.3f, -0.8f);

			Assert.That(Quaternion.Angle(a, b), Is.EqualTo(0f), "the same rolls must produce the same shot");
		}

		[Test]
		public void Scatter_NeverExceedsTheAuthoredWorstCase_InAnyDirection()
		{
			const float spread = 3f;

			/* Sampled across the whole roll square rather than at a few points, because the failure
			 * this guards against — treating independent uniform rolls as a square — only shows up
			 * near the corners (1, 1), (1, -1) and so on. */
			for (int yaw = -10; yaw <= 10; yaw++)
			{
				for (int pitch = -10; pitch <= 10; pitch++)
				{
					Quaternion scatter = AIAimSolver.RollScatter(spread, yaw / 10f, pitch / 10f);
					float off = Vector3.Angle(Vector3.forward, scatter * Vector3.forward);

					Assert.That(off, Is.LessThanOrEqualTo(spread + 0.01f),
						$"roll ({yaw / 10f}, {pitch / 10f}) missed by {off} degrees against a {spread} degree spread");
				}
			}
		}

		[Test]
		public void Scatter_AtTheCornerOfTheRollSquare_IsTheSpreadNotItsDiagonal()
		{
			/* The unit-disc fold, stated as its own case. Unfolded, (1, 1) is sqrt(2) times the radius
			 * and would miss by nearly 3 degrees against a 2 degree spread. */
			Quaternion scatter = AIAimSolver.RollScatter(2f, 1f, 1f);
			float off = Vector3.Angle(Vector3.forward, scatter * Vector3.forward);

			Assert.That(off, Is.EqualTo(2f).Within(0.01f));
		}

		[Test]
		public void Scatter_ReachesEveryDirection()
		{
			// A held error that only ever pushed one way would read as a systematic offset, not as
			// aim quality, so the roll has to be able to miss up, down, left and right.
			bool up = false, down = false, left = false, right = false;

			for (int yaw = -10; yaw <= 10; yaw++)
			{
				for (int pitch = -10; pitch <= 10; pitch++)
				{
					Vector3 v = AIAimSolver.RollScatter(10f, yaw / 10f, pitch / 10f) * Vector3.forward;
					up |= v.y > 0.01f;
					down |= v.y < -0.01f;
					left |= v.x < -0.01f;
					right |= v.x > 0.01f;
				}
			}

			Assert.That(up && down && left && right, Is.True);
		}

		// --- Composing the direction -----------------------------------------------------------

		[Test]
		public void ComposeAim_OnADegenerateSolve_ReportsFailureRatherThanZero()
		{
			bool ok = AIAimSolver.TryComposeAim(Vector3.zero, Vector3.zero, Quaternion.identity, out Vector3 direction);

			Assert.That(ok, Is.False, "the caller must be able to tell 'no solution' from 'a direction of zero'");
			Assert.That(direction, Is.EqualTo(Vector3.zero));
		}

		[Test]
		public void ComposeAim_ReturnsAUnitDirection()
		{
			bool ok = AIAimSolver.TryComposeAim(new Vector3(0f, 1.45f, 0f), new Vector3(20f, 0.9f, 5f),
				Quaternion.identity, out Vector3 direction);

			Assert.That(ok, Is.True);
			Assert.That(direction.magnitude, Is.EqualTo(1f).Within(1e-4f));
		}

		// --- The regression --------------------------------------------------------------------

		[Test]
		public void AnAimSolvedFromTheAimOrigin_PassesThroughTheAimPoint()
		{
			GameObject npc = new GameObject("NPC");
			npc.transform.SetParent(root.transform, false);
			npc.transform.position = Vector3.zero;
			Physics.SyncTransforms();

			CapsuleCollider target = CreateCharacterCollider(new Vector3(20f, CapsuleRootHeight, 4f));

			Vector3 origin = CharacterAimOrigin.Resolve(null, npc.transform);
			Vector3 aimPoint = AIAimSolver.ResolveAimPoint(target.transform.position, target, AIAimPoint.Center);

			Assert.That(AIAimSolver.TryComposeAim(origin, aimPoint, Quaternion.identity, out Vector3 direction), Is.True);

			// Whichever way the NPC is facing, the shot must arrive where it was aimed.
			float range = Vector3.Distance(origin, aimPoint);
			Vector3 arrival = origin + direction * range;

			Assert.That(Vector3.Distance(arrival, aimPoint), Is.LessThan(1e-3f));
		}

		[Test]
		public void AnAimSolvedFromTheFeet_PassesOverTheTargetsHead()
		{
			/* The bug as reported, held as a test. Solving from transform.position — the character
			 * root, which is the feet — and then firing from the eye displaces the whole ray upward by
			 * the eye height. It is a parallel ray, so the miss is the same at any range, which is why
			 * the report describes it as never improving. */
			GameObject npc = new GameObject("NPC");
			npc.transform.SetParent(root.transform, false);
			npc.transform.position = Vector3.zero;
			Physics.SyncTransforms();

			CapsuleCollider target = CreateCharacterCollider(new Vector3(20f, CapsuleRootHeight, 4f));

			Vector3 feet = npc.transform.position;
			Vector3 origin = CharacterAimOrigin.Resolve(null, npc.transform);
			Vector3 aimPoint = AIAimSolver.ResolveAimPoint(target.transform.position, target, AIAimPoint.Center);

			Assert.That(AIAimSolver.TryComposeAim(feet, aimPoint, Quaternion.identity, out Vector3 oldDirection), Is.True);

			// Where the shot actually goes, fired from the eye along the feet's aim.
			float range = Vector3.Distance(origin, aimPoint);
			Vector3 arrival = origin + oldDirection * range;

			Assert.That(arrival.y, Is.GreaterThan(target.bounds.max.y),
				$"the old solve put the shot at y={arrival.y} over a head at y={target.bounds.max.y}");
			Assert.That(Vector3.Distance(arrival, aimPoint), Is.GreaterThan(1.4f),
				"the miss is the eye height, and it does not shrink with range");
		}
	}
}
