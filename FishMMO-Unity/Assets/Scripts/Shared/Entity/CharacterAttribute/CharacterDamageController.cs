#if !UNITY_SERVER
using FishMMO.Client;
#endif
using FishNet.Object;
using System;
using UnityEngine;

namespace FishMMO.Shared
{
	[RequireComponent(typeof(Character))]
	public class CharacterDamageController : NetworkBehaviour, IDamageable, IHealable
	{
		public Character Character;

		public bool Immortal = false;

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

		//public List<Character> Attackers;

		private CharacterResourceAttribute resourceInstance; // cache the resource

#if !UNITY_SERVER
		public bool ShowDamage = true;
		public event Func<string, Vector3, Color, float, float, bool, Cached3DLabel> OnDamageDisplay;

		public bool ShowHeals = true;
		public event Func<string, Vector3, Color, float, float, bool, Cached3DLabel> OnHealedDisplay;

		public override void OnStartClient()
		{
			base.OnStartClient();
			if (ResourceAttribute == null ||
				!Character.AttributeController.TryGetResourceAttribute(ResourceAttribute.ID, out this.resourceInstance))
			{
				throw new UnityException("Character Damage Controller ResourceAttribute is missing");
			}
			if (!base.IsOwner)
			{
				enabled = false;
				return;
			}



			if (Character.LabelMaker != null)
			{
				OnDamageDisplay += Character.LabelMaker.Display;
				OnHealedDisplay += Character.LabelMaker.Display;
			}
		}

		public override void OnStopClient()
		{
			base.OnStopClient();
			if (Character.LabelMaker != null)
			{
				OnDamageDisplay -= Character.LabelMaker.Display;
				OnHealedDisplay -= Character.LabelMaker.Display;
			}
		}
#endif

		public int ApplyModifiers(Character target, int amount, DamageAttributeTemplate damageAttribute)
		{
			const int MIN_DAMAGE = 0;
			const int MAX_DAMAGE = 999999;

			if (target == null || damageAttribute == null)
				return 0;

			if (target.AttributeController.TryGetAttribute(damageAttribute.Resistance.ID, out CharacterAttribute resistance))
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
				if (amount < 1)
				{
					return;
				}
				resourceInstance.Consume(amount);

				attacker.AchievementController.Increment(DamageAchievementTemplate, (uint)amount);
				Character.AchievementController.Increment(DamagedAchievementTemplate, (uint)amount);

#if !UNITY_SERVER
				if (ShowDamage)
				{
					Vector3 displayPos = Character.transform.position;
					displayPos.y += Character.CharacterController.FullCapsuleHeight;
					OnDamageDisplay?.Invoke(amount.ToString(), displayPos, new Color(255.0f, 128.0f, 128.0f), 10.0f, 10.0f, false);
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
			killer.AchievementController.Increment(KillAchievementTemplate, 1);
			Character.AchievementController.Increment(KilledAchievementTemplate, 1);

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

				healer.AchievementController.Increment(HealAchievementTemplate, (uint)amount);
				Character.AchievementController.Increment(HealedAchievementTemplate, (uint)amount);

#if !UNITY_SERVER
				if (ShowHeals)
				{
					Vector3 displayPos = Character.transform.position;
					displayPos.y += Character.CharacterController.FullCapsuleHeight;
					OnHealedDisplay?.Invoke(amount.ToString(), displayPos, new Color(128.0f, 255.0f, 128.0f), 10.0f, 10.0f, false);
				}
#endif
			}
		}
	}
}