using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FishMMO.Core;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The Combat module: every character's damage controller is a part (no prefab authors one), players take
	/// their combat triggers from the module's asset, the scene server adopts the combat system by label and the character
	/// system no longer handles a death itself, and the client's death dialog and combat text are the module's.
	/// </summary>
	[TestFixture]
	public class CombatModuleTests
	{
		private const string ModuleFolder = "Assets/FishMMO/Modules/Combat";

		[Test]
		public void TheModuleIsDeclared()
		{
			var problems = new List<string>();
			List<GameModule> found = ModuleCatalog.Discover(new[] { typeof(CombatModule).Assembly }, problems);
			LogAssert.IsTrue(found.Any(m => m.Id == CombatModule.ModuleId), "the module declares itself ([assembly: GameModule])");
		}

		[Test]
		public void EveryCharacterGetsTheDamageController_AndNoPrefabAuthorsIt()
		{
			LogAssert.IsTrue(CharacterParts.PartsFor(typeof(PlayerCharacter)).Contains(typeof(CharacterDamageController)), "a player gets it");
			LogAssert.IsTrue(CharacterParts.PartsFor(typeof(NPC)).Contains(typeof(CharacterDamageController)), "and an NPC");

			string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(ModuleFolder + "/Runtime/CharacterDamageController.cs"));
			LogAssert.IsFalse(string.IsNullOrEmpty(guid), "the controller is in the module");
			foreach (string prefab in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath))
			{
				LogAssert.IsFalse(ProjectFiles.ReadAllText(prefab).Contains("guid: " + guid), $"{prefab} does not author the damage controller");
			}
			foreach (string file in new[] { "Shared/Implementation/Entity/PlayerCharacter.cs", "Shared/Implementation/Entity/NPC/NPC.cs" })
			{
				LogAssert.IsFalse(Read("Assets/FishMMO/" + file).Contains("RequireComponent(typeof(CharacterDamageController))"),
					$"{file} no longer requires it");
			}
		}

		[Test]
		public void PlayersCountTheirDamageHealingAndKills_FromTheModulesAsset()
		{
			string asset = Read(ModuleFolder + "/Templates/PlayerCombatTriggers.asset");
			foreach ((string list, string trigger) in new[]
			{
				("OnDamage", "Count Damage Dealt"), ("OnHeal", "Count Healing Done"), ("OnKill", "Count Monster Kill"),
			})
			{
				string triggerGuid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path($"Assets/FishMMO/Modules/Achievements/Templates/Counters/{trigger}.asset"));
				LogAssert.IsFalse(string.IsNullOrEmpty(triggerGuid), $"{trigger} exists");
				Match entries = Regex.Match(asset, $@"\n  {list}:\n((?:  - .*\n)*)");
				LogAssert.IsTrue(entries.Success && entries.Groups[1].Value.Contains(triggerGuid), $"a player's {list} runs '{trigger}' (the race prefabs did)");
			}
			string assetGuid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(ModuleFolder + "/Templates/PlayerCombatTriggers.asset"));
			LogAssert.IsTrue(Regex.IsMatch(Read("Assets/AddressableAssetsData/AssetGroups/Shared_Static_Permanent.asset"),
				$@"- m_GUID: {assetGuid}\n(?:    .*\n)*?    - Shared_Static_Permanent\n"), "loaded at boot on every peer, so the cache has it before a character wakes");
		}

		[Test]
		public void TheAssetsTriggers_AreAddedToAPlayersLists_Once()
		{
			var go = new GameObject("CombatModule_Triggers");
			PlayerCombatTriggers triggers = ScriptableObject.CreateInstance<PlayerCombatTriggers>();
			Trigger kill = ScriptableObject.CreateInstance<Trigger>();
			try
			{
				triggers.OnKill.Add(kill);
				CharacterDamageController damage = go.AddComponent<CharacterDamageController>();
				triggers.ApplyTo(damage);
				triggers.ApplyTo(damage);
				LogAssert.AreEqual(1, damage.OnKillTriggers.Count, "added once, however often it is applied");
				LogAssert.AreSame(kill, damage.OnKillTriggers[0]);
				LogAssert.AreEqual(0, damage.OnDamageTriggers.Count, "an empty list adds nothing");
			}
			finally
			{
				Object.DestroyImmediate(go);
				Object.DestroyImmediate(triggers);
				Object.DestroyImmediate(kill);
			}
		}

		[Test]
		public void TheSceneServerAdoptsTheCombatSystem_AndTheCharacterSystemNoLongerHandlesADeath()
		{
			string system = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(ModuleFolder + "/Server/CombatSystem.asset"));
			LogAssert.IsTrue(Regex.IsMatch(Read("Assets/AddressableAssetsData/AssetGroups/Server_Static_Permanent.asset"),
				$@"- m_GUID: {system}\n(?:    .*\n)*?    - SceneServer_Systems\n"), "the system carries the SceneServer_Systems label");

			string combat = Read(ModuleFolder + "/Server/CombatSystem.cs");
			foreach (string handler in new[]
			{
				"RegisterBroadcast<RespawnAtBindPointBroadcast>", "RegisterBroadcast<ResurrectAcceptBroadcast>",
				"ICharacterDamageController.OnKilled += CharacterDamageController_OnKilled",
				"ICharacterDamageController.OnResurrectOffered += CharacterDamageController_OnResurrectOffered",
				"characterSystem.OnSpawnCharacter += CharacterSystem_OnSpawnCharacter",
			})
			{
				LogAssert.IsTrue(combat.Contains(handler), $"the combat system: {handler}");
			}

			string characterSystem = string.Join("\n", new[] { "", ".Connection", ".Loading" }.Select(part =>
				Read($"Assets/FishMMO/Server/Implementation/World/SceneServer/Character/CharacterSystem{part}.cs")));
			foreach (string gone in new[]
			{
				"RespawnAtBindPointBroadcast>(", "ResurrectAcceptBroadcast>(", "ICharacterDamageController.OnKilled +=",
				"ICharacterDamageController.OnResurrectOffered +=", "new DeathBroadcast()",
			})
			{
				LogAssert.IsFalse(characterSystem.Contains(gone), $"the character system no longer does it: {gone}");
			}
			LogAssert.IsTrue(typeof(FishMMO.Server.Core.World.SceneServer.ICharacterDeathHost)
				.IsAssignableFrom(typeof(FishMMO.Server.Implementation.World.SceneServer.CharacterSystem)),
				"the character system lends the world's part of a death");
		}

		[Test]
		public void TheClientsDeathDialogAndCombatText_AreTheModules()
		{
			string dialog = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(ModuleFolder + "/Client/UIDeathDialog.prefab"));
			LogAssert.IsTrue(Regex.IsMatch(Read("Assets/AddressableAssetsData/AssetGroups/Client_Dynamic.asset"),
				$@"- m_GUID: {dialog}\n    m_Address: UIDeathDialog\n(?:    .*\n)*?    - ClientWorldGUI_Panels\n"),
				"the dialog is placed into the world GUI under the name the client opens it by");
			string script = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(ModuleFolder + "/Client/UITKDeathDialog.cs"));
			LogAssert.IsFalse(Read("Assets/Scenes/Client/ClientWorldGUI.unity").Contains(script), "ClientWorldGUI.unity no longer carries it");

			string client = Read("Assets/FishMMO/Client/Client.cs");
			LogAssert.IsFalse(client.Contains("DeathBroadcast"), "the core client no longer handles a death");
			LogAssert.IsFalse(Regex.IsMatch(client, @"\bCombatTextDisplay\b|\bClientCombatDisplay\b"), "nor draws combat text");
			LogAssert.IsTrue(Read(ModuleFolder + "/Client/DeathDialogClient.cs").Contains("RegisterBroadcast<DeathBroadcast>(OnDeathBroadcast)"),
				"the module's death service does");
		}

		[Test]
		public void APartHearsTheNetworkStart_AsTheControllerDidAsABehaviour()
		{
			string character = Read("Assets/FishMMO/Shared/Implementation/Entity/BaseCharacter.cs");
			LogAssert.IsTrue(character.Contains("CharacterParts.Forward(this, part => part.OnStartNetwork());"), "the network start is forwarded");
			LogAssert.IsTrue(character.Contains("CharacterParts.Forward(this, part => part.OnStopNetwork());"), "and the stop");
		}

		private static string Read(string path) => ProjectFiles.ReadAllText(path).Replace("\r\n", "\n");
	}
}
