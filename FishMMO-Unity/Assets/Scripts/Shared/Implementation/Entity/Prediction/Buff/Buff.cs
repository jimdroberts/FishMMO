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
				NextTickTick = currentTick;
				Stacks = stacks;
				TickCount = tickCount;
				return;
			}
			ExpiryTick = currentTick + DurationToTicks(Template.Duration, tickDelta);
			NextTickTick = currentTick + DurationToTicks(Template.TickRate, tickDelta);
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
		/// <returns>True if the tick fired (snapshot must be marked dirty).</returns>
		// AggressiveInlining intentionally absent: the try/catch prevents the JIT from
		// inlining this method regardless, so the attribute would be silently ignored.
		public bool TryTick(ICharacter target, uint currentTick, float tickDelta)
		{
			if (Template == null || NextTickTick == TimeManager.UNSET_TICK)
			{
				return false;
			}

			uint tickRateTicks = DurationToTicks(Template.TickRate, tickDelta);
			if (tickRateTicks == 0u)
			{
				return false;
			}

			uint lastEligibleTick = ExpiryTick != TimeManager.UNSET_TICK &&
				(int)(currentTick - ExpiryTick) >= 0 ? ExpiryTick : currentTick;
			bool fired = false;
			while ((int)(lastEligibleTick - NextTickTick) >= 0)
			{
				try
				{
					Template.OnTick(this, target);
				}
				catch (System.Exception ex)
				{
					// Without this catch, a throwing OnTick leaves NextTickTick un-advanced,
					// causing the tick to re-fire on every subsequent game tick — flooding the
					// server with redundant calls and desync'ing prediction state permanently.
					Log.Warning("Buff", $"OnTick threw on template {Template?.ID.ToString() ?? "unknown"}: {ex}");
				}
				TickCount++;
				// Accumulate the per-tick multiplier using Stacks as it was when OnTick
				// fired. AttributeTickBuffTemplate.OnTick uses (1 + Stacks) as its
				// multiplier; recording that same value here lets OnRemove reverse the
				// exact total modifier regardless of stack changes during the buff's life.
				CumulativeTickMultiplier += (1 + Stacks);
				NextTickTick += tickRateTicks;
				fired = true;
			}
			return fired;
		}

		/// <summary>
		/// Resets the expiry to <c>currentTick + durationTicks</c>, refreshing the buff's lifetime.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void ResetDuration(uint currentTick, float tickDelta)
		{
			ExpiryTick = currentTick + DurationToTicks(Template.Duration, tickDelta);
		}

		/// <summary>
		/// Resets <see cref="NextTickTick"/> to <c>currentTick + tickRateTicks</c>.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void ResetTickTime(uint currentTick, float tickDelta)
		{
			NextTickTick = currentTick + DurationToTicks(Template.TickRate, tickDelta);
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