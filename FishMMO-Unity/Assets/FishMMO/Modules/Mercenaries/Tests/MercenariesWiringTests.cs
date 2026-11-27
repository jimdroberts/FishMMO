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
	/// The Mercenaries module as the builder made it (issue #135): every kit hires a Mercenary with exactly the kit's
	/// abilities, every kit has a Mercenary for hire titled "Mercenary" that offers it, and the server system and the
	/// window are where the game loads them from.
	/// </summary>
	[TestFixture]
	public class MercenariesWiringTests
	{
		private const string SharedGroup = "Assets/AddressableAssetsData/AssetGroups/Shared_Static_Permanent.asset";
		private const string ServerGroup = "Assets/AddressableAssetsData/AssetGroups/Server_Static_Permanent.asset";
		private const string ClientGroup = "Assets/AddressableAssetsData/AssetGroups/Client_Dynamic.asset";

		private static string Read(string path) => ProjectFiles.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), path)).Replace("\r\n", "\n");

		private static bool HasLabel(string group, string assetPath, string label)
		{
			string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(assetPath));
			return !string.IsNullOrEmpty(guid) && Regex.IsMatch(group, $@"- m_GUID: {guid}\n(?:    .*\n)*?    - {label}\n");
		}

		[Test]
		public void EveryKit_HiresAMercenary_WithExactlyItsAbilities()
		{
			string shared = Read(SharedGroup);
			foreach (MercenaryBuilder.KitSpec spec in MercenaryBuilder.Kits)
			{
				string kitPath = MercenaryBuilder.KitPath(spec);
				MercenaryKitTemplate kit = AssetDatabase.LoadAssetAtPath<MercenaryKitTemplate>(ProjectFiles.Path(kitPath));
				LogAssert.IsNotNull(kit, $"{kitPath} exists");
				LogAssert.AreEqual(spec.Abilities.Length, kit.Abilities.Count, $"{spec.Key}: its preset abilities");
				LogAssert.IsTrue(kit.HireCost > 0, $"{spec.Key}: hiring costs something");
				LogAssert.IsNotNull(kit.Prefab, $"{spec.Key}: the NPC hired");
				Mercenary mercenary = kit.Prefab.GetComponent<Mercenary>();
				LogAssert.IsNotNull(mercenary, $"{spec.Key}: the NPC hired is a Mercenary");
				LogAssert.AreEqual(kit.Abilities.Count, mercenary.Abilities.Count, $"{spec.Key}: the prefab knows the kit's abilities and no others");
				for (int i = 0; i < kit.Abilities.Count; ++i)
				{
					LogAssert.IsTrue(mercenary.Abilities.Contains(kit.Abilities[i]), $"{spec.Key}: knows {kit.Abilities[i].name}");
				}
				LogAssert.IsTrue(HasLabel(shared, kitPath, "Shared_Static_Permanent"), $"{spec.Key}: the kit loads on every peer");
				LogAssert.IsTrue(HasLabel(shared, MercenaryBuilder.MercenaryPath(spec), "Shared_Static_Permanent"), $"{spec.Key}: the hired NPC can be spawned");
			}
		}

		[Test]
		public void TheHiredNpc_CarriesItsNamerAheadOfItsNpc_SoItCanWearTheHiredName()
		{
			foreach (MercenaryBuilder.KitSpec spec in MercenaryBuilder.Kits)
			{
				GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectFiles.Path(MercenaryBuilder.MercenaryPath(spec)));
				LogAssert.IsNotNull(prefab, $"{spec.Key}: hired prefab");
				Component[] components = prefab.GetComponents<Component>();
				int namer = System.Array.FindIndex(components, c => c is SceneObjectNamer);
				int npc = System.Array.FindIndex(components, c => c is NPC);
				LogAssert.IsTrue(namer >= 0 && npc >= 0 && namer < npc, $"{spec.Key}: the namer's payload is read before the NPC's");
			}
		}

		[Test]
		public void EveryKit_HasAMercenaryForHire_TitledMercenary()
		{
			string shared = Read(SharedGroup);
			foreach (MercenaryBuilder.KitSpec spec in MercenaryBuilder.Kits)
			{
				string path = MercenaryBuilder.RecruiterPath(spec);
				GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectFiles.Path(path));
				LogAssert.IsNotNull(prefab, $"{path} exists");
				MercenaryRecruiter recruiter = prefab.GetComponent<MercenaryRecruiter>();
				LogAssert.IsNotNull(recruiter, $"{spec.Key}: it is a Mercenary");
				LogAssert.AreEqual("Mercenary", recruiter.Title, "titled Mercenary under the name");
				LogAssert.IsNotNull(recruiter.Kit, $"{spec.Key}: it offers a kit");
				LogAssert.AreEqual(spec.DisplayName, recruiter.Kit.DisplayName, $"{spec.Key}: its own kit");
				LogAssert.AreEqual(1, recruiter.OnInteractTriggers.Count, "speaking to it runs the Mercenary Interact trigger");
				LogAssert.AreEqual(MercenaryBuilder.TriggerPath, AssetDatabase.GetAssetPath(recruiter.OnInteractTriggers[0]));
				LogAssert.IsTrue(HasLabel(shared, path, "Shared_Static_Permanent"), $"{spec.Key}: a cut scene can place it");
			}
			LogAssert.IsTrue(HasLabel(shared, MercenaryBuilder.TriggerPath, "Shared_Static_Permanent"), "the trigger loads on every peer");
		}

		[Test]
		public void TheSceneServer_LoadsTheMercenarySystem_WithACurrency()
		{
			MercenarySystem system = AssetDatabase.LoadAssetAtPath<MercenarySystem>(ProjectFiles.Path(MercenaryBuilder.SystemPath));
			LogAssert.IsNotNull(system, $"{MercenaryBuilder.SystemPath} is a MercenarySystem");
			SerializedProperty currency = new SerializedObject(system).FindProperty("currencyTemplate");
			LogAssert.IsNotNull(currency.objectReferenceValue, "hires are paid in the Currency attribute");
			LogAssert.IsTrue(HasLabel(Read(ServerGroup), MercenaryBuilder.SystemPath, "SceneServer_Systems"),
				"the asset carries the SceneServer_Systems label, or no scene server has mercenaries");
		}

		[Test]
		public void TheMercenaryWindow_IsAModulePanelOfTheWorldGui()
		{
			GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectFiles.Path(MercenaryBuilder.PanelPath));
			LogAssert.IsNotNull(prefab, $"{MercenaryBuilder.PanelPath} exists");
			LogAssert.AreEqual(UITKMercenary.ControlName, prefab.name, "the prefab's name is the panel's UIManager key");
			UITKMercenary window = prefab.GetComponent<UITKMercenary>();
			LogAssert.IsNotNull(window, "the prefab carries the window");
			LogAssert.IsNotNull(window.Document, "its Document is assigned");
			LogAssert.IsNotNull(window.Document.visualTreeAsset, "rendering the mercenary markup");
			LogAssert.IsFalse(window.StartOpen, "it starts hidden");
			LogAssert.IsTrue(HasLabel(Read(ClientGroup), MercenaryBuilder.PanelPath, ModulePanelHost.LabelFor("ClientWorldGUI")),
				"the prefab is addressable as a world GUI panel");
		}
	}
}
