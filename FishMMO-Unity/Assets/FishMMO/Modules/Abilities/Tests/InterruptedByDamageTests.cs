using System.IO;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins <see cref="AbilityTemplate.InterruptedByDamage"/> (issue #241): off unless authored, inert with no
	/// cast, and applied by the server ability system from the damage event.
	/// </summary>
	[TestFixture]
	public class InterruptedByDamageTests
	{
		[Test]
		public void IsOffUnlessAuthored()
		{
			AbilityTemplate template = ScriptableObject.CreateInstance<AbilityTemplate>();
			try
			{
				LogAssert.IsFalse(template.InterruptedByDamage, "existing abilities must not start breaking on damage");
			}
			finally
			{
				Object.DestroyImmediate(template);
			}
		}

		[Test]
		public void BreaksNothing_WhenNotCasting()
		{
			GameObject go = new GameObject("InterruptedByDamage");
			try
			{
				AbilityController controller = go.AddComponent<AbilityController>();
				LogAssert.IsFalse(controller.InterruptIfDamageBreaksCast(), "no cast in progress: nothing to break");
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void TheServerAbilitySystem_AppliesItFromTheDamageEvent()
		{
			string source = File.ReadAllText("Assets/FishMMO/Modules/Abilities/Server/Ability/AbilitySystem.cs").Replace("\r\n", "\n");
			LogAssert.IsTrue(source.Contains("ICharacterDamageController.OnDamaged += CharacterDamageController_OnDamaged;"), "subscribed at init");
			LogAssert.IsTrue(source.Contains("ICharacterDamageController.OnDamaged -= CharacterDamageController_OnDamaged;"), "and released");
			LogAssert.IsTrue(source.Contains("abilityController.InterruptIfDamageBreaksCast()"), "the handler breaks the cast");
		}
	}
}
