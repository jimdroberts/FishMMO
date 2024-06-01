using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	public class CharacterDamageController : CharacterBehaviour, ICharacterDamageController
	{
		[Tooltip("The resource attribute the damage will be applied to.")]
		public CharacterAttributeTemplate ResourceAttribute;

		public AchievementTemplate DamageAchievementTemplate;
		public AchievementTemplate DamagedAchievementTemplate;
		public AchievementTemplate KillAchievementTemplate;
		public AchievementTemplate KilledAchievementTemplate;
		public AchievementTemplate HealAchievementTemplate;
		public AchievementTemplate HealedAchievementTemplate;
		public AchievementTemplate ResurrectAchievementTemplate;
		public AchievementTemplate ResurrectedAchievementTemplate;

		public bool Immortal { get; set; }

		public bool IsAlive
		{
			get
			{
				if (resourceInstance == null)
				{
					return false;
				}
				return resourceInstance.CurrentValue > 0;
			}
		}

		//public List<Character> Attackers;

		private CharacterResourceAttribute resourceInstance; // cache the resource

#if !UNITY_SERVER
		public override void OnStartCharacter()
		{
			base.OnStartCharacter();
			if (ResourceAttribute == null ||
				!Character.TryGet(out ICharacterAttributeController attributeController) ||
				!attributeController.TryGetResourceAttribute(ResourceAttribute.ID, out this.resourceInstance))
			{
				throw new UnityException("Character Damage Controller ResourceAttribute is missing");
			}
			if (!base.IsOwner)
			{
				enabled = false;
				return;
			}
		}
#endif

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

		public void Damage(ICharacter attacker, int amount, DamageAttributeTemplate damageAttribute)
		{
			if (Immortal)
			{
				return;
			}

			if (resourceInstance != null && resourceInstance.CurrentValue > 0.0f)
			{
				amount = ApplyModifiers(Character, amount, damageAttribute);
				if (amount < 1)
				{
					return;
				}
				resourceInstance.Consume(amount);

				ICharacterDamageController.OnDamaged?.Invoke(attacker, Character, amount, damageAttribute);

				uint fullAmount = (uint)amount;

				if (attacker.TryGet(out IAchievementController attackerAchievementController))
				{
					attackerAchievementController.Increment(DamageAchievementTemplate, fullAmount);
				}
				
				if (Character.TryGet(out IAchievementController defenderAchievementController))
				{
					defenderAchievementController.Increment(DamagedAchievementTemplate, fullAmount);
				}

				// check if we died
				if (resourceInstance.CurrentValue <= 0.001f)
				{
					Kill(attacker);
				}
			}
		}

		public void Kill(ICharacter killer)
		{
			if (Immortal)
			{
				return;
			}

			if (killer != null &&
				killer.TryGet(out IAchievementController killerAchievementController))
			{
				killerAchievementController.Increment(KillAchievementTemplate, 1);
			}
			if (Character != null &&
				Character.TryGet(out IAchievementController defenderAchievementController))
			{
				defenderAchievementController.Increment(KilledAchievementTemplate, 1);
			}

			// SELF
			if (Character.TryGet(out IBuffController buffController))
			{
				buffController.RemoveAll();
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

		public void Heal(ICharacter healer, int amount)
		{
			if (resourceInstance != null && resourceInstance.CurrentValue > 0.0f)
			{
				resourceInstance.Gain(amount);

				ICharacterDamageController.OnHealed?.Invoke(healer, Character, amount);

				uint fullAmount = (uint)amount;
				
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
}