using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.Server.Implementation.World.SceneServer;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins the travel spells' shared pieces (issue #241, Phase 1): the refusal order, the local-move rule every transfer
	/// uses, the transfer seam's behaviour without a server, and that a character leaves a scene server by one hand-off only.
	/// </summary>
	[TestFixture]
	public class TravelSpellTests
	{
		private const string CharacterFolder = "Assets/FishMMO/Server/Implementation/World/SceneServer/Character/";

		[TestCase(false, true, true, false, TravelSpellRefusal.CannotAct)]
		[TestCase(true, true, true, false, TravelSpellRefusal.InArena)]
		[TestCase(true, false, true, false, TravelSpellRefusal.NoTravelZone)]
		[TestCase(true, false, false, false, TravelSpellRefusal.NoDestination)]
		[TestCase(true, false, false, true, TravelSpellRefusal.None)]
		public void Decide_RefusesInOrder(bool canAct, bool inArena, bool inZone, bool hasDestination, TravelSpellRefusal expected)
		{
			LogAssert.AreEqual(expected, TravelSpellRules.Decide(canAct, inArena, inZone, hasDestination));
		}

		[Test]
		public void Describe_SaysSomethingForEveryRefusalThePlayerCanActOn()
		{
			LogAssert.IsNull(TravelSpellRules.Describe(TravelSpellRefusal.None, "x"), "success says nothing");
			LogAssert.IsNull(TravelSpellRules.Describe(TravelSpellRefusal.CannotAct, "x"), "a dead or stunned caster is told by its own state");
			LogAssert.AreEqual("no mark", TravelSpellRules.Describe(TravelSpellRefusal.NoDestination, "no mark"));
			LogAssert.IsTrue(!string.IsNullOrEmpty(TravelSpellRules.Describe(TravelSpellRefusal.InArena, "x")));
			LogAssert.IsTrue(!string.IsNullOrEmpty(TravelSpellRules.Describe(TravelSpellRefusal.NoTravelZone, "x")));
			LogAssert.IsTrue(!string.IsNullOrEmpty(TravelSpellRules.Describe(TravelSpellRefusal.TransferFailed, "x")));
		}

		[TestCase(false, true, true, true, false, TravelSpellRefusal.CannotAct)]
		[TestCase(true, true, true, true, false, TravelSpellRefusal.InArena)]
		[TestCase(true, false, true, true, false, TravelSpellRefusal.NoTravelZone)]
		[TestCase(true, false, false, true, false, TravelSpellRefusal.InInstance)]
		[TestCase(true, false, false, false, false, TravelSpellRefusal.UnsafeSpot)]
		[TestCase(true, false, false, false, true, TravelSpellRefusal.None)]
		public void DecideMark_RefusesInOrder(bool canAct, bool inArena, bool inZone, bool inInstance, bool safeSpot, TravelSpellRefusal expected)
		{
			LogAssert.AreEqual(expected, TravelSpellRules.DecideMark(canAct, inArena, inZone, inInstance, safeSpot));
			LogAssert.IsTrue(!string.IsNullOrEmpty(TravelSpellRules.Describe(TravelSpellRefusal.InInstance, null)));
			LogAssert.IsTrue(!string.IsNullOrEmpty(TravelSpellRules.Describe(TravelSpellRefusal.UnsafeSpot, null)));
		}

		// A mark in the scene you stand in keeps you in your own channel; any other scene, or leaving an instance, asks for the mark's.
		[TestCase("Moron", false, "Moron", 42L, 0L)]
		[TestCase("Kelp", false, "Moron", 42L, 42L)]
		[TestCase("Crypt", true, "Moron", 42L, 42L)]
		[TestCase("Moron", true, "Moron", 42L, 42L)]
		public void RecallChannel_StaysInThisChannelOnlyForTheSceneYouStandIn(string current, bool inInstance, string markScene, long markID, long expected)
		{
			LogAssert.AreEqual(expected, RecallAction.RecallChannel(current, inInstance, markScene, markID));
		}

		[Test]
		public void TheMark_IsAPlayerPart_AndSavesThroughTheModule()
		{
			LogAssert.IsTrue(CharacterParts.PartsFor(typeof(PlayerCharacter)).Contains(typeof(TravelMarkController)), "every player carries a mark");
			LogAssert.IsFalse(CharacterParts.PartsFor(typeof(NPC)).Contains(typeof(TravelMarkController)), "an NPC does not");
			LogAssert.IsTrue(CharacterPersistenceParticipants.All.Any(p => p is MarkPersistenceParticipant), "the mark loads and saves with the character");
		}

		[Test]
		public void TheMark_IsDirtyUntilTheSameMarkIsConfirmed()
		{
			GameObject go = new GameObject("TravelMark");
			try
			{
				TravelMarkController marks = go.AddComponent<TravelMarkController>();
				marks.Restore("Moron", 7, new Vector3(1, 2, 3), 90f);
				LogAssert.IsTrue(marks.HasMark && !marks.PersistenceDirty, "a restored mark is not a change");

				marks.SetMark("Kelp", 8, new Vector3(4, 5, 6), 450f);
				LogAssert.IsTrue(marks.PersistenceDirty, "a new mark is saved");
				LogAssert.AreEqual(90f, marks.Heading, "the heading is kept in [0, 360)");

				marks.MarkPersisted("Moron", 7, new Vector3(1, 2, 3), 90f);
				LogAssert.IsTrue(marks.PersistenceDirty, "confirming an older mark leaves the new one dirty");

				marks.MarkPersisted("Kelp", 8, new Vector3(4, 5, 6), 90f);
				LogAssert.IsFalse(marks.PersistenceDirty, "confirming this mark clears it");

				marks.ResetState(true);
				LogAssert.IsFalse(marks.HasMark, "a pooled character carries no mark");
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void TheBindstone_AndMark_ShareOneSpotRule()
		{
			string bindstone = CodeOnly(File.ReadAllText("Assets/FishMMO/Modules/WorldInteractables/Runtime/Actions/BindstoneAction.cs"));
			LogAssert.IsTrue(bindstone.Contains("StandingSpot.IsValid(player, RequiredClearance, out string rejection)"), "the bindstone uses the shared rule");
			LogAssert.IsFalse(bindstone.Contains("CharacterCollisionsOverlap"), "and keeps no copy of it");
			string mark = CodeOnly(File.ReadAllText("Assets/FishMMO/Modules/Travel/Runtime/TravelSpells/MarkLocationAction.cs"));
			LogAssert.IsTrue(mark.Contains("StandingSpot.IsValid(player, RequiredClearance, out rejection)"), "Mark uses the same rule");
		}

		// Same scene, no channel asked: local. Same scene, own channel: local. Another channel: a transfer.
		[TestCase("Moron", 7L, false, "Moron", 0L, true)]
		[TestCase("Moron", 7L, false, "Moron", 7L, true)]
		[TestCase("Moron", 7L, false, "Moron", 8L, false)]
		[TestCase("Moron", 7L, false, "Kelp", 0L, false)]
		// Inside an instance the scene is the instance's own, and it has no channels.
		[TestCase("Crypt", 7L, true, "Crypt", 9L, true)]
		[TestCase("Crypt", 7L, true, "Moron", 0L, false)]
		[TestCase(null, 7L, false, "Moron", 0L, false)]
		[TestCase("Moron", 7L, false, null, 0L, false)]
		public void IsLocalMove_IsTheSceneAndChannelRule(string current, long currentID, bool inInstance, string destination, long destinationID, bool expected)
		{
			LogAssert.AreEqual(expected, CharacterTransfer.IsLocalMove(current, currentID, inInstance, destination, destinationID));
		}

		[Test]
		public void Transfer_WithoutAHost_RefusesAndMovesNothing()
		{
			ICharacterTransferHost previous = CharacterTransfer.Host;
			CharacterTransfer.Host = null;
			try
			{
				LogAssert.AreEqual(TransferOutcome.Refused,
					CharacterTransfer.Transfer(null, new TransferDestination("Moron", 0, Vector3.zero, Quaternion.identity), out TransferRefusal refusal));
				LogAssert.AreEqual(TransferRefusal.NoHost, refusal);
				LogAssert.AreEqual(TransferOutcome.Refused, CharacterTransfer.TransferToBindPoint(null, out refusal));
				LogAssert.AreEqual(TransferRefusal.NoHost, refusal);
			}
			finally
			{
				CharacterTransfer.Host = previous;
			}
		}

		/// <summary>
		/// Every route a character takes off a scene server to a place elsewhere — teleporter, bind respawn, channel switch,
		/// leaving an instance, a travel spell — goes through CharacterSystem.Transfer.cs. There were four hand-written copies,
		/// each clearing different bookkeeping; a fifth must not grow back beside the helper.
		/// </summary>
		[Test]
		public void OnlyTheTransferHelper_ReleasesACharacterForAHandOff()
		{
			foreach (string path in Directory.GetFiles(CharacterFolder, "CharacterSystem*.cs"))
			{
				string file = Path.GetFileName(path).Replace('\\', '/');
				if (file == "CharacterSystem.Transfer.cs")
				{
					continue;
				}
				string code = CodeOnly(File.ReadAllText(path));
				LogAssert.IsFalse(code.Contains("skipOnDisconnect: true"),
					$"{file} releases a character for a hand-off itself; call HandOffToScene / DeferHandOff instead.");
				LogAssert.IsFalse(Regex.IsMatch(code, @"InstanceID\s*=\s*0\s*;"),
					$"{file} clears the instance bookkeeping itself; the hand-off does that.");
			}

			string transfer = CodeOnly(File.ReadAllText(CharacterFolder + "CharacterSystem.Transfer.cs"));
			LogAssert.IsTrue(transfer.Contains("RemoveCharacterConnectionMapping(conn, skipOnDisconnect: true);"), "the helper releases");
			LogAssert.IsTrue(transfer.Contains("SuppressCombatLingerOnDisconnect(conn);"), "a hand-off is never a combat logout");
			LogAssert.IsTrue(transfer.Contains("NoteDeliberateInstanceExit("), "leaving an instance by any route is a deliberate exit");

			string connection = CodeOnly(File.ReadAllText(CharacterFolder + "CharacterSystem.Connection.cs"));
			LogAssert.IsFalse(connection.Contains("UnloadSceneForConnection("),
				"the teleporter no longer unloads by scene name (it may name another channel) and waits on the client");
		}

		private static string CodeOnly(string source)
		{
			System.Text.StringBuilder code = new System.Text.StringBuilder(source.Length);
			foreach (string line in source.Replace("\r\n", "\n").Split('\n'))
			{
				string trimmed = line.TrimStart();
				if (trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*"))
				{
					continue;
				}
				code.Append(line).Append('\n');
			}
			return code.ToString();
		}
	}
}
