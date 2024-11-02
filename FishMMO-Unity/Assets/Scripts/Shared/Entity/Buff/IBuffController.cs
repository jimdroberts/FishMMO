using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public interface IBuffController : ICharacterBehaviour
	{
		static Action<Buff> OnAddTime;
		static Action<Buff> OnSubtractTime;
		static Action<Buff> OnAddBuff;
		static Action<Buff> OnRemoveBuff;
		static Action<Buff> OnAddDebuff;
		static Action<Buff> OnRemoveDebuff;

		Dictionary<int, Buff> Buffs { get; }
		void Apply(BaseBuffTemplate template);
		void Apply(Buff buff);
		void Remove(int buffID);
		void RemoveAll();
	}
}