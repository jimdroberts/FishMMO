﻿using UnityEngine;

namespace FishMMO.Shared
{
	public class CharacterDamageController : CharacterBehaviour, ICharacterDamageController
	{
		public AchievementTemplate DamageAchievementTemplate;
		public AchievementTemplate DamagedAchievementTemplate;
		public AchievementTemplate KillAchievementTemplate;
		public AchievementTemplate KilledAchievementTemplate;
		public AchievementTemplate HealAchievementTemplate;
		public AchievementTemplate HealedAchievementTemplate;
		public AchievementTemplate ResurrectAchievementTemplate;
		public AchievementTemplate ResurrectedAchievementTemplate;

		[SerializeField]
		private bool immortal = false;
		public bool Immortal { get { return this.immortal; } set { this.immortal = value; } }

		public bool IsAlive
		{
			get
			{
				if (ResourceInstance == null)
				{
					return false;
				}
				return ResourceInstance.CurrentValue > 0;
			}
		}

		//public List<Character> Attackers;

		private CharacterResourceAttribute resourceInstance;
		/// <summary>
		/// Cache the resource
		/// </summary>
		public CharacterResourceAttribute ResourceInstance
		{
			get
			{
				if (resourceInstance == null)
				{
					if (!Character.TryGet(out ICharacterAttributeController attributeController))
					{
						throw new UnityException($"{gameObject.name} ICharacterAttributeController is missing");
					}
					if (!attributeController.TryGetHealthAttribute(out CharacterResourceAttribute health))
					{
						throw new UnityException($"{gameObject.name} Health Resource Attribute is missing");
					}
					resourceInstance = health;
				}
				return resourceInstance;
			}
		}

		public int ApplyModifiers(ICharacter target, int amount, DamageAttributeTemplate damageAttribute)
		{
			const int MIN_DAMAGE = 0;
			const int MAX_DAMAGE = 999999;

			if (target == null ||
				!target.TryGet(out ICharacterAttributeController attributeController) ||
				damageAttribute == null)
				return 0;

			if (attributeController.TryGetAttribute(damageAttribute.Resistance.ID, out CharacterAttribute resistance))
			{
				amount = (amount - resistance.FinalValue).Clamp(MIN_DAMAGE, MAX_DAMAGE);
			}
			return amount;
		}

		public void Damage(ICharacter attacker, int amount, DamageAttributeTemplate damageAttribute, bool ignoreAchievements = false)
		{
			if (Immortal)
			{
				return;
			}

			if (ResourceInstance == null)
			{
				return;
			}

			// We are already dead.
			if (ResourceInstance.CurrentValue <= 0.0f)
			{
				return;
			}

			amount = ApplyModifiers(Character, amount, damageAttribute);

			if (amount < 1)
			{
				return;
			}
			ResourceInstance.Consume(amount);

			ICharacterDamageController.OnDamaged?.Invoke(attacker, Character, amount, damageAttribute);

			uint fullAmount = (uint)amount;

			if (!ignoreAchievements)
			{
				if (attacker.TryGet(out IAchievementController attackerAchievementController))
				{
					attackerAchievementController.Increment(DamageAchievementTemplate, fullAmount);
				}

				if (Character.TryGet(out IAchievementController defenderAchievementController))
				{
					defenderAchievementController.Increment(DamagedAchievementTemplate, fullAmount);
				}
			}

			// Check if we died after taking damage.
			if (ResourceInstance.CurrentValue <= 0.0f)
			{
				Kill(attacker);
			}
		}

		public void Kill(ICharacter killer)
		{
			if (Immortal)
			{
				return;
			}

			if (killer != null)
			{
				// Reward the killer with faction.
				if (killer.TryGet(out IFactionController factionController) &&
					Character.TryGet(out IFactionController defenderFactionController))
				{
					factionController.AdjustFaction(defenderFactionController, 0.01f, 0.01f);
				}
				
				// Reward the killer with kill achievements.
				if (killer.TryGet(out IAchievementController killerAchievementController))
				{
					killerAchievementController.Increment(KillAchievementTemplate, 1);
				}
			}
			
			// Reward the defender with death achievements.
			if (Character.TryGet(out IAchievementController defenderAchievementController))
			{
				defenderAchievementController.Increment(KilledAchievementTemplate, 1);
			}

			// Remove all buffs
			if (Character.TryGet(out IBuffController buffController))
			{
				buffController.RemoveAll();
			}

			// Kill the players pet
			if (Character.TryGet(out IPetController petController) &&
				petController.Pet != null)
			{
				if (petController.Pet.TryGet(out ICharacterDamageController petCharacterDamageController))
				{
					petCharacterDamageController.Kill(null);
				}
			}

			ICharacterDamageController.OnKilled?.Invoke(killer, Character);

			//handle kill rewards

			/*if (Character.TryGet(out IQuestController questController))
			{
				//questController.OnKill(Entity);
			}
			if (Character.TryGet(out IAchievementController achievementController) &&
				KillAchievementTemplate != null &&
				KilledAchievementTemplate != null)
			{
				foreach (AchievementTemplate achievement in KilledAchievementTemplates)
				{
					achievement.OnGainValue.Invoke(Character, killer, 1);
				}
			}

			//KILLER
			CharacterAttributeController killerAttributes = killer.GetComponent<CharacterAttributeController>();
			if (killerAttributes != null)
			{
			}
			QuestController killersQuests = killer.GetComponent<QuestController>();
			if (killersQuests != null)
			{
				//killersQuests.OnKill(Entity);
			}
			if (OnKillAchievements != null && OnKillAchievements.Count > 0)
			{
				AchievementController achievements = killer.GetComponent<AchievementController>();
				if (achievements != null)
				{
					foreach (AchievementTemplate achievement in OnKillAchievements)
					{
						achievement.OnGainValue.Invoke(killer, Character, 1);
					}
				}
			}

			Destroy(this.gameObject);
			this.gameObject.SetActive(false);*/
		}

		public void Heal(ICharacter healer, int amount, bool ignoreAchievements = false)
		{
			if (ResourceInstance != null && ResourceInstance.CurrentValue > 0.0f)
			{
				ResourceInstance.Gain(amount);

				ICharacterDamageController.OnHealed?.Invoke(healer, Character, amount);

				uint fullAmount = (uint)amount;
				
				if (!ignoreAchievements)
				{
					if (healer != null &&
					healer.TryGet(out IAchievementController healerAchievementController))
					{
						healerAchievementController.Increment(HealAchievementTemplate, fullAmount);
					}
					if (Character != null &&
						Character.TryGet(out IAchievementController healedAchievementController)) 
					{
						healedAchievementController.Increment(HealedAchievementTemplate, fullAmount);
					}
				}
			}
		}

		public void CompleteHeal()
		{
			if (ResourceInstance != null)
			{
				float toHeal = ResourceInstance.FinalValue - ResourceInstance.CurrentValue;
				ResourceInstance.Gain(toHeal);
			}
		}
	}
}