using System.Collections.Generic;
using UnityEngine;

public abstract class QuestObjective : ScriptableObject
{
	public long RequiredValue;
	public List<BaseItemTemplate> Rewards;
}