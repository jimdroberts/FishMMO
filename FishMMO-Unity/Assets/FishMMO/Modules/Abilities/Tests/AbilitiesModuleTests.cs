using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FishMMO.Core;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using NUnit.Framework;
using UnityEditor;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The Abilities module: the module declares itself, the scene server adopts its system by label, the core
	/// character save reaches abilities only through <see cref="IAbilityPersistence"/>, and the core's grant and equip seams
	/// are installed by the module.
	/// </summary>
	[TestFixture]
	public class AbilitiesModuleTests
	{
		private const string ModuleFolder = "Assets/FishMMO/Modules/Abilities";

		[Test]
		public void TheModuleIsDeclared()
		{
			var problems = new List<string>();
			List<GameModule> found = ModuleCatalog.Discover(new[] { typeof(AbilitiesModule).Assembly }, problems);
			LogAssert.IsTrue(found.Any(m => m.Id == AbilitiesModule.ModuleId), "the module declares itself ([assembly: GameModule])");
		}

		[Test]
		public void TheSceneServerAdoptsTheAbilitySystem_ByLabel()
		{
			AssertAdoptedByLabel(ModuleFolder + "/Server/AbilitySystem.asset");
		}

		[Test]
		public void TheControllerIsTheCoresAbilityPersistence_AndTheCoreSaveNamesNoAbilityType()
		{
			LogAssert.IsTrue(typeof(IAbilityPersistence).IsAssignableFrom(typeof(AbilityController)), "the controller implements the core save contract");
			LogAssert.IsTrue(typeof(IAbilityController).IsAssignableFrom(typeof(IAbilityKnowledgeController)), "the module's knowledge handle extends the core one");
			string save = string.Join("\n", new[] { ".Loading", ".Saving", ".Social" }.Select(part =>
				Read($"Assets/FishMMO/Server/Implementation/World/SceneServer/Character/CharacterSystem{part}.cs")));
			foreach (string gone in new[] { "new Ability(", "BaseAbilityTemplate", "AbilityEvent>", "KnownAbilityAddBroadcast", "abilityController.KnownAbilities" })
			{
				LogAssert.IsFalse(save.Contains(gone), $"the core character system no longer names {gone}");
			}
			LogAssert.IsTrue(Read(ModuleFolder + "/Server/Ability/AbilitySystem.cs").Contains("characterSystem.OnSpawnCharacter += CharacterSystem_OnSpawnCharacter"),
				"the module's system sends a spawning player its known abilities");
		}

		[Test]
		public void TheCoreGrantAndEquipSeams_AreInstalled()
		{
			LogAssert.IsNotNull(AbilityGrants.Granter, "content that names abilities (a lore reward) can teach them");
			LogAssert.IsNotNull(AbilityGrants.Equipper, "an NPC's authored kit is equipped at server start");
			LogAssert.IsTrue(Read("Assets/FishMMO/Shared/Implementation/Entity/NPC/NPC.cs").Contains("AbilityGrants.Equip(this, Abilities);"),
				"the NPC asks for its kit as it starts on the server");
		}

		[Test]
		public void TheAbilitiesWindowAndCastBar_AreTheModulesPanels()
		{
			foreach ((string prefab, string address, string script) in new[]
			{
				("Assets/FishMMO/Modules/Abilities/Client/Ability/UIAbilities.prefab", "UIAbilities", "Assets/FishMMO/Modules/Abilities/Client/Ability/UITKAbilities.cs"),
				("Assets/FishMMO/Modules/Abilities/Client/Ability/UICastBar.prefab", "UICastBar", "Assets/FishMMO/Modules/Abilities/Client/Ability/UITKCastBar.cs"),
			})
			{
				string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(prefab));
				LogAssert.IsTrue(Regex.IsMatch(Read("Assets/AddressableAssetsData/AssetGroups/Client_Dynamic.asset"),
					$@"- m_GUID: {guid}\n    m_Address: {address}\n(?:    .*\n)*?    - ClientWorldGUI_Panels\n"),
					$"{address} is placed into the world GUI under the name the client opens it by");
				string scriptGuid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(script));
				LogAssert.IsFalse(Read("Assets/Scenes/Client/ClientWorldGUI.unity").Contains(scriptGuid), $"ClientWorldGUI.unity no longer carries {address}");
			}
		}

		private static string Read(string path) => ProjectFiles.ReadAllText(path).Replace("\r\n", "\n");

		private static void AssertAdoptedByLabel(string assetPath)
		{
			string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(assetPath));
			LogAssert.IsFalse(string.IsNullOrEmpty(guid), $"{assetPath} exists");
			LogAssert.IsTrue(Regex.IsMatch(Read("Assets/AddressableAssetsData/AssetGroups/Server_Static_Permanent.asset"),
				$@"- m_GUID: {guid}\n(?:    .*\n)*?    - SceneServer_Systems\n"), $"{assetPath} carries the SceneServer_Systems label");
			LogAssert.IsFalse(Read("Assets/Scenes/Server/SceneServer.unity").Contains(guid), $"SceneServer.unity no longer lists {assetPath}");
		}
	}
}
