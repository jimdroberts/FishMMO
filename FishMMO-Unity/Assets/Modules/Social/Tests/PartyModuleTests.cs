using System;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using FishMMO.Client;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Server.Implementation.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The party in the Social module (Phase 3, 2026-11-16): the core reads a membership through a read-only
	/// <see cref="IPartyController"/> and changes it only through <see cref="IPartySystem{TConnection}"/>; everything else
	/// about a party is the module's.
	/// </summary>
	[TestFixture]
	public class PartyModuleTests
	{
		private const string ModuleFolder = "Assets/Modules/Social";
		private const string Scripts = "Assets/Scripts/";

		[Test]
		public void EveryPlayerGetsAPartyMembership_NoNpcDoes()
		{
			LogAssert.IsTrue(CharacterParts.PartsFor(typeof(PlayerCharacter)).Contains(typeof(PartyController)),
				"a player character gets its party membership as a part");
			LogAssert.IsFalse(CharacterParts.PartsFor(typeof(NPC)).Contains(typeof(PartyController)), "an NPC does not");
			LogAssert.IsTrue(typeof(IPartyController).IsAssignableFrom(typeof(PartyController)) &&
				typeof(IPartyMembership).IsAssignableFrom(typeof(PartyController)), "the part answers both the core and the module contract");

			string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(ModuleFolder + "/Runtime/PartyController.cs"));
			foreach (string race in new[] { "Human", "Elf", "Orc" })
			{
				LogAssert.IsFalse(ProjectFiles.ReadAllText($"Assets/Prefabs/Shared/Entity/PlayableCharacters/{race}.prefab").Contains(guid),
					$"{race}.prefab does not author the party controller");
			}
		}

		[Test]
		public void TheCoreReadsAMembershipButCannotChangeIt()
		{
			LogAssert.IsTrue(typeof(IPartyController).GetProperty(nameof(IPartyController.ID)).SetMethod == null &&
				typeof(IPartyController).GetProperty(nameof(IPartyController.Rank)).SetMethod == null,
				"the core contract is read-only");

			foreach (string path in new[]
			{
				"Assets/Modules/Matchmaking/Server/MatchmakingSystem.GroupFinder.cs",
				"Assets/Modules/Matchmaking/Server/MatchmakingSystem.DungeonFinder.cs",
				Scripts + "Server/Implementation/World/SceneServer/Character/CharacterSystem.Loading.cs",
				Scripts + "Server/Implementation/World/SceneServer/Character/CharacterSystem.Social.cs",
				Scripts + "Client/Input/PlayerInputController.cs",
			})
			{
				// The finders are the Matchmaking module's; checked when it is installed.
				if (!ProjectFiles.Exists(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), path)))
				{
					continue;
				}
				string code = ProjectFiles.ReadAllText(path);
				LogAssert.IsFalse(Regex.IsMatch(code, @"new Party[A-Za-z]*Broadcast\("), $"{path} sends no party broadcast: the module's wire types are its own");
				LogAssert.IsFalse(Regex.IsMatch(code, @"\.(ID|Rank) = (0|PartyRank\.|partyID|rank|partyData)"), $"{path} sets no party membership");
			}

			if (!ProjectFiles.Exists(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Assets/Modules/Matchmaking/Server/MatchmakingSystem.GroupFinder.cs")))
			{
				return;
			}
			string finder = ProjectFiles.ReadAllText("Assets/Modules/Matchmaking/Server/MatchmakingSystem.GroupFinder.cs");
			LogAssert.IsTrue(finder.Contains("partySystem.ShowMembership(conn, character, partyID, rank);") &&
				finder.Contains("partySystem.ClearMembership(conn, partyID);"), "the group finder goes through the party system");
			LogAssert.IsTrue(ProjectFiles.ReadAllText("Assets/Modules/Matchmaking/Server/MatchmakingSystem.DungeonFinder.cs")
				.Contains("partySystem.ClearMembership(conn, partyID);"), "so does the dungeon finder");
		}

		[Test]
		public void TheMembershipLoadsThroughTheModule_AndTheRosterIsSentByIt()
		{
			LogAssert.IsTrue(CharacterPersistenceParticipants.All.Any(p => p is PartyPersistenceParticipant),
				"the character load reads the party membership through the module's participant");
			string loading = ProjectFiles.ReadAllText(Scripts + "Server/Implementation/World/SceneServer/Character/CharacterSystem.Loading.cs");
			LogAssert.IsFalse(loading.Contains("ICharacterPartyService") || loading.Contains("IPartyController") || loading.Contains("IPartySystem"),
				"the core load names nothing of parties");
			LogAssert.IsTrue(loading.Contains("participant.FetchAsync(this, charData)"), "participants get the character's row (its world server)");

			string participant = ProjectFiles.ReadAllText(ModuleFolder + "/Server/PartyPersistence.cs");
			LogAssert.IsTrue(participant.Contains("partyResult.Data.Value.WorldServerID != worldServerID") &&
				participant.Contains("partySystem.RemoveCharacterFromPartyAsync(membership.CharacterID, membership.PartyID, reason)"),
				"a membership from another world server is still dropped on arrival, through the party system");
			LogAssert.IsTrue(ProjectFiles.ReadAllText(ModuleFolder + "/Server/PartySystem.cs").Contains("characterSystem.OnSpawnCharacter += CharacterSystem_OnSpawnCharacter;"),
				"the party system sends a spawned member the roster");
		}

		[Test]
		public void TheSceneServerAdoptsThePartySystem_AndTheWorldGuiThePartyFrame()
		{
			string system = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(ModuleFolder + "/Server/PartySystem.asset"));
			string server = ProjectFiles.ReadAllText("Assets/AddressableAssetsData/AssetGroups/Server_Static_Permanent.asset").Replace("\r\n", "\n");
			LogAssert.IsTrue(Regex.IsMatch(server, $@"- m_GUID: {system}\n(?:    .*\n)*?    - SceneServer_Systems\n"), "the party system carries the SceneServer_Systems label");
			LogAssert.IsFalse(ProjectFiles.ReadAllText("Assets/Scenes/Server/SceneServer.unity").Contains(system), "and the scene server's own list does not name it");

			string prefab = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(ModuleFolder + "/Client/UIParty.prefab"));
			string client = ProjectFiles.ReadAllText("Assets/AddressableAssetsData/AssetGroups/Client_Dynamic.asset").Replace("\r\n", "\n");
			LogAssert.IsTrue(Regex.IsMatch(client, $@"- m_GUID: {prefab}\n    m_Address: UIParty\n(?:    .*\n)*?    - ClientWorldGUI_Panels\n"),
				"the party frame is placed into ClientWorldGUI under its UIManager key");
			string script = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(ModuleFolder + "/Client/UITKParty.cs"));
			LogAssert.IsFalse(ProjectFiles.ReadAllText("Assets/Scenes/Client/ClientWorldGUI.unity").Contains(script), "the scene no longer holds it");
		}

		[Test]
		public void InviteToPartyIsAModuleEntryOnTheWorldMenu()
		{
			SocialClient.Install(); // [RuntimeInitializeOnLoadMethod] does not run in EditMode.
			CharacterMenuEntries.Entry invite = CharacterMenuEntries.All.FirstOrDefault(e => e.Id == SocialClient.InviteToPartyEntryId);
			LogAssert.IsNotNull(invite, "the module registers Invite to Party");
			LogAssert.IsTrue(invite.Applies(new CharacterMenuTarget(5, "Bo", CharacterMenuSource.World)), "on a player in the world");
			LogAssert.IsFalse(invite.Applies(new CharacterMenuTarget(5, "Bo", CharacterMenuSource.Guild)) ||
				invite.Applies(new CharacterMenuTarget(5, "Bo", CharacterMenuSource.Party)), "not on the roster menus, which never offered it");
			LogAssert.IsTrue(invite.Order > CharacterMenuEntries.All.First(e => e.Id == SocialClient.AddFriendEntryId).Order,
				"after Add Friend, where it always was");
		}

		[Test]
		public void ReopeningThePartyPanelRefreshesRowsInPlace()
		{
			string code = ProjectFiles.ReadAllText(ModuleFolder + "/Client/UITKParty.cs").Replace("\r\n", "\n");
			string show = Body(code, "protected override void OnAfterShow()");
			LogAssert.IsFalse(show.Contains("RebuildRosterView"), "OnAfterShow must not rebuild the roster: it throws away every row and buff icon on every open");
			LogAssert.IsTrue(show.Contains("ApplyModelToRow(model)"), "it re-reads each row from its model instead");
		}

		/// <summary>The exact local value outranks the quantised copy of itself.</summary>
		/// <remarks>
		/// The scene server includes the recipient's own row so the miss counter that greys a member
		/// out stays intact — absence is the signal — but the three fractions on that row are a
		/// byte-quantised copy of a number the panel has just derived exactly from the local
		/// reconciled controller.
		/// </remarks>
		[Test]
		public void YourOwnPartyRowKeepsItsExactValues()
		{
			Assert.IsTrue(UITKParty.ShouldApplyBroadcastVitals(hasLocalVitals: false),
				"Another member, or your own row before the controller resolves: the payload is the only source.");
			Assert.IsFalse(UITKParty.ShouldApplyBroadcastVitals(hasLocalVitals: true),
				"Once the exact fraction has been read locally, the quantised copy may not overwrite it.");
		}

		// ── Party vitals quantisation ───────────────────────────────────

		/// <summary>Fractions survive the byte round trip closely enough to draw.</summary>
		[Test]
		public void PartyVitals_FractionQuantisationIsAccurateEnoughToDraw()
		{
			Assert.AreEqual(0, PartyVitalsQuantiser.FractionToByte(0.0f));
			Assert.AreEqual(255, PartyVitalsQuantiser.FractionToByte(1.0f));
			Assert.AreEqual(0.0f, PartyVitalsQuantiser.ByteToFraction(0));
			Assert.AreEqual(1.0f, PartyVitalsQuantiser.ByteToFraction(255));

			for (float f = 0.0f; f <= 1.0f; f += 0.01f)
			{
				float round = PartyVitalsQuantiser.ByteToFraction(PartyVitalsQuantiser.FractionToByte(f));
				Assert.Less(System.Math.Abs(round - f), 0.005f, $"Fraction {f} lost too much precision.");
			}
		}

		/// <summary>Out-of-range and NaN inputs clamp instead of wrapping.</summary>
		/// <remarks>
		/// A percentage can arrive above one from an over-heal, and NaN from a maximum of zero on a
		/// character whose attributes have not finished loading. Wrapping either into a byte would
		/// draw a full bar on a dying party member.
		/// </remarks>
		[Test]
		public void PartyVitals_QuantisationClampsHostileInputs()
		{
			Assert.AreEqual(0, PartyVitalsQuantiser.FractionToByte(float.NaN));
			Assert.AreEqual(0, PartyVitalsQuantiser.FractionToByte(-5.0f));
			Assert.AreEqual(255, PartyVitalsQuantiser.FractionToByte(17.0f));

			Assert.AreEqual(0, PartyVitalsQuantiser.RateToUInt16(float.NaN));
			Assert.AreEqual(0, PartyVitalsQuantiser.RateToUInt16(-1.0f));
			Assert.AreEqual(ushort.MaxValue, PartyVitalsQuantiser.RateToUInt16(1e9f));
			Assert.AreEqual(1234, PartyVitalsQuantiser.RateToUInt16(1234.4f));
		}

		/// <summary>A method's body, brace-matched from the first brace after its signature.</summary>
		private static string Body(string source, string signature)
		{
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, "found: " + signature);
			int open = source.IndexOf('{', start);
			int depth = 0;
			for (int i = open; i < source.Length; ++i)
			{
				if (source[i] == '{') ++depth;
				else if (source[i] == '}' && --depth == 0)
				{
					return source.Substring(open, i - open + 1);
				}
			}
			LogAssert.Fail("unterminated body for " + signature);
			return string.Empty;
		}
	}
}
