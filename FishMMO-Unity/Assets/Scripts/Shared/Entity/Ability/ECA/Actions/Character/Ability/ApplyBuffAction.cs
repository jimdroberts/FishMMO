using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Apply Buff Action", menuName = "FishMMO/Actions/Apply Buff")]
	public class ApplyBuffAction : BaseAction
	{
		public int Stacks;
		public BaseBuffTemplate BuffTemplate;

		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (eventData.TryGet(out CharacterTargetEventData targetEventData))
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
				Log.Warning("ApplyBuffAction: Expected CharacterTargetEventData.");
			}
		}
	}
}