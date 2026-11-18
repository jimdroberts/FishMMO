using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// One collider the swept query found, with the impact information a hit needs.
	/// </summary>
	public readonly struct AbilitySweepHit
	{
		/// <summary>The collider that was hit.</summary>
		public readonly Collider Collider;

		/// <summary>World point of impact, for effects that need one.</summary>
		public readonly Vector3 Point;

		/// <summary>Surface normal at <see cref="Point"/>, or the reverse of travel for an overlap.</summary>
		public readonly Vector3 Normal;

		/// <summary>Distance along the swept segment, zero for a collider already overlapping at the start.</summary>
		public readonly float Distance;

		/// <summary>
		/// <see cref="Point"/> expressed in the hit BODY's own space, captured while the query's world
		/// was still standing.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>The only impact position that survives leaving the rewind scope.</b>
		/// <c>AbilityObject.ResolveSweptHits</c> queries inside a rewind to the caster's view and then
		/// dispatches the results after it has closed — deliberately, so no damage or authored action
		/// ever runs against a displaced world. <see cref="Point"/> therefore describes a world that no
		/// longer exists by the time anything reads it, and comparing it against anything read from a
		/// live transform mixes the two. At 200&#160;ms that is metres.
		/// </para>
		/// <para>
		/// A LOCAL point has no such problem, because the body and everything defined relative to it
		/// were displaced together: the relationship is identical in the rewound world and the live
		/// one. That is what lets <see cref="ShieldVolume"/> be tested against a hit long after the
		/// scope has closed. Zero when the hit resolved to no body.
		/// </para>
		/// </remarks>
		public readonly Vector3 LocalPoint;

		public AbilitySweepHit(Collider collider, Vector3 point, Vector3 normal, float distance, Vector3 localPoint)
		{
			Collider = collider;
			Point = point;
			Normal = normal;
			Distance = distance;
			LocalPoint = localPoint;
		}
	}

	/// <summary>
	/// The swept shape query an <see cref="AbilityObject"/> resolves its hits with, in place of
	/// Unity's collision callbacks.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why an explicit query at all.</b> An ability object moves by a closed-form position
	/// evaluated once per tick (<see cref="AbilityMoveTransformAction"/>) on a kinematic body, so
	/// <c>OnCollisionEnter</c> had two defects that cannot be fixed where it fires. It is not lag
	/// compensated — the physics step resolves against present positions while the caster's client
	/// rendered its peers in the past, and you cannot open a rewind scope around a step that has
	/// already run; and a body teleported once per tick tunnels straight through any target thinner
	/// than its per-tick step. Rewinding once for every projectile at once is not an option either,
	/// because each has a different caster with a different view offset. Hence one query per object,
	/// swept along the segment it just travelled, run inside that caster's own rewind.
	/// </para>
	/// <para>
	/// <b>Two queries, not one.</b> A shape cast is documented not to register a collider it starts
	/// inside of. Measured against this editor it does report one for spheres and capsules — with a
	/// zero distance and a zero normal — but not dependably enough to build on, and a box cast is
	/// explicitly excluded. That blind spot is exactly the case a target which moved onto the object
	/// between ticks falls into, which is the case lag compensation exists to resolve correctly, so
	/// an overlap anchored at the segment start covers it outright. The same overlap is what lets a
	/// stationary object — one with no move action, whose segment has zero length and so admits no
	/// cast at all — resolve hits. Colliders both queries find are reported once.
	/// </para>
	/// <para>
	/// <b>Results are ordered before they are returned.</b> A cast buffer is filled in broadphase
	/// order, which is neither reproducible across runs nor agreed between peers, and the caller
	/// caps the set by decrementing <c>HitCount</c> as it walks it — so the cap would otherwise
	/// choose arbitrary victims. Ordering is distance along the sweep, then the identity keys in
	/// <see cref="TargetOrdering"/>; never the Unity instance id, which is per-process.
	/// </para>
	/// </remarks>
	public static class AbilityObjectSweep
	{
		/// <summary>
		/// Segment length below which the sweep degenerates to a stationary overlap, in metres.
		/// </summary>
		/// <remarks>
		/// A cast needs a direction, and normalising a segment shorter than this produces noise
		/// rather than a heading. Well under a millimetre, so nothing that actually moves takes the
		/// overlap path.
		/// </remarks>
		public const float MinimumSweepDistance = 1e-4f;

		/// <summary>Largest buffer the query is allowed to grow to before results are truncated.</summary>

		private static RaycastHit[] castBuffer = new RaycastHit[32];
		private static Collider[] overlapBuffer = new Collider[32];

		/// <summary>Reused rank column so ordering a hit set allocates nothing.</summary>
		private static readonly List<TargetRank> ranks = new List<TargetRank>(64);

		/// <summary>Unordered hits, gathered from both queries before they are ranked.</summary>
		private static readonly List<AbilitySweepHit> gathered = new List<AbilitySweepHit>(64);

		/// <summary>Collision-matrix row per layer, built on first use.</summary>
		private static readonly int[] layerMasks = new int[32];
		private static bool layerMasksBuilt;

		/// <summary>
		/// The layers a collider on <paramref name="layer"/> collides with, read from the project's
		/// collision matrix.
		/// </summary>
		/// <remarks>
		/// The matrix is what decided which contacts <c>OnCollisionEnter</c> ever saw, so deriving
		/// the query mask from it is what keeps the swept query hitting the same set of things the
		/// callback did. It is a project setting, identical on every peer, and global rather than
		/// per physics scene — so one cached row per layer is correct for every scene a server hosts.
		/// </remarks>
		public static int CollisionMaskForLayer(int layer)
		{
			if (layer < 0 || layer > 31)
			{
				return 0;
			}
			if (!layerMasksBuilt)
			{
				BuildLayerMasks();
			}
			return layerMasks[layer];
		}

		private static void BuildLayerMasks()
		{
			for (int a = 0; a < 32; ++a)
			{
				int mask = 0;
				for (int b = 0; b < 32; ++b)
				{
					if (!Physics.GetIgnoreLayerCollision(a, b))
					{
						mask |= 1 << b;
					}
				}
				layerMasks[a] = mask;
			}
			layerMasksBuilt = true;
		}

		/// <summary>
		/// Drops the cached collision matrix so the next query re-reads it.
		/// </summary>
		/// <remarks>
		/// The matrix is a project setting that can be edited between play sessions, and the cache is
		/// static so it survives a domain reload that did not recompile. Also the hook a test uses
		/// after changing <c>Physics.IgnoreLayerCollision</c>.
		/// </remarks>
		public static void ResetLayerMasks() => layerMasksBuilt = false;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetOnDomainReload() => ResetLayerMasks();

		/// <summary>
		/// Resolves everything the object's shape touched moving from <paramref name="from"/> to
		/// <paramref name="to"/>, in a deterministic order.
		/// </summary>
		/// <param name="physicsScene">
		/// The scene's own physics world. Never the global <see cref="Physics"/> API: a scene server
		/// hosts many scenes and the default one holds none of the relevant colliders.
		/// </param>
		/// <param name="shape">
		/// The collider whose dimensions the sweep uses. A sphere, capsule or box is swept as itself;
		/// anything else (including null) degrades to a ray, which is thinner than the real shape but
		/// still swept and still compensated.
		/// </param>
		/// <param name="shapeTransform">
		/// The live transform the shape's local centre, rotation and scale are resolved through —
		/// the ability object's own, which is already at <paramref name="to"/>.
		/// </param>
		/// <param name="from">Where the object was when it last resolved hits.</param>
		/// <param name="to">Where the object is now.</param>
		/// <param name="mask">Layers to query, normally from <see cref="CollisionMaskForLayer"/>.</param>
		/// <param name="results">Receives the ordered hits. Cleared first.</param>
		/// <returns>The number of hits written to <paramref name="results"/>.</returns>
		public static int Sweep(
			PhysicsScene physicsScene,
			Collider shape,
			Transform shapeTransform,
			Vector3 from,
			Vector3 to,
			LayerMask mask,
			List<AbilitySweepHit> results)
		{
			if (results == null)
			{
				return 0;
			}
			results.Clear();

			if (shapeTransform == null || mask.value == 0)
			{
				return 0;
			}

			gathered.Clear();

			Vector3 delta = to - from;
			float distance = delta.magnitude;
			Vector3 direction = distance >= MinimumSweepDistance ? delta / distance : Vector3.zero;

			/* The overlap comes first because the cast cannot see what it starts inside of. Both are
			 * anchored at `from`: the cast covers the segment travelled, and the overlap covers the
			 * point the cast is blind at. Anything sitting at `to` and nowhere else is reached by the
			 * cast; if it arrived there after the object did, the next tick's overlap catches it. */
			GatherOverlap(physicsScene, shape, shapeTransform, from, direction, mask);

			if (distance >= MinimumSweepDistance)
			{
				GatherCast(physicsScene, shape, shapeTransform, from, direction, distance, mask);
			}


			return Order(results);
		}

		/// <summary>Fills <see cref="gathered"/> with colliders overlapping the shape at the segment start.</summary>
		private static void GatherOverlap(
			PhysicsScene physicsScene, Collider shape, Transform t, Vector3 from, Vector3 direction, LayerMask mask)
		{
			Vector3 origin = from + CenterOffset(shape, t);
			int count;

			while (true)
			{
				switch (shape)
				{
					case SphereCollider sphere:
						count = physicsScene.OverlapSphere(origin, WorldRadius(sphere, t), overlapBuffer, mask, QueryTriggerInteraction.Ignore);
						break;
					case CapsuleCollider capsule:
						{
							ResolveCapsule(capsule, t, origin, out Vector3 point0, out Vector3 point1, out float radius);
							count = physicsScene.OverlapCapsule(point0, point1, radius, overlapBuffer, mask, QueryTriggerInteraction.Ignore);
						}
						break;
					case BoxCollider box:
						count = physicsScene.OverlapBox(origin, WorldHalfExtents(box, t), overlapBuffer, t.rotation, mask, QueryTriggerInteraction.Ignore);
						break;
					default:
						/* A ray has no volume, so there is no overlap test that corresponds to it.
						 * The cast alone covers the ray case; a shapeless object simply has no start
						 * overlap to miss. */
						return;
				}

				/* The shared grow-on-full helper, not a private copy of it. Behaviourally the same
				 * doubling against the same ceiling, but it is also the one place that REPORTS a
				 * saturated query — TargetOrdering.WarnQueryBufferSaturated. A sweep that filled its
				 * buffer used to truncate in broadphase order and say nothing, while every other
				 * spatial query in the project said so once per session. */
				if (!TargetOrdering.TryGrowQueryBuffer(ref overlapBuffer, count))
				{
					break;
				}
			}

			// The reverse of travel is the only normal an overlap can honestly report: the shape is
			// already inside, so there is no surface crossing to take one from.
			Vector3 normal = direction == Vector3.zero ? Vector3.up : -direction;

			for (int i = 0; i < count; ++i)
			{
				Collider hit = overlapBuffer[i];
				if (!Accept(hit, t))
				{
					continue;
				}
				/* Bounds.ClosestPoint, not Collider.ClosestPoint: the latter logs an error for a
				 * non-convex mesh collider rather than answering, and this is only ever an effect
				 * position. */
				Vector3 overlapPoint = hit.bounds.ClosestPoint(origin);
				gathered.Add(new AbilitySweepHit(hit, overlapPoint, normal, 0f, ToBodyLocal(hit, overlapPoint)));
			}
		}

		/// <summary>Fills <see cref="gathered"/> with colliders the shape crossed along the segment.</summary>
		private static void GatherCast(
			PhysicsScene physicsScene, Collider shape, Transform t, Vector3 from, Vector3 direction, float distance, LayerMask mask)
		{
			Vector3 origin = from + CenterOffset(shape, t);
			int count;

			while (true)
			{
				switch (shape)
				{
					case SphereCollider sphere:
						count = physicsScene.SphereCast(origin, WorldRadius(sphere, t), direction, castBuffer, distance, mask, QueryTriggerInteraction.Ignore);
						break;
					case CapsuleCollider capsule:
						{
							ResolveCapsule(capsule, t, origin, out Vector3 point0, out Vector3 point1, out float radius);
							count = physicsScene.CapsuleCast(point0, point1, radius, direction, castBuffer, distance, mask, QueryTriggerInteraction.Ignore);
						}
						break;
					case BoxCollider box:
						count = physicsScene.BoxCast(origin, WorldHalfExtents(box, t), direction, castBuffer, t.rotation, distance, mask, QueryTriggerInteraction.Ignore);
						break;
					default:
						count = physicsScene.Raycast(origin, direction, castBuffer, distance, mask, QueryTriggerInteraction.Ignore);
						break;
				}

				// The shared grow-on-full helper, for the same reason as the overlap above.
				if (!TargetOrdering.TryGrowQueryBuffer(ref castBuffer, count))
				{
					break;
				}
			}

			for (int i = 0; i < count; ++i)
			{
				RaycastHit hit = castBuffer[i];
				if (!Accept(hit.collider, t))
				{
					continue;
				}
				gathered.Add(new AbilitySweepHit(hit.collider, hit.point, hit.normal, hit.distance,
					ToBodyLocal(hit.collider, hit.point)));
			}
		}

		/// <summary>
		/// Whether a raw query result belongs in the hit set.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>The object never hits itself.</b> A collision callback could not report a body against
		/// itself; a query has no such notion, and the shape sits exactly where the sweep begins, so
		/// its own collider comes back from the overlap every single tick. Children are excluded with
		/// it — a prefab is free to hang its hitbox off a child, and both would otherwise appear.
		/// </para>
		/// <para>
		/// <b>And never twice.</b> The overlap and the cast can both report the same collider, and
		/// the overlap's record is the one kept: where a cast reports something it started inside it
		/// does so with a zero distance and a zero normal, which is strictly less information than
		/// the overlap already gathered.
		/// </para>
		/// </remarks>
		private static bool Accept(Collider hit, Transform shapeTransform)
		{
			if (hit == null || hit.transform.IsChildOf(shapeTransform))
			{
				return false;
			}

			for (int i = 0; i < gathered.Count; ++i)
			{
				if (gathered[i].Collider == hit)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// Expresses a world impact point in the hit body's own space, while the query's world is
		/// still standing.
		/// </summary>
		/// <remarks>
		/// Resolved through <see cref="TargetOrdering.ResolveHitRoot"/> — the same walk every other
		/// hit-resolving path uses — so a character rigged with a child hitbox produces a point
		/// relative to the CHARACTER rather than to the bone that happened to be struck, and an
		/// authored shield volume means one thing whatever the rig.
		/// </remarks>
		private static Vector3 ToBodyLocal(Collider hit, Vector3 worldPoint)
		{
			GameObject root = TargetOrdering.ResolveHitRoot(hit, out FishMMO.Shared.Core.ICharacter _);
			return root != null ? root.transform.InverseTransformPoint(worldPoint) : Vector3.zero;
		}

		/// <summary>Writes <see cref="gathered"/> into <paramref name="results"/> in sweep order.</summary>
		private static int Order(List<AbilitySweepHit> results)
		{
			int count = gathered.Count;
			if (count == 0)
			{
				return 0;
			}

			ranks.Clear();
			for (int i = 0; i < count; ++i)
			{
				AbilitySweepHit hit = gathered[i];
				ranks.Add(TargetOrdering.Rank(i, hit.Collider.gameObject, hit.Distance));
			}

			/* Distance first — what the object reached first is what it hits first, and that is the
			 * only reading a pierce can act on. Ties fall through to TargetOrdering's identity keys,
			 * which every peer computes to the same value; the buffer order they replace does not
			 * survive between two runs, let alone between two peers. */
			TargetOrdering.SortByDistance(ranks);

			for (int i = 0; i < ranks.Count; ++i)
			{
				results.Add(gathered[ranks[i].Index]);
			}

			gathered.Clear();
			ranks.Clear();
			return results.Count;
		}

		/// <summary>
		/// The shape's centre, expressed as a world offset from its transform's own position.
		/// </summary>
		/// <remarks>
		/// The transform is already at the end of the segment, so the sweep cannot read the centre
		/// off it directly — it needs the offset to re-apply at the start position.
		/// </remarks>
		public static Vector3 CenterOffset(Collider shape, Transform t)
		{
			switch (shape)
			{
				case SphereCollider sphere:
					return t.TransformPoint(sphere.center) - t.position;
				case CapsuleCollider capsule:
					return t.TransformPoint(capsule.center) - t.position;
				case BoxCollider box:
					return t.TransformPoint(box.center) - t.position;
				default:
					return Vector3.zero;
			}
		}

		/// <summary>
		/// A sphere collider's world radius, scaled the way Unity scales one: by the largest
		/// component of the lossy scale, so a non-uniformly scaled sphere stays a sphere.
		/// </summary>
		public static float WorldRadius(SphereCollider sphere, Transform t)
		{
			Vector3 scale = Abs(t.lossyScale);
			return sphere.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
		}

		/// <summary>A box collider's world half extents.</summary>
		public static Vector3 WorldHalfExtents(BoxCollider box, Transform t)
		{
			return Vector3.Scale(box.size * 0.5f, Abs(t.lossyScale));
		}

		/// <summary>
		/// The two sphere centres and the radius that describe a capsule collider in world space,
		/// centred on <paramref name="center"/>.
		/// </summary>
		/// <remarks>
		/// Radius scales by the larger of the two axes across the capsule and height by the axis
		/// along it, which is what Unity does; and the height is floored at a full diameter, because
		/// a capsule authored shorter than that is a sphere and a negative half-segment would put the
		/// two centres the wrong way round.
		/// </remarks>
		public static void ResolveCapsule(
			CapsuleCollider capsule, Transform t, Vector3 center,
			out Vector3 point0, out Vector3 point1, out float radius)
		{
			Vector3 scale = Abs(t.lossyScale);
			Vector3 axis;
			float radiusScale;
			float heightScale;

			switch (capsule.direction)
			{
				case 0:
					axis = t.right;
					radiusScale = Mathf.Max(scale.y, scale.z);
					heightScale = scale.x;
					break;
				case 1:
					axis = t.up;
					radiusScale = Mathf.Max(scale.x, scale.z);
					heightScale = scale.y;
					break;
				default:
					axis = t.forward;
					radiusScale = Mathf.Max(scale.x, scale.y);
					heightScale = scale.z;
					break;
			}

			radius = capsule.radius * radiusScale;
			float height = Mathf.Max(capsule.height * heightScale, radius * 2f);
			float halfSegment = height * 0.5f - radius;

			point0 = center + axis * halfSegment;
			point1 = center - axis * halfSegment;
		}

		private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
	}
}
