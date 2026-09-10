using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// Shows what an observed character is casting on its nameplate.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why observers needed this.</b> The only thing that reached an observer about another
	/// character's activation was the spawned ability object, so a self-buff, a pet summon or any
	/// consumable happened in complete silence: a character stood still, a bar somewhere changed,
	/// and nothing on screen connected the two. <c>CharacterCastBroadcast</c> is sent from the
	/// activation state machine, so it covers every activation whether or not anything spawns.
	/// </para>
	/// <para>
	/// <b>Not for the owner.</b> The server excludes the owner from the broadcast, and this ignores
	/// the local character if one ever arrives: the owner's own cast is predicted and already drawn
	/// by its cast bar, which is ahead of anything the server can say. Drawing a second, later
	/// version of it on the owner's own nameplate is exactly the desync the message is meant to
	/// avoid.
	/// </para>
	/// </remarks>
	public sealed class ClientCastNameplateDisplay
	{
		/// <summary>
		/// The shortest time a cast stays on a nameplate.
		/// </summary>
		/// <remarks>
		/// An instant ability starts and ends inside one tick, so start and stop arrive together
		/// and a plate written and cleared in the same frame shows nothing at all. Holding the row
		/// briefly is what makes an instant cast readable as an event.
		/// </remarks>
		public const float MinimumDwellSeconds = 0.6f;

		/// <summary>
		/// Extra time past a cast's own duration before it is dropped without a stop message.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Not a loss allowance.</b> The stop rides <c>Channel.Reliable</c>, and that is a
		/// correctness requirement rather than a comfort: the receiver keys a stop on the caster, so
		/// an unreliable stop from cast N reordering behind the start of cast N+1 clears the wrong
		/// row — reachable at instant-attack cadence, and pinned by
		/// <c>CastVisibilityTests.TheCastMessageIsReliableAndAStopNamesWhatItEnds</c>. (This used to
		/// say the stop was unreliable and could be lost; it cannot.)
		/// </para>
		/// <para>
		/// What the grace is for is a stop that never EXISTS: a caster that disconnects or is
		/// despawned mid-cast, and a cast that finishes slower than this peer computed because a
		/// speed debuff landed on the caster after the row was opened. Without it such a plate would
		/// read "Casting" until that character left view.
		/// </para>
		/// </remarks>
		public const float ExpiryGraceSeconds = 1.5f;

		/// <summary>What one observed character is currently casting.</summary>
		private struct ActiveCast
		{
			/// <summary>The character's nameplate, so it can be cleared without a lookup.</summary>
			public Nameplate Plate;
			/// <summary>
			/// The caster, so a pooled instance that has since become somebody else is recognised.
			/// </summary>
			/// <remarks>
			/// Characters despawn to the object pool, not to destruction, so the plate stays alive
			/// and Unity's null test on it says nothing. A caster that is no longer spawned, or that
			/// has come back out of the pool under a different object id, is not the character this
			/// row was written for — and its plate must not be touched, because by then it may be
			/// carrying that new character's own cast.
			/// </remarks>
			public NetworkObject Caster;
			/// <summary>What was being cast, so a stop for a different cast is refused.</summary>
			public long ReferenceID;
			/// <summary>Unscaled time the row may first be cleared at.</summary>
			public float EarliestClear;
			/// <summary>Unscaled time the row is dropped at, with no stop message.</summary>
			public float Expiry;
			/// <summary>True once a stop arrived but the dwell had not elapsed.</summary>
			public bool StopPending;
		}

		/// <summary>Casts in flight, keyed by the caster's network object id.</summary>
		private readonly Dictionary<int, ActiveCast> active = new Dictionary<int, ActiveCast>();

		/// <summary>Ids collected for removal during a sweep, reused to avoid allocating.</summary>
		private readonly List<int> expired = new List<int>();

		/// <summary>The network manager this display is registered against.</summary>
		private NetworkManager networkManager;

		/// <summary>Registers the broadcast handler.</summary>
		/// <param name="networkManager">The client's network manager.</param>
		public void Initialize(NetworkManager networkManager)
		{
			if (networkManager == null)
			{
				return;
			}

			Shutdown();
			this.networkManager = networkManager;
			networkManager.ClientManager.RegisterBroadcast<CharacterCastBroadcast>(OnCastBroadcast);
			/* Casts that were ALREADY RUNNING when this client started observing the caster.
			 *
			 * The live message is sent once, on the tick a cast begins, to whoever was observing at
			 * that instant — so walking into range (or being un-culled, which the streaming budget
			 * treats as routine) part-way through a five second cast produced a nameplate that said
			 * nothing, followed by a stop for a cast this display had never heard of. The spawn
			 * payload now carries the running activation and the controller replays it here in the
			 * same shape, so both arrive down one code path and get the same catch-up arithmetic. */
			AbilityController.OnObservedActivationCatchUp += OnCastCatchUp;
		}

		/// <summary>Unregisters and clears every row this display wrote.</summary>
		public void Shutdown()
		{
			AbilityController.OnObservedActivationCatchUp -= OnCastCatchUp;

			if (networkManager != null)
			{
				networkManager.ClientManager.UnregisterBroadcast<CharacterCastBroadcast>(OnCastBroadcast);
				networkManager = null;
			}

			foreach (KeyValuePair<int, ActiveCast> pair in active)
			{
				ClearStatus(pair.Value.Plate);
			}
			active.Clear();
		}

		/// <summary>
		/// Clears rows whose cast has ended or timed out. Call once per frame.
		/// </summary>
		public void Tick()
		{
			if (active.Count == 0)
			{
				return;
			}

			float now = Time.unscaledTime;
			expired.Clear();

			foreach (KeyValuePair<int, ActiveCast> pair in active)
			{
				ActiveCast cast = pair.Value;

				bool dwellElapsed = now >= cast.EarliestClear;
				bool timedOut = now >= cast.Expiry;

				/* A plate destroyed under us is not an error and not something to clear: the
				 * character died and its body was removed, or it left observer range, and the
				 * nameplate went with it. Dropping the row here is what stops the sweep from
				 * touching a destroyed component every frame. */
				if (cast.Plate == null || !CasterStillThis(cast.Caster, pair.Key))
				{
					// Gone, or pooled and reborn as someone else. Nameplate.OnDisable cleared the row.
					expired.Add(pair.Key);
					continue;
				}

				if ((cast.StopPending && dwellElapsed) || timedOut)
				{
					ClearStatus(cast.Plate);
					expired.Add(pair.Key);
				}
			}

			for (int i = 0; i < expired.Count; ++i)
			{
				active.Remove(expired[i]);
			}
		}

		/// <summary>Applies one start or stop.</summary>
		private void OnCastBroadcast(CharacterCastBroadcast msg, Channel channel)
		{
			if (!msg.Started)
			{
				Stop(msg.CasterObjectID, msg.ReferenceID);
				return;
			}

			Start(msg);
		}

		/// <summary>
		/// Applies a cast that was already running when this client began observing its caster.
		/// </summary>
		/// <remarks>
		/// Deliberately the same path a live start takes, rather than a second implementation of it:
		/// the message the controller hands over carries the server tick the activation STARTED on,
		/// which is exactly what <see cref="ElapsedSince"/> needs to open the row part-way through
		/// instead of restarting a cast that may be nearly over. Ignored unless this display is
		/// registered, so a controller spawning before <see cref="Initialize"/> cannot write a row
		/// that nothing would ever sweep.
		/// </remarks>
		private void OnCastCatchUp(CharacterCastBroadcast msg)
		{
			if (networkManager == null || !msg.Started)
			{
				return;
			}
			Start(msg);
		}

		/// <summary>Opens or replaces the row for one starting cast.</summary>
		private void Start(CharacterCastBroadcast msg)
		{
			if (!TryResolveObserved(msg.CasterObjectID, out NetworkObject caster, out Nameplate plate, out AbilityController controller))
			{
				return;
			}

			/* The duration is READ, not received: it is on the template, templates are immutable
			 * and every peer holds the same ones, so sending it would pay per cast per observer for
			 * a number already in memory.
			 *
			 * It is the caster's UNMODIFIED activation time — a haste buff on the caster shortens
			 * the real one and this does not know that. Which is fine for what it is used for: how
			 * long to hold the row when no stop message arrives. The stop is what normally ends it,
			 * and the grace below already absorbs a longer or shorter real cast. */
			float duration = ResolveDuration(msg, controller, out bool durationKnown);

			/* The message spent a network delay in flight and the cast has been running for all of
			 * it. Elapsed is measured from the tick the server stamped, less the interpolation this
			 * client renders its peers behind — the same correction the ability object's spawn
			 * applies, so the row and the projectile agree about when the cast began. A cast whose
			 * whole duration has already passed is not drawn at all rather than started late. */
			float elapsed = ElapsedSince(msg.ServerTick);
			float remaining = duration - elapsed;
			if (duration > 0.0f && remaining <= 0.0f)
			{
				/* Too late to draw, but not too late to matter: this start supersedes whatever the
				 * row said before it, so the previous cast's text must not be left standing — and
				 * NOT through the dwell, which is what used to make that sentence untrue. Stop
				 * honours MinimumDwellSeconds by default, so a row younger than 0.6s was merely
				 * marked StopPending and the previous cast's text stayed on the plate for the rest
				 * of the dwell: the exact outcome this branch exists to prevent. A superseding start
				 * is not a cast ending, it is the row being reassigned, and the dwell protects a row
				 * from vanishing before it can be read rather than from newer truth. */
				Stop(msg.CasterObjectID, referenceID: 0, respectDwell: false);
				return;
			}

			plate.SetLine(NameplateSlot.Status, DescribeCast(msg, controller));

			float now = Time.unscaledTime;
			active[msg.CasterObjectID] = new ActiveCast
			{
				Plate = plate,
				Caster = caster,
				ReferenceID = msg.ReferenceID,
				EarliestClear = now + MinimumDwellSeconds,
				Expiry = ResolveExpiry(now, duration, durationKnown, remaining, HeldAllowance(msg, controller)),
				StopPending = false,
			};
		}

		/// <summary>Marks a cast finished, honouring the minimum dwell by default.</summary>
		/// <param name="casterObjectID">The caster.</param>
		/// <param name="referenceID">
		/// What ended, or zero to end whatever is running. A stop naming a different cast than
		/// the row shows is refused: it belongs to an earlier activation and the row has moved on.
		/// </param>
		/// <param name="respectDwell">
		/// True for an activation that ENDED, where the dwell keeps a row up long enough to be read.
		/// False when the row is being reassigned rather than ended — a start so late that its whole
		/// cast has already elapsed — where deferring the clear leaves the previous cast's text
		/// standing and nothing replaces it.
		/// </param>
		private void Stop(int casterObjectID, long referenceID, bool respectDwell = true)
		{
			if (!active.TryGetValue(casterObjectID, out ActiveCast cast))
			{
				return;
			}

			if (referenceID != 0 && cast.ReferenceID != 0 && cast.ReferenceID != referenceID)
			{
				return;
			}

			if (!respectDwell || Time.unscaledTime >= cast.EarliestClear)
			{
				ClearStatus(cast.Plate);
				active.Remove(casterObjectID);
				return;
			}

			// Too soon to be seen. Tick clears it once the dwell has elapsed.
			cast.StopPending = true;
			active[casterObjectID] = cast;
		}

		/// <summary>
		/// When a row is dropped if no stop message ever arrives for it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>A zero-duration cast expires on the DWELL, not on the grace.</b> The server no longer
		/// sends a stop for an activation that began and ended on one server tick (see
		/// <c>AbilityController.SuppressesRedundantCastStop</c>) — 17 of the 37 authored abilities
		/// are instant, the default attack among them — so for those this is the only thing that ever
		/// clears the row. Sizing it from <see cref="ExpiryGraceSeconds"/> would leave "Casting
		/// Punch" on a plate for 1.5&#160;s per swing, which at auto-attack cadence never comes off
		/// at all. <see cref="MinimumDwellSeconds"/> is the row's whole intended life: long enough to
		/// read an instant cast as an event, short enough not to be believed as an ongoing one.
		/// </para>
		/// <para>
		/// <b>Only when the duration is actually KNOWN to be zero.</b> An ability this peer cannot
		/// resolve — one learned after it started observing, or an NPC's set it never received —
		/// also reports zero, and that one may well be a five second cast whose stop is coming. Those
		/// keep the grace, which is why <see cref="ResolveDuration"/> reports whether it resolved
		/// anything rather than leaving the caller to read zero two ways.
		/// </para>
		/// <para>
		/// A HELD ability keeps the grace too, zero-duration or not: it has a hold allowance, it runs
		/// past its window for as long as the player holds it, and the server always sends its stop.
		/// </para>
		/// </remarks>
		/// <param name="now">Unscaled time the row is being opened at.</param>
		/// <param name="duration">The activation's computed duration in seconds.</param>
		/// <param name="durationKnown">False when the ability could not be resolved at all.</param>
		/// <param name="remaining">Seconds of the activation still to run, which may be negative.</param>
		/// <param name="heldAllowance">Extra seconds a held activation may legitimately run for.</param>
		/// <returns>The unscaled time to drop the row at.</returns>
		public static float ResolveExpiry(float now, float duration, bool durationKnown,
			float remaining, float heldAllowance)
		{
			if (durationKnown && duration <= 0.0f && heldAllowance <= 0.0f)
			{
				return now + MinimumDwellSeconds;
			}
			return now + Mathf.Max(remaining, 0.0f) + heldAllowance + ExpiryGraceSeconds;
		}

		/// <summary>
		/// Drops every row without unregistering. For a world change, where the network manager
		/// survives but every object id in <see cref="active"/> is about to mean something else.
		/// </summary>
		public void Clear()
		{
			foreach (KeyValuePair<int, ActiveCast> pair in active)
			{
				if (CasterStillThis(pair.Value.Caster, pair.Key))
				{
					ClearStatus(pair.Value.Plate);
				}
			}
			active.Clear();
		}

		/// <summary>Whether the caster this row was written for is still the one under that id.</summary>
		private static bool CasterStillThis(NetworkObject caster, int objectID)
		{
			return caster != null && caster.IsSpawned && caster.ObjectId == objectID;
		}

		/// <summary>
		/// Extra time a held ability may legitimately run past its activation window.
		/// </summary>
		/// <remarks>
		/// A charged ability keeps going after <c>remainingTicks</c> hits zero, for up to the cap
		/// <see cref="AbilityController.ComputeMaxHoldTicks"/> describes — twice the activation
		/// time, floored at a second. An expiry sized from the activation time alone dropped the
		/// row two-thirds of the way through a full charge. Granted to every held ability rather
		/// than charged ones alone: a channel cannot outrun its window, so for it this only
		/// lengthens a safety net that the reliable stop makes moot.
		/// </remarks>
		private float HeldAllowance(CharacterCastBroadcast msg, AbilityController controller)
		{
			if (msg.IsConsumable || controller == null || networkManager?.TimeManager == null ||
				!controller.RequiresHeld(msg.ReferenceID) ||
				!controller.TryGetAbilityForVisuals(msg.ReferenceID, out Ability ability))
			{
				return 0.0f;
			}

			float tickDelta = (float)networkManager.TimeManager.TickDelta;
			return AbilityController.ComputeMaxHoldTicks(ability.ActivationTime, tickDelta) * tickDelta;
		}

		/// <summary>
		/// Clears the status row, tolerating a plate Unity has already destroyed.
		/// </summary>
		/// <remarks>
		/// A plain <c>?.</c> is not enough here. A destroyed <c>UnityEngine.Object</c> is not null
		/// to the null-conditional operator — only to Unity's own overloaded comparison — so the
		/// call would reach a destroyed component and throw. That is the ordinary case, not an
		/// exotic one: a character that dies mid-cast loses its body, and its nameplate with it.
		/// </remarks>
		private static void ClearStatus(Nameplate plate)
		{
			if (plate == null)
			{
				return;
			}
			plate.ClearLine(NameplateSlot.Status);
		}

		/// <summary>
		/// How long the activation takes, computed the way the server computed it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Nothing about this is sent. The ability resolves through the caster's own known set — so
		/// a crafted ability reports the activation time of the composition rather than of its base
		/// template — and the haste that shortens it is read off the caster's cast- or attack-speed
		/// ATTRIBUTE through the same <c>CalculateSpeedReduction</c> the server used.
		/// </para>
		/// <para>
		/// The attribute rather than the buff list, deliberately. A buff is one contributor to cast
		/// speed among several — equipment and archetype modify the same attribute — and the
		/// attribute is where they are already summed. Observers receive it: the caster's attribute
		/// controller is fed by <c>CharacterAttributesBroadcast</c>, the same channel that keeps a
		/// peer's health bar current.
		/// </para>
		/// <para>
		/// An ability this peer cannot resolve reports zero AND <paramref name="resolved"/> false.
		/// The two have to be told apart: a genuine zero is an instant cast that will never be
		/// stopped, while an unresolvable one may be a long cast whose stop is still coming. See
		/// <see cref="ResolveExpiry"/>.
		/// </para>
		/// </remarks>
		/// <param name="msg">The activation being drawn.</param>
		/// <param name="controller">The caster's ability controller, or null.</param>
		/// <param name="resolved">True when a template was found and the duration means something.</param>
		private static float ResolveDuration(CharacterCastBroadcast msg, AbilityController controller, out bool resolved)
		{
			if (msg.IsConsumable)
			{
				/* No haste on an item: ConsumableTemplate.ActivationTime is used raw by
				 * TryStartConsumable, with no speed attribute applied. */
				ConsumableTemplate consumable = BaseItemTemplate.Get<ConsumableTemplate>((int)msg.ReferenceID);
				resolved = consumable != null;
				return resolved ? consumable.ActivationTime : 0.0f;
			}

			if (controller == null || !controller.TryGetAbilityForVisuals(msg.ReferenceID, out Ability ability))
			{
				resolved = false;
				return 0.0f;
			}

			resolved = true;
			return ability.ActivationTime *
				controller.CalculateSpeedReduction(controller.GetActivationAttributeTemplate(ability));
		}

		/// <summary>Seconds of the cast that had already elapsed when the message arrived.</summary>
		private float ElapsedSince(uint serverTick)
		{
			if (networkManager?.TimeManager == null || serverTick == 0u)
			{
				return 0.0f;
			}

			// The projectile's own catch-up, so the row and the bolt agree about when a cast began.
			uint ticks = AbilityController.ComputeObserverCatchUpTicks(networkManager.TimeManager, serverTick);
			return ticks * (float)networkManager.TimeManager.TickDelta;
		}

		/// <summary>The row's wording.</summary>
		/// <remarks>
		/// Names what is being cast, because "Casting" alone does not distinguish a heal from a
		/// fireball to somebody deciding whether to interrupt it. The ability is resolved from the
		/// caster's own known set through <c>TryGetAbilityForVisuals</c> — the same lookup the
		/// activation handler uses — so the row names the CRAFTED ability the player built rather
		/// than the base template underneath it.
		/// </remarks>
		private static string DescribeCast(CharacterCastBroadcast msg, AbilityController controller)
		{
			if (msg.IsConsumable)
			{
				BaseItemTemplate item = BaseItemTemplate.Get<BaseItemTemplate>((int)msg.ReferenceID);
				return item != null ? $"Using {item.Name}" : "Using an item";
			}

			if (controller != null && controller.TryGetAbilityForVisuals(msg.ReferenceID, out Ability ability))
			{
				return $"Casting {ability.Name}";
			}

			/* The observer does not know this ability. It started observing after the caster
			 * learned it and the learn broadcast has not arrived, or the caster is an NPC whose
			 * set this peer never received. The row still appears — that something is being cast
			 * is the point — without a name it cannot vouch for. */
			return "Casting";
		}

		/// <summary>
		/// Finds the nameplate and ability controller of an observed character, refusing the local one.
		/// </summary>
		private bool TryResolveObserved(int casterObjectID, out NetworkObject caster, out Nameplate plate, out AbilityController controller)
		{
			caster = null;
			plate = null;
			controller = null;

			if (networkManager?.ClientManager == null ||
				!networkManager.ClientManager.Objects.Spawned.TryGetValue(casterObjectID, out caster) ||
				caster == null ||
				caster.IsOwner)
			{
				return false;
			}

			ICharacter character = caster.GetComponent<ICharacter>();
			if (character == null)
			{
				return false;
			}

			controller = caster.GetComponent<AbilityController>();
			plate = character.CharacterNameplate;
			if (plate == null)
			{
				// A character authored without a nameplate. Nothing to write on; not an error.
				Log.Debug("ClientCastNameplateDisplay", $"Character {casterObjectID} has no nameplate.");
				return false;
			}
			return true;
		}
	}
}
