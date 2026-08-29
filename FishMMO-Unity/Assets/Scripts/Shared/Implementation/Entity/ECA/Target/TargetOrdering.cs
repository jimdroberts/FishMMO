using System.Collections.Generic;
using FishMMO.Logging;
using FishMMO.Shared.Core;
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

		/// <summary>
		/// Tiebreak for un-networked candidates that share a name: a stable hash of the candidate's
		/// authored world position, which both peers load from the same scene. (This used to be the
		/// Unity instance id, which is a per-process number and put two same-named scene objects in a
		/// different order on each peer.)
		/// </summary>
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

		/// <summary>Largest a query buffer is allowed to grow to before results are truncated.</summary>
		/// <remarks>
		/// Matches <c>AbilityObjectSweep</c>'s ceiling, so every query in the project truncates at the
		/// same point rather than at whichever bound its own author picked.
		/// </remarks>
		public const int MaximumQueryBufferSize = 256;

		/// <summary>
		/// Query buffer size for a caller whose authored cap is <paramref name="maxHits"/>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Deliberately larger than the cap. Sizing the buffer at exactly <c>MaxHits</c> makes the
		/// physics broadphase perform the truncation, in its own order, before the caller ever sees
		/// the candidates — so a cap of 5 in a crowd of 20 picked five arbitrary characters and the
		/// deterministic sort that follows had nothing to work with. Querying wide and capping after
		/// the sort is what makes the cap mean "the first five in a defined order".
		/// </para>
		/// <para>
		/// This is the STARTING size, not a limit: it only moves the truncation point from
		/// <c>maxHits</c> up to <c>maxHits * 4</c>. A caller must still grow the buffer through
		/// <see cref="TryGrowQueryBuffer{T}"/> when a query comes back full, or the same failure
		/// returns in a denser crowd.
		/// </para>
		/// <para>
		/// Lives here rather than on <c>TargetSelector</c> because the ability actions resolve hits
		/// without a selector and need the identical rule; it was duplicated inline in
		/// <c>AbilityApplyAreaAction</c> for exactly as long as it lived somewhere only selectors
		/// could reach.
		/// </para>
		/// </remarks>
		public static int QueryBufferSize(int maxHits)
		{
			int cap = Mathf.Max(1, maxHits);
			return Mathf.Clamp(cap * 4, 32, MaximumQueryBufferSize);
		}

		/// <summary>
		/// Doubles a query buffer that came back full, so the caller can re-run the query.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A non-allocating physics query returns at most <c>buffer.Length</c> results and says
		/// nothing about how many it discarded. A full buffer is therefore indistinguishable from an
		/// exactly-full one, and the discarded entries were chosen by the broadphase — so a cap or a
		/// sort applied afterwards is ordering an arbitrary subset. Re-querying into a bigger buffer
		/// is the only way to learn whether anything was lost.
		/// </para>
		/// <para>
		/// Use it as the condition of the query loop:
		/// <code>
		/// int count;
		/// while (true)
		/// {
		///     count = physicsScene.OverlapSphere(centre, radius, hits, mask, QueryTriggerInteraction.UseGlobal);
		///     if (!TargetOrdering.TryGrowQueryBuffer(ref hits, count)) break;
		/// }
		/// </code>
		/// </para>
		/// </remarks>
		/// <typeparam name="T">Buffer element type — <c>Collider</c> or <c>RaycastHit</c>.</typeparam>
		/// <param name="buffer">The buffer, replaced with a larger one when this returns true.</param>
		/// <param name="count">Result count the query just returned.</param>
		/// <returns>True when the buffer grew and the query must be re-run.</returns>
		public static bool TryGrowQueryBuffer<T>(ref T[] buffer, int count)
		{
			if (buffer == null)
			{
				buffer = new T[QueryBufferSize(0)];
				return true;
			}

			if (count < buffer.Length)
			{
				return false;
			}

			if (buffer.Length >= MaximumQueryBufferSize)
			{
				WarnQueryBufferSaturated();
				return false;
			}

			buffer = new T[Mathf.Min(MaximumQueryBufferSize, buffer.Length * 2)];
			return true;
		}

		/// <summary>True once the saturation warning has been issued this session.</summary>
		private static bool warnedQueryBufferSaturated;

		/// <summary>
		/// Reports the one case this module cannot make deterministic, once per session.
		/// </summary>
		/// <remarks>
		/// At <see cref="MaximumQueryBufferSize"/> candidates the broadphase truncates and no
		/// ordering downstream can recover the set it discarded. Warned rather than thrown, because
		/// dropping the whole query would be worse than an arbitrary subset of it — but warned at
		/// all, because this used to be the one failure in the target system with no symptom other
		/// than an ability quietly choosing different victims in a crowd. Once per session: it fires
		/// from a per-tick path and a repeating log would bury the rest of the frame.
		/// </remarks>
		private static void WarnQueryBufferSaturated()
		{
			if (warnedQueryBufferSaturated)
			{
				return;
			}
			warnedQueryBufferSaturated = true;
			Log.Warning("TargetOrdering",
				$"A spatial query filled its {MaximumQueryBufferSize}-entry buffer. Beyond this the physics " +
				"broadphase truncates the candidates in its own order, so any MaxHits cap applied afterwards " +
				"selects from an arbitrary subset. Reduce the query radius or raise " +
				nameof(MaximumQueryBufferSize) + ". Reported once per session.");
		}

		/// <summary>Resets the once-per-session warning. For tests.</summary>
		internal static void ResetQueryBufferWarning() => warnedQueryBufferSaturated = false;

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
				secondaryKey = StablePositionKey(candidate.transform.position);
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
		/// Resolves the body a raw collider hit belongs to, and the character on it if there is one.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A prefab is free to hang its hitbox off a child transform, so the collider a query returns
		/// is frequently not the object anything downstream cares about. The rigidbody's GameObject
		/// where there is one — which is what <c>Collision.gameObject</c> reported back when hits came
		/// from collision callbacks — then a parent walk for a character rigged without one.
		/// </para>
		/// <para>
		/// This is the same resolution <see cref="Rank"/> performs for its <c>ObjectId</c> key, and the
		/// reason both exist is that a bare <c>GetComponent</c> on the collider gets two things wrong
		/// at once: it silently drops a character whose hitbox is a child, and it counts a character
		/// with two colliders twice. <c>AbilityApplyAreaAction</c> had both faults while the sweep next
		/// to it did not, so the two hit-resolving paths disagreed about who was even a candidate.
		/// One implementation is what keeps them honest.
		/// </para>
		/// </remarks>
		/// <param name="collider">The collider a query returned. May be null.</param>
		/// <param name="character">The character that owns it, or null.</param>
		/// <returns>The resolved body, or null when <paramref name="collider"/> is null.</returns>
		public static GameObject ResolveHitRoot(Collider collider, out ICharacter character)
		{
			character = null;
			if (collider == null)
			{
				return null;
			}

			Rigidbody body = collider.attachedRigidbody;
			GameObject root = body != null ? body.gameObject : collider.gameObject;

			if (!root.TryGetComponent(out character))
			{
				character = root.GetComponentInParent<ICharacter>();
			}
			return root;
		}

		/// <summary>
		/// The key a hit should be deduplicated and capped by: the character where there is one,
		/// otherwise the resolved body.
		/// </summary>
		/// <remarks>
		/// Keyed on the character so two hitboxes on one body cost one hit and occupy one slot of a
		/// <c>MaxHits</c> cap. A cap that counts colliders rather than victims means the same ability
		/// hits a different NUMBER of characters depending on how its targets are rigged.
		/// </remarks>
		/// <param name="collider">The collider a query returned.</param>
		/// <param name="character">The character that owns it, or null.</param>
		/// <returns>The dedupe key, or null when <paramref name="collider"/> is null.</returns>
		public static GameObject ResolveHitKey(Collider collider, out ICharacter character)
		{
			GameObject root = ResolveHitRoot(collider, out character);
			return character != null ? character.GameObject : root;
		}

		/// <summary>
		/// The same dedupe key as <see cref="ResolveHitKey"/>, for an object that did not arrive
		/// through a physics query.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Exists so a selector can compare a hit against its own spatial context — "is this candidate
		/// the caster?" — on the same terms it compares two hits against each other. The context is an
		/// <c>EventData.Target</c>, which is a GameObject rather than a collider, and testing it with a
		/// bare reference comparison against <c>hit.gameObject</c> asks a different question: a caster
		/// whose hitbox is a child does not equal its own hitbox, so the self-exclusion in the nearest,
		/// furthest and random selectors let the caster select itself.
		/// </para>
		/// <para>
		/// <see cref="Collider.attachedRigidbody"/> has no GameObject equivalent, so the rigidbody is
		/// found by walking the parents instead. The two agree for anything a query can return: a
		/// collider's attached rigidbody is the nearest one at or above it in the hierarchy.
		/// </para>
		/// </remarks>
		/// <param name="candidate">The object to key. May be null.</param>
		/// <returns>The dedupe key, or null when <paramref name="candidate"/> is null.</returns>
		public static GameObject ResolveObjectKey(GameObject candidate)
		{
			if (candidate == null)
			{
				return null;
			}

			Rigidbody body = candidate.GetComponentInParent<Rigidbody>();
			GameObject root = body != null ? body.gameObject : candidate;

			if (!root.TryGetComponent(out ICharacter character))
			{
				character = root.GetComponentInParent<ICharacter>();
			}
			return character != null ? character.GameObject : root;
		}

		/// <summary>
		/// Drops every candidate whose body is already represented, keeping the first of each.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Run it on a SORTED list, between the sort and the cap.</b> The entry it keeps for a body
		/// is the first one it meets, so on a distance-ordered list that is the body's nearest
		/// collider — the reading every consumer wants — and on an unsorted one it is whichever the
		/// broadphase listed first, which is the arbitrary choice the whole module exists to remove.
		/// </para>
		/// <para>
		/// <b>Why it is needed at all.</b> A prefab may hang several colliders off one body, and a
		/// <c>MaxHits</c> cap applied to the raw hits then counts colliders rather than victims: the
		/// same ability affects a different NUMBER of characters depending on how its targets are
		/// rigged, and a random selection weights a body by its collider count. Static scenery is
		/// untouched by this — a wall with twenty colliders and no rigidbody keys each collider to
		/// itself, so twenty separate candidates is exactly what comes back.
		/// </para>
		/// <para>
		/// A linear scan over what has been kept rather than a set: the list is bounded by the query
		/// buffer, so this allocates nothing and hashes nothing. <see cref="object.ReferenceEquals"/>
		/// rather than <c>==</c>, because Unity overloads equality on <c>Object</c> to ask the engine
		/// whether the native object is still alive — a native crossing per comparison, for a question
		/// that is not open: every operand came out of a query in this same frame.
		/// </para>
		/// </remarks>
		/// <param name="ranks">The ordered rank list, truncated in place.</param>
		/// <param name="keys">
		/// The dedupe key of each candidate, indexed by <see cref="TargetRank.Index"/>. A null key is
		/// treated as its own body and never collapses with another.
		/// </param>
		public static void DedupeByBody(List<TargetRank> ranks, IReadOnlyList<GameObject> keys)
		{
			if (ranks == null || keys == null || ranks.Count < 2)
			{
				return;
			}

			int write = 0;
			for (int read = 0; read < ranks.Count; ++read)
			{
				GameObject key = KeyAt(keys, ranks[read].Index);
				if (key != null)
				{
					bool duplicate = false;
					for (int kept = 0; kept < write; ++kept)
					{
						if (ReferenceEquals(KeyAt(keys, ranks[kept].Index), key))
						{
							duplicate = true;
							break;
						}
					}
					if (duplicate)
					{
						continue;
					}
				}
				ranks[write] = ranks[read];
				++write;
			}

			if (write < ranks.Count)
			{
				ranks.RemoveRange(write, ranks.Count - write);
			}
		}

		/// <summary>Reads a candidate's key, tolerating a rank whose index is out of range.</summary>
		private static GameObject KeyAt(IReadOnlyList<GameObject> keys, int index)
		{
			return index >= 0 && index < keys.Count ? keys[index] : null;
		}

		/// <summary>
		/// True when <paramref name="key"/> is already present in <paramref name="keptKeys"/>.
		/// </summary>
		/// <remarks>
		/// The streaming form of <see cref="DedupeByBody"/>, for a caller that emits as it walks an
		/// already-ordered set rather than truncating a rank list. Same linear scan, and the same
		/// <see cref="object.ReferenceEquals"/> for the same reason.
		/// </remarks>
		public static bool ContainsBody(List<GameObject> keptKeys, GameObject key)
		{
			if (keptKeys == null || key == null)
			{
				return false;
			}
			for (int i = 0; i < keptKeys.Count; ++i)
			{
				if (ReferenceEquals(keptKeys[i], key))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Orders by network identity: ObjectId, then name, then position key, then original index.
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

		/* SortColliders was REMOVED, not left unused.
		 *
		 * It ordered an overlap buffer by network identity at the query boundary, on the theory that
		 * imposing SOME reproducible order there fixed every consumer at once. It does the opposite
		 * for the one operation that actually reads the order: a MaxHits cap. Truncating an
		 * identity-ordered set keeps the lowest ObjectIds — the characters the server happened to
		 * spawn earliest — not the ones nearest the blast, so a 3-target AoE in a crowd of 8 hit the
		 * same three every time and never the ones standing on the impact point. Every selector that
		 * caps already ranked by distance and disagreed with it.
		 *
		 * The deeper problem is that the query boundary cannot know the answer. Distance and identity
		 * are both legitimate orders and only the CALLER knows which one its cap means, which is why
		 * LagCompensatedQuery.OverlapSphere now returns the buffer unordered and every consumer states
		 * its own order. SortRaycastHits survives because a ray genuinely has only one reading —
		 * distance along the line — and Unity's non-allocating overload promises none.
		 *
		 * Leaving this in place as a shorter-looking option is how the next caller re-introduces an
		 * identity-ordered cap. */

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
											currentObjectId, currentName, currentSecondary, currentDistance) > 0)
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
		/// Comparison over raw key columns, used by <see cref="SortRaycastHits"/> where the entries
		/// carry no index of their own (they are being moved) and identity alone must break every tie.
		/// </summary>
		/// <remarks>
		/// Distance always leads. The <c>distanceFirst</c> switch this used to take existed only for
		/// <c>SortColliders</c>, which ordered an overlap by identity and is gone — see the note where
		/// it used to be.
		/// </remarks>
		private static int CompareKeys(
			int aObjectId, int aName, int aSecondary, float aDistance,
			int bObjectId, int bName, int bSecondary, float bDistance)
		{
			if (aDistance != bDistance)
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
		/// Stable hash of a world position at millimetre resolution. Used only to separate
		/// un-networked candidates that share a name.
		/// </summary>
		/// <remarks>
		/// <b>It hashes the LIVE position, not an authored one</b> — the caller reads
		/// <c>candidate.transform.position</c>. For the case this key exists to serve that is the same
		/// thing: un-networked candidates are scene objects, every peer loads them from the same scene,
		/// and they do not move. It is only a total-order tiebreak of last resort, reached when two
		/// candidates share both an ObjectId and a name, so anything networked is separated long before
		/// it gets here. Two un-networked candidates that share a name AND move independently would
		/// order differently on two peers; nothing in the project does that today, and a candidate that
		/// needs a reproducible identity should carry a NetworkObject rather than rely on this.
		/// </remarks>
		public static int StablePositionKey(Vector3 position)
		{
			unchecked
			{
				int x = Mathf.RoundToInt(position.x * 1000f);
				int y = Mathf.RoundToInt(position.y * 1000f);
				int z = Mathf.RoundToInt(position.z * 1000f);
				int hash = (int)2166136261;
				hash = (hash ^ x) * 16777619;
				hash = (hash ^ y) * 16777619;
				hash = (hash ^ z) * 16777619;
				return hash;
			}
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
