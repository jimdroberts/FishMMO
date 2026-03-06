using System.Runtime.CompilerServices;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a single instance of a buff applied to a character, tracking time, stacks, and template.
	/// All state is deterministic — timing advances via explicit deltaTime, not frame-dependent values.
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
		/// The remaining duration of the buff in seconds.
		/// </summary>
		public float RemainingTime;

		/// <summary>
		/// The remaining time until the next tick in seconds.
		/// </summary>
		public float TickTime;

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
		/// The template that defines this buff's behavior and properties.
		/// </summary>
		public BaseBuffTemplate Template { get; private set; }

		/// <summary>
		/// Creates a new buff instance from a template ID with optional overrides for timing and stacks.
		/// </summary>
		/// <param name="templateID">The template ID for the buff.</param>
		/// <param name="remainingTime">Override remaining time, or -1 to use the template default.</param>
		/// <param name="tickTime">Override tick time, or -1 to use the template default.</param>
		/// <param name="stacks">The initial stack count.</param>
		/// <param name="tickCount">The number of ticks that have already fired (for restoration).</param>
		public Buff(int templateID, float remainingTime = -1f, float tickTime = -1f, int stacks = 0, int tickCount = 0)
		{
			Template = BaseBuffTemplate.Get<BaseBuffTemplate>(templateID);
			RemainingTime = remainingTime < 0f ? Template.Duration : remainingTime;
			TickTime = tickTime < 0f ? Template.TickRate : tickTime;
			Stacks = stacks;
			TickCount = tickCount;
		}

		/// <summary>
		/// Subtracts time from the remaining duration of the buff.
		/// </summary>
		/// <param name="time">The amount of time to subtract (seconds).</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void SubtractTime(float time)
		{
			RemainingTime -= time;
		}

		/// <summary>
		/// Subtracts time from the remaining tick time for the buff.
		/// </summary>
		/// <param name="time">The amount of time to subtract (seconds).</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void SubtractTickTime(float time)
		{
			TickTime -= time;
		}

		/// <summary>
		/// Tries to trigger a tick for the buff if the tick timer has expired.
		/// </summary>
		/// <param name="target">The character affected by the buff.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void TryTick(ICharacter target)
		{
			if (TickTime <= 0.0f)
			{
				Template.OnTick(this, target);
				ResetTickTime();
			}
		}

		/// <summary>
		/// Resets the remaining duration to the template's default duration.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void ResetDuration()
		{
			RemainingTime = Template.Duration;
		}

		/// <summary>
		/// Resets the tick timer to the template's default tick rate.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void ResetTickTime()
		{
			TickTime = Template.TickRate;
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
	}
}