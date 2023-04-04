using FishNet.Object;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Character))]
public class CharacterDamageController : NetworkBehaviour, IDamageable, IHealable
{
	public Character character;

	public bool immortal = false;

	[Tooltip("The resource attribute the damage will be applied to.")]
	public CharacterAttributeTemplate resourceAttribute;

	public List<AchievementTemplate> OnDamagedAchievements;
	public List<AchievementTemplate> OnDamageDealtAchievements;

	public List<AchievementTemplate> OnHealedAchievements;
	public List<AchievementTemplate> OnHealAchievements;

	//public List<Character> Attackers;

	private CharacterResourceAttribute resourceInstance; // cache the resource

	public override void OnStartClient()
	{
		base.OnStartClient();
		if (character == null || resourceAttribute == null || !character.AttributeController.TryGetResourceAttribute(resourceAttribute.Name, out this.resourceInstance))
		{
			throw new UnityException("Character Damage Controller ResourceAttribute is missing");
		}
		if (!base.IsOwner)
		{
			enabled = false;
			return;
		}
	}

	public int ApplyModifiers(Character target, DamageAttributeTemplate damageAttribute, int amount)
	{
		const int MIN_DAMAGE = 0;
		const int MAX_DAMAGE = 999999;

		if (target == null || damageAttribute == null)
			return 0;

		if (target.AttributeController.TryGetAttribute(damageAttribute.Resistance.Name, out CharacterAttribute resistance))
		{
			amount = (amount - resistance.FinalValue).Clamp(MIN_DAMAGE, MAX_DAMAGE);
		}
		return amount;
	}

	public void Damage(Character attacker, int amount, DamageAttributeTemplate damageAttribute)
	{
		if (immortal) return;

		if (resourceInstance != null && resourceInstance.CurrentValue > 0)
		{
			amount = ApplyModifiers(character, damageAttribute, amount);
			resourceInstance.Consume(amount);

			// tell the client to display damage
			//UILabel3D.Create(amount.ToString(), 24, damageAttribute.DisplayColor, true, transform);

			//SELF
			/*if (character.QuestController != null)
			{
				//QuestController.OnDamageTaken(Entity, amount);
			}
			if (character.AchievementController != null && OnDamagedAchievements != null && OnDamagedAchievements.Count > 0)
			{
				foreach (AchievementTemplate achievement in OnDamagedAchievements)
				{
					achievement.OnGainValue.Invoke(character, attacker, amount);
				}
			}

			//ATTACKER
			QuestController attackerQuests = attacker.GetComponent<QuestController>();
			if (attackerQuests != null)
			{
				//attackerQuests.OnDamageDealt(Entity, amount);
			}
			if (OnDamageDealtAchievements != null && OnDamageDealtAchievements.Count > 0)
			{
				AchievementController achievements = attacker.GetComponent<AchievementController>();
				if (achievements != null)
				{
					foreach (AchievementTemplate achievement in OnDamageDealtAchievements)
					{
						achievement.OnGainValue.Invoke(attacker, character, amount);
					}
				}
			}*/

			// check if we died
			if (resourceInstance != null && resourceInstance.CurrentValue < 1 && character.DeathController != null)
			{
				character.DeathController.Kill(attacker);
			}
		}
	}

	public void Heal(Character healer, int amount)
	{
		if (resourceInstance != null && resourceInstance.CurrentValue > 0.0f)
		{
			//UILabel3D.Create(amount.ToString(), 24, Color.blue, true, transform);

			resourceInstance.Gain(amount);

			//SELF
			if (character.QuestController != null)
			{
				//QuestController.OnHealed(Entity, amount);
			}
			if (character.AchievementController != null && OnHealedAchievements != null && OnHealedAchievements.Count > 0)
			{
				foreach (AchievementTemplate achievement in OnHealAchievements)
				{
					achievement.OnGainValue.Invoke(character, healer, amount);
				}
			}

			//ATTACKER
			QuestController healerQuests = healer.GetComponent<QuestController>();
			if (healerQuests != null)
			{
				//healerQuests.OnHeal(Entity, amount);
			}
			if (OnHealAchievements != null && OnHealAchievements.Count > 0)
			{
				AchievementController achievements = healer.GetComponent<AchievementController>();
				if (achievements != null)
				{
					foreach (AchievementTemplate achievement in OnHealAchievements)
					{
						achievement.OnGainValue.Invoke(healer, character, amount);
					}
				}
			}
		}
	}
}