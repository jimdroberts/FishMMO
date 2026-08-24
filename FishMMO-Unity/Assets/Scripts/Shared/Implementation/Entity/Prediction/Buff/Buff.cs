using System.Runtime.CompilerServices;
using FishNet.Managing.Timing;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a single instance of a buff applied to a character, tracking time, stacks, and template.
	/// All state is deterministic — timing is tick-based (<see cref="ExpiryTick"/>,
	/// <see cref="NextTickTick"/>) rather than float-second accumulators, eliminating
	/// floating-point drift across prediction ticks.
	/// </summary>
	public class Buff
	{
		/// <summary>
		/// Version number for this buff instance, used for database state-driven safety on updates.
		/// Incremented whenever the buff's state changes in a way that requires persistence
		/// (e.g., stacks added/removed, remaining time checkpointed).
		/// </summary>
		public long Version;

		/// <summary>
		/// The absolute network tick at which this buff expires.
		/// A buff has expired when <c>(int)(currentTick - ExpiryTick) &gt;= 0</c>.
		/// Permanent buffs use <see cref="TimeManager.UNSET_TICK"/>.
		/// </summary>
		public uint ExpiryTick;

		/// <summary>
		/// The absolute network tick at which the next <see cref="BaseBuffTemplate.OnTick"/>
		/// should fire. Advances by one tick-rate interval after each fire so missed
		/// intervals catch up without drifting the cadence to the late current tick.
		/// </summary>
		public uint NextTickTick;

		/// <summary>
		/// The current number of stacks of this buff.
		/// </summary>
		public int Stacks;

		/// <summary>
		/// The number of ticks that have fired for this buff instance.
		/// Used by cumulative tick templates to track total applied modifiers for clean reversal.
		/// Serialized in payload for deterministic restoration.
		/// </summary>
		public int TickCount;

		/// <summary>
		/// Running sum of <c>(1 + Stacks)</c> for every tick that has fired on this buff instance.
		/// Tracks the exact cumulative multiplier applied by templates such as
		/// <see cref="AttributeTickBuffTemplate"/> so that <c>OnRemove</c> can reverse
		/// the precise total modifier regardless of how many times <see cref="Stacks"/>
		/// changed between tick firings. Reconciled via
		/// <see cref="BuffReconcileEntry.CumulativeTickMultiplier"/> so rollback replay
		/// and post-reconcile removal both produce correct reversal.
		/// </summary>
		public int CumulativeTickMultiplier;

		/// <summary>
		/// The template that defines this buff's behavior and properties.
		/// </summary>
		public BaseBuffTemplate Template { get; private set; }

		/// <summary>
		/// The character that applied this buff, snapshotted at application time. May be null.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Attribution for periodic effects. A damage-over-time tick routed through
		/// <see cref="ICharacterDamageController.Damage"/> needs somebody to credit — for threat,
		/// for kill credit, and for the attacker's own combat state — and the buff is the only
		/// thing that still exists by the time the tick fires. The ability that applied it is long
		/// gone.
		/// </para>
		/// <para>
		/// Deliberately <b>not</b> serialized into the reconcile payload or the database. The
		/// server holds the authoritative buff instance for the whole of its life, which is the
		/// only place attribution is acted on; a client has no use for it, and a DoT restored from
		/// the database after a relog has outlived any sensible claim to credit a kill.
		/// </para>
		/// <para>
		/// A missing caster never suppresses the effect — see <see cref="Caster"/>.
		/// </para>
		/// </remarks>
		private ICharacter caster;

		/// <summary>
		/// True while this buff is ticking inside a prediction replay rather than for the first time.
		/// </summary>
		/// <remarks>
		/// Reconcile replays every input since the last authoritative state, so a DoT's tick fires
		/// again on each pass. The resource mutation must repeat — that is what keeps the client's
		/// predicted health in step — but the ECA triggers hanging off it must not, or a single
		/// tick of poison counts a dozen times toward an achievement.
		/// </remarks>
		public bool IsReplaying { get; private set; }

		/// <summary>
		/// The character to credit for this buff's periodic effects, or null when there is nobody
		/// left to credit.
		/// </summary>
		/// <remarks>
		/// Returns null once the caster has been destroyed — a player who disconnected, an NPC that
		/// despawned. That is deliberately <em>not</em> treated as a reason to stop ticking: a
		/// lingering poison is part of the simulation whether or not the person who applied it is
		/// still in the scene, so the effect lands with a null attacker and simply generates no
		/// threat and no kill credit.
		/// <para>
		/// The null check goes through <see cref="UnityEngine.Object"/> on purpose. A destroyed
		/// component compared through an interface reference uses reference equality and reports
		/// itself as non-null, so the plain test would hand out a dead object.
		/// </para>
		/// </remarks>
		public ICharacter Caster
		{
			get
			{
				if (caster == null)
				{
					return null;
				}

				if (caster is UnityEngine.Object unityObject && unityObject == null)
				{
					// Drop the reference so a destroyed character is not kept alive by this buff.
					caster = null;
					return null;
				}

				return caster;
			}
		}

		/// <summary>
		/// Snapshots the character responsible for this buff.
		/// </summary>
		/// <remarks>
		/// Called again when an existing buff is re-applied or stacked, so a fresh application
		/// re-points attribution at whoever cast it most recently. A null is ignored rather than
		/// clearing an existing caster: a re-application from a source that does not know its
		/// initiator (a region trigger, a database restore) should not strip credit from one that
		/// did.
		/// </remarks>
		/// <param name="caster">The character applying the buff, or null if unknown.</param>
		public void SetCaster(ICharacter caster)
		{
			if (caster != null)
			{
				this.caster = caster;
			}
		}

		/// <summary>
		/// Fixed time step per tick for duration UI conversions.
		/// Cached from <c>TimeManager.TickDelta</c> at application time and refreshed every
		/// tick by <see cref="BuffController.Tick"/> so that <see cref="RemainingSeconds"/>
		/// always reports against the latest tick rate even if this buff was constructed
		/// before <c>TimeManager</c> was ready (the constructor may have been handed 0).
		/// </summary>
		private float tickDelta;

		/// <summary>
		/// Updates the cached tick-delta used by <see cref="RemainingSeconds"/>. Called by
		/// <see cref="BuffController.Tick"/> every tick so UI duration bars never get stuck
		/// reporting 0 when a buff was constructed before TimeManager became available.
		/// Ignores non-positive values to preserve the last good delta.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void SetTickDelta(float tickDelta)
		{
			if (tickDelta > 0f)
			{
				this.tickDelta = tickDelta;
			}
		}

		/// <summary>
		/// Creates a new buff instance from a fresh application.
		/// <see cref="ExpiryTick"/> and <see cref="NextTickTick"/> are computed from
		/// the template's duration and tick-rate via <c>ceil(seconds / tickDelta)</c>,
		/// matching <see cref="CooldownInstance"/>'s convention.
		/// </summary>
		/// <param name="templateID">The template ID for the buff.</param>
		/// <param name="currentTick">The network tick at the moment of application.</param>
		/// <param name="tickDelta">Fixed seconds-per-tick for the active session.</param>
		/// <param name="stacks">The initial stack count.</param>
		/// <param name="tickCount">The number of ticks that have already fired (for restoration).</param>
		public Buff(int templateID, uint currentTick, float tickDelta, int stacks = 0, int tickCount = 0)
		{
			// Defensive guard: if a caller constructs a buff before TimeManager.TickDelta
			// is available, tickDelta arrives as 0. DurationToTicks(_, 0) returns 0, which
			// collapses ExpiryTick / NextTickTick onto currentTick and causes the buff to
			// expire on the same tick it was applied. Fall back to a 30 tps assumption so
			// the buff still survives at least one Tick() evaluation; SetTickDelta will
			// repair the cached value on the next BuffController.Tick.
			if (tickDelta <= 0f)
			{
				tickDelta = 1f / 30f;
			}
			this.tickDelta = tickDelta;
			Template = BaseBuffTemplate.Get<BaseBuffTemplate>(templateID);
			if (Template == null)
			{
				ExpiryTick = currentTick;  // treat missing template as instantly expired
				NextTickTick = TimeManager.UNSET_TICK;
				Stacks = stacks;
				TickCount = tickCount;
				return;
			}
			ExpiryTick = GetExpiryTick(Template, currentTick, tickDelta);
			NextTickTick = GetNextTickTick(Template, currentTick, tickDelta);
			Stacks = stacks;
			TickCount = tickCount;
		}

		/// <summary>
		/// Restores a buff from serialized absolute-tick values (reconcile / payload restore).
		/// Does not recompute tick values from the template — uses the provided values directly.
		/// </summary>
		/// <param name="templateID">The template ID for the buff.</param>
		/// <param name="expiryTick">Absolute tick at which the buff expires.</param>
		/// <param name="nextTickTick">Absolute tick at which the next OnTick fires.</param>
		/// <param name="tickDelta">Fixed seconds-per-tick (for <see cref="RemainingSeconds"/> UI conversion).</param>
		/// <param name="stacks">The current stack count.</param>
		/// <param name="tickCount">The number of ticks that have already fired.</param>
		public Buff(int templateID, uint expiryTick, uint nextTickTick, float tickDelta, int stacks, int tickCount)
		{
			// Defensive guard: never let a stuck-at-zero tickDelta corrupt the UI duration
			// bar (RemainingSeconds would always return 0). SetTickDelta repairs the cache
			// on the next BuffController.Tick once TimeManager is available.
			if (tickDelta <= 0f)
			{
				tickDelta = 1f / 30f;
			}
			this.tickDelta = tickDelta;
			Template = BaseBuffTemplate.Get<BaseBuffTemplate>(templateID);
			ExpiryTick = expiryTick;
			NextTickTick = nextTickTick;
			Stacks = stacks;
			TickCount = tickCount;
		}

		/// <summary>
		/// Returns true when this buff has expired at the given tick.
		/// Uses a signed cast for overflow-safe wrap-around comparison; safe for any
		/// duration &lt; 2^31 ticks (~810 days at 30 tps).
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool HasExpired(uint currentTick) =>
			ExpiryTick != TimeManager.UNSET_TICK && (int)(currentTick - ExpiryTick) >= 0;

		/// <summary>
		/// Computes the remaining buff duration in seconds for the given tick.
		/// Returns zero if the buff has already expired. Used by the UI duration slider.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public float RemainingSeconds(uint currentTick)
		{
			if (ExpiryTick == TimeManager.UNSET_TICK)
			{
				return Template != null && Template.Duration > 0f ? Template.Duration : 0f;
			}
			if (HasExpired(currentTick))
			{
				return 0f;
			}
			return (ExpiryTick - currentTick) * tickDelta;
		}

		/// <summary>
		/// Tries to trigger a tick for the buff if <see cref="NextTickTick"/> has been reached,
		/// then advances <see cref="NextTickTick"/> by the template tick-rate in ticks.
		/// </summary>
		/// <param name="target">The character affected by the buff.</param>
		/// <param name="currentTick">The current network tick.</param>
		/// <param name="tickDelta">Fixed seconds-per-tick of the active session.</param>
		/// <param name="isReplaying">
		/// True when this tick is part of a prediction replay. Exposed to templates via
		/// <see cref="IsReplaying"/> so effects that raise events can suppress them.
		/// </param>
		/// <returns>True if tick state changed (snapshot must be marked dirty).</returns>
		// AggressiveInlining intentionally absent: the try/catch prevents the JIT from
		// inlining this method regardless, so the attribute would be silently ignored.
		public bool TryTick(ICharacter target, uint currentTick, float tickDelta, bool isReplaying = false)
		{
			if (Template == null || NextTickTick == TimeManager.UNSET_TICK)
			{
				return false;
			}

			IsReplaying = isReplaying;

			uint tickRateTicks = DurationToTicks(Template.TickRate, tickDelta);
			if (tickRateTicks == 0u)
			{
				return false;
			}

			uint lastEligibleTick = ExpiryTick != TimeManager.UNSET_TICK &&
				(int)(currentTick - ExpiryTick) >= 0 ? ExpiryTick : currentTick;
			bool changed = false;
			while ((int)(lastEligibleTick - NextTickTick) >= 0)
			{
				bool tickSucceeded = false;
				try
				{
					Template.OnTick(this, target);
					tickSucceeded = true;
				}
				catch (System.Exception ex)
				{
					// Failed ticks are skipped rather than retried every game tick. Template
					// implementations must keep OnTick atomic; generic rollback is not possible here.
					Log.Warning("Buff", $"OnTick threw on template {Template?.ID.ToString() ?? "unknown"}: {ex}");
				}
				if (tickSucceeded)
				{
					TickCount++;
					// Accumulate the per-tick multiplier using Stacks as it was when OnTick
					// fired. AttributeTickBuffTemplate.OnTick uses (1 + Stacks) as its
					// multiplier; recording that same value here lets OnRemove reverse the
					// exact total modifier regardless of stack changes during the buff's life.
					CumulativeTickMultiplier += (1 + Stacks);
				}
				NextTickTick += tickRateTicks;
				changed = true;
			}
			return changed;
		}

		/// <summary>
		/// Resets the expiry to <c>currentTick + durationTicks</c>, refreshing the buff's lifetime.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void ResetDuration(uint currentTick, float tickDelta)
		{
			ExpiryTick = GetExpiryTick(Template, currentTick, tickDelta);
		}

		/// <summary>
		/// Resets <see cref="NextTickTick"/> to <c>currentTick + tickRateTicks</c>.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void ResetTickTime(uint currentTick, float tickDelta)
		{
			NextTickTick = GetNextTickTick(Template, currentTick, tickDelta);
		}

		/// <summary>
		/// Computes the absolute network tick at which this buff expires based on the template's duration.
		/// Returns <see cref="TimeManager.UNSET_TICK"/> for permanent buffs, or
		/// <c>currentTick + ceil(duration / tickDelta)</c> for timed buffs.
		/// </summary>
		/// <param name="template">The buff template providing duration and permanent flag.</param>
		/// <param name="currentTick">The application tick.</param>
		/// <param name="tickDelta">Fixed seconds-per-tick for the active session.</param>
		/// <returns>The expiry tick, or <see cref="TimeManager.UNSET_TICK"/> for permanent buffs.</returns>
		internal static uint GetExpiryTick(BaseBuffTemplate template, uint currentTick, float tickDelta)
		{
			if (template == null)
			{
				return currentTick;
			}
			if (template.IsPermanent)
			{
				return TimeManager.UNSET_TICK;
			}
			return currentTick + DurationToTicks(template.Duration, tickDelta);
		}

		/// <summary>
		/// Computes the absolute network tick at which the next <see cref="BaseBuffTemplate.OnTick"/>
		/// should fire, based on the template's tick rate.
		/// Returns <see cref="TimeManager.UNSET_TICK"/> when the template or tick rate yields zero ticks.
		/// </summary>
		/// <param name="template">The buff template providing the tick rate.</param>
		/// <param name="currentTick">The application tick.</param>
		/// <param name="tickDelta">Fixed seconds-per-tick for the active session.</param>
		/// <returns>The next tick tick, or <see cref="TimeManager.UNSET_TICK"/> if no tick is scheduled.</returns>
		internal static uint GetNextTickTick(BaseBuffTemplate template, uint currentTick, float tickDelta)
		{
			if (template == null)
			{
				return TimeManager.UNSET_TICK;
			}
			uint tickRateTicks = DurationToTicks(template.TickRate, tickDelta);
			return tickRateTicks == 0u ? TimeManager.UNSET_TICK : currentTick + tickRateTicks;
		}

		/// <summary>
		/// Applies the buff's effects to the target character.
		/// </summary>
		/// <param name="target">The character receiving the buff.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Apply(ICharacter target)
		{
			Template.OnApply(this, target);
		}

		/// <summary>
		/// Removes the buff's effects from the target character.
		/// </summary>
		/// <param name="target">The character losing the buff.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Remove(ICharacter target)
		{
			Template.OnRemove(this, target);
		}

		/// <summary>
		/// Adds a stack to the buff and applies stack effects to the target.
		/// </summary>
		/// <param name="target">The character receiving the stack.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void AddStack(ICharacter target)
		{
			Template.OnApplyStack(this, target);
			++Stacks;
		}

		/// <summary>
		/// Removes a stack from the buff and removes stack effects from the target.
		/// </summary>
		/// <param name="target">The character losing the stack.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void RemoveStack(ICharacter target)
		{
			Template.OnRemoveStack(this, target);
			--Stacks;
		}

		/// <summary>
		/// Converts a duration in seconds to a tick count using ceiling division,
		/// matching <see cref="CooldownInstance"/>'s convention. Returns at least 1 tick
		/// for any positive duration so the buff always survives at least one tick.
		/// </summary>
		/// <remarks>
		/// Exposed as <c>internal</c> so the unit test assembly can validate this
		/// formula against production rather than re-implementing it. Production
		/// callers remain within <see cref="Buff"/>.
		/// </remarks>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal static uint DurationToTicks(float seconds, float tickDelta)
		{
			if (tickDelta <= 0f || seconds <= 0f)
			{
				return 0u;
			}
			uint ticks = (uint)System.Math.Ceiling(seconds / tickDelta);
			return ticks < 1u ? 1u : ticks;
		}
	}
}