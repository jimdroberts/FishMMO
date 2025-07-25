using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Apply Buff Action", menuName = "FishMMO/Triggers/Actions/Character/Apply Buff")]
	public class ApplyBuffAction : BaseAction
	{
		public int Stacks;
		public BaseBuffTemplate BuffTemplate;

		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (eventData.TryGet(out CharacterHitEventData targetEventData))
			{
				if (targetEventData.Target.TryGet(out IBuffController buffController))
				{
					for (int i = 0; i < Stacks; ++i)
					{
						buffController.Apply(BuffTemplate);
					}
				}
			}
			else
			{
				Log.Warning("ApplyBuffAction", "Expected CharacterHitEventData.");
			}
		}
		public override string GetFormattedDescription()
		{
			return $"Applies <color=#FFD700>{Stacks}</color> stack(s) of <color=#FFD700>{BuffTemplate?.name ?? "Buff"}</color> to the target.";
		}
	}
}