using System.Collections.Generic;

namespace FishMMO.Shared
{
	public class DeadNPCRespawnCondition : BaseRespawnCondition
	{
		public List<NPC> NPCs;

		public string GetFormattedDescription()
		{
			if (NPCs == null || NPCs.Count == 0)
				return "Respawn if all tracked NPCs are dead (no NPCs specified).";
			return $"Respawn if all {NPCs.Count} tracked NPCs are dead.";
		}

		public override bool OnCheckCondition(ObjectSpawner spawner)
		{
			if (NPCs == null ||
				NPCs.Count < 1)
			{
				return true;
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