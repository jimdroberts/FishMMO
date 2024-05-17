using System;
using UnityEngine;

namespace FishMMO.Shared
{
	public interface ICharacterDamageController : ICharacterBehaviour, IDamageable, IHealable
	{
		bool Immortal { get; set; }
		bool IsAlive { get; }
		void Kill(ICharacter killer);

#if !UNITY_SERVER
		event Func<string, Vector3, Color, float, float, bool, IReference> OnDamageDisplay;
		event Func<string, Vector3, Color, float, float, bool, IReference> OnHealedDisplay;
#endif
	}
}