using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using FishMMO.Client;
using FishMMO.Server.Implementation.World.SceneServer;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The Duels module as shipped (issue #250): the server system and the HUD are where the game loads them from, the
	/// context menu offers the challenge. The duel boards live with the other boards (LeaderboardWiringTests).
	/// </summary>
	[TestFixture]
	public class DuelsWiringTests
	{
		private const string ModuleFolder = "Assets/FishMMO/Modules/Duels";
		private const string SystemAsset = ModuleFolder + "/Server/DuelSystem.asset";
		private const string HudPrefab = ModuleFolder + "/Client/UIDuel.prefab";
		private const string HudUxml = ModuleFolder + "/Client/UIDuel.uxml";
		private const string ServerGroup = "Assets/AddressableAssetsData/AssetGroups/Server_Static_Permanent.asset";
		private const string ClientGroup = "Assets/AddressableAssetsData/AssetGroups/Client_Dynamic.asset";

		private static string Read(string path) => ProjectFiles.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), path)).Replace("\r\n", "\n");

		private static bool HasLabel(string group, string assetPath, string label)
		{
			string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(assetPath));
			return !string.IsNullOrEmpty(guid) && Regex.IsMatch(group, $@"- m_GUID: {guid}\n(?:    .*\n)*?    - {label}\n");
		}

		[Test]
		public void TheSceneServer_LoadsTheDuelSystem_WithAFifteenMetreGround()
		{
			DuelSystem system = AssetDatabase.LoadAssetAtPath<DuelSystem>(ProjectFiles.Path(SystemAsset));
			LogAssert.IsNotNull(system, $"{SystemAsset} is a DuelSystem");
			LogAssert.AreEqual(new Vector3(15f, 15f, 15f), system.ArenaSize, "the ground issue #250 asks for, configurable on the asset");
			LogAssert.IsTrue(HasLabel(Read(ServerGroup), SystemAsset, "SceneServer_Systems"),
				"the asset carries the SceneServer_Systems label, or no scene server runs duels");
		}

		[Test]
		public void TheDuelHud_IsAModulePanelOfTheWorldGui()
		{
			GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectFiles.Path(HudPrefab));
			LogAssert.IsNotNull(prefab, $"{HudPrefab} exists");
			LogAssert.AreEqual(UITKDuel.ControlName, prefab.name, "the prefab's name is the panel's UIManager key");
			UITKDuel hud = prefab.GetComponent<UITKDuel>();
			LogAssert.IsNotNull(hud, "the prefab carries the duel HUD");
			LogAssert.IsNotNull(hud.Document, "its Document is assigned");
			LogAssert.AreEqual(HudUxml, AssetDatabase.GetAssetPath(hud.Document.visualTreeAsset), "rendering the duel markup");
			LogAssert.IsFalse(hud.StartOpen, "it starts hidden");
			LogAssert.IsTrue(HasLabel(Read(ClientGroup), HudPrefab, ModulePanelHost.LabelFor("ClientWorldGUI")),
				"the prefab is addressable as a world GUI panel, or no challenge is ever shown");
		}

		[Test]
		public void TheContextMenu_OffersAChallenge_OnPlayersInTheWorldOnly()
		{
			DuelClient.Install();
			CharacterMenuEntries.Entry entry = null;
			foreach (CharacterMenuEntries.Entry candidate in CharacterMenuEntries.All)
			{
				if (candidate.Id == DuelClient.EntryId)
				{
					entry = candidate;
				}
			}
			LogAssert.IsNotNull(entry, "the challenge is registered");
			LogAssert.IsTrue(entry.Applies(new CharacterMenuTarget(5, "Ana", CharacterMenuSource.World)), "offered on a player in the world");
			LogAssert.IsFalse(entry.Applies(new CharacterMenuTarget(5, "Ana", CharacterMenuSource.Guild)), "not on a guild roster row");
			LogAssert.IsFalse(entry.Applies(new CharacterMenuTarget(5, "Ana", CharacterMenuSource.Party)), "nor a party row");
		}

		[Test]
		public void TheCharacterSheet_ShowsTheDuelRecord_OfItsOwnCharacterOnly()
		{
			DuelClient.Install();
			CharacterSheetSections.Section section = null;
			foreach (CharacterSheetSections.Section candidate in CharacterSheetSections.All)
			{
				if (candidate.Id == DuelClient.SheetSectionId)
				{
					section = candidate;
				}
			}
			LogAssert.IsNotNull(section, "the Duels section is registered");
			LogAssert.AreEqual("Duels", section.Title, "under its own heading, apart from the arena's");
			LogAssert.IsFalse(section.Fill(null, new List<CharacterSheetRow>()), "no subject, no rows");

			var rows = new List<CharacterSheetRow>();
			int changes = 0;
			Action counted = () => changes++;
			CharacterSheetSections.Changed += counted;
			try
			{
				DuelClient.ClearRecord();
				changes = 0;
				LogAssert.IsFalse(DuelClient.FillSheetFor(7, rows), "no record yet: no section");

				DuelClient.SetRecord(7, new DuelRecordBroadcast { Rating = 1532, Wins = 3, Losses = 1, Draws = 2 });
				LogAssert.AreEqual(1, changes, "an open sheet is told the record moved");
				LogAssert.IsTrue(DuelClient.FillSheetFor(7, rows), "the character the record is for");
				LogAssert.AreEqual(4, rows.Count, "Rating, Wins, Losses, Draws");
				LogAssert.AreEqual("Rating", rows[0].Label);
				LogAssert.AreEqual("1532", rows[0].Value);
				LogAssert.AreEqual("3", rows[1].Value);
				LogAssert.AreEqual("1", rows[2].Value);
				LogAssert.AreEqual("2", rows[3].Value);
				LogAssert.IsTrue(rows[0].Description.Contains("Elo"), "hovering the rating says what it is");

				rows.Clear();
				LogAssert.IsFalse(DuelClient.FillSheetFor(8, rows), "never someone else's sheet");

				DuelClient.ClearRecord();
				LogAssert.IsFalse(DuelClient.FillSheetFor(7, rows), "gone when the character leaves the world");
			}
			finally
			{
				CharacterSheetSections.Changed -= counted;
				DuelClient.ClearRecord();
			}
		}

		[Test]
		public void TheSceneServer_SendsTheRecordOnArrival()
		{
			string system = Read(ModuleFolder + "/Server/DuelSystem.cs");
			LogAssert.IsTrue(system.Contains("characterSystem.OnSpawnCharacter += OnCharacterSpawned;"),
				"the system follows arrivals, or the sheet stays empty until the first duel");
			LogAssert.IsTrue(Regex.IsMatch(system, @"OnCharacterSpawned\([^)]*\)\s*\{[\s\S]*?FetchRecordAsync[\s\S]*?SendRecord\(0, record\)"),
				"it reads the record and sends it as DuelID 0 (no toast)");
			string hud = Read(ModuleFolder + "/Client/UITKDuel.cs");
			LogAssert.IsTrue(Regex.IsMatch(hud, @"if \(msg\.DuelID == 0\)\s*\{\s*return;"),
				"the HUD announces only records that follow a duel");
		}
	}
}
