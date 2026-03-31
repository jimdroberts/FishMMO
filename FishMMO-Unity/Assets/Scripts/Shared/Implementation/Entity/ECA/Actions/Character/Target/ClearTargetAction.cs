using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that clears the initiator's current target.
	/// </summary>
	[Serializable]
	public class ClearTargetAction : BaseAction
	{
		/// <inheritdoc />
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (initiator == null)
			{
				return;
			}

			if (!initiator.TryGet(out ITargetController targetController))
			{
				return;
			}

			targetController.UpdateTarget(Vector3.zero, Vector3.zero, 0f);
		}
	}
}