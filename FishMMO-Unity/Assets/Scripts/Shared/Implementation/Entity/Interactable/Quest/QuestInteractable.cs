using System.Collections.Generic;
using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents an NPC that players can interact with to accept or turn in quests.
	/// Configured via a list of <see cref="QuestTemplate"/> assets.
	/// </summary>
	[RequireComponent(typeof(SceneObjectNamer))]
	public class QuestInteractable : Interactable, IQuestInteractable
	{
		/// <summary>
		/// The quest templates offered by this interactable.
		/// </summary>
		public List<QuestTemplate> Templates = new List<QuestTemplate>();

		/// <inheritdoc />
		List<QuestTemplate> IQuestInteractable.QuestTemplates => Templates;

		private string title = "Quest";

		/// <summary>
		/// Display title shown above the interactable.
		/// </summary>
		public override string Title { get { return title; } }

		/// <summary>
		/// Title color for the quest interactable UI label.
		/// </summary>
		public override Color TitleColor { get { return TinyColor.ToUnityColor(TinyColor.gold); } }

		public override void OnAwake()
		{
			base.OnAwake();

			if (Templates != null && Templates.Count > 0 && Templates[0] != null)
			{
				title = Templates[0].Name;
			}
		}

		public override bool CanInteract(IPlayerCharacter character)
		{
			if (Templates == null ||
				Templates.Count < 1 ||
				!base.CanInteract(character))
			{
				return false;
			}
			return true;
		}
	}
}