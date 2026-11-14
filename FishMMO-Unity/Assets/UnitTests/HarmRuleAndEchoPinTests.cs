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
		/// None of the shipped damage events carried an alliance condition, so every hit landed on neutrals, allies, party
		/// and guild. The rule now lives where every damage source passes.
		/// </summary>
		[Test]
		public void EveryDamageSourceAsksWhoMayBeHarmed()
		{
			string damage = Read("Shared/Implementation/Entity/Prediction/CharacterAttribute/CharacterDamageController.cs");
			LogAssert.IsTrue(damage.Contains("if (!FactionHarm.Allows(attacker, Character))"),
				"CharacterDamageController.Damage refuses harm the faction rule does not allow");

			string harm = Read("Shared/Implementation/Entity/Faction/FactionHarm.cs");
			LogAssert.IsTrue(harm.Contains("ArenaTeamRegistry.TryResolveAlliance(attackerSide, defenderSide, out FactionAllianceLevel arenaLevel)"),
				"inside an arena the match's sides decide, whatever anyone's toggle says");
			LogAssert.IsTrue(harm.Contains("defenderFaction.AggressionEnabled") && harm.Contains("attackerFaction.AggressionEnabled"),
				"either side's aggression opens neutrals (the defender's is how a struck neutral fights back)");
			LogAssert.IsTrue(harm.Contains("character is Pet pet && pet.PetOwner != null ? pet.PetOwner : character"),
				"a pet answers as its owner, attacking and attacked");
			int companions = harm.IndexOf("if (SharesSide(attackerSide, defenderSide))");
			LogAssert.IsTrue(companions > harm.IndexOf("ArenaTeamRegistry.TryResolveAlliance(attackerSide") &&
				companions < harm.IndexOf("if (defenderFaction.AggressionEnabled)") &&
				companions < harm.IndexOf("if (attackerFaction.AggressionEnabled)"),
				"party, guild and one's own pet are refused after the arena and before either toggle: aggression is never friendly fire");
			LogAssert.IsTrue(harm.Contains("party.ID != 0 && party.ID == otherParty.ID") && harm.Contains("guild.ID != 0 && guild.ID == otherGuild.ID"),
				"companions are the same non-zero party or guild");
		}

		/// <summary>
		/// Every hostile route asks the same gate, so none is the one that still lands on a friend: debuffs, knockback,
		/// interrupts, taunts, buff-stripping dispels, drains, and the pet attack order.
		/// </summary>
		[Test]
		public void EveryHostileRouteAsksTheSameGate()
		{
			string evade = Read("Shared/Implementation/Entity/CharacterEvade.cs");
			LogAssert.IsTrue(evade.Contains("return RefusesHostileEffects(target) || !FactionHarm.Allows(source, target);"),
				"the hostile-effect gate includes the harm rule");
			LogAssert.IsTrue(evade.Contains("RefusesHostileEffects(target, caster);"), "debuffs ask it with their caster");

			string actions = "Shared/Implementation/Entity/ECA/Actions/Character/";
			foreach ((string file, string call) in new[]
			{
				("KnockbackHitAction.cs", "CharacterEvade.RefusesHostileEffects(target, initiator)"),
				("InterruptAction.cs", "CharacterEvade.RefusesHostileEffects(target, initiator)"),
				("ApplyDispelAction.cs", "IncludeBuffs && !ReferenceEquals(target, initiator) && CharacterEvade.RefusesHostileEffects(target, initiator)"),
				("ConsumeResourceAction.cs", "CharacterEvade.RefusesHostileEffects(characterToConsume, initiator)"),
				("ApplyTauntAction.cs", "FactionHarm.MayHarm(initiator, target, out _)"),
			})
			{
				LogAssert.IsTrue(Read(actions + file).Contains(call), $"{file} asks the harm rule");
			}

			string pets = Read("Server/Implementation/World/SceneServer/Pet/PetSystem.cs");
			LogAssert.IsTrue(pets.Contains("!FactionHarm.MayHarm(petController.Pet, target, out _)"),
				"a pet is sent only at what its owner may harm");
		}

		/// <summary>
		/// Nameplates carry a standing mark (swords, lozenge, shield) tinted by the viewer's standing colour; the player's
		/// own plate shows the swords only while their aggression is on.
		/// </summary>
		[Test]
		public void NameplateMarksFollowStanding()
		{
			string display = Read("Client/World/ClientNameplateDisplay.cs");
			LogAssert.IsTrue(display.Contains("NameplateMark mark = Standing(localCharacter, character, out Color tint);") &&
				display.Contains("plate.SetMark(mark, tint);") && display.Contains("plate.AllianceTint = tint;"),
				"every plate the sweep draws is marked and coloured by the one standing rule");
			LogAssert.IsTrue(display.Contains("tint = aggressive ? ColorVision.Hostile : ColorVision.Neutral;") &&
				display.Contains("return aggressive ? NameplateMark.Hostile : NameplateMark.None;"),
				"the player's own plate is red with crossed swords while aggression is on, neutral and unmarked while off");
			LogAssert.IsTrue(display.Contains("tint = aggressive ? ColorVision.Hostile : ColorVision.Friendly;") &&
				display.Contains("return aggressive ? NameplateMark.Hostile : NameplateMark.Friendly;"),
				"the player's own pet is red with crossed swords while their aggression is on, a friendly shield while off");
			LogAssert.IsTrue(display.Contains("				if (visible)\n				{"),
				"the sweep marks every visible plate, the player's own pet included");
			LogAssert.IsTrue(display.Contains("if (SameGuild(instance?.localCharacter, character))") &&
				display.Contains("plate.SetLine(NameplateSlot.GuildName, guildLine.Text, ColorVision.Friendly);") &&
				display.Contains("plate.SetLine(NameplateSlot.GuildName, guildLine.Text);"),
				"guild rows are green for the local player's guild, own row included, and grey for every other guild");
			LogAssert.IsTrue(Read("Client/Client.cs").Contains("ClientNameplateDisplay.ApplyGuildRowColor(c);"),
				"a guild row is coloured the moment it is written, not a sweep later");

			string target = Read("Client/GUI/World/Target/UITKTarget.cs");
			LogAssert.IsTrue(target.Contains("mark = ClientNameplateDisplay.Standing(Character, card.Character, out color);"),
				"both target cards take their colour and mark from the plates' rule");
			LogAssert.IsTrue(target.Contains("RefreshStanding(pinnedCard)") && target.Contains("RefreshStanding(hoverCard)"),
				"a card's standing is re-read while it is up, so a pin follows an aggression toggle");
			string targetUxml = Read("Client/GUI/World/Target/UITarget.uxml");
			LogAssert.IsTrue(System.Text.RegularExpressions.Regex.Matches(targetUxml, "name=\"target-mark\"").Count == 2,
				"the pinned and the hover card each carry a mark element");
			string targetUss = Read("Client/GUI/World/Target/UITarget.uss");

			string controller = Read("Shared/Implementation/Entity/Faction/FactionController.cs");
			LogAssert.IsTrue(controller.Contains("FactionAllianceLevel allianceLevel = GetDisplayedAllianceLevel(otherFactionController);"),
				"the nameplate colour and the mark follow one standing rule");

			string uss = Read("Client/GUI/Nameplates/UINameplates.uss");
			foreach (string art in new[] { "NameplateHostile.png", "NameplateNeutral.png", "NameplateFriendly.png" })
			{
				LogAssert.IsTrue(uss.Contains($"url(\"Icons/{art}\")"), $"UINameplates.uss draws {art}");
				LogAssert.IsTrue(targetUss.Contains($"url(\"../../Nameplates/Icons/{art}\")"), $"UITarget.uss draws {art}");
				LogAssert.IsTrue(File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "Assets/Scripts/Client/GUI/Nameplates/Icons", art)),
					$"{art} ships beside the stylesheet");
			}
		}

		/// <summary>The toggle is the server's to write, and everyone reads it.</summary>
		[Test]
		public void AggressionIsServerWrittenAndObserved()
		{
			string controller = Read("Shared/Implementation/Entity/Faction/FactionController.cs");
			LogAssert.IsTrue(controller.Contains("WritePermission = WritePermission.ServerOnly") &&
				controller.Contains("ReadPermission = ReadPermission.Observers"),
				"the aggression SyncVar is written by the server only and read by owner and observers");
			LogAssert.IsTrue(FactionController.AggressionLockSeconds > 0f,
				"turning aggression off is locked for a while after a hit that needed it");
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
