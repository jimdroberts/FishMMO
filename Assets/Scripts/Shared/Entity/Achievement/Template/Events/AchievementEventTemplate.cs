using UnityEngine;

public class AchievementEventTemplate : ScriptableObject
{
	public virtual void Invoke(Character self, Character other) { }
	public virtual void Invoke(Character self, Character other, long amount) { }
}