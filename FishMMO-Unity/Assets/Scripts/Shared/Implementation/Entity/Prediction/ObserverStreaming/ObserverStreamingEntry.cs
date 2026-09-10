using System.Collections.Generic;
using FishNet.Component.Observing;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Observing;
using FishNet.Transporting;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// One registered character in <see cref="ObserverStreamingRegistry"/>: the per-observer send
	/// intervals the scheduler assigned it, the range it is currently visible from, and the
	/// cached relevance inputs (combat, party, guild) used to rank it for each viewer.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Doubles as the object's <see cref="IObserverSendFilter"/>. <see cref="ShouldSend"/> runs
	/// once per observer per unreliable RPC, so it is a dictionary lookup and a modulo — nothing
	/// that allocates or walks a collection.
	/// </para>
	/// <para>
	/// Two things can slow an observer down: the viewer's full-rate cap (this entry's own
	/// intervals, assigned by <see cref="ObserverStreamingRegistry"/>) and the observer's distance
	/// from the object (<see cref="NetworkTransformDistanceLod"/> on the same GameObject). They
	/// are combined by taking the <b>larger</b> interval, never by gating one behind the other:
	/// two independent modulo gates only coincide once in N×M ticks and would starve the observer.
	/// An engaged observer is exempt from both — except from the engaged-overflow interval, which is
	/// the bound on that exemption; see <see cref="ResolveEffectiveInterval"/>.
	/// </para>
	/// </remarks>
	public sealed class ObserverStreamingEntry : IObserverSendFilter
	{
		/// <summary>The character's network object.</summary>
		public NetworkObject NetworkObject { get; }

		/// <summary>The character.</summary>
		public ICharacter Character { get; }

		/// <summary>True for player characters, which are the only viewers the cap is computed for.</summary>
		public bool IsPlayer { get; }

		/// <summary>
		/// What kind of entity this is, read from its <see cref="ClassifiedDistanceCondition"/>.
		/// </summary>
		/// <remarks>
		/// Objects whose distance condition is a plain FishNet <c>DistanceCondition</c>, or which have
		/// none at all, report <see cref="ObserverClassification.Monster"/> and fall back to the shared
		/// <c>ObserverStreamingPolicy.VisibilityBudget</c> — the behaviour of the whole system before
		/// classifications existed.
		/// </remarks>
		public ObserverClassification Classification =>
			classifiedCondition != null ? classifiedCondition.Classification : ObserverClassification.Monster;

		/// <summary>
		/// The bucket this object is ranked in: its classification, or
		/// <see cref="UnclassifiedRankBucket"/> when it has none.
		/// </summary>
		/// <remarks>
		/// Distinct from <see cref="Classification"/> on purpose. An unclassified object is measured
		/// against the shared <c>ObserverStreamingPolicy.VisibilityBudget</c>, so counting it in the
		/// Monster bucket would push every real monster's rank up against the Monster budget while
		/// judging the unclassified object itself against a different number.
		/// </remarks>
		public int RankBucket => classifiedCondition != null ? (int)classifiedCondition.Classification : UnclassifiedRankBucket;

		/// <summary>Rank bucket for objects with no classification; never a valid enum value.</summary>
		public const int UnclassifiedRankBucket = -1;

		/// <summary>
		/// True when this object declares its own classification, and so is ranked and budgeted
		/// against its own kind rather than against the shared pool.
		/// </summary>
		public bool HasClassification => classifiedCondition != null;

		/// <summary>
		/// How many objects of this kind one viewer may observe. 0 means unlimited.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Per classification, which is what stops one crowd squeezing out another. Under a single
		/// shared budget the failures were not hypothetical: forty players in a town square evicted the
		/// banker they were queueing for, because a stationary service NPC scored below every living
		/// thing near it and nothing distinguished the two costs.
		/// </para>
		/// <para>
		/// A Titan is authored at 0 — unlimited — which is what "always visible when in the scene"
		/// means here: its range is the only thing that ever hides it.
		/// </para>
		/// </remarks>
		public int VisibilityBudget =>
			classifiedCondition != null
				? classifiedCondition.VisibilityBudget
				: ObserverStreamingPolicy.VisibilityBudget;

		/// <summary>The configured range from the prefab's distance condition, before density scaling.</summary>
		public float BaseRange { get; }

		/// <summary>The range most recently applied to the distance condition.</summary>
		public float AppliedRange { get; private set; }

		/// <summary>
		/// Longest range among this character's known abilities, cached for the pass. Drives how far
		/// out its own view must be tick-exact; see <c>ObserverStreamingPolicy.ResolveEngagementRange</c>.
		/// </summary>
		public float LongestAbilityRange { get; private set; }

		/// <summary>
		/// Object id of the character this one currently targets, or 0. Pinned into its visibility
		/// budget so a fight cannot despawn its own target.
		/// </summary>
		public long CurrentTargetObjectId { get; private set; }

		/// <summary>Party id cached for the current scheduling pass; 0 when none.</summary>
		public long PartyID { get; private set; }

		/// <summary>Guild id cached for the current scheduling pass; 0 when none.</summary>
		public long GuildID { get; private set; }

		/// <summary>Combat state cached for the current scheduling pass.</summary>
		public bool InCombat { get; private set; }

		/// <summary>
		/// True when <paramref name="otherCharacterID"/> has contributed to this character's current
		/// combat — that is, when the two are actually fighting each other.
		/// </summary>
		/// <remarks>
		/// Read live rather than cached with the rest of the pass state: it is a question about a
		/// PAIR, so there is nothing to cache on one entry, and the underlying lookup is a
		/// dictionary probe on a set holding a handful of attackers.
		/// </remarks>
		/// <param name="otherCharacterID">The other character's id.</param>
		public bool IsFoughtBy(long otherCharacterID)
		{
			return damageController != null && damageController.HasCombatContributor(otherCharacterID);
		}

		/// <summary>World position cached for the current scheduling pass.</summary>
		public Vector3 Position { get; private set; }

		/// <summary>
		/// Distance to the nearest PLAYER in this object's scene as of the last scheduling pass, or
		/// <see cref="NoViewerDistance"/> when there is none.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A real proximity number, not an observer-membership one: the registry measures it before
		/// the range filter, so a character the visibility budget has evicted from every viewer still
		/// reports how far away the nearest player actually is. That distinction is what
		/// <c>AIController</c> needs — observer membership is a bandwidth decision, and using it as
		/// "is anybody near" let a budget eviction full-heal a monster with a player standing next
		/// to it.
		/// </para>
		/// <para>
		/// Only valid when <see cref="HasViewerMeasurement"/> is true. An object registered between
		/// two passes has never been measured, and "no measurement" must never be read as "nobody
		/// is near".
		/// </para>
		/// </remarks>
		public float NearestViewerDistance { get; private set; } = NoViewerDistance;

		/// <summary>Value of <see cref="NearestViewerDistance"/> when no player was in the scene.</summary>
		public const float NoViewerDistance = float.PositiveInfinity;

		/// <summary>True once a scheduling pass has measured <see cref="NearestViewerDistance"/>.</summary>
		public bool HasViewerMeasurement { get; private set; }

		/// <summary>
		/// Offers one viewer's distance to this object; the smallest offered in a pass is kept.
		/// </summary>
		/// <remarks>
		/// Called for every (viewer, object) pair the registry considers, BEFORE the range filter,
		/// which is what makes the result a proximity measurement rather than a restatement of who
		/// can currently see whom.
		/// </remarks>
		/// <param name="distance">Distance from a player viewer to this object.</param>
		internal void NoteViewerDistance(float distance)
		{
			if (distance < NearestViewerDistance)
			{
				NearestViewerDistance = distance;
			}
		}

		/// <summary>Number of viewers this entry is currently rate limited for.</summary>
		public int LimitedObserverCount => intervalsByClientId.Count;

		/// <summary>
		/// Why a per-observer interval was assigned. The engagement exemption honours one of these
		/// and not the other, which is the whole reason the origin is recorded.
		/// </summary>
		public enum IntervalOrigin : byte
		{
			/// <summary>No interval assigned; the observer is at full rate as far as the cap is concerned.</summary>
			None = 0,

			/// <summary>
			/// Assigned by the viewer's relevance cap. Scored by combat, party, guild and proximity,
			/// knowing nothing about whether the observer can reach the object, so an engaged observer
			/// is exempt from it.
			/// </summary>
			RelevanceCap = 1,

			/// <summary>
			/// Assigned because the viewer is already streaming
			/// <c>ObserverStreamingPolicy.EngagedFullRateBudget</c> engaged characters at full rate.
			/// This IS the bound on the engagement exemption, so the exemption must not cancel it.
			/// </summary>
			EngagedOverflow = 2,
		}

		/// <summary>One observer's assigned interval and which policy assigned it.</summary>
		private struct CapInterval
		{
			public byte Interval;
			public IntervalOrigin Origin;
		}

		private readonly DistanceCondition distanceCondition;
		private readonly ClassifiedDistanceCondition classifiedCondition;
		private readonly NetworkTransformDistanceLod distanceLod;
		private readonly Dictionary<int, CapInterval> intervalsByClientId = new Dictionary<int, CapInterval>();

		/// <summary>
		/// Observers this entry has already sent to since they became observers. See
		/// <see cref="ShouldSend"/> for what the first send is exempt from, and
		/// <see cref="ForgetDepartedObservers"/> for how a re-admitted observer gets the exemption back.
		/// </summary>
		private readonly HashSet<int> servedClientIds = new HashSet<int>();

		/// <summary>Pass-scoped scratch, shared across entries: the client ids currently observing one object.</summary>
		private static readonly HashSet<int> observingScratch = new HashSet<int>();

		/// <summary>Pass-scoped scratch, shared across entries: served ids whose observer has left.</summary>
		private static readonly List<int> departedScratch = new List<int>();
		private IPartyController partyController;
		private IGuildController guildController;
		private ICharacterDamageController damageController;
		private IAbilityController abilityController;
		private ITargetController targetController;
		private bool behavioursResolved;

		public ObserverStreamingEntry(NetworkObject networkObject, ICharacter character)
		{
			NetworkObject = networkObject;
			Character = character;
			IsPlayer = character is IPlayerCharacter;

			/* Scanned rather than fetched with GetObserverCondition<DistanceCondition>(): that
			 * helper matches on GetType() == typeof(T), an EXACT comparison, so it returns null for
			 * the ClassifiedDistanceCondition subclass the project actually authors. Reading the
			 * cloned list directly finds either one. */
			distanceCondition = FindDistanceCondition(networkObject);
			classifiedCondition = distanceCondition as ClassifiedDistanceCondition;
			BaseRange = distanceCondition != null ? distanceCondition.GetMaximumDistance() : 0f;
			AppliedRange = BaseRange;
			Position = networkObject.transform.position;
			distanceLod = networkObject.GetComponent<NetworkTransformDistanceLod>();
		}

		/// <summary>
		/// The object's distance condition, whatever concrete type it is.
		/// </summary>
		/// <remarks>
		/// The conditions are cloned per object during <c>NetworkObject.Preinitialize</c>, which runs
		/// before <c>OnStartServer</c> builds this entry, so the list is complete when it is read.
		/// </remarks>
		private static DistanceCondition FindDistanceCondition(NetworkObject networkObject)
		{
			return FindClassifiedCondition(networkObject) ?? FindPlainDistanceCondition(networkObject);
		}

		/// <summary>
		/// The object's <see cref="ClassifiedDistanceCondition"/>, or null. Allocation-free, so the
		/// registry can ask it on every timed evaluation of every unclassified scene object without
		/// building an entry it is about to throw away.
		/// </summary>
		internal static ClassifiedDistanceCondition FindClassifiedCondition(NetworkObject networkObject)
		{
			if (networkObject == null || networkObject.NetworkObserver == null)
			{
				return null;
			}
			IReadOnlyList<ObserverCondition> conditions = networkObject.NetworkObserver.ObserverConditions;
			for (int i = 0; i < conditions.Count; ++i)
			{
				if (conditions[i] is ClassifiedDistanceCondition found)
				{
					return found;
				}
			}
			return null;
		}

		private static DistanceCondition FindPlainDistanceCondition(NetworkObject networkObject)
		{
			if (networkObject.NetworkObserver == null)
			{
				return null;
			}
			foreach (ObserverCondition condition in networkObject.NetworkObserver.ObserverConditions)
			{
				if (condition is DistanceCondition found)
				{
					return found;
				}
			}
			return null;
		}

		/// <summary>Test seam: builds an entry with an explicit distance LOD (or none).</summary>
		internal ObserverStreamingEntry(NetworkObject networkObject, ICharacter character, NetworkTransformDistanceLod distanceLod)
			: this(networkObject, character)
		{
			this.distanceLod = distanceLod;
		}

		/// <summary>True when a <see cref="NetworkTransformDistanceLod"/> also shapes this object's sends.</summary>
		public bool HasDistanceLod => distanceLod != null;

		/// <summary>True when this character's visibility range can be changed at runtime.</summary>
		public bool HasDistanceCondition => distanceCondition != null;

		/// <summary>
		/// Refreshes the cached relevance inputs. Called once per entry per scheduling pass so
		/// the O(viewers × entries) ranking below reads fields, not behaviours.
		/// </summary>
		public void RefreshForPass()
		{
			if (!behavioursResolved)
			{
				/* Character is null for entries the budget tracks but nobody plays — a dropped
				 * sword, a waypoint. They have a position and a classification and nothing else,
				 * so every relevance input below stays at its zero value and they rank purely by
				 * proximity, which is the only thing that distinguishes one from another. */
				if (Character != null)
				{
					Character.TryGet(out partyController);
					Character.TryGet(out guildController);
					Character.TryGet(out damageController);
					Character.TryGet(out abilityController);
					Character.TryGet(out targetController);
				}
				behavioursResolved = true;
			}

			PartyID = partyController != null ? partyController.ID : 0;
			GuildID = guildController != null ? guildController.ID : 0;
			InCombat = damageController != null && damageController.IsInCombat;
			LongestAbilityRange = abilityController is AbilityController abilities ? abilities.LongestKnownAbilityRange : 0f;

			CurrentTargetObjectId = 0;
			if (targetController != null)
			{
				/* The client-reported frame first — it is the only thing that knows what the
				 * player is LOOKING at. The server's own Current is cast-scoped (written on
				 * ability acquisition, cleared by a miss), so before this the target pin did not
				 * exist for an opponent the player had selected but not yet landed a cast on, and
				 * the budget could evict them at the exact moment of engagement. The fallback
				 * keeps NPCs (no reporting client) pinning their acquisition targets as before.
				 * The report is server-verified on receipt and the pin is range-bounded at use,
				 * so a forged id buys nothing — see TargetSelectionBroadcast. */
				if (targetController.HasClientSelectedTarget)
				{
					CurrentTargetObjectId = targetController.ClientSelectedTargetObjectId;
				}
				else
				{
					Transform target = targetController.Current.Target;
					if (target != null && target.TryGetComponent(out NetworkObject targetObject))
					{
						CurrentTargetObjectId = targetObject.ObjectId;
					}
				}
			}

			Position = NetworkObject.transform.position;

			/* Reset before the pass ranks anything, and mark the measurement valid here rather than
			 * after ranking: a scene with no players in it produces no viewer loop at all, and that
			 * is a measurement — "nobody is near" — not a missing one. */
			NearestViewerDistance = NoViewerDistance;
			HasViewerMeasurement = true;

			ForgetDepartedObservers();
		}

		/// <summary>
		/// Drops first-send bookkeeping for connections that are no longer observers, so a connection
		/// re-admitted later — a range re-entry, a budget re-admit, a scene load boundary — is exempt
		/// again on its first packet.
		/// </summary>
		/// <remarks>
		/// Once per scheduling pass, the cadence the rest of this entry is refreshed on. Shared static
		/// scratch collections rather than per-entry ones: this runs for every entry in the pass and
		/// must not allocate. Same idiom as <c>NetworkTransformDistanceLod.departed</c>.
		/// </remarks>
		private void ForgetDepartedObservers()
		{
			if (servedClientIds.Count < 1)
			{
				return;
			}

			HashSet<NetworkConnection> observers = NetworkObject.Observers;
			observingScratch.Clear();
			if (observers != null)
			{
				foreach (NetworkConnection observer in observers)
				{
					if (observer != null)
					{
						observingScratch.Add(observer.ClientId);
					}
				}
			}

			departedScratch.Clear();
			foreach (int clientId in servedClientIds)
			{
				if (!observingScratch.Contains(clientId))
				{
					departedScratch.Add(clientId);
				}
			}
			for (int i = 0; i < departedScratch.Count; ++i)
			{
				servedClientIds.Remove(departedScratch[i]);
			}
		}

		/// <summary>
		/// Applies a density-scaled range to the distance condition when it differs from the
		/// applied one by at least <see cref="ObserverStreamingPolicy.RangeChangeThreshold"/>.
		/// The timed observer rebuild picks the new distance up on its next cycle.
		/// </summary>
		/// <returns>True when the range was changed.</returns>
		public bool ApplyRange(float range)
		{
			if (distanceCondition == null)
			{
				return false;
			}
			if (Mathf.Abs(range - AppliedRange) < ObserverStreamingPolicy.RangeChangeThreshold)
			{
				return false;
			}
			AppliedRange = range;
			distanceCondition.SetMaximumDistance(range);
			return true;
		}

		/// <summary>Forgets every per-observer interval; every observer is back at full rate.</summary>
		public void ClearIntervals()
		{
			intervalsByClientId.Clear();
		}

		/// <summary>
		/// Sets the send interval for one observer as a RELEVANCE CAP interval. An interval of 1
		/// removes the limit.
		/// </summary>
		public void SetInterval(NetworkConnection connection, byte interval)
		{
			SetInterval(connection, interval, IntervalOrigin.RelevanceCap);
		}

		/// <summary>
		/// Sets the send interval for one observer and records which policy assigned it. An interval
		/// of 1, or an origin of <see cref="IntervalOrigin.None"/>, removes the limit.
		/// </summary>
		public void SetInterval(NetworkConnection connection, byte interval, IntervalOrigin origin)
		{
			if (connection == null)
			{
				return;
			}
			if (interval <= 1 || origin == IntervalOrigin.None)
			{
				intervalsByClientId.Remove(connection.ClientId);
			}
			else
			{
				intervalsByClientId[connection.ClientId] = new CapInterval { Interval = interval, Origin = origin };
			}
		}

		/// <summary>Send interval assigned to an observer by the viewer cap; 1 when unlimited.</summary>
		public byte GetInterval(NetworkConnection connection)
		{
			return connection != null && intervalsByClientId.TryGetValue(connection.ClientId, out CapInterval assigned) ? assigned.Interval : (byte)1;
		}

		/// <summary>Which policy assigned this observer's interval; <see cref="IntervalOrigin.None"/> when none did.</summary>
		public IntervalOrigin GetIntervalOrigin(NetworkConnection connection)
		{
			return connection != null && intervalsByClientId.TryGetValue(connection.ClientId, out CapInterval assigned) ? assigned.Origin : IntervalOrigin.None;
		}

		/// <summary>
		/// Send interval an observer actually receives, composing the cap interval, the distance LOD
		/// interval and the engagement exemption.
		/// </summary>
		public byte GetEffectiveInterval(NetworkConnection connection)
		{
			return ResolveEffectiveInterval(
				GetInterval(connection),
				GetIntervalOrigin(connection),
				distanceLod != null ? distanceLod.GetInterval(connection) : (byte)1,
				distanceLod != null && distanceLod.IsEngaged(connection));
		}

		/// <summary>
		/// The composition rule, as a pure function so it is testable without a NetworkManager.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Truth table:
		/// <list type="table">
		/// <item><description>not engaged, any origin → <c>max(cap, lod)</c>. The two policies
		/// compose by taking the LARGER interval, never by gating one behind the other: two
		/// independent modulo gates coincide once in N×M ticks and would starve the
		/// observer.</description></item>
		/// <item><description>engaged, origin <see cref="IntervalOrigin.None"/> or
		/// <see cref="IntervalOrigin.RelevanceCap"/> → 1. This is the exemption: the cap is scored by
		/// relevance and knows nothing about distance, so without it a character standing next to its
		/// attacker could be throttled to every 2nd tick and lag compensation would rewind to a pose
		/// that was never rendered.</description></item>
		/// <item><description>engaged, origin <see cref="IntervalOrigin.EngagedOverflow"/> → the cap
		/// interval. That interval IS the bound on the exemption — assigned precisely because the
		/// viewer already has <c>EngagedFullRateBudget</c> engaged characters at full rate — so
		/// exempting it cancelled the budget that assigned it. While the origin was not recorded,
		/// <c>EngagedFullRateBudget</c> and <c>EngagedOverflowInterval</c> had no runtime effect at
		/// all: the registry assigned the overflow interval and this method discarded it on the same
		/// engagement test the registry had just used, so thirty players inside 40 m were still thirty
		/// full-rate streams and <c>LastPassLimitedPairs</c> counted throttles that never
		/// happened.</description></item>
		/// </list>
		/// </para>
		/// <para>
		/// The LOD interval is not composed into the engaged rows because the LOD exempts engaged
		/// observers itself (<c>NetworkTransformDistanceLod.BandObserver</c> writes an interval of 1
		/// for them), so it is already 1 in both of them.
		/// </para>
		/// </remarks>
		/// <param name="capInterval">Interval the viewer cap assigned; 1 for none.</param>
		/// <param name="capOrigin">Which policy assigned <paramref name="capInterval"/>.</param>
		/// <param name="lodInterval">Interval the distance LOD assigned; 1 for none.</param>
		/// <param name="engaged">True when the observer is inside its own engagement radius of this object.</param>
		public static byte ResolveEffectiveInterval(byte capInterval, IntervalOrigin capOrigin, byte lodInterval, bool engaged)
		{
			if (engaged)
			{
				return capOrigin == IntervalOrigin.EngagedOverflow && capInterval > 1 ? capInterval : (byte)1;
			}
			return capInterval > lodInterval ? capInterval : lodInterval;
		}

		/// <inheritdoc/>
		public bool ShouldSend(NetworkObject networkObject, NetworkConnection connection, Channel channel)
		{
			if (connection == null)
			{
				return true;
			}

			/* The first send to a new observer is never shaped.
			 *
			 * The receiver's previous goal is the reliable spawn baseline, whose tick is 0, and
			 * NetworkTransform.GetTickDifference reads a zero predecessor as exactly ONE tick of
			 * motion. Skip the first packet and the next one carries N ticks of motion to be played
			 * in one — the same lurch the _observersRpcSettled latch was added to remove, except that
			 * latch is per BEHAVIOUR and is re-armed only by a reliable send or ResetState, never by
			 * an observer being ADDED. Claimed on unreliable sends only: the reliable baseline is what
			 * the exemption is measured FROM, so counting it would spend the exemption before the
			 * first unreliable packet needed it. Released again when the observer leaves — see
			 * ForgetDepartedObservers. */
			bool isOwner = connection == networkObject.Owner;
			bool firstSend = !isOwner && channel == Channel.Unreliable && servedClientIds.Add(connection.ClientId);

			/* Reliable sends are never shaped: the settle after a stop must reach everyone. FishNet
			 * only consults the filter for unreliable RPCs, but the contract is enforced inside
			 * ShouldSendToObserver too so a caller invoking this directly gets the same answer.
			 *
			 * The owner always hears about its own character; only spectators are shaped. (A
			 * NetworkTransform that its owner would discard is already excluded before the filter
			 * runs — see NetworkBehaviour.ExcludeOwnerFromUnbufferedObserversRpcs.) */
			return ObserverStreamingPolicy.ShouldSendToObserver(
				channel,
				isOwner,
				firstSend,
				GetEffectiveInterval(connection),
				networkObject.TimeManager != null ? networkObject.TimeManager.LocalTick : 0u,
				connection.ClientId);
		}
	}
}
