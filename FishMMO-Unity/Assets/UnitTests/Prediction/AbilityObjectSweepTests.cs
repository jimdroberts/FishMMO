using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using FishMMO.Shared;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;
using UnityLogAssert = UnityEngine.TestTools.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Coverage for the swept query that replaced <c>AbilityObject.OnCollisionEnter</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// An ability object is a kinematic body teleported once per tick to a closed-form position, so
	/// Unity's collision callback was wrong twice: it resolved against the server's present positions
	/// with no way to wrap a rewind scope around a physics step that had already run, and it tunnelled
	/// through anything thinner than one tick of travel. These tests exercise the replacement — the
	/// geometry it sweeps, the blind spot it covers with a second query, and the order it hands its
	/// results back in.
	/// </para>
	/// <para>
	/// They run against the editor's real physics scene with real colliders, so what is measured here
	/// is what PhysX actually reports; they do not stand up two networked peers, so the rewind itself
	/// is covered by <see cref="LagCompensationTests"/> rather than here.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class AbilityObjectSweepTests
	{
		/// <summary>
		/// Everything is built far from the origin: edit-mode tests share one physics scene, and a
		/// fixture that assumes it is alone at the origin will one day meet one that also does.
		/// </summary>
		private static readonly Vector3 Arena = new Vector3(5000f, 5000f, 5000f);

		private readonly List<GameObject> spawned = new List<GameObject>();
		private readonly List<AbilitySweepHit> hits = new List<AbilitySweepHit>();

		[TearDown]
		public void DestroySpawned()
		{
			for (int i = 0; i < spawned.Count; ++i)
			{
				if (spawned[i] != null)
				{
					Object.DestroyImmediate(spawned[i]);
				}
			}
			spawned.Clear();
			hits.Clear();
			Physics.SyncTransforms();
		}

		// ── Helpers ──────────────────────────────────────────────────────────────────

		private GameObject Track(GameObject go)
		{
			spawned.Add(go);
			return go;
		}

		/// <summary>A solid box target at an arena-relative position.</summary>
		private GameObject MakeBox(string name, Vector3 localPosition, Vector3 size)
		{
			GameObject go = Track(new GameObject(name));
			go.transform.position = Arena + localPosition;
			BoxCollider box = go.AddComponent<BoxCollider>();
			box.size = size;
			return go;
		}

		/// <summary>The moving ability object: a sphere collider on its own transform.</summary>
		private Transform MakeProjectile(float radius, out SphereCollider shape)
		{
			GameObject go = Track(new GameObject("Projectile"));
			go.transform.position = Arena;
			shape = go.AddComponent<SphereCollider>();
			shape.radius = radius;
			return go.transform;
		}

		private static PhysicsScene Scene(Transform t) => t.gameObject.scene.GetPhysicsScene();

		/// <summary>Every layer, so a test never depends on the project's collision matrix.</summary>
		private static LayerMask AllLayers => ~0;

		private int Sweep(Transform projectile, Collider shape, Vector3 from, Vector3 to, LayerMask mask)
		{
			projectile.position = to;
			Physics.SyncTransforms();
			return AbilityObjectSweep.Sweep(Scene(projectile), shape, projectile, from, to, mask, hits);
		}

		private string HitNames()
		{
			List<string> names = new List<string>();
			for (int i = 0; i < hits.Count; ++i)
			{
				names.Add(hits[i].Collider.name);
			}
			return string.Join(" → ", names);
		}

		private bool HitsInclude(string name)
		{
			for (int i = 0; i < hits.Count; ++i)
			{
				if (hits[i].Collider.name == name)
				{
					return true;
				}
			}
			return false;
		}

		// ── Tunnelling ───────────────────────────────────────────────────────────────

		/// <summary>
		/// The defect the sweep exists to fix: a target thinner than one tick of travel.
		/// </summary>
		/// <remarks>
		/// A kinematic body is teleported the whole step at once, so the physics engine only ever
		/// sees it before and after — never in between. The discrete overlap at the destination is
		/// asserted alongside the sweep so the test states the gap rather than implying it.
		/// </remarks>
		[Test]
		public void Sweep_ThinTargetInsideOneTickStep_IsHit_WhereADiscreteTestMissesIt()
		{
			Transform projectile = MakeProjectile(0.1f, out SphereCollider shape);
			MakeBox("ThinWall", new Vector3(0f, 0f, 5f), new Vector3(4f, 4f, 0.2f));

			Vector3 from = Arena;
			Vector3 to = Arena + new Vector3(0f, 0f, 10f);

			projectile.position = to;
			Physics.SyncTransforms();

			Collider[] discrete = new Collider[8];
			int discreteCount = Scene(projectile).OverlapSphere(to, 0.1f, discrete, AllLayers, QueryTriggerInteraction.Ignore);
			// The projectile's own collider is at `to` and answers every overlap there; the sweep
			// excludes it, and so must this probe.
			int discreteOthers = 0;
			for (int i = 0; i < discreteCount; ++i)
			{
				if (discrete[i] != null && !discrete[i].transform.IsChildOf(projectile))
				{
					++discreteOthers;
				}
			}
			LogAssert.AreEqual(0, discreteOthers,
				"A discrete test at the destination must miss the wall; if it does not, this test is no longer measuring tunnelling.");

			int count = Sweep(projectile, shape, from, to, AllLayers);

			TestContext.WriteLine($"MEASURE step 10m, wall 0.2m deep at 5m: discrete={discreteOthers} swept={count} [{HitNames()}]");

			LogAssert.IsTrue(count >= 1, "The swept query must find a wall the object passed straight through.");
			LogAssert.IsTrue(HitsInclude("ThinWall"), "The wall inside the travelled segment is the hit.");
		}

		// ── The cast's blind spot ────────────────────────────────────────────────────

		/// <summary>
		/// A target already overlapping the shape at the start of the segment is still reported.
		/// </summary>
		/// <remarks>
		/// Unity's shape casts deliberately do not report what they begin inside of, which is exactly
		/// the case that arises when a peer moves onto the projectile between ticks — the case lag
		/// compensation exists to resolve correctly. The overlap query anchored at the segment start
		/// covers it.
		/// </remarks>
		[Test]
		public void Sweep_TargetOverlappingAtTheSegmentStart_IsStillReported()
		{
			Transform projectile = MakeProjectile(0.25f, out SphereCollider shape);
			MakeBox("Engulfing", Vector3.zero, new Vector3(2f, 2f, 2f));

			Vector3 from = Arena;
			Vector3 to = Arena + new Vector3(0f, 0f, 6f);

			projectile.position = to;
			Physics.SyncTransforms();

			RaycastHit[] castOnly = new RaycastHit[8];
			int castCount = Scene(projectile).SphereCast(from, 0.25f, Vector3.forward, castOnly, 6f, AllLayers, QueryTriggerInteraction.Ignore);

			int count = Sweep(projectile, shape, from, to, AllLayers);

			TestContext.WriteLine($"MEASURE start-overlap: bare cast reported {castCount}, sweep reported {count} [{HitNames()}]");

			LogAssert.IsTrue(HitsInclude("Engulfing"),
				"A collider the shape starts inside must be reported; the cast alone is blind to it.");
		}

		/// <summary>
		/// An object that does not move still resolves hits.
		/// </summary>
		/// <remarks>
		/// A stationary volume — a lingering area effect with no move action — has a zero-length
		/// segment, and a cast with no direction is not a query. It degrades to an overlap instead of
		/// silently never hitting anything, which is what the collision callback used to do for it.
		/// </remarks>
		[Test]
		public void Sweep_StationaryObject_ResolvesAnOverlapInstead()
		{
			Transform projectile = MakeProjectile(1.5f, out SphereCollider shape);
			MakeBox("Standing", new Vector3(0.5f, 0f, 0f), Vector3.one);

			int count = Sweep(projectile, shape, Arena, Arena, AllLayers);

			LogAssert.AreEqual(1, count, $"A stationary object must still find what is inside it. Got [{HitNames()}]");
			LogAssert.IsTrue(HitsInclude("Standing"), "The overlapping target is the hit.");
		}

		// ── Ordering ─────────────────────────────────────────────────────────────────

		/// <summary>
		/// Hits come back in the order the object reached them.
		/// </summary>
		/// <remarks>
		/// The caller caps the set by draining <c>HitCount</c> as it walks it, so an unordered buffer
		/// would let the physics broadphase choose a pierce's victims.
		/// </remarks>
		[Test]
		public void Sweep_Results_AreOrderedByDistanceAlongTheSegment()
		{
			Transform projectile = MakeProjectile(0.1f, out SphereCollider shape);
			// Created back to front, so passing by luck of the insertion order is not possible.
			MakeBox("Third", new Vector3(0f, 0f, 8f), new Vector3(2f, 2f, 0.4f));
			MakeBox("First", new Vector3(0f, 0f, 2f), new Vector3(2f, 2f, 0.4f));
			MakeBox("Second", new Vector3(0f, 0f, 5f), new Vector3(2f, 2f, 0.4f));

			int count = Sweep(projectile, shape, Arena, Arena + new Vector3(0f, 0f, 10f), AllLayers);

			TestContext.WriteLine($"MEASURE sweep order: {HitNames()}");

			LogAssert.AreEqual(3, count, "All three walls lie inside the segment.");
			LogAssert.AreEqual("First", hits[0].Collider.name, "Nearest along the sweep comes first.");
			LogAssert.AreEqual("Second", hits[1].Collider.name, "Then the middle one.");
			LogAssert.AreEqual("Third", hits[2].Collider.name, "Then the furthest.");
			LogAssert.IsTrue(hits[0].Distance <= hits[1].Distance && hits[1].Distance <= hits[2].Distance,
				"The reported distances must be non-decreasing.");
		}

		/// <summary>
		/// Equidistant hits are separated by identity, not by the order the scene happens to hold them.
		/// </summary>
		/// <remarks>
		/// Two colliders overlapping the segment start both report distance zero, so the tiebreak is
		/// all there is. It has to be a key both peers compute to the same value —
		/// <see cref="TargetOrdering.StableNameKey"/> here, since neither candidate is networked —
		/// and never the Unity instance id, which is per-process and would put the same pair in a
		/// different order on the client than on the server.
		/// </remarks>
		[Test]
		public void Sweep_EquidistantHits_AreOrderedByIdentity_NotByCreationOrder()
		{
			string forwards = OrderOfTwoOverlappingTargets(createAlphaFirst: true);
			DestroySpawned();
			string backwards = OrderOfTwoOverlappingTargets(createAlphaFirst: false);

			TestContext.WriteLine($"MEASURE alpha-first: {forwards} | omega-first: {backwards}");

			LogAssert.AreEqual(forwards, backwards,
				"Two equidistant hits must come back in the same order whichever was created first; " +
				"anything else is the broadphase choosing, and the two peers do not share one.");

			int alphaKey = TargetOrdering.StableNameKey("AlphaTarget");
			int omegaKey = TargetOrdering.StableNameKey("OmegaTarget");
			string expected = alphaKey < omegaKey ? "AlphaTarget → OmegaTarget" : "OmegaTarget → AlphaTarget";

			LogAssert.AreEqual(expected, forwards,
				"The order must be the stable name key's, which every peer computes identically.");
		}

		private string OrderOfTwoOverlappingTargets(bool createAlphaFirst)
		{
			Transform projectile = MakeProjectile(0.5f, out SphereCollider shape);

			if (createAlphaFirst)
			{
				MakeBox("AlphaTarget", Vector3.zero, Vector3.one);
				MakeBox("OmegaTarget", Vector3.zero, Vector3.one);
			}
			else
			{
				MakeBox("OmegaTarget", Vector3.zero, Vector3.one);
				MakeBox("AlphaTarget", Vector3.zero, Vector3.one);
			}

			int count = Sweep(projectile, shape, Arena, Arena + new Vector3(0f, 0f, 3f), AllLayers);
			LogAssert.AreEqual(2, count, "Both targets sit on the segment start and must both be reported.");
			return HitNames();
		}

		/// <summary>
		/// The object's own colliders are never in the hit set.
		/// </summary>
		/// <remarks>
		/// A collision callback could not report a body against itself. A query has no such notion,
		/// and the shape sits exactly where the sweep begins, so its own collider would come back
		/// from the overlap on every single tick — and consume a hit from the budget doing it.
		/// </remarks>
		[Test]
		public void Sweep_OwnColliders_AreNeverReported()
		{
			Transform projectile = MakeProjectile(0.5f, out SphereCollider shape);

			GameObject childHitbox = new GameObject("ChildHitbox");
			childHitbox.transform.SetParent(projectile, false);
			childHitbox.AddComponent<BoxCollider>();

			MakeBox("RealTarget", new Vector3(0f, 0f, 4f), Vector3.one);

			int count = Sweep(projectile, shape, Arena, Arena + new Vector3(0f, 0f, 8f), AllLayers);

			LogAssert.AreEqual(1, count, $"Only the real target may be reported. Got [{HitNames()}]");
			LogAssert.IsTrue(HitsInclude("RealTarget"), "And it is the one that is not part of the projectile.");
		}

		/// <summary>
		/// A collider both queries find is reported once.
		/// </summary>
		/// <remarks>
		/// The overlap at the segment start and the cast along it can return the same collider — this
		/// editor's sphere cast does report what it started inside, with a zero distance. Reporting it
		/// twice would charge two hits for one target on the first tick alone.
		/// </remarks>
		[Test]
		public void Sweep_ColliderFoundByBothQueries_IsReportedOnce()
		{
			Transform projectile = MakeProjectile(0.5f, out SphereCollider shape);
			// Long enough that the cast travels through it after starting inside it.
			MakeBox("Engulfing", Vector3.zero, new Vector3(2f, 2f, 6f));

			int count = Sweep(projectile, shape, Arena, Arena + new Vector3(0f, 0f, 8f), AllLayers);

			LogAssert.AreEqual(1, count, $"One collider is one hit however many queries found it. Got [{HitNames()}]");
		}

		// ── Query filtering ──────────────────────────────────────────────────────────

		/// <summary>A layer the mask excludes is not swept, in either query.</summary>
		[Test]
		public void Sweep_MaskExcludingTheTargetLayer_FindsNothing()
		{
			Transform projectile = MakeProjectile(0.5f, out SphereCollider shape);
			// One overlapping the start and one along the segment, so both query paths are covered.
			MakeBox("AtStart", Vector3.zero, Vector3.one);
			MakeBox("AlongSweep", new Vector3(0f, 0f, 4f), Vector3.one);

			LayerMask everythingButDefault = ~(1 << 0);
			int count = Sweep(projectile, shape, Arena, Arena + new Vector3(0f, 0f, 8f), everythingButDefault);

			LogAssert.AreEqual(0, count, $"Nothing on an excluded layer may be reported. Got [{HitNames()}]");
		}

		/// <summary>
		/// Trigger colliders are ignored, as they were by the collision callback this replaced.
		/// </summary>
		[Test]
		public void Sweep_TriggerColliders_AreIgnored()
		{
			Transform projectile = MakeProjectile(0.25f, out SphereCollider shape);
			GameObject volume = MakeBox("TriggerVolume", new Vector3(0f, 0f, 4f), new Vector3(2f, 2f, 2f));
			volume.GetComponent<BoxCollider>().isTrigger = true;

			int count = Sweep(projectile, shape, Arena, Arena + new Vector3(0f, 0f, 8f), AllLayers);

			LogAssert.AreEqual(0, count,
				$"A trigger never raised OnCollisionEnter and must not raise a swept hit either. Got [{HitNames()}]");
		}

		/// <summary>An empty mask short-circuits rather than querying every layer.</summary>
		[Test]
		public void Sweep_EmptyMask_ReturnsNothing()
		{
			Transform projectile = MakeProjectile(0.5f, out SphereCollider shape);
			MakeBox("Ignored", new Vector3(0f, 0f, 2f), Vector3.one);

			int count = Sweep(projectile, shape, Arena, Arena + new Vector3(0f, 0f, 4f), new LayerMask());

			LogAssert.AreEqual(0, count, "A mask of zero layers can hit nothing.");
		}

		// ── Shape resolution ─────────────────────────────────────────────────────────

		/// <summary>A capsule-shaped projectile sweeps as a capsule, not as a ray.</summary>
		[Test]
		public void Sweep_CapsuleShape_SweepsTheAuthoredVolume()
		{
			GameObject go = Track(new GameObject("CapsuleProjectile"));
			go.transform.position = Arena;
			CapsuleCollider capsule = go.AddComponent<CapsuleCollider>();
			capsule.radius = 0.75f;
			capsule.height = 2f;
			capsule.direction = 1;

			// Offset sideways by more than a ray would reach but less than the capsule's radius.
			MakeBox("Grazed", new Vector3(0.6f, 0f, 4f), new Vector3(0.2f, 0.2f, 0.2f));

			int count = Sweep(go.transform, capsule, Arena, Arena + new Vector3(0f, 0f, 8f), AllLayers);

			TestContext.WriteLine($"MEASURE capsule sweep: {count} [{HitNames()}]");
			LogAssert.IsTrue(HitsInclude("Grazed"),
				"A target inside the capsule's radius must be hit; a ray down the centre line would miss it.");
		}

		/// <summary>
		/// A shape the sweep cannot model degrades to a ray rather than to nothing.
		/// </summary>
		[Test]
		public void Sweep_NoShape_StillSweepsAsARay()
		{
			GameObject go = Track(new GameObject("ShapelessProjectile"));
			go.transform.position = Arena;

			MakeBox("OnTheLine", new Vector3(0f, 0f, 4f), Vector3.one);
			MakeBox("OffTheLine", new Vector3(6f, 0f, 4f), Vector3.one);

			int count = Sweep(go.transform, null, Arena, Arena + new Vector3(0f, 0f, 8f), AllLayers);

			LogAssert.AreEqual(1, count, $"The ray hits what is on the line and nothing else. Got [{HitNames()}]");
			LogAssert.IsTrue(HitsInclude("OnTheLine"), "The target on the centre line is hit.");
		}

		/// <summary>
		/// The shape's local centre is re-applied at the segment start, not read off the transform.
		/// </summary>
		/// <remarks>
		/// The transform has already been advanced to the end of the segment by the time hits are
		/// resolved, so reading the world centre directly would sweep from the wrong place — by
		/// exactly one tick of travel, every tick.
		/// </remarks>
		[Test]
		public void CenterOffset_IsTheShapeCentreRelativeToItsTransform()
		{
			GameObject go = Track(new GameObject("Offset"));
			go.transform.position = Arena;
			go.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
			SphereCollider sphere = go.AddComponent<SphereCollider>();
			sphere.center = new Vector3(0f, 0f, 2f);

			Vector3 offset = AbilityObjectSweep.CenterOffset(sphere, go.transform);

			// Rotated 90 degrees about Y, the collider's local +Z points along world +X.
			LogAssert.IsTrue((offset - new Vector3(2f, 0f, 0f)).magnitude < 0.001f,
				$"The centre offset must follow the transform's rotation; got {offset}.");
			LogAssert.IsTrue(AbilityObjectSweep.CenterOffset(null, go.transform) == Vector3.zero,
				"A shapeless object sweeps from its own position.");
		}

		/// <summary>A sphere stays a sphere under non-uniform scale, the way Unity scales one.</summary>
		[Test]
		public void WorldRadius_UsesTheLargestScaleComponent()
		{
			GameObject go = Track(new GameObject("ScaledSphere"));
			go.transform.localScale = new Vector3(1f, 3f, 2f);
			SphereCollider sphere = go.AddComponent<SphereCollider>();
			sphere.radius = 0.5f;

			LogAssert.AreEqual(1.5f, AbilityObjectSweep.WorldRadius(sphere, go.transform),
				"Unity scales a sphere collider by the largest lossy scale component.");
		}

		/// <summary>A box's half extents scale per axis.</summary>
		[Test]
		public void WorldHalfExtents_ScalePerAxis()
		{
			GameObject go = Track(new GameObject("ScaledBox"));
			go.transform.localScale = new Vector3(2f, 1f, 0.5f);
			BoxCollider box = go.AddComponent<BoxCollider>();
			box.size = new Vector3(1f, 2f, 4f);

			Vector3 extents = AbilityObjectSweep.WorldHalfExtents(box, go.transform);

			LogAssert.IsTrue((extents - new Vector3(1f, 1f, 1f)).magnitude < 0.001f,
				$"Half extents must be size * 0.5 scaled per axis; got {extents}.");
		}

		/// <summary>
		/// A capsule authored shorter than its own diameter resolves to a sphere, not to an inverted
		/// segment.
		/// </summary>
		[Test]
		public void ResolveCapsule_HeightBelowADiameter_CollapsesToASphere()
		{
			GameObject go = Track(new GameObject("SquatCapsule"));
			go.transform.position = Arena;
			CapsuleCollider capsule = go.AddComponent<CapsuleCollider>();
			capsule.radius = 1f;
			capsule.height = 0.5f;
			capsule.direction = 1;

			AbilityObjectSweep.ResolveCapsule(capsule, go.transform, Arena,
				out Vector3 point0, out Vector3 point1, out float radius);

			LogAssert.AreEqual(1f, radius, "Radius is unaffected by an under-height capsule.");
			LogAssert.IsTrue((point0 - point1).magnitude < 0.001f,
				$"Both sphere centres must coincide; got {point0} and {point1}, which would sweep an inverted capsule.");
		}

		/// <summary>The capsule's segment follows the authored direction axis and the transform's rotation.</summary>
		[Test]
		public void ResolveCapsule_SegmentFollowsTheDirectionAxis()
		{
			GameObject go = Track(new GameObject("LyingCapsule"));
			go.transform.position = Arena;
			CapsuleCollider capsule = go.AddComponent<CapsuleCollider>();
			capsule.radius = 0.5f;
			capsule.height = 4f;
			capsule.direction = 2;

			AbilityObjectSweep.ResolveCapsule(capsule, go.transform, Arena,
				out Vector3 point0, out Vector3 point1, out float radius);

			LogAssert.AreEqual(0.5f, radius, "An unscaled capsule keeps its authored radius.");
			LogAssert.IsTrue((point0 - (Arena + new Vector3(0f, 0f, 1.5f))).magnitude < 0.001f,
				$"A Z-aligned capsule's first centre sits half a segment along +Z; got {point0}.");
			LogAssert.IsTrue((point1 - (Arena + new Vector3(0f, 0f, -1.5f))).magnitude < 0.001f,
				$"…and the second half a segment along -Z; got {point1}.");
		}

		// ── Layer mask ───────────────────────────────────────────────────────────────

		/// <summary>
		/// The query mask is the project's collision matrix, which is what decided the contacts the
		/// callback ever saw.
		/// </summary>
		[Test]
		public void CollisionMaskForLayer_MirrorsTheCollisionMatrix()
		{
			AbilityObjectSweep.ResetLayerMasks();

			for (int layer = 0; layer < 32; ++layer)
			{
				int expected = 0;
				for (int other = 0; other < 32; ++other)
				{
					if (!Physics.GetIgnoreLayerCollision(layer, other))
					{
						expected |= 1 << other;
					}
				}
				LogAssert.AreEqual(expected, AbilityObjectSweep.CollisionMaskForLayer(layer),
					$"Layer {layer}'s query mask must be its row of the collision matrix.");
			}
		}

		/// <summary>Reading the same row twice is the cache, not a second matrix walk with a different answer.</summary>
		[Test]
		public void CollisionMaskForLayer_IsStable_AndRefusesLayersOutsideTheMatrix()
		{
			AbilityObjectSweep.ResetLayerMasks();
			int first = AbilityObjectSweep.CollisionMaskForLayer(0);
			int second = AbilityObjectSweep.CollisionMaskForLayer(0);
			AbilityObjectSweep.ResetLayerMasks();
			int afterReset = AbilityObjectSweep.CollisionMaskForLayer(0);

			LogAssert.AreEqual(first, second, "The cached row must not change between reads.");
			LogAssert.AreEqual(first, afterReset, "Rebuilding the cache must reproduce the same row.");
			LogAssert.AreEqual(0, AbilityObjectSweep.CollisionMaskForLayer(-1), "A layer below the matrix hits nothing.");
			LogAssert.AreEqual(0, AbilityObjectSweep.CollisionMaskForLayer(32), "A layer above the matrix hits nothing.");
		}

		// ── AbilityObject hit dispatch ───────────────────────────────────────────────

		private static readonly MethodInfo DispatchSweptHit = typeof(AbilityObject)
			.GetMethod("DispatchSweptHit", BindingFlags.Instance | BindingFlags.NonPublic);

		private static bool Dispatch(AbilityObject abilityObject, Collider collider)
		{
			AbilitySweepHit hit = new AbilitySweepHit(collider, Vector3.zero, Vector3.up, 1f);
			return (bool)DispatchSweptHit.Invoke(abilityObject, new object[] { hit });
		}

		/// <summary>An ability object with no caster, so dispatch exercises the bookkeeping alone.</summary>
		private AbilityObject MakeAbilityObject(int hitCount)
		{
			GameObject go = Track(new GameObject("SweptAbilityObject"));
			go.transform.position = Arena;
			AbilityObject abilityObject = go.AddComponent<AbilityObject>();
			abilityObject.HitCount = hitCount;
			return abilityObject;
		}

		/// <summary>
		/// The same target costs one hit however many ticks it stays inside the sweep.
		/// </summary>
		/// <remarks>
		/// The query runs every tick, so a pierce that ends up inside a character overlaps it on all
		/// of them. Without a lifetime hit set it would drain its whole hit count into one victim in
		/// a fraction of a second — a failure mode the collision callback did not have, because
		/// <c>OnCollisionEnter</c> fires on entry rather than on contact.
		/// </remarks>
		[Test]
		public void DispatchSweptHit_SameTargetAcrossTicks_CostsOneHit()
		{
			LogAssert.IsNotNull(DispatchSweptHit, "DispatchSweptHit must exist; the sweep dispatches through it.");

			AbilityObject abilityObject = MakeAbilityObject(5);
			Collider victim = MakeBox("Victim", new Vector3(0f, 0f, 3f), Vector3.one).GetComponent<Collider>();

			LogAssert.IsTrue(Dispatch(abilityObject, victim), "The first hit does not end the object.");
			LogAssert.IsTrue(Dispatch(abilityObject, victim), "Nor does re-reporting the same target.");
			LogAssert.IsTrue(Dispatch(abilityObject, victim), "However many ticks it lingers.");

			LogAssert.AreEqual(4, abilityObject.HitCount, "Exactly one hit may be charged for one target.");
		}

		/// <summary>Two colliders on one body are one target, not two.</summary>
		/// <remarks>
		/// A hit resolves through the attached rigidbody's GameObject, which is what
		/// <c>Collision.gameObject</c> reported — so a character whose hitboxes sit on children still
		/// costs one hit rather than one per bone. A body rigged without a rigidbody on the root is
		/// covered by the parent walk that follows, the same one <c>EventData.SetTarget</c> makes.
		/// </remarks>
		[Test]
		public void DispatchSweptHit_TwoCollidersOnOneBody_CostOneHit()
		{
			AbilityObject abilityObject = MakeAbilityObject(5);

			GameObject character = Track(new GameObject("Character"));
			character.transform.position = Arena + new Vector3(0f, 0f, 3f);
			Rigidbody body = character.AddComponent<Rigidbody>();
			body.isKinematic = true;

			GameObject torso = new GameObject("Torso");
			torso.transform.SetParent(character.transform, false);
			Collider torsoCollider = torso.AddComponent<BoxCollider>();

			GameObject head = new GameObject("Head");
			head.transform.SetParent(character.transform, false);
			Collider headCollider = head.AddComponent<SphereCollider>();

			LogAssert.IsTrue(Dispatch(abilityObject, torsoCollider), "The torso hit lands.");
			LogAssert.IsTrue(Dispatch(abilityObject, headCollider), "The head belongs to the same body.");

			LogAssert.AreEqual(4, abilityObject.HitCount, "Two hitboxes on one character are one hit.");
		}

		/// <summary>
		/// Exhausting the hit count ends the object and abandons the rest of the sweep.
		/// </summary>
		/// <remarks>
		/// The remaining hits must be abandoned rather than applied: everything after this point in
		/// the ordered set belongs to an object that no longer exists.
		/// </remarks>
		[Test]
		public void DispatchSweptHit_LastHit_EndsTheObject_AndStopsTheSweep()
		{
			AbilityObject abilityObject = MakeAbilityObject(1);
			Collider victim = MakeBox("FinalVictim", new Vector3(0f, 0f, 3f), Vector3.one).GetComponent<Collider>();

			bool destroyedFlag;
			// DestroyAbilityObjectInternal calls Object.Destroy, which edit mode logs an error for and
			// then declines; the state transition under test happens before that call either way.
			UnityLogAssert.ignoreFailingMessages = true;
			try
			{
				bool keepSweeping = Dispatch(abilityObject, victim);
				LogAssert.IsFalse(keepSweeping, "A hit that exhausts the budget must abandon the rest of the sweep.");

				FieldInfo destroyed = typeof(AbilityObject).GetField("destroyed", BindingFlags.Instance | BindingFlags.NonPublic);
				LogAssert.IsNotNull(destroyed, "The destroy guard must exist.");
				destroyedFlag = (bool)destroyed.GetValue(abilityObject);
			}
			finally
			{
				UnityLogAssert.ignoreFailingMessages = false;
			}

			LogAssert.IsTrue(destroyedFlag, "Exhausting HitCount ends the object.");
			LogAssert.AreEqual(0, abilityObject.HitCount, "The final hit is still charged.");
		}

		/// <summary>
		/// The Unity collision callback is gone and must stay gone.
		/// </summary>
		/// <remarks>
		/// Re-adding it would double every hit — once from the sweep and once from the physics step —
		/// and quietly reintroduce the uncompensated path the sweep exists to replace.
		/// </remarks>
		[Test]
		public void AbilityObject_DeclaresNoCollisionCallback()
		{
			BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

			LogAssert.IsNull(typeof(AbilityObject).GetMethod("OnCollisionEnter", all),
				"Hits resolve through the swept query; a collision callback alongside it would double every hit.");
			LogAssert.IsNull(typeof(AbilityObject).GetMethod("OnTriggerEnter", all),
				"Same for the trigger callback — the sweep ignores triggers on purpose.");
		}
	}
}
