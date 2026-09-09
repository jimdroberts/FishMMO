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
		/// The stop is unreliable, so it can be lost. Without this a lost stop would leave a plate
		/// reading "Casting" until that character left view. The grace covers a slow finish — a
		/// speed debuff applied mid-cast lengthens the real one — without leaving the row up long
		/// enough to be believed.
		/// </remarks>
		public const float ExpiryGraceSeconds = 1.5f;

		/// <summary>What one observed character is currently casting.</summary>
		private struct ActiveCast
		{
			/// <summary>The character's nameplate, so it can be cleared without a lookup.</summary>
			public Nameplate Plate;
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
		}

		/// <summary>Unregisters and clears every row this display wrote.</summary>
		public void Shutdown()
		{
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
				if (cast.Plate == null)
				{
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
				Stop(msg.CasterObjectID);
				return;
			}

			if (!TryResolveObserved(msg.CasterObjectID, out Nameplate plate, out AbilityController controller))
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
			float duration = ResolveDuration(msg, controller);

			/* The message spent a network delay in flight and the cast has been running for all of
			 * it. Elapsed is measured from the tick the server stamped, less the interpolation this
			 * client renders its peers behind — the same correction the ability object's spawn
			 * applies, so the row and the projectile agree about when the cast began. A cast whose
			 * whole duration has already passed is not drawn at all rather than started late. */
			float elapsed = ElapsedSince(msg.ServerTick);
			float remaining = duration - elapsed;
			if (duration > 0.0f && remaining <= 0.0f)
			{
				return;
			}

			plate.SetLine(NameplateSlot.Status, DescribeCast(msg, controller));

			float now = Time.unscaledTime;
			active[msg.CasterObjectID] = new ActiveCast
			{
				Plate = plate,
				EarliestClear = now + MinimumDwellSeconds,
				Expiry = now + Mathf.Max(remaining, 0.0f) + ExpiryGraceSeconds,
				StopPending = false,
			};
		}

		/// <summary>Marks a cast finished, honouring the minimum dwell.</summary>
		private void Stop(int casterObjectID)
		{
			if (!active.TryGetValue(casterObjectID, out ActiveCast cast))
			{
				return;
			}

			if (Time.unscaledTime >= cast.EarliestClear)
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
		/// An ability this peer cannot resolve reports zero, which the caller treats as "no duration
		/// to expire on" and falls back to the grace alone.
		/// </para>
		/// </remarks>
		private static float ResolveDuration(CharacterCastBroadcast msg, AbilityController controller)
		{
			if (msg.IsConsumable)
			{
				/* No haste on an item: ConsumableTemplate.ActivationTime is used raw by
				 * TryStartConsumable, with no speed attribute applied. */
				ConsumableTemplate consumable = BaseItemTemplate.Get<ConsumableTemplate>((int)msg.ReferenceID);
				return consumable != null ? consumable.ActivationTime : 0.0f;
			}

			if (controller == null || !controller.TryGetAbilityForVisuals(msg.ReferenceID, out Ability ability))
			{
				return 0.0f;
			}

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

			uint ticks = AbilityController.ComputeObserverFastForwardTicks(
				networkManager.TimeManager.Tick, serverTick, LagCompensationTick.SpectatorInterpolationTicks);
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
		private bool TryResolveObserved(int casterObjectID, out Nameplate plate, out AbilityController controller)
		{
			plate = null;
			controller = null;

			if (networkManager?.ClientManager == null ||
				!networkManager.ClientManager.Objects.Spawned.TryGetValue(casterObjectID, out NetworkObject caster) ||
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
