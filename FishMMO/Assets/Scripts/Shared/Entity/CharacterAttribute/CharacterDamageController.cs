using FishNet.Object;
using System;
using UnityEngine;

[RequireComponent(typeof(Character))]
public class CharacterDamageController : NetworkBehaviour, IDamageable, IHealable
{
	public Character Character;

	public bool Immortal = false;

	[Tooltip("The resource attribute the damage will be applied to.")]
	public CharacterAttributeTemplate ResourceAttribute;

	// subscribe to this event with Quests/Achievements that should update when the character is Damaged
	public event Action<Character, Character, int> OnDamaged;

#if !UNITY_SERVER || UNITY_EDITOR
	public bool ShowDamage = true;
	public event Action<Vector3, Color, float, string> OnDamageDisplay;
#endif

	// subscribe to this event with Quests/Achievements that should update when the character is Killed
	public event Action<Character> OnKilled;

	// subscribe to this event with Quests/Achievements that should update when the character is Healed
	public event Action<Character, int> OnHealed;

#if !UNITY_SERVER || UNITY_EDITOR
	public bool ShowHeals = true;
	public event Action<Vector3, Color, float, string> OnHealedDisplay;
#endif

	//public List<Character> Attackers;

	private CharacterResourceAttribute resourceInstance; // cache the resource

	public override void OnStartClient()
	{
		base.OnStartClient();
		if (Character == null ||
			ResourceAttribute == null ||
			!Character.AttributeController.TryGetResourceAttribute(ResourceAttribute.Name, out this.resourceInstance))
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
		if (Immortal) return;

		if (resourceInstance != null && resourceInstance.CurrentValue > 0)
		{
			amount = ApplyModifiers(Character, amount, damageAttribute);
			resourceInstance.Consume(amount);

			OnDamaged?.Invoke(attacker, Character, amount);

#if !UNITY_SERVER || UNITY_EDITOR
			if (ShowDamage)
			{
				Vector3 displayPos = Character.transform.position;
				displayPos.y += Character.CharacterController.FullCapsuleHeight;
				OnDamageDisplay?.Invoke(displayPos, new Color(255.0f, 128.0f, 128.0f), 10.0f, amount.ToString());
			}
#endif

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
			resourceInstance.Gain(amount);

			OnHealed?.Invoke(healer, amount);

#if !UNITY_SERVER || UNITY_EDITOR
			if (ShowHeals)
			{
				Vector3 displayPos = Character.transform.position;
				displayPos.y += Character.CharacterController.FullCapsuleHeight;
				OnHealedDisplay?.Invoke(displayPos, new Color(128.0f, 255.0f, 128.0f), 10.0f, amount.ToString());
			}
#endif
		}
	}
}