using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Controller for managing quest instances for a character. Handles quest lookup and acquisition.
	/// </summary>
	public class QuestController : CharacterBehaviour, IQuestController
	{
		/// <summary>
		/// Dictionary mapping quest names to quest instances for this character.
		/// </summary>
		private Dictionary<string, QuestInstance> quests = new Dictionary<string, QuestInstance>();

		/// <summary>
		/// Called when the character starts. Disables the controller if not owned by the local player.
		/// </summary>
		public override void OnStartCharacter()
		{
			base.OnStartCharacter();

			if (!base.IsOwner)
			{
				enabled = false;
				return;
			}
		}

		/// <summary>
		/// Public accessor for the quest dictionary.
		/// </summary>
		public Dictionary<string, QuestInstance> Quests
		{
			get { return this.quests; }
		}

		/// <summary>
		/// Attempts to retrieve a quest instance by name.
		/// </summary>
		/// <param name="name">The name of the quest to look up.</param>
		/// <param name="quest">The found quest instance, or null if not found.</param>
		/// <returns>True if the quest is found, false otherwise.</returns>
		public bool TryGetQuest(string name, out QuestInstance quest)
		{
			return this.quests.TryGetValue(name, out quest);
		}

		/// <summary>
		/// Unity Update loop. (Implementation for quest updates can be added here.)
		/// </summary>
		void Update()
		{
			// Quest update logic can be implemented here.
		}

		/// <summary>
		/// Acquires (accepts) a new quest for the character. (Implementation needed)
		/// </summary>
		/// <param name="quest">The quest template to acquire.</param>
		public void Acquire(QuestTemplate quest)
		{
			// Implementation for quest acquisition should be added here.
		}
	}
}