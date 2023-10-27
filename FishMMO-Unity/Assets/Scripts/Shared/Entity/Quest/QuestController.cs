using FishNet.Object;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	[RequireComponent(typeof(Character))]
	public class QuestController : NetworkBehaviour
	{
		private Dictionary<string, QuestInstance> quests = new Dictionary<string, QuestInstance>();

		public Character Character;

		public override void OnStartClient()
		{
			base.OnStartClient();

			if (!base.IsOwner)
			{
				enabled = false;
				return;
			}
		}

		public Dictionary<string, QuestInstance> Quests
		{
			get
			{
				return this.quests;
			}
		}

		public bool TryGetQuest(string name, out QuestInstance quest)
		{
			return this.quests.TryGetValue(name, out quest);
		}

		void Update()
		{
		}

		public void Acquire(QuestTemplate quest)
		{

		}
	}
}