using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that advances a specific quest objective for the initiating character.
	/// Typically used in dialogue OnSelect actions or interactable triggers.
	/// Server-only execution.
	/// </summary>
	[Serializable]
	public class AdvanceQuestObjectiveAction : BaseAction
	{
		/// <summary>
		/// The quest template containing the objective to advance.
		/// </summary>
		[Tooltip("The quest template containing the objective to advance.")]
		public QuestTemplate QuestTemplate;

		/// <summary>
		/// Zero-based index of the objective to advance.
		/// </summary>
		[Tooltip("Zero-based index of the objective to advance.")]
		public int ObjectiveIndex;

		/// <summary>
		/// The amount to increment the objective by.
		/// </summary>
		[Tooltip("The amount to increment the objective by.")]
		[Min(1)]
		public long Amount = 1;

		/// <inheritdoc />
		public override void Execute(ICharacter initiator, EventData eventData)
		{
#if UNITY_SERVER
			if (QuestTemplate == null || initiator == null)
			{
				return;
			}

			if (!initiator.TryGet(out IQuestController questController))
			{
				return;
			}

			questController.AdvanceObjective(QuestTemplate.Name, ObjectiveIndex, Amount);
#endif
		}
	}
}