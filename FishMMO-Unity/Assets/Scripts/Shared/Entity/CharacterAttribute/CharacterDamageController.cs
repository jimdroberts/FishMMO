using UnityEngine;

namespace FishMMO.Shared
{
	public class CharacterDamageController : CharacterBehaviour, ICharacterDamageController
	{
		/// <summary>
		/// Achievement template for dealing damage to another character.
		/// </summary>
		public AchievementTemplate DamageAchievementTemplate;
		/// <summary>
		/// Achievement template for receiving damage from another character.
		/// </summary>
		public AchievementTemplate DamagedAchievementTemplate;
		/// <summary>
		/// Achievement template for killing another character.
		/// </summary>
		public AchievementTemplate KillAchievementTemplate;
		/// <summary>
		/// Achievement template for being killed by another character.
		/// </summary>
		public AchievementTemplate KilledAchievementTemplate;
		/// <summary>
		/// Achievement template for healing another character.
		/// </summary>
		public AchievementTemplate HealAchievementTemplate;
		/// <summary>
		/// Achievement template for being healed by another character.
		/// </summary>
		public AchievementTemplate HealedAchievementTemplate;
		/// <summary>
		/// Achievement template for resurrecting another character.
		/// </summary>
		public AchievementTemplate ResurrectAchievementTemplate;
		/// <summary>
		/// Achievement template for being resurrected by another character.
		/// </summary>
		public AchievementTemplate ResurrectedAchievementTemplate;

		/// <summary>
		/// If true, this character cannot be damaged or killed.
		/// </summary>
		[SerializeField]
		private bool immortal = false;
		/// <summary>
		/// Gets or sets whether the character is immortal (cannot be damaged or killed).
		/// </summary>
		public bool Immortal { get { return this.immortal; } set { this.immortal = value; } }

		/// <summary>
		/// Returns true if the character is alive (resource attribute's current value is above zero).
		/// </summary>
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

		//public List<Character> Attackers; // Uncomment and implement if tracking attackers is needed.

		/// <summary>
		/// Cached reference to the character's health resource attribute.
		/// Lazily initialized on first access; throws if missing.
		/// </summary>
		private CharacterResourceAttribute resourceInstance;
		/// <summary>
		/// Gets the cached health resource attribute for this character.
		/// Throws an exception if the attribute controller or health attribute is missing.
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

		/// <summary>
		/// Applies resistance modifiers to the damage amount for the target character.
		/// Subtracts the target's resistance value from the incoming damage and clamps the result.
		/// </summary>
		/// <param name="target">The character receiving damage.</param>
		/// <param name="amount">The base damage amount.</param>
		/// <param name="damageAttribute">The damage type being applied.</param>
		/// <returns>The modified damage amount after resistance is applied.</returns>
		public int ApplyModifiers(ICharacter target, int amount, DamageAttributeTemplate damageAttribute)
		{
			const int MIN_DAMAGE = 0;
			const int MAX_DAMAGE = 999999;

			if (target == null ||
				!target.TryGet(out ICharacterAttributeController attributeController) ||
				damageAttribute == null)
				return 0;

			// If the target has a resistance attribute for this damage type, subtract its value from the damage.
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