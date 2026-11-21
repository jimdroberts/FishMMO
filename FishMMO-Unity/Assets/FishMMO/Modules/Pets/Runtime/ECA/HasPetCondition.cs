using System;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks if the character currently has an active pet.
	/// </summary>
	[Serializable]
	[UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "FishMMO.Shared", sourceAssembly: "FishMMO.Shared")]
	public class HasPetCondition : BaseCondition
	{
		/// <inheritdoc />
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			ICharacter characterToCheck = (eventData?.TargetCharacter ?? initiator);
			if (characterToCheck == null)
			{
				return false;
			}

			if (!characterToCheck.TryGet(out IPetController petController))
			{
				return false;
			}

			return petController.Pets.Count > 0;
		}
	}
}