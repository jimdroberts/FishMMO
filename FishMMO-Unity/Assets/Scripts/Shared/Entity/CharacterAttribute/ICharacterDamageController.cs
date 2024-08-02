using System;
using UnityEngine;

namespace FishMMO.Shared
{
	public interface ICharacterDamageController : ICharacterBehaviour, IDamageable, IHealable
	{
		static Action<ICharacter, ICharacter, int, DamageAttributeTemplate> OnDamaged;
		static Action<ICharacter, ICharacter> OnKilled;
		static Action<ICharacter, ICharacter, int> OnHealed;

		bool Immortal { get; set; }
		bool IsAlive { get; }
		CharacterResourceAttribute ResourceInstance { get; }
		void Kill(ICharacter killer);
		void CompleteHeal();
	}
}