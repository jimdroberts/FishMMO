using System.IO;
using NUnit.Framework;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins three combat and client fixes of 2026-11-14 to their source, where a regression would be silent in play.
	/// </summary>
	public class HarmRuleAndEchoPinTests
	{
		private static string Read(string relative) =>
			ProjectFiles.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Assets/Scripts", relative));

		/// <summary>
		/// Every hostile route asks the same gate, so none is the one that still lands on a friend: debuffs, knockback,
		/// interrupts, taunts, buff-stripping dispels, drains, and the pet attack order.
		/// </summary>
		[Test]
		public void EveryHostileRouteAsksTheSameGate()
		{
			string evade = Read("Shared/Implementation/Entity/CharacterEvade.cs");
			LogAssert.IsTrue(evade.Contains("return RefusesHostileEffects(target) || !HarmRule.Allows(source, target);"),
				"the hostile-effect gate includes the harm rule");
			LogAssert.IsTrue(evade.Contains("RefusesHostileEffects(target, caster);"), "debuffs ask it with their caster");

			string actions = "Shared/Implementation/Entity/ECA/Actions/Character/";
			foreach ((string file, string call) in new[]
			{
				("KnockbackHitAction.cs", "CharacterEvade.RefusesHostileEffects(target, initiator)"),
				("InterruptAction.cs", "CharacterEvade.RefusesHostileEffects(target, initiator)"),
				("ApplyDispelAction.cs", "IncludeBuffs && !ReferenceEquals(target, initiator) && CharacterEvade.RefusesHostileEffects(target, initiator)"),
				("ConsumeResourceAction.cs", "CharacterEvade.RefusesHostileEffects(characterToConsume, initiator)"),
				("ApplyTauntAction.cs", "HarmRule.MayHarm(initiator, target, out _)"),
			})
			{
				LogAssert.IsTrue(Read(actions + file).Contains(call), $"{file} asks the harm rule");
			}

			string pets = Read("Server/Implementation/World/SceneServer/Pet/PetSystem.cs");
			LogAssert.IsTrue(pets.Contains("!HarmRule.MayHarm(petController.Pet, target, out _)"),
				"a pet is sent only at what its owner may harm");
		}

		/// <summary>
		/// A hit the server resolved and sent back was predicted again on the caster; the target's health had often already
		/// arrived (unreliable datagrams overtake the reliable hit message), so the bar showed the hit twice.
		/// </summary>
		[Test]
		public void AServerEchoedHitIsNotPredictedAgain()
		{
			foreach (string action in new[] { "ApplyDamageAction.cs", "ApplyHealAction.cs" })
			{
				string source = Read("Shared/Implementation/Entity/ECA/Actions/Character/" + action);
				LogAssert.IsTrue(source.Contains("IsEchoOnClient(initiator, eventData)"),
					$"{action} returns before predicting a server-echoed hit");
			}
		}

		/// <summary>Browser-paced frames are Firefox's fix only; every other browser keeps Unity's uncapped loop.</summary>
		[Test]
		public void OnlyFirefoxRunsOnAnimationFrames()
		{
			string jslib = Read("Client/WebGL/FrameLoop.jslib");
			LogAssert.IsTrue(jslib.Contains("/firefox/i.test(navigator.userAgent"), "the animation-frame loop is chosen for Firefox only");
			string loop = Read("Shared/Implementation/Bootstrap/WebFrameLoop.cs");
			LogAssert.IsTrue(loop.Contains("if (!OnAnimationFrames)") && loop.Contains("Application.targetFrameRate = framesPerSecond;"),
				"other browsers set targetFrameRate as before");
		}
	}
}
