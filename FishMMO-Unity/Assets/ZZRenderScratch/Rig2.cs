using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// Attribute controller fake: the source the resource bars, target frame and inspect window
	/// all read from.
	/// </summary>
	/// <remarks>
	/// Health, mana and stamina are resolved by template rather than by a fixed index, so the
	/// fixture looks up the real <c>CharacterAttributeTemplate</c> assets and registers them the
	/// way the boot-time loader does.
	/// </remarks>
	public sealed class FakeAttributes : ICharacterAttributeController
	{
		private readonly Dictionary<int, CharacterAttribute> attributes = new Dictionary<int, CharacterAttribute>();
		private readonly Dictionary<int, CharacterResourceAttribute> resources = new Dictionary<int, CharacterResourceAttribute>();

		private int healthID = -1, manaID = -1, staminaID = -1;

		public FakeAttributes(ICharacter character) { Character = character; }

		public ICharacter Character { get; private set; }
		public bool Initialized => true;
		public void InitializeOnce(ICharacter character) { Character = character; }
		public void OnStartCharacter() { }
		public void OnStopCharacter() { }

		public Dictionary<int, CharacterAttribute> Attributes => attributes;
		public Dictionary<int, CharacterResourceAttribute> ResourceAttributes => resources;

		public bool IsPropagating => false;
		public void BeginPropagation() { }
		public void EndPropagation() { }
		public void EnqueueNotification(CharacterAttribute attribute) { }
		public void BeginNotificationSuppression() { }
		public void EndNotificationSuppression() { }
		public void Regenerate(uint tick) { }
		public void ApplyResourceState(CharacterAttributeResourceState resourceState) { }
		public CharacterAttributeResourceState GetResourceState() => default;

		public void AddAttribute(CharacterAttribute instance)
		{
			if (instance == null) { return; }
			attributes[instance.Template.ID] = instance;
		}

		/// <summary>No modifier ledger on this render rig; nothing to release.</summary>
		public void ClearModifierSource(ModifierSource source) { }

		public void SetAttribute(int id, int value, int? modifier = null)
		{
			attributes[id] = new CharacterAttribute(this, id, value, modifier ?? 0);
		}

		public void SetResourceAttribute(int id, int value, float currentValue, int? modifier = null)
		{
			CharacterResourceAttribute r = new CharacterResourceAttribute(this, id, value, currentValue, modifier ?? 0);
			resources[id] = r;
			attributes[id] = r;
		}

		/// <summary>Records which template IDs stand for the three resources.</summary>
		public void BindResources(int health, int mana, int stamina)
		{
			healthID = health;
			manaID = mana;
			staminaID = stamina;
		}

		public bool TryGetAttribute(CharacterAttributeTemplate template, out CharacterAttribute attribute)
		{
			if (template == null) { attribute = null; return false; }
			return attributes.TryGetValue(template.ID, out attribute);
		}

		public bool TryGetAttribute(int id, out CharacterAttribute attribute)
			=> attributes.TryGetValue(id, out attribute);

		public bool TryGetResourceAttribute(CharacterAttributeTemplate template, out CharacterResourceAttribute attribute)
		{
			if (template == null) { attribute = null; return false; }
			return resources.TryGetValue(template.ID, out attribute);
		}

		public bool TryGetResourceAttribute(int id, out CharacterResourceAttribute attribute)
			=> resources.TryGetValue(id, out attribute);

		public bool TryGetHealthAttribute(out CharacterResourceAttribute health)
			=> resources.TryGetValue(healthID, out health);

		public bool TryGetManaAttribute(out CharacterResourceAttribute mana)
			=> resources.TryGetValue(manaID, out mana);

		public bool TryGetStaminaAttribute(out CharacterResourceAttribute stamina)
			=> resources.TryGetValue(staminaID, out stamina);
	}

	/// <summary>Achievement controller fake — the Achievements panel reads <c>Achievements</c>.</summary>
	public sealed class FakeAchievements : IAchievementController
	{
		private readonly Dictionary<int, Achievement> achievements = new Dictionary<int, Achievement>();

		public FakeAchievements(ICharacter character) { Character = character; }

		public ICharacter Character { get; private set; }
		public bool Initialized => true;
		public void InitializeOnce(ICharacter character) { Character = character; }
		public void OnStartCharacter() { }
		public void OnStopCharacter() { }

		public Dictionary<int, Achievement> Achievements => achievements;

		public void SetAchievement(int templateID, byte tier, uint value, bool skipEvent = false)
		{
			achievements[templateID] = new Achievement(templateID, tier, value);
		}

		public bool TryGetAchievement(int templateID, out Achievement achievement)
			=> achievements.TryGetValue(templateID, out achievement);

		public void Increment(AchievementTemplate template, uint amount)
		{
			if (template != null) { Increment(template.ID, amount); }
		}

		public void Increment(int templateID, uint amount)
		{
			if (achievements.TryGetValue(templateID, out Achievement a))
			{
				SetAchievement(templateID, a.CurrentTier, a.CurrentValue + amount);
			}
		}
	}

	/// <summary>Pet controller fake — the pet panel reads <c>Pets</c>, stance and movement order.</summary>
	public sealed class FakePet : IPetController
	{
		public FakePet(ICharacter character) { Character = character; }

		public ICharacter Character { get; private set; }
		public bool Initialized => true;
		public void InitializeOnce(ICharacter character) { Character = character; }
		public void OnStartCharacter() { }
		public void OnStopCharacter() { }

		private readonly List<Pet> pets = new List<Pet>();
		public IReadOnlyList<Pet> Pets => pets;
		public int PetSlots => PetSlotRules.DefaultSlots;
		public bool AddPet(Pet pet) { if (pet == null || pets.Contains(pet)) return false; pets.Add(pet); return true; }
		public bool RemovePet(Pet pet) => pets.Remove(pet);
		public void ClearPets() => pets.Clear();
		public PetStance Stance { get; set; } = PetStance.Defensive;
		public PetMovementOrder MovementOrder { get; set; } = PetMovementOrder.Follow;
		public int AttackPriority { get; set; } = PetAttackPriority.Default;

		public event Action<IPetController, ICharacter> OnOwnerAttacked;
#pragma warning disable 0067
		public event Action<IPetController> OnPetSlotsReduced;
#pragma warning restore 0067

		public List<Trigger> OnPetSummonTriggers { get; } = new List<Trigger>();
		public List<Trigger> OnPetDismissTriggers { get; } = new List<Trigger>();
	}
}
