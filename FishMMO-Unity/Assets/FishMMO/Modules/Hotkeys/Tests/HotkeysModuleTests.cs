using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FishMMO.Core;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Server.Implementation.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using NUnit.Framework;
using UnityEditor;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The Hotkeys module: the bar on the player's entity, the hotkey system's handlers, its writes through the
	/// ownership gate, the departing bar written by the save participant, the forget cleanup reached through the ability
	/// system's event, and the wiring that lets the folder be removed (a label-adopted system, a label-placed bar, the bar
	/// actions added through <c>ModuleInputActions</c>). Moved from CharacterCoreTests, AbilityForgetTests,
	/// AuditFollowUpPinsTests and CharacterWriteGateTests with the module.
	/// </summary>
	[TestFixture]
	public class HotkeysModuleTests
	{
		private const string ModuleFolder = "Assets/FishMMO/Modules/Hotkeys";
		private const string SystemPath = ModuleFolder + "/Server/HotkeySystem.cs";
		private const string InterfacePath = ModuleFolder + "/Server/IHotkeySystem.cs";
		private const string ParticipantPath = ModuleFolder + "/Server/HotkeyPersistence.cs";

		private static (World World, ModuleInstall Install) InstallWithHotkeys(SimulationSide side)
		{
			var world = new World();
			var scheduler = new SystemScheduler(world);
			var problems = new List<string>();
			List<GameModule> found = ModuleCatalog.Discover(new[] { typeof(MessagingModule).Assembly, typeof(HotkeyModule).Assembly }, problems);
			ModuleInstall install = ModuleCatalog.Resolve(found, problems).Install(world, scheduler, side);
			return (world, install);
		}

		// ── The bar on the entity ────────────────────────────────────────────────

		[Test]
		public void TheModuleIsDeclaredAndInstallsCleanly()
		{
			(World _, ModuleInstall install) = InstallWithHotkeys(SimulationSide.Server);
			LogAssert.IsTrue(install.Modules.Any(m => m.Id == HotkeyModule.ModuleId), "the module declares itself ([assembly: GameModule])");
			LogAssert.IsTrue(install.Problems.Count == 0, "and installs without problems: " + string.Join("; ", install.Problems));
		}

		[Test]
		public void APlayerOnTheServerHasEveryHotkeySlot_InAListOfItsOwn()
		{
			(World world, ModuleInstall _) = InstallWithHotkeys(SimulationSide.Server);
			Entity a = world.Spawn(Archetypes.Player);
			Entity b = world.Spawn(Archetypes.Player);
			List<HotkeyData> slots = world.Get<HotkeyBar>(a).Slots;
			LogAssert.AreEqual(Constants.Configuration.MaximumPlayerHotkeys, slots.Count, "every slot exists, empty");
			for (int i = 0; i < slots.Count; i++)
			{
				LogAssert.AreEqual(i, slots[i].Slot, "slots are in slot order");
				LogAssert.AreEqual(HotkeyData.UnsetReferenceID, slots[i].ReferenceID, "and unbound");
			}
			LogAssert.IsFalse(ReferenceEquals(slots, world.Get<HotkeyBar>(b).Slots), "two players never share one bar");
		}

		[Test]
		public void TheClientKeepsNoHotkeyBar_AndNpcsGetNone()
		{
			(World client, ModuleInstall _) = InstallWithHotkeys(SimulationSide.Client);
			LogAssert.IsFalse(client.Has<HotkeyBar>(client.Spawn(Archetypes.Player)), "the bar is server state; the client is sent it");

			(World server, ModuleInstall _) = InstallWithHotkeys(SimulationSide.Server);
			LogAssert.IsFalse(server.Has<HotkeyBar>(server.Spawn(Archetypes.Npc)), "NPCs get no bar");
		}

		// ── The hotkey system ────────────────────────────────────────────────────

		[Test]
		public void HotkeyHandlers_EchoTheAuthoritativeSlot_WhenTheStateGateRefuses()
		{
			/* The client applies a binding the instant the icon is dropped, so a silent refusal
			 * leaves it holding a binding the server never took until the next login. */
			string source = ReadSource(SystemPath);

			string single = MethodBody(source,
				"public void OnServerHotkeySetBroadcastReceived(",
				"public void OnServerHotkeySetMultipleBroadcastReceived(");
			LogAssert.IsTrue(single.Contains("PlayerRequestGate.SkipCanAct"),
				"the single-slot handler applies the state gate itself so it can answer");
			AssertOrdered(single, "CharacterStateValidation.CanAct(playerCharacter)", "AcknowledgeHotkey(conn, playerCharacter, msg.HotkeyData.Slot)",
				"...and answers the refusal with the authoritative slot");

			/* Ended at the acknowledgement helpers, which are declared directly after the bulk handler. */
			string multiple = MethodBody(source,
				"public void OnServerHotkeySetMultipleBroadcastReceived(",
				"private void AcknowledgeHotkey(");
			LogAssert.IsTrue(multiple.Contains("PlayerRequestGate.SkipCanAct"),
				"the bulk handler applies the state gate itself so it can answer");
			AssertOrdered(multiple, "CharacterStateValidation.CanAct(playerCharacter)", "AcknowledgeAllHotkeys(conn, playerCharacter)",
				"...and answers the refusal with the whole bar");
		}

		[Test]
		public void ForgettingPrunesAndEchoesHotkeys()
		{
			/* Bindings are validated when they are MADE and in the login prune and nowhere in between,
			 * so without this the server's bar and the database row keep naming a dead ability until
			 * the character relogs. */
			string source = ReadSource(SystemPath);
			string body = MethodBody(source,
				"public bool ForgetAbilityBindings(IPlayerCharacter playerCharacter, long abilityID)",
				"private static bool IsHotkeyReferenceValid(IPlayerCharacter playerCharacter, HotkeyData hotkey)");

			LogAssert.IsTrue(body.Contains("hotkey.Type != HotkeyTypeAbility"),
				"only ability bindings may be cleared: the id spaces are not disjoint, so an id alone is not a match");
			LogAssert.IsTrue(body.Contains("hotkey.ReferenceID != abilityID"),
				"and only the bindings that actually name the forgotten ability");
			LogAssert.IsTrue(body.Contains("EmptyHotkey(hotkey.Slot)"),
				"a clear must be the same clear the login prune writes, or the client cannot recognise it as empty");
			LogAssert.IsTrue(body.Contains("StageHotkeyPersist(playerCharacter)"),
				"the correction has to reach the database row, not just the session");
			LogAssert.IsTrue(body.Contains("AcknowledgeAllHotkeys(conn, playerCharacter)"),
				"and the client's bar has to be told, or it draws the dead binding for the rest of the session");
			LogAssert.IsTrue(body.Contains("if (!changed)"),
				"a forget that cleared nothing must not write the bar back");

			LogAssert.IsTrue(ReadSource(InterfacePath).Contains("bool ForgetAbilityBindings("),
				"the cleanup is on the interface, not only on the concrete system");

			/* The core ability system names no hotkey: it raises an event the module listens to. */
			string code = CodeOnly(source);
			LogAssert.IsTrue(code.Contains("abilitySystem.OnAbilityForgotten += AbilitySystem_OnAbilityForgotten;"),
				"the hotkey system listens for forgotten abilities");
			LogAssert.IsTrue(CodeOnly(MethodBody(source, "public override void OnDeinitialize()", "protected override void OnUpdate("))
				.Contains("abilitySystem.OnAbilityForgotten -= AbilitySystem_OnAbilityForgotten;"),
				"and stops listening when it tears down");
			LogAssert.IsTrue(CodeOnly(MethodBody(source, "private void AbilitySystem_OnAbilityForgotten(", "/// <summary>"))
				.Contains("ForgetAbilityBindings(character, abilityID)"),
				"which clears the bindings");
		}

		[Test]
		public void TheBarIsSentAfterTheLoginPrune()
		{
			/* Sent by the core's login data it went out before the prune, so the client's first bar could name what the
			 * prune then cleared. */
			string code = CodeOnly(ReadSource(SystemPath));
			AssertOrdered(code, "StageHotkeyPersist(character);", "SendBar(character);", "the bar is sent after the prune staged its corrections");
			string social = ReadSource("Assets/FishMMO/Server/Implementation/World/SceneServer/Character/CharacterSystem.Social.cs");
			LogAssert.IsFalse(social.Contains("HotkeySetMultipleBroadcast"), "the core login send names nothing of hotkeys");
		}

		// ── Writes through the ownership gate ────────────────────────────────────

		[Test]
		public void TheSystemsWrites_GoThroughTheOwnershipGate()
		{
			string code = CodeOnly(ReadSource(SystemPath));
			LogAssert.IsTrue(code.Contains("hotkeyService.PersistOwnedAsync(hotkeys, ClaimsOf(claim))"), "the pump writes through the ownership gate");
			LogAssert.IsFalse(code.Contains("hotkeyService.PersistAsync("), "and never through the ungated write");

			string participant = CodeOnly(ReadSource(ParticipantPath));
			LogAssert.IsTrue(participant.Contains("await hotkeyService.PersistOwnedAsync(hotkeys.Items, claims)"), "the departing bar too");
			LogAssert.IsFalse(participant.Contains("hotkeyService.PersistAsync("), "and never through the ungated write");
		}

		[Test]
		public void AHotkeyBarRefusedByTheGate_IsNotRestaged()
		{
			LogAssert.IsFalse(HotkeySystem.RestagesAfter(Ok(4, 4, 4)), "a complete bar is done");
			LogAssert.IsTrue(HotkeySystem.RestagesAfter(Ok(4, 4, 2)), "a short one is written again from the live bar");
			LogAssert.IsTrue(HotkeySystem.RestagesAfter(Ok(4, 3, 3)), "as is one the service filtered");
			LogAssert.IsFalse(HotkeySystem.RestagesAfter(Ok(4, 0, 0, 4)), "but not one refused row by row for a lost claim");
			LogAssert.IsFalse(HotkeySystem.RestagesAfter(
				DatabaseResult<BulkWriteResult>.Failure(DatabaseErrorCodes.Forbidden, "claim lost")), "nor one refused whole");
			LogAssert.IsTrue(HotkeySystem.RestagesAfter(
				DatabaseResult<BulkWriteResult>.Failure(DatabaseErrorCodes.DatabaseError, "down", isTransient: true)), "a database fault is retried");
		}

		// ── The departing bar ────────────────────────────────────────────────────

		[Test]
		public void TheDepartingBar_IsCapturedByTheParticipant_AndKeyedPerCharacter()
		{
			/* Flushed from OnDisconnect it was a write of its own, kept ahead of the release only by the lane order — and at
			 * shutdown the hotkey system tears down, and flushed, BEFORE the character system releases every claim. Now the
			 * participant's departure capture takes it, and the core writes it with the save-and-release. */
			var participant = new HotkeyPersistenceParticipant();
			LogAssert.AreEqual("hotkeys", participant.Name, "the participant names itself");
			LogAssert.IsTrue(participant.SavesPerCharacter,
				"its rows are queued on each character's lane, behind whatever the pump queued for that character");
			LogAssert.IsTrue(CharacterPersistenceParticipants.All.Any(p => p is HotkeyPersistenceParticipant),
				"and it is registered ([assembly: CharacterPersistence])");

			string code = CodeOnly(ReadSource(ParticipantPath));
			string departure = MethodBody(code, "public override void CaptureDeparture(", "public override async Task<bool> SaveAsync(");
			LogAssert.IsTrue(departure.Contains("hotkeySystem.TakeDepartingBar(player)"),
				"the departure takes the live bar through the hotkey system, which drops what it had staged for the character");
			string capture = MethodBody(code, "public override void Capture(", "public override void CaptureDeparture(");
			LogAssert.IsFalse(capture.Contains("Items.Add"), "the periodic save captures nothing: the pump writes a resident's changes");

			string system = ReadSource(SystemPath);
			LogAssert.IsFalse(CodeOnly(system).Contains("OnDisconnect +="), "the hotkey system does not write on disconnect");
			string teardown = CodeOnly(MethodBody(system, "public override void OnDeinitialize()", "protected override void OnUpdate("));
			LogAssert.IsFalse(teardown.Contains("FlushPendingHotkeyWrites()"), "nor from its own teardown, which runs before the claims are released");

			string loading = ReadSource("Assets/FishMMO/Server/Implementation/World/SceneServer/Character/CharacterSystem.Loading.cs");
			LogAssert.IsFalse(loading.Contains("HotkeyBar"), "the core load names nothing of hotkeys; the participant applies the bar");
		}

		// ── Wiring ───────────────────────────────────────────────────────────────

		[Test]
		public void TheSceneServerAdoptsTheHotkeySystem_AndTheWorldGuiPlacesTheBar()
		{
			string system = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(ModuleFolder + "/Server/HotkeySystem.asset"));
			string server = ProjectFiles.ReadAllText("Assets/AddressableAssetsData/AssetGroups/Server_Static_Permanent.asset").Replace("\r\n", "\n");
			LogAssert.IsTrue(Regex.IsMatch(server, $@"- m_GUID: {system}\n(?:    .*\n)*?    - SceneServer_Systems\n"), "the system carries the SceneServer_Systems label");
			LogAssert.IsFalse(ProjectFiles.ReadAllText("Assets/Scenes/Server/SceneServer.unity").Contains(system), "the scene server's own list does not name it");

			string bar = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(ModuleFolder + "/Client/UIHotkeyBar.prefab"));
			string client = ProjectFiles.ReadAllText("Assets/AddressableAssetsData/AssetGroups/Client_Dynamic.asset").Replace("\r\n", "\n");
			LogAssert.IsTrue(Regex.IsMatch(client, $@"- m_GUID: {bar}\n(?:    .*\n)*?    - ClientWorldGUI_Panels\n"), "the bar carries the ClientWorldGUI_Panels label");
			string script = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(ModuleFolder + "/Client/UITKHotkeyBar.cs"));
			LogAssert.IsFalse(ProjectFiles.ReadAllText("Assets/Scenes/Client/ClientWorldGUI.unity").Contains(script), "ClientWorldGUI.unity no longer carries the bar");
		}

		[Test]
		public void TheExtraBarsActions_AreCaptionedInTheOptions()
		{
			/* Registered at run time start (RuntimeInitializeOnLoadMethod), which an edit-mode test never reaches; the
			 * registration is idempotent, so it is made here. Moved from KeyBindingDisplayNameTests. */
			typeof(FishMMO.Client.HotkeyKeyMap).GetMethod("Install", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
				.Invoke(null, null);
			System.Reflection.MethodInfo caption = typeof(FishMMO.Client.UITKOptions).GetMethod("DisplayNameFor",
				System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
			LogAssert.IsNotNull(caption, "UITKOptions must still declare DisplayNameFor");
			LogAssert.AreEqual("Hotbar 2 Modifier", (string)caption.Invoke(null, new object[] { "Hotbar2Modifier" }),
				"actions created at run time for extra bars are captioned too, not shown by name");
		}

		[Test]
		public void TheBarActionsAreAddedThroughTheCoresSeam()
		{
			LogAssert.IsTrue(CodeOnly(ReadSource(ModuleFolder + "/Client/HotkeyKeyMap.cs")).Contains("ModuleInputActions.Register(AddBarActions, TryGetDisplayName);"),
				"the module adds its bar actions to the player's controls through ModuleInputActions");
			LogAssert.IsTrue(CodeOnly(ReadSource("Assets/FishMMO/Client/Input/PlayerInputController.cs")).Contains("ModuleInputActions.AddActions(Controls)"),
				"and the core input controller asks for them without naming the module");
		}

		// ── Helpers ──────────────────────────────────────────────────────────────

		private static DatabaseResult<BulkWriteResult> Ok(int supplied, int attempted, int applied, int unowned = 0)
		{
			return DatabaseResult<BulkWriteResult>.Success(new BulkWriteResult(supplied, attempted, applied, unowned));
		}

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(ProjectFiles.Exists(path), $"{relativePath} not found at {path}.");
			return ProjectFiles.ReadAllText(path).Replace("\r\n", "\n");
		}

		/// <summary>Comment lines stripped, so prose about a construct does not trip a scan for it.</summary>
		private static string CodeOnly(string source)
		{
			var kept = new StringBuilder(source.Length);
			foreach (string line in source.Split('\n'))
			{
				string t = line.TrimStart();
				if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("/*", StringComparison.Ordinal) || t.StartsWith("*", StringComparison.Ordinal))
				{
					continue;
				}
				kept.Append(line).Append('\n');
			}
			return kept.ToString();
		}

		private static string MethodBody(string source, string signature, string nextSymbol)
		{
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, $"the source must still declare {signature}");
			int end = source.IndexOf(nextSymbol, start + signature.Length, StringComparison.Ordinal);
			LogAssert.IsTrue(end > start, $"the end of {signature} must be locatable");
			return source.Substring(start, end - start);
		}

		private static void AssertOrdered(string text, string first, string second, string message)
		{
			int a = text.IndexOf(first, StringComparison.Ordinal);
			int b = a >= 0 ? text.IndexOf(second, a + first.Length, StringComparison.Ordinal) : -1;
			LogAssert.IsTrue(a >= 0 && b > a, $"{message} ('{first}' before '{second}')");
		}
	}
}
