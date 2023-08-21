using FishNet.Object;
using System;
using UnityEngine;

[RequireComponent(typeof(Character))]
public class CharacterDamageController : NetworkBehaviour, IDamageable, IHealable
{
	public Character character;

	public bool immortal = false;
	public bool showDamage = true;

	[Tooltip("The resource attribute the damage will be applied to.")]
	public CharacterAttributeTemplate ResourceAttribute;

	// subscribe to this event with Quests/Achievements that should update when the character is Damaged
	public event Action<Character, int> OnDamaged;

	// subscribe to this event with Quests/Achievements that should update when the character is Killed
	public event Action<Character> OnKilled;

	// subscribe to this event with Quests/Achievements that should update when the character is Healed
	public event Action<Character, int> OnHealed;

	//public List<Character> Attackers;

	private CharacterResourceAttribute resourceInstance; // cache the resource

	public override void OnStartClient()
	{
		base.OnStartClient();
		if (character == null ||
			ResourceAttribute == null ||
			!character.AttributeController.TryGetResourceAttribute(ResourceAttribute.Name, out this.resourceInstance))
		{
			throw new UnityException("Character Damage Controller ResourceAttribute is missing");
		}
		if (!base.IsOwner)
		{
			enabled = false;
			return;
		}
	}

	public int ApplyModifiers(Character target, int amount, DamageAttributeTemplate damageAttribute)
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
			amount = ApplyModifiers(character, amount, damageAttribute);
			resourceInstance.Consume(amount);

			// tell the client to display damage
			if (IsClient && showDamage)
			{
				//UILabel3D.Create(amount.ToString(), 24, damageAttribute.DisplayColor, true, transform);
			}

			OnDamaged?.Invoke(attacker, amount);

			// check if we died
			if (resourceInstance != null && resourceInstance.CurrentValue < 1)
			{
				Kill(attacker);
			}
		}
	}

	public void Kill(Character killer)
	{
		OnKilled?.Invoke(killer);

		//UILabel3D.Create("DEAD!", 32, Color.red, true, transform);

		//SELF
		/*EntitySpawnTracker spawnTracker = character.GetComponent<EntitySpawnTracker>();
		if (spawnTracker != null)
		{
			spawnTracker.OnKilled();
		}
		if (character.BuffController != null)
		{
			character.BuffController.RemoveAll();
		}
		if (character.QuestController != null)
		{
			//QuestController.OnKill(Entity);
		}
		if (character.AchievementController != null && OnKilledAchievements != null && OnKilledAchievements.Count > 0)
		{
			foreach (AchievementTemplate achievement in OnKilledAchievements)
			{
				achievement.OnGainValue.Invoke(character, killer, 1);
			}
		}

		//KILLER
		CharacterAttributeController killerAttributes = killer.GetComponent<CharacterAttributeController>();
		if (killerAttributes != null)
		{
			//handle kill rewards?
			CharacterAttribute experienceAttribute;
			if (killerAttributes.TryGetAttribute("Experience", out experienceAttribute))
			{

			}
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
					achievement.OnGainValue.Invoke(killer, character, 1);
				}
			}
		}

		Destroy(this.gameObject);
		this.gameObject.SetActive(false);*/
	}

	public void Heal(Character healer, int amount)
	{
		if (resourceInstance != null && resourceInstance.CurrentValue > 0.0f)
		{
			//UILabel3D.Create(amount.ToString(), 24, Color.blue, true, transform);

			resourceInstance.Gain(amount);

			OnHealed?.Invoke(healer, amount);
		}
	}
}