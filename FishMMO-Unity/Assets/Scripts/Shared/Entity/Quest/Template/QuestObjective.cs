using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	public abstract class QuestObjective : ScriptableObject
	{
		public long RequiredValue;
		public List<BaseItemTemplate> Rewards;
	}
}