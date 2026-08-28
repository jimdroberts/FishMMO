using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// One candidate's sort keys, detached from the GameObject it came from.
	/// </summary>
	/// <remarks>
	/// The selectors keep their candidates in a parallel list and sort these instead, so every
	/// ranking rule in the target system is a pure function of numbers and can be exercised without
	/// a NetworkManager, a physics scene, or a live character.
	/// </remarks>
	public readonly struct TargetRank
	{
		/// <summary>Index of the candidate in the caller's parallel list.</summary>
		public readonly int Index;

		/// <summary>
		/// The candidate's <see cref="NetworkObject.ObjectId"/>, or <see cref="int.MaxValue"/> when it
		/// has none.
		/// </summary>
		/// <remarks>
		/// The server assigns this and every peer receives the same value, which is the only reason
		/// it works as a cross-peer sort key. Un-networked scene objects sort last as a group and are
		/// separated by <see cref="NameKey"/>.
		/// </remarks>
		public readonly int ObjectId;

		/// <summary>
		/// Stable hash of the candidate's name, separating un-networked scene objects from one
		/// another in a way both peers compute identically.
		/// </summary>
		public readonly int NameKey;

		/// <summary>Last-resort local tiebreak — the Unity instance id.</summary>
		public readonly int SecondaryKey;

		/// <summary>Distance from the query origin, measured in the same world the query ran in.</summary>
		public readonly float Distance;

		public TargetRank(int index, int objectId, int nameKey, int secondaryKey, float distance)
		{
			Index = index;
			ObjectId = objectId;
			NameKey = nameKey;
			SecondaryKey = secondaryKey;
			Distance = distance;
		}
	}

	/// <summary>
	/// Deterministic ordering, ranking and shape tests shared by every target selector.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why ordering is a correctness concern.</b> <c>OverlapSphere</c> and <c>Raycast</c> fill
	/// their buffers in broadphase order, which is a function of the physics scene's internal state
	/// and is not reproducible between two runs, let alone between two peers. Any selector that then
	/// caps at <c>MaxHits</c>, picks "the first match", or rolls a random index is choosing out of an
	/// unordered set — so the same cast can hit different characters on different runs. Imposing a
	/// total order on the candidates before any of those steps is what makes the outcome a function
	/// of the world rather than of the broadphase.
	/// </para>
	/// <para>
	/// <b>Total order, not merely a stable sort.</b> Every comparator here ends in the candidate's
	/// own index, so no two entries ever compare equal. That means the result does not depend on
	/// whether the sort algorithm happens to be stable — <see cref="List{T}.Sort"/> is not.
	/// </para>
	/// </remarks>
	public static class TargetOrdering
	{
		/// <summary>Sort key for a candidate that carries no <see cref="NetworkObject"/>.</summary>
		public const int UnnetworkedObjectId = int.MaxValue;

		/// <summary>
		/// Builds the sort keys for one candidate GameObject.
		/// </summary>
		/// <param name="index">Index of the candidate in the caller's list.</param>
		/// <param name="candidate">The candidate GameObject. May be null.</param>
		/// <param name="distance">Distance from the query origin.</param>
		public static TargetRank Rank(int index, GameObject candidate, float distance)
		{
			int objectId = UnnetworkedObjectId;
			int nameKey = 0;
			int secondaryKey = 0;

			if (candidate != null)
			{
				nameKey = StableNameKey(candidate.name);
				secondaryKey = candidate.GetInstanceID();
				// GetComponentInParent so a hit on a child collider still resolves to the character's
				// own NetworkObject — otherwise two colliders on the same character would sort as if
				// they were unrelated objects.
				NetworkObject networkObject = candidate.GetComponentInParent<NetworkObject>();
				if (networkObject != null)
				{
					objectId = networkObject.ObjectId;
				}
			}

			return new TargetRank(index, objectId, nameKey, secondaryKey, distance);
		}

		/// <summary>
		/// Orders by network identity: ObjectId, then name, then instance id, then original index.
		/// </summary>
		/// <returns>Negative when <paramref name="a"/> sorts first.</returns>
		public static int CompareStable(TargetRank a, TargetRank b)
		{
			if (a.ObjectId != b.ObjectId)
			{
				return a.ObjectId < b.ObjectId ? -1 : 1;
			}
			if (a.NameKey != b.NameKey)
			{
				return a.NameKey < b.NameKey ? -1 : 1;
			}
			if (a.SecondaryKey != b.SecondaryKey)
			{
				return a.SecondaryKey < b.SecondaryKey ? -1 : 1;
			}
			if (a.Index != b.Index)
			{
				return a.Index < b.Index ? -1 : 1;
			}
			return 0;
		}

		/// <summary>Orders by ascending distance, breaking ties with <see cref="CompareStable"/>.</summary>
		public static int CompareByDistance(TargetRank a, TargetRank b)
		{
			if (a.Distance != b.Distance)
			{
				return a.Distance < b.Distance ? -1 : 1;
			}
			return CompareStable(a, b);
		}

		/// <summary>Applies the identity order in place.</summary>
		public static void SortStable(List<TargetRank> ranks) => ranks?.Sort(CompareStable);

		/// <summary>Applies the distance order in place.</summary>
		public static void SortByDistance(List<TargetRank> ranks) => ranks?.Sort(CompareByDistance);

		/// <summary>
		/// Index into <paramref name="ranks"/> of the closest candidate, ties broken by identity.
		/// </summary>
		/// <returns>-1 when the list is empty.</returns>
		public static int NearestIndex(IReadOnlyList<TargetRank> ranks)
		{
			if (ranks == null || ranks.Count == 0)
			{
				return -1;
			}

			int best = 0;
			for (int i = 1; i < ranks.Count; ++i)
			{
				if (CompareByDistance(ranks[i], ranks[best]) < 0)
				{
					best = i;
				}
			}
			return best;
		}

		/// <summary>
		/// Index into <paramref name="ranks"/> of the furthest candidate, ties broken by identity.
		/// </summary>
		/// <remarks>
		/// Deliberately not "the last entry of a distance sort": that would break ties by picking the
		/// <i>highest</i> identity while <see cref="NearestIndex"/> picks the lowest, so two selectors
		/// pointed at the same equidistant pair would disagree about which one they are talking about.
		/// </remarks>
		/// <returns>-1 when the list is empty.</returns>
		public static int FurthestIndex(IReadOnlyList<TargetRank> ranks)
		{
			if (ranks == null || ranks.Count == 0)
			{
				return -1;
			}

			int best = 0;
			for (int i = 1; i < ranks.Count; ++i)
			{
				if (ranks[i].Distance > ranks[best].Distance ||
					(ranks[i].Distance == ranks[best].Distance && CompareStable(ranks[i], ranks[best]) < 0))
				{
					best = i;
				}
			}
			return best;
		}

		/// <summary>
		/// Number of entries that survive a <paramref name="maxHits"/> cap.
		/// </summary>
		/// <remarks>
		/// A cap is only meaningful once the set it is applied to is ordered; this is separated from
		/// the sort so a test can pin that relationship rather than infer it.
		/// </remarks>
		public static int CappedCount(int count, int maxHits)
		{
			if (count <= 0)
			{
				return 0;
			}
			if (maxHits <= 0)
			{
				return count;
			}
			return count < maxHits ? count : maxHits;
		}

		/// <summary>Truncates an ordered rank list to <paramref name="maxHits"/> entries.</summary>
		public static void ApplyMaxHits(List<TargetRank> ranks, int maxHits)
		{
			if (ranks == null)
			{
				return;
			}
			int keep = CappedCount(ranks.Count, maxHits);
			if (keep < ranks.Count)
			{
				ranks.RemoveRange(keep, ranks.Count - keep);
			}
		}

		/// <summary>
		/// True when <paramref name="targetPosition"/> lies inside a cone of
		/// <paramref name="coneAngleDegrees"/> total spread, opening along <paramref name="forward"/>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>A target standing on the origin is outside every cone.</b> That case is the caster
		/// itself, and the previous formulation selected it whenever the cone was 180&#176; or wider:
		/// the caster-to-caster vector is zero, <c>Vector3.normalized</c> returns zero rather than
		/// throwing, and <c>Acos(Dot(forward, zero)) == Acos(0) == 90&#176;</c> — which passes any
		/// half-angle of 90&#176; or more. A cone is a direction test and a point with no direction
		/// cannot satisfy one.
		/// </para>
		/// <para>
		/// Compared as a dot product against the cosine of the half-angle rather than through
		/// <c>Acos</c>: same answer, no transcendental, and no accumulation of the rounding that makes
		/// an exactly-on-the-edge target land differently on two peers.
		/// </para>
		/// </remarks>
		public static bool IsWithinCone(Vector3 origin, Vector3 forward, Vector3 targetPosition, float coneAngleDegrees)
		{
			if (coneAngleDegrees <= 0f)
			{
				return false;
			}

			Vector3 toTarget = targetPosition - origin;
			if (toTarget.sqrMagnitude < 1e-8f)
			{
				return false;
			}

			// A cone of 360 degrees is a sphere; every direction qualifies and the forward vector
			// stops mattering, including when it is degenerate.
			if (coneAngleDegrees >= 360f)
			{
				return true;
			}

			if (forward.sqrMagnitude < 1e-8f)
			{
				return false;
			}

			float cosHalfAngle = Mathf.Cos(coneAngleDegrees * 0.5f * Mathf.Deg2Rad);
			float dot = Vector3.Dot(forward.normalized, toTarget.normalized);
			return dot >= cosHalfAngle;
		}

		/// <summary>
		/// Orders a filled <c>OverlapSphere</c> buffer by network identity, in place.
		/// </summary>
		/// <remarks>
		/// Applied at the query boundary so every consumer — selectors and the ability actions that
		/// resolve hits without one — sees the same order for the same overlap. Insertion sort
		/// because these buffers hold tens of entries at most and it allocates nothing.
		/// </remarks>
		public static void SortColliders(Collider[] hits, int count)
		{
			if (hits == null || count < 2)
			{
				return;
			}
			if (count > hits.Length)
			{
				count = hits.Length;
			}

			// Keys are resolved once and carried alongside the entries. Recomputing them inside the
			// comparison would put a GetComponentInParent walk on every compare, which is how an
			// ordering fix turns into a per-hit performance regression.
			EnsureKeyBuffers(count);
			for (int i = 0; i < count; ++i)
			{
				TargetRank rank = Rank(i, hits[i] != null ? hits[i].gameObject : null, 0f);
				keyObjectIds[i] = rank.ObjectId;
				keyNames[i] = rank.NameKey;
				keySecondary[i] = rank.SecondaryKey;
				keyDistance[i] = 0f;
			}

			for (int i = 1; i < count; ++i)
			{
				Collider current = hits[i];
				int currentObjectId = keyObjectIds[i];
				int currentName = keyNames[i];
				int currentSecondary = keySecondary[i];

				int j = i - 1;
				while (j >= 0 && CompareKeys(keyObjectIds[j], keyNames[j], keySecondary[j], 0f,
											currentObjectId, currentName, currentSecondary, 0f, false) > 0)
				{
					hits[j + 1] = hits[j];
					keyObjectIds[j + 1] = keyObjectIds[j];
					keyNames[j + 1] = keyNames[j];
					keySecondary[j + 1] = keySecondary[j];
					--j;
				}
				hits[j + 1] = current;
				keyObjectIds[j + 1] = currentObjectId;
				keyNames[j + 1] = currentName;
				keySecondary[j + 1] = currentSecondary;
			}
		}

		/// <summary>
		/// Orders a filled <c>Raycast</c> buffer by distance along the ray, ties by identity.
		/// </summary>
		/// <remarks>
		/// Distance first because a ray is a line and "what it passed through, in order" is the only
		/// reading a pierce or beam effect can act on. Unity guarantees no order at all for the
		/// non-allocating overloads, so without this a two-hit pierce chose its victims arbitrarily.
		/// </remarks>
		public static void SortRaycastHits(RaycastHit[] hits, int count)
		{
			if (hits == null || count < 2)
			{
				return;
			}
			if (count > hits.Length)
			{
				count = hits.Length;
			}

			EnsureKeyBuffers(count);
			for (int i = 0; i < count; ++i)
			{
				TargetRank rank = Rank(i, hits[i].collider != null ? hits[i].collider.gameObject : null, hits[i].distance);
				keyObjectIds[i] = rank.ObjectId;
				keyNames[i] = rank.NameKey;
				keySecondary[i] = rank.SecondaryKey;
				keyDistance[i] = rank.Distance;
			}

			for (int i = 1; i < count; ++i)
			{
				RaycastHit current = hits[i];
				int currentObjectId = keyObjectIds[i];
				int currentName = keyNames[i];
				int currentSecondary = keySecondary[i];
				float currentDistance = keyDistance[i];

				int j = i - 1;
				while (j >= 0 && CompareKeys(keyObjectIds[j], keyNames[j], keySecondary[j], keyDistance[j],
											currentObjectId, currentName, currentSecondary, currentDistance, true) > 0)
				{
					hits[j + 1] = hits[j];
					keyObjectIds[j + 1] = keyObjectIds[j];
					keyNames[j + 1] = keyNames[j];
					keySecondary[j + 1] = keySecondary[j];
					keyDistance[j + 1] = keyDistance[j];
					--j;
				}
				hits[j + 1] = current;
				keyObjectIds[j + 1] = currentObjectId;
				keyNames[j + 1] = currentName;
				keySecondary[j + 1] = currentSecondary;
				keyDistance[j + 1] = currentDistance;
			}
		}

		/// <summary>Reusable key columns, so a per-hit sort allocates nothing.</summary>
		private static int[] keyObjectIds = new int[32];
		private static int[] keyNames = new int[32];
		private static int[] keySecondary = new int[32];
		private static float[] keyDistance = new float[32];

		private static void EnsureKeyBuffers(int count)
		{
			if (keyObjectIds.Length >= count)
			{
				return;
			}
			int size = keyObjectIds.Length;
			while (size < count)
			{
				size *= 2;
			}
			keyObjectIds = new int[size];
			keyNames = new int[size];
			keySecondary = new int[size];
			keyDistance = new float[size];
		}

		/// <summary>
		/// Comparison over raw key columns, used by the in-place buffer sorts where the entries carry
		/// no index of their own (they are being moved) and identity alone must break every tie.
		/// </summary>
		private static int CompareKeys(
			int aObjectId, int aName, int aSecondary, float aDistance,
			int bObjectId, int bName, int bSecondary, float bDistance,
			bool distanceFirst)
		{
			if (distanceFirst && aDistance != bDistance)
			{
				return aDistance < bDistance ? -1 : 1;
			}
			if (aObjectId != bObjectId)
			{
				return aObjectId < bObjectId ? -1 : 1;
			}
			if (aName != bName)
			{
				return aName < bName ? -1 : 1;
			}
			if (aSecondary != bSecondary)
			{
				return aSecondary < bSecondary ? -1 : 1;
			}
			return 0;
		}

		/// <summary>
		/// A hash of a GameObject's name that both peers compute identically.
		/// </summary>
		/// <remarks>
		/// Not <see cref="string.GetHashCode()"/>: that is permitted to be randomised per process, so
		/// using it as a cross-peer sort key would order the same two scene objects differently on the
		/// client and on the server. FNV-1a over the UTF-16 code units has no such freedom.
		/// </remarks>
		public static int StableNameKey(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return 0;
			}
			unchecked
			{
				uint hash = 2166136261u;
				for (int i = 0; i < name.Length; ++i)
				{
					hash = (hash ^ name[i]) * 16777619u;
				}
				return (int)hash;
			}
		}
	}
}
