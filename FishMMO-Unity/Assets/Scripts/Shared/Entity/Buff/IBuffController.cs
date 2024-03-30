using System.Collections.Generic;

namespace FishMMO.Shared
{
	public interface IBuffController : ICharacterBehaviour
	{
		Dictionary<int, Buff> Buffs { get; }
		void Apply(BuffTemplate template);
		void Apply(Buff buff);
		void Remove(int buffID);
		void RemoveAll();
	}
}