using System.IO;
using NUnit.Framework;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins three combat and client fixes of 2026-11-14 to their source, where a regression would be silent in play.
	/// </summary>
	public class FactionHarmTests
	{
		private static string Read(string relative) =>
			ProjectFiles.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Assets/FishMMO", relative));

		private static string ReadModule(string relative) =>
			ProjectFiles.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Assets/FishMMO/Modules/Factions", relative));

		/// <summary>
		/// None of the shipped damage events carried an alliance condition, so every hit landed on neutrals, allies, party
		/// and guild. The rule now lives where every damage source passes.
		/// </summary>
		[Test]
		public void EveryDamageSourceAsksWhoMayBeHarmed()
		{
			string damage = Read("Shared/Implementation/Entity/Prediction/CharacterAttribute/CharacterDamageController.cs");
			LogAssert.IsTrue(damage.Contains("if (!HarmRule.Allows(attacker, Character))"),
				"CharacterDamageController.Damage refuses harm the rule does not allow");

			/* The rule is Core (HarmRule); features supply relations, asked in order until one decides: the arena's sides
			 * first (whatever anyone's toggle says), then wildlife, then companions (never friendly fire), then factions and
			 * the toggles. */
			var order = new System.Collections.Generic.List<System.Type>();
			foreach (IHarmRelation relation in HarmRule.Relations)
			{
				order.Add(relation.GetType());
			}
			// The arena's relation is the Matchmaking module's; by name, and only when that module is installed.
			int arena = order.FindIndex(t => t.Name == "ArenaHarmRelation");
			int wildlife = order.IndexOf(typeof(WildlifeHarmRelation));
			int companions = order.IndexOf(typeof(CompanionHarmRelation));
			int factions = order.IndexOf(typeof(FactionHarmRelation));
			LogAssert.IsTrue(wildlife >= 0 && companions >= 0 && factions >= 0, "the shipped relations are discovered");
			LogAssert.IsTrue((arena < 0 || arena < wildlife) && wildlife < companions && companions < factions,
				"arena, then wildlife, then companions, then factions: aggression is never friendly fire");

			string rule = Read("Shared/Implementation/Entity/Relations/HarmRule.cs");
			// Through the core companion seam since 2026-11-20 (a pet answers NPC.CompanionOwner).
			LogAssert.IsTrue(rule.Contains("character is NPC npc && npc.CompanionOwner != null ? npc.CompanionOwner : character"),
				"a pet answers as its owner, attacking and attacked");
			LogAssert.IsTrue(rule.Contains("party.ID != 0 && party.ID == otherParty.ID") && rule.Contains("guild.ID != 0 && guild.ID == otherGuild.ID"),
				"companions are the same non-zero party or guild");
			const string ArenaRelationPath = "Assets/FishMMO/Modules/Matchmaking/Runtime/ArenaHarmRelation.cs";
			if (ProjectFiles.Exists(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), ArenaRelationPath)))
			{
				LogAssert.IsTrue(ProjectFiles.ReadAllText(ArenaRelationPath).Contains("ArenaTeamRegistry.TryResolveAlliance(attackerSide, defenderSide, out FactionAllianceLevel arenaLevel)"),
					"inside an arena the match's sides decide");
			}
			string faction = ReadModule("Runtime/FactionHarmRelation.cs");
			LogAssert.IsTrue(faction.Contains("defenderFaction.AggressionEnabled") && faction.Contains("attackerFaction.AggressionEnabled"),
				"either side's aggression opens neutrals (the defender's is how a struck neutral fights back)");
		}

		/// <summary>With a feature's relation removed (its module deleted), the rule still answers, without that feature's say.</summary>
		[Test]
		public void TheRuleAnswersWithAnyRelationRemoved()
		{
			try
			{
				LogAssert.IsTrue(HarmRule.Uninstall(typeof(FactionHarmRelation)), "the faction relation was installed");
				foreach (IHarmRelation relation in HarmRule.Relations)
				{
					LogAssert.IsFalse(relation is FactionHarmRelation, "and is gone");
				}
				LogAssert.IsTrue(HarmRule.MayHarm(null, null, out FishMMO.Shared.Core.ICharacter aggressor) && aggressor == null, "the world harms freely");
			}
			finally
			{
				HarmRule.Rediscover();
			}
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

			string controller = ReadModule("Runtime/FactionController.cs");
			LogAssert.IsTrue(controller.Contains("FactionAllianceLevel allianceLevel = GetDisplayedAllianceLevel(otherFactionController);"),
				"the nameplate colour and the mark follow one standing rule");

			string uss = Read("Client/GUI/Nameplates/UINameplates.uss");
			foreach (string art in new[] { "NameplateHostile.png", "NameplateNeutral.png", "NameplateFriendly.png" })
			{
				LogAssert.IsTrue(uss.Contains($"url(\"Icons/{art}\")"), $"UINameplates.uss draws {art}");
				LogAssert.IsTrue(targetUss.Contains($"url(\"../../Nameplates/Icons/{art}\")"), $"UITarget.uss draws {art}");
				LogAssert.IsTrue(File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "Assets/FishMMO/Client/GUI/Nameplates/Icons", art)),
					$"{art} ships beside the stylesheet");
			}
		}

		/// <summary>The toggle is the server's to write, and everyone reads it.</summary>
		[Test]
		public void AggressionIsServerWrittenAndObserved()
		{
			string controller = ReadModule("Runtime/FactionController.cs");
			/* Was a SyncVar (server-written, observer-read). A character part has none: the toggle rides the spawn
			 * payload for every reader and each change goes to the owner and every observer. */
			LogAssert.IsTrue(controller.Contains("writer.WriteBoolean(aggressionEnabled);") &&
				controller.Contains("ObserverBroadcastScope.BroadcastToObserversExceptOwner(NetworkObject, state, Channel.Reliable);") &&
				controller.Contains("owner.Broadcast(state, true, Channel.Reliable);"),
				"the aggression toggle is in the spawn payload and each change reaches the owner and every observer");
			LogAssert.IsTrue(controller.Contains("if (IsServerStarted && Character != null && NetworkObject != null)"),
				"only the server sends a change");
			LogAssert.IsTrue(FactionController.AggressionLockSeconds > 0f,
				"turning aggression off is locked for a while after a hit that needed it");
		}

		/// <summary>
		/// The lock: 5 minutes, restarted by any hit or kill made with aggression on and by any hit taken with it on; the owner
		/// learns the server's lock (hits taken are seen only there) through an owner-only SyncVar.
		/// </summary>
		[Test]
		public void AggressionLockIsFiveMinutesRestartedByHitsMadeOrTaken()
		{
			LogAssert.AreEqual(300f, FactionController.AggressionLockSeconds, "the lock is five minutes");
			string rule = Read("Shared/Implementation/Entity/Relations/HarmRule.cs");
			int allows = rule.IndexOf("public static bool Allows(", System.StringComparison.Ordinal);
			int end = rule.IndexOf("private static bool IsUnrestricted", allows, System.StringComparison.Ordinal);
			string body = rule.Substring(allows, end - allows);
			LogAssert.IsTrue(body.Contains("relation.OnAllowed(attackerSide, defenderSide)"), "an allowed hit is told to every relation");
			LogAssert.IsTrue(body.Contains("NoteRefusal(attacker, defender, refusedBy)"), "a refused hit is offered to the auto-aggression count");
			string faction = ReadModule("Runtime/FactionHarmRelation.cs");
			int onAllowed = faction.IndexOf("public void OnAllowed(", System.StringComparison.Ordinal);
			string allowedBody = faction.Substring(onAllowed, faction.IndexOf("private static void RefreshLockIfAggressive", onAllowed, System.StringComparison.Ordinal) - onAllowed);
			LogAssert.IsTrue(allowedBody.Contains("RefreshLockIfAggressive(attackerSide)") && allowedBody.Contains("RefreshLockIfAggressive(defenderSide)"),
				"an allowed hit restarts the lock of each side whose aggression is on, attacker and defender");
			string controller = ReadModule("Runtime/FactionController.cs");
			LogAssert.IsTrue(controller.Contains("PayloadVisibility.IsOwner(Host, conn) ? aggressionLockedUntilTick : 0u") &&
				controller.Contains("new FactionAggressionLockBroadcast { LockedUntilTick = aggressionLockedUntilTick }"),
				"the lock end is sent to the owner only");
		}

		/// <summary>Three different casts at the same character within 10 s turn aggression on; projectiles of one cast count once.</summary>
		[Test]
		public void ThreeRefusedCastsAtOneTargetWithinTheWindowProvoke()
		{
			AggressionProvocation p = default;
			LogAssert.IsFalse(p.Note(7, 100, 0f, 3, 10f), "first cast");
			LogAssert.IsFalse(p.Note(7, 100, 0.1f, 3, 10f), "a second projectile of the same cast does not count");
			LogAssert.IsFalse(p.Note(7, 200, 4f, 3, 10f), "second cast");
			LogAssert.IsTrue(p.Note(7, 300, 9f, 3, 10f), "third cast within 10 s provokes");
			LogAssert.IsFalse(p.Note(7, 400, 9.5f, 3, 10f), "the count starts again after provoking");
		}

		[Test]
		public void RefusedCastsOutsideTheWindowOrAtAnotherTargetDoNotProvoke()
		{
			AggressionProvocation p = default;
			p.Note(7, 1, 0f, 3, 10f);
			p.Note(7, 2, 5f, 3, 10f);
			LogAssert.IsFalse(p.Note(7, 3, 11f, 3, 10f), "the oldest of the three is 11 s old");
			LogAssert.IsTrue(p.Note(7, 4, 14f, 3, 10f), "the window slides: casts at 5, 11 and 14 s provoke");

			AggressionProvocation q = default;
			q.Note(7, 1, 0f, 3, 10f);
			q.Note(7, 2, 1f, 3, 10f);
			LogAssert.IsFalse(q.Note(8, 3, 2f, 3, 10f), "a different target starts the count again");
			LogAssert.IsFalse(q.Note(7, 4, 3f, 3, 10f), "and returning to the first starts again too");
		}

		/// <summary>Only the struck character of a cast counts (never area splash), and only for want of aggression.</summary>
		[Test]
		public void OnlyAStruckNonCompanionCountsTowardAutoAggression()
		{
			string rule = Read("Shared/Implementation/Entity/Relations/HarmRule.cs");
			int start = rule.IndexOf("private static void NoteRefusal(", System.StringComparison.Ordinal);
			string body = rule.Substring(start, rule.IndexOf("public static bool AreCompanions", start, System.StringComparison.Ordinal) - start);
			LogAssert.IsTrue(body.Contains("attack.Splash") && body.Contains("ReferenceEquals(defender, attack.Target)"),
				"splash victims and characters other than the one struck are not counted");
			string faction = ReadModule("Runtime/FactionHarmRelation.cs");
			LogAssert.IsTrue(faction.Contains("!ReferenceEquals(refusedBy, this)"),
				"only a refusal the faction relation made counts: companions and arena matches refuse whatever the toggle says");
			LogAssert.IsTrue(Read("Shared/Implementation/Entity/ECA/Actions/Character/Ability/AbilityApplyAreaAction.cs").Contains("collisionEvent.IsSplash = true"),
				"area blasts mark their victims as splash");
			LogAssert.IsTrue(Read("Shared/Implementation/Entity/Prediction/Ability/Template/Events/AbilityOnHitEvent.cs").Contains("HarmRule.BeginAttack("),
				"every ability hit runs inside an attack scope");
		}

	}
}
