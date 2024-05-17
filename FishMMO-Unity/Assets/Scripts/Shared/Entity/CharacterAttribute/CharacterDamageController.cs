using System;
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

		public Action<ICharacter, int> OnDamaged;
		public Action<ICharacter> OnKilled;
		public Action<ICharacter, int> OnHealed;

		//public List<Character> Attackers;

		private CharacterResourceAttribute resourceInstance; // cache the resource

#if !UNITY_SERVER
		public bool ShowDamage = true;
		/// <summary>
		/// <text, position, color, fontSize, persistTime, manualCache>
		/// </summary>
		public event Func<string, Vector3, Color, float, float, bool, IReference> OnDamageDisplay;

		public bool ShowHeals = true;
		/// <summary>
		/// <text, position, color, fontSize, persistTime, manualCache>
		/// </summary>
		public event Func<string, Vector3, Color, float, float, bool, IReference> OnHealedDisplay;

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

			if (resourceInstance != null && resourceInstance.CurrentValue > 0)
			{
				amount = ApplyModifiers(Character, amount, damageAttribute);
				if (amount < 1)
				{
					return;
				}
				resourceInstance.Consume(amount);

				if (attacker.TryGet(out IAchievementController attackerAchievementController))
				{
					attackerAchievementController.Increment(DamageAchievementTemplate, (uint)amount);
				}
				
				if (Character.TryGet(out IAchievementController defenderAchievementController))
				{
					defenderAchievementController.Increment(DamagedAchievementTemplate, (uint)amount);
				}

#if !UNITY_SERVER
				if (PlayerCharacter != null && ShowDamage)
				{
					Vector3 displayPos = PlayerCharacter.Transform.position;
					displayPos.y += PlayerCharacter.CharacterController.FullCapsuleHeight;
					OnDamageDisplay?.Invoke(amount.ToString(), displayPos, damageAttribute.DisplayColor, 4.0f, 1.0f, false);
				}
#endif

				// check if we died
				if (resourceInstance != null && resourceInstance.CurrentValue < 1)
				{
					Kill(attacker);
				}
			}
		}

		public void Kill(ICharacter killer)
		{
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

		public void Heal(ICharacter healer, int amount)
		{
			if (resourceInstance != null && resourceInstance.CurrentValue > 0.0f)
			{
				resourceInstance.Gain(amount);
				
				if (healer != null &&
					healer.TryGet(out IAchievementController healerAchievementController))
				{
					healerAchievementController.Increment(HealAchievementTemplate, (uint)amount);
				}
				if (Character != null &&
					Character.TryGet(out IAchievementController healedAchievementController)) 
				{
					healedAchievementController.Increment(HealedAchievementTemplate, (uint)amount);
				}

#if !UNITY_SERVER
				if (PlayerCharacter != null &&
					ShowHeals)
				{
					Vector3 displayPos = PlayerCharacter.Transform.position;
					displayPos.y += PlayerCharacter.CharacterController.FullCapsuleHeight;
					OnHealedDisplay?.Invoke(amount.ToString(), displayPos, new TinyColor(64, 64, 255).ToUnityColor(), 4.0f, 1.0f, false);
				}
#endif
			}
		}
	}
}