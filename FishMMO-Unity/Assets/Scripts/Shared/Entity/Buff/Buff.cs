using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Cysharp.Text;

namespace FishMMO.Shared
{
	public class Buff
	{
		private float tickTime;
		private List<BuffAttribute> attributeBonuses = new List<BuffAttribute>();
		private List<Buff> stacks = new List<Buff>();

		public float RemainingTime;
		public BuffTemplate Template { get; private set; }
		public List<BuffAttribute> AttributeBonuses { get { return attributeBonuses; } }
		public List<Buff> Stacks { get { return stacks; } }

		public Buff(int templateID)
		{
			Template = BuffTemplate.Get<BuffTemplate>(templateID);
			tickTime = Template.TickRate;
			RemainingTime = Template.Duration;
		}

		public Buff(int templateID, float remainingTime)
		{
			Template = BuffTemplate.Get<BuffTemplate>(templateID);
			tickTime = Template.TickRate;
			RemainingTime = remainingTime;
		}

		public Buff(int templateID, float remainingTime, List<Buff> stacks)
		{
			Template = BuffTemplate.Get<BuffTemplate>(templateID);
			tickTime = Template.TickRate;
			RemainingTime = remainingTime;
			this.stacks = stacks;
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
			tickTime -= time;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void AddTickTime(float time)
		{
			tickTime += time;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void TryTick(ICharacter target)
		{
			if (tickTime <= 0.0f)
			{
				//Template.OnTick(this, target);
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
			tickTime = Template.TickRate;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void Reset()
		{
			attributeBonuses.Clear();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void AddAttributeBonus(BuffAttribute buffAttributeInstance)
		{
			attributeBonuses.Add(buffAttributeInstance);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Apply(ICharacter target)
		{
			//Template.OnApply(this, target);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Remove(ICharacter target)
		{
			//Template.OnRemove(this, target);
			Reset();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void AddStack(Buff stack, ICharacter target)
		{
			//Template.OnApplyStack(stack, target);
			stacks.Add(stack);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void RemoveStack(ICharacter target)
		{
			//Template.OnRemoveStack(this, target);
		}

		public string Tooltip()
		{
			using (var sb = ZString.CreateStringBuilder())
			{
				sb.Append("<size=120%><color=#f5ad6e>");
				sb.Append(Template.Name);
				sb.Append("</color></size>");
				sb.AppendLine();
				sb.Append("<color=#a66ef5>Remaining Time: ");
				sb.Append(RemainingTime);
				sb.Append("</color>");
				if (attributeBonuses != null)
				{
					foreach (BuffAttribute attribute in attributeBonuses)
					{
						sb.AppendLine();
						sb.Append(attribute.Tooltip());
					}
				}
				return sb.ToString();
			}
		}
	}
}