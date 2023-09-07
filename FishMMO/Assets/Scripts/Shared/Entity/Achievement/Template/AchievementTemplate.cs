using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "New Achievement", menuName = "Item/Achievement/Achievement", order = 1)]
public sealed class AchievementTemplate : CachedScriptableObject<AchievementTemplate>
{
	public List<AchievementTier> Tiers;

	public string Name { get { return this.name; } }
}