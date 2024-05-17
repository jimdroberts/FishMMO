using System.Collections.Generic;

namespace FishMMO.Shared
{
	public class DeadNPCRespawnCondition : BaseRespawnCondition
	{
		public List<NPC> NPCs;

		public override bool OnCheckCondition(ObjectSpawner spawner)
		{
			if (NPCs == null ||
				NPCs.Count < 1)
			{
				return false;
			}

			foreach (NPC npc in NPCs)
			{
				if (npc == null ||
					!npc.TryGet(out ICharacterDamageController damageController))
				{
					continue;
				}

				if (damageController.IsAlive)
				{
					// if any NPC is alive still we can't respawn
					return false;
				}
			}
			// if all NPCs are dead we can respawn
			return true;
		}
	}
}