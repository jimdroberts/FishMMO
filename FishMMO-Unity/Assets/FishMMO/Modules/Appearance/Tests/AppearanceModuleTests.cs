using System.Collections.Generic;
using System.Linq;
using FishMMO.Core;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEditor;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The Appearance module: its three components are character parts every player gets when it wakes, and
	/// no race prefab authors them, so removing the folder leaves the prefabs whole.
	/// </summary>
	[TestFixture]
	public class AppearanceModuleTests
	{
		private const string ModuleFolder = "Assets/FishMMO/Modules/Appearance";

		[Test]
		public void TheModuleIsDeclared()
		{
			var problems = new List<string>();
			List<GameModule> found = ModuleCatalog.Discover(new[] { typeof(AppearanceModule).Assembly }, problems);
			LogAssert.IsTrue(found.Any(m => m.Id == AppearanceModule.ModuleId), "the module declares itself ([assembly: GameModule])");
		}

		[Test]
		public void EveryPlayerGetsTheThreeParts_AndNoPrefabAuthorsThem()
		{
			foreach ((System.Type part, string file) in new[]
			{
				(typeof(BodyVisibilityManager), "BodyVisibilityManager"),
				(typeof(CharacterAppearanceManager), "CharacterAppearanceManager"),
				(typeof(EquipmentVisualController), "EquipmentVisualController"),
			})
			{
				LogAssert.IsTrue(CharacterParts.PartsFor(typeof(PlayerCharacter)).Contains(part), $"a player gets {file} as a part");
				LogAssert.IsFalse(CharacterParts.PartsFor(typeof(NPC)).Contains(part), $"an NPC does not get {file}");
				string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path($"{ModuleFolder}/Runtime/{file}.cs"));
				foreach (string race in new[] { "Human", "Elf", "Orc" })
				{
					LogAssert.IsFalse(ProjectFiles.ReadAllText($"Assets/Prefabs/Shared/Entity/PlayableCharacters/{race}.prefab").Contains(guid),
						$"{race}.prefab does not author {file}");
				}
			}
			LogAssert.IsFalse(ProjectFiles.ReadAllText("Assets/FishMMO/Shared/Implementation/Entity/PlayerCharacter.cs").Contains("RequireComponent(typeof(BodyVisibilityManager))"),
				"the player no longer requires the components");
		}
	}
}
