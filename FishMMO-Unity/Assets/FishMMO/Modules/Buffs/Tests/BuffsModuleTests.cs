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
	/// The Buffs module (2026-11-20): the module declares itself, its controller is the core's buff seams, the core reaches
	/// buffs only through them, its mitigation is installed into the core's hook, and its strips are the world GUI's panels.
	/// </summary>
	[TestFixture]
	public class BuffsModuleTests
	{
		[Test]
		public void TheModuleIsDeclared()
		{
			var problems = new List<string>();
			List<GameModule> found = ModuleCatalog.Discover(new[] { typeof(BuffsModule).Assembly }, problems);
			LogAssert.IsTrue(found.Any(m => m.Id == BuffsModule.ModuleId), "the module declares itself ([assembly: GameModule])");
		}

		[Test]
		public void TheControllerIsTheCoresBuffSeams()
		{
			LogAssert.IsTrue(typeof(ICharacterBuffs).IsAssignableFrom(typeof(BuffController)), "what the core does with buffs");
			LogAssert.IsTrue(typeof(IBuffPersistence).IsAssignableFrom(typeof(BuffController)), "buffs in the save");
			LogAssert.IsTrue(typeof(ICharacterBuffs).IsAssignableFrom(typeof(IBuffController)), "the module's contract extends the core's");
			LogAssert.IsTrue(typeof(IBuffTemplateInfo).IsAssignableFrom(typeof(BaseBuffTemplate)), "the core reads a template through its view");
		}

		[Test]
		public void TheCoreNamesNoBuffImplementationType()
		{
			foreach (string file in new[]
			{
				"Assets/FishMMO/Server/Implementation/World/SceneServer/Character/CharacterSystem.Saving.cs",
				"Assets/FishMMO/Server/Implementation/World/SceneServer/Character/CharacterSystem.Loading.cs",
				"Assets/FishMMO/Server/Implementation/World/SceneServer/Character/CharacterSystem.Connection.cs",
				"Assets/FishMMO/Shared/Implementation/Entity/Prediction/Weather/WeatherExposureController.cs",
				"Assets/FishMMO/Client/GUI/World/Target/UITKTarget.cs",
			})
			{
				string code = Regex.Replace(Read(file), @"//[^\n]*", string.Empty);
				LogAssert.IsFalse(Regex.IsMatch(code, @"\b(IBuffController|BaseBuffTemplate|new Buff\()"), $"{file} reaches buffs only through the core seams");
			}
		}

		[Test]
		public void TheMitigationIsInstalled()
		{
			LogAssert.IsNotNull(DamageMitigation.Negator, "shields absorb damage");
			LogAssert.IsNotNull(DamageMitigation.Deflector, "deflection turns objects aside");
			LogAssert.IsNotNull(DamageMitigation.VolumeBlocker, "guard volumes block");
		}

		[Test]
		public void TheStripsAreTheModulesPanels()
		{
			foreach ((string prefab, string address, string script) in new[]
			{
				("Assets/FishMMO/Modules/Buffs/Client/Buff/UIBuff.prefab", "UIBuff", "Assets/FishMMO/Modules/Buffs/Client/Buff/UITKBuff.cs"),
				("Assets/FishMMO/Modules/Buffs/Client/Buff/UIDebuff.prefab", "UIDebuff", "Assets/FishMMO/Modules/Buffs/Client/Buff/UITKDebuff.cs"),
			})
			{
				string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(prefab));
				LogAssert.IsTrue(Regex.IsMatch(Read("Assets/AddressableAssetsData/AssetGroups/Client_Dynamic.asset"),
					$@"- m_GUID: {guid}\n    m_Address: {address}\n(?:    .*\n)*?    - ClientWorldGUI_Panels\n"),
					$"{address} is placed into the world GUI under the name the client shows it by");
				string scriptGuid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(script));
				LogAssert.IsFalse(Read("Assets/Scenes/Client/ClientWorldGUI.unity").Contains(scriptGuid), $"ClientWorldGUI.unity no longer carries {address}");
			}
		}

		private static string Read(string path) => ProjectFiles.ReadAllText(path).Replace("\r\n", "\n");
	}
}
