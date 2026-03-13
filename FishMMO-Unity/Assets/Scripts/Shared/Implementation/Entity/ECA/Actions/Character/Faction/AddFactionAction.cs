using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that adds a faction reputation amount for a character.
	/// Server-only execution.
	/// </summary>
	[Serializable]
	public class AddFactionAction : BaseAction
	{
		/// <summary>
		/// The faction template to modify.
		/// </summary>
		[Tooltip("The faction template to modify.")]
		public FactionTemplate FactionTemplate;

		/// <summary>
		/// The amount to add to faction standing (can be negative).
		/// </summary>
		[Tooltip("The amount to add to faction standing (can be negative).")]
		public int Amount = 1;

		/// <inheritdoc />
		public override void Execute(ICharacter initiator, EventData eventData)
		{
#if UNITY_SERVER
			if (FactionTemplate == null || initiator == null)
			{
				return;
			}

			if (!initiator.TryGet(out IFactionController factionController))
			{
				return;
			}

			factionController.Add(FactionTemplate, Amount);
#endif
		}
	}
}