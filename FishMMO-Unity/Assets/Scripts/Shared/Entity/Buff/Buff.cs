using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a single instance of a buff applied to a character, tracking time, stacks, and template.
	/// </summary>
	public class Buff
	{
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
		/// The template that defines this buff's behavior and properties.
		/// </summary>
		public BaseBuffTemplate Template { get; private set; }

		/// <summary>
		/// Creates a new buff instance from a template ID, using the template's default duration and tick rate.
		/// </summary>
		/// <param name="templateID">The template ID for the buff.</param>
		public Buff(int templateID)
		{
			Template = BaseBuffTemplate.Get<BaseBuffTemplate>(templateID);
			TickTime = Template.TickRate;
			RemainingTime = Template.Duration;
		}

		/// <summary>
		/// Creates a new buff instance from a template ID and a specific remaining time.
		/// </summary>
		/// <param name="templateID">The template ID for the buff.</param>
		/// <param name="remainingTime">The remaining time for the buff.</param>
		public Buff(int templateID, float remainingTime)
		{
			Template = BaseBuffTemplate.Get<BaseBuffTemplate>(templateID);
			TickTime = Template.TickRate;
			RemainingTime = remainingTime;
		}

		/// <summary>
		/// Creates a new buff instance from a template ID, remaining time, tick time, and stack count.
		/// </summary>
		/// <param name="templateID">The template ID for the buff.</param>
		/// <param name="remainingTime">The remaining time for the buff.</param>
		/// <param name="tickTime">The remaining time until the next tick.</param>
		/// <param name="stacks">The number of stacks for the buff.</param>
		public Buff(int templateID, float remainingTime, float tickTime, int stacks)
		{
			Template = BaseBuffTemplate.Get<BaseBuffTemplate>(templateID);
			TickTime = tickTime;
			RemainingTime = remainingTime;
			Stacks = stacks;
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
		/// Adds time to the remaining duration of the buff.
		/// </summary>
		/// <param name="time">The amount of time to add (seconds).</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void AddTime(float time)
		{
			RemainingTime += time;
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
		/// Adds time to the remaining tick time for the buff.
		/// </summary>
		/// <param name="time">The amount of time to add (seconds).</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void AddTickTime(float time)
		{
			TickTime += time;
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