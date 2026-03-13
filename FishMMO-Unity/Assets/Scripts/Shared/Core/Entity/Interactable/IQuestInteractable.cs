using System;
using System.Collections.Generic;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Server-side interface for quest interactables.
	/// Exposes the list of quest templates available from this NPC.
	/// </summary>
	public interface IQuestInteractable : IInteractable
	{
		/// <summary>
		/// The quest templates offered by this interactable.
		/// </summary>
		List<QuestTemplate> QuestTemplates { get; }
	}
}
