using System;
using System.Collections.Generic;
using UnityEditor;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// The Pets module's part of the render rig: a fake pet roster on the rig's characters, and the pet panel
	/// in the HUD overlap probe. Moved out of the core rig with the module.
	/// </summary>
	public static class PetsRenderScratch
	{
		/// <summary>The pet panel's layout.</summary>
		public const string PetControlUxml = "Assets/FishMMO/Modules/Pets/Client/Pet/UIPetControl.uxml";

		[InitializeOnLoadMethod]
		private static void Install()
		{
			Rig.ExtraFakes -= AddFakes;
			Rig.ExtraFakes += AddFakes;
			if (!HudOverlapProbe.Mounts.Exists(m => m.Name == "UIPetControl"))
			{
				HudOverlapProbe.Mounts.Add(new HudOverlapProbe.Mount { Name = "UIPetControl", Uxml = PetControlUxml, Selector = "pet-root", PanelType = typeof(UITKPetControl) });
			}
		}

		private static void AddFakes(ICharacter character, Dictionary<Type, ICharacterBehaviour> behaviours)
		{
			Rig.Register<IPetController>(behaviours, new FakePet(character));
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
