using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Apply Dispel Action", menuName = "FishMMO/Triggers/Actions/Character/Apply Dispel")]
	public class ApplyDispelAction : BaseAction
	{
		public byte AmountToRemove;
		public bool IncludeDebuffs;
		public bool IncludeBuffs;

		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (eventData.TryGet(out CharacterHitEventData targetEventData))
			{
				if (targetEventData.Target.TryGet(out IBuffController defenderBuffController))
				{
					for (int i = 0; i < AmountToRemove && defenderBuffController.Buffs.Count > 0; ++i)
					{
						defenderBuffController.RemoveRandom(targetEventData.RNG, IncludeBuffs, IncludeDebuffs);
					}
				}
			}
			else
			{
				Log.Warning("ApplyDispelAction", "Expected CharacterHitEventData.");
			}
		}
	}
}