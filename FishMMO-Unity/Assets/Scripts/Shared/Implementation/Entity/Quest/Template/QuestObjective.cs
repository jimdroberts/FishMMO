using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base class for quest objectives. Defines required value and rewards for completing the objective.
	/// </summary>
	public abstract class QuestObjective : ScriptableObject
	{
		/// <summary>
		/// The value required to complete this objective (e.g., number of items, kills, etc.).
		/// </summary>
		public long RequiredValue;

		/// <summary>
		/// List of item templates rewarded upon completion of this objective.
		/// </summary>
		public List<BaseItemTemplate> Rewards;
	}
}