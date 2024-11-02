using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	public class Buff
	{
		public float RemainingTime;
		public float TickTime;
		public int Stacks;

		public BaseBuffTemplate Template { get; private set; }

		public Buff(int templateID)
		{
			Template = BaseBuffTemplate.Get<BaseBuffTemplate>(templateID);
			TickTime = Template.TickRate;
			RemainingTime = Template.Duration;
		}

		public Buff(int templateID, float remainingTime)
		{
			Template = BaseBuffTemplate.Get<BaseBuffTemplate>(templateID);
			TickTime = Template.TickRate;
			RemainingTime = remainingTime;
		}

		public Buff(int templateID, float remainingTime, float tickTime, int stacks)
		{
			Template = BaseBuffTemplate.Get<BaseBuffTemplate>(templateID);
			TickTime = tickTime;
			RemainingTime = remainingTime;
			Stacks = stacks;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void SubtractTime(float time)
		{
			RemainingTime -= time;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void AddTime(float time)
		{
			RemainingTime += time;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void SubtractTickTime(float time)
		{
			TickTime -= time;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void AddTickTime(float time)
		{
			TickTime += time;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void TryTick(ICharacter target)
		{
			if (TickTime <= 0.0f)
			{
				Template.OnTick(this, target);
				ResetTickTime();
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void ResetDuration()
		{
			RemainingTime = Template.Duration;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void ResetTickTime()
		{
			TickTime = Template.TickRate;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Apply(ICharacter target)
		{
			Template.OnApply(this, target);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Remove(ICharacter target)
		{
			Template.OnRemove(this, target);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void AddStack(ICharacter target)
		{
			Template.OnApplyStack(this, target);
			++Stacks;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void RemoveStack(ICharacter target)
		{
			Template.OnRemoveStack(this, target);
			--Stacks;
		}
	}
}