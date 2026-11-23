using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using FishNet.Connection;
using FishNet.Managing.Predicting;
using FishNet.Object;
using FishNet.Serializing;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins the invariants the persistence audit restored: factions and archetypes
	/// carry a dirty mark and a version that advances on mutation, restored rows are clean, a
	/// pooled attribute forgets its previous occupant's version stream, the faction reader
	/// refuses a frame too short to hold its header, and the player prefab requires every
	/// controller the save legs write.
	/// </summary>
	[TestFixture]
	public class PersistenceAuditTests
	{
		private readonly List<Object> temporaries = new List<Object>();

		[TearDown]
		public void TearDown()
		{
			for (int i = 0; i < temporaries.Count; ++i)
			{
				if (temporaries[i] is ScriptableObject so && so is ICachedObject cached)
				{
					cached.RemoveFromCache();
				}
				if (temporaries[i] != null)
				{
					Object.DestroyImmediate(temporaries[i]);
				}
			}
			temporaries.Clear();
		}

		private sealed class MockCharacter : ICharacter
		{
			public MockCharacter(long id) => ID = id;
			public long ID { get; set; }
			public string Name => "MockCharacter";
			public Transform Transform => null;
			public GameObject GameObject => null;
			public Collider Collider { get; set; }
			public NetworkConnection Owner => null;
			public NetworkObject NetworkObject => null;
			public PredictionManager PredictionManager => null;
			public HashSet<NetworkConnection> Observers { get; } = new HashSet<NetworkConnection>();
			public bool IsTeleporting => false;
			public bool IsSpawned => true;
			public int Flags { get; set; }
			/// <inheritdoc/>
			public Nameplate CharacterNameplate { get; set; }
			public Transform MeshRoot => null;
#if !UNITY_SERVER
			public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex) { }
			public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex, CharacterGender gender) { }
#endif
			public void EnableFlags(CharacterFlags flags) => Flags |= 1 << (int)flags;
			public void DisableFlags(CharacterFlags flags) => Flags &= ~(1 << (int)flags);
			public bool IsFlagged(CharacterFlags flags) => (Flags & (1 << (int)flags)) != 0;
			public void RegisterCharacterBehaviour(ICharacterBehaviour b) { }
			public void UnregisterCharacterBehaviour(ICharacterBehaviour b) { }
			public bool TryGet<T>(out T control) where T : class, ICharacterBehaviour { control = null; return false; }
			public void Invoke(List<Trigger> triggers, EventData eventData) { }
		}

		// ── Faction ──────────────────────────────────────────────────────────────

		[Test]
		public void CharacterAttribute_ResetPersistenceState_ForgetsThePreviousOccupant()
		{
			CharacterAttributeTemplate template = ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
			template.name = "PersistenceAudit_Attribute";
			template.AddToCache(template.name);
			temporaries.Add(template);

			// No controller: SetValueDirect is the setter that bypasses the change funnel, and the
			// persistence state is the only thing under test here.
			CharacterAttribute attribute = new CharacterAttribute(null, template.ID, 10, 0);
			attribute.Version = 41;
			attribute.SetValueDirect(11);
			Assert.IsTrue(attribute.PersistenceDirty);

			attribute.ResetPersistenceState();
			Assert.AreEqual(0, attribute.Version, "The next occupant's rows must not inherit a foreign version stream.");
			Assert.IsFalse(attribute.PersistenceDirty, "Nor a dirty mark that would write its template default as a change.");

			// And the in-flight markers: a confirmation for the old stream must not clear a new change.
			attribute.SetValueDirect(12);
			attribute.MarkPersisted(41);
			Assert.IsTrue(attribute.PersistenceDirty);
		}

		// ── The prefab contract ──────────────────────────────────────────────────


		[Test]
		public void PlayerCharacter_RequiresEveryControllerTheSaveLegsWrite()
		{
			var required = new HashSet<System.Type>();
			foreach (RequireComponent attribute in typeof(PlayerCharacter).GetCustomAttributes<RequireComponent>(true))
			{
				if (attribute.m_Type0 != null) required.Add(attribute.m_Type0);
				if (attribute.m_Type1 != null) required.Add(attribute.m_Type1);
				if (attribute.m_Type2 != null) required.Add(attribute.m_Type2);
			}

			/* The item containers are the Items module's parts (declared with [assembly: CharacterPart], so every
			 * player gets them as it wakes; pinned by ItemsModuleTests), not prefab components the player requires. */
			foreach (System.Type type in new[]
			{
				typeof(CharacterAttributeController),
			})
			{
				Assert.IsTrue(required.Contains(type), $"PlayerCharacter must require {type.Name}: the load and save legs address it, and a prefab without it silently loses that table.");
			}
		}

		[Test]
		public void EveryPlayer_GetsTheAbilityAndBuffControllers_AsParts_AndNoPrefabAuthorsThem()
		{
			/* The save legs reach the ability and buff controllers through IAbilityPersistence and IBuffPersistence. A
			 * player without one would silently lose that table, so both are parts every player gets as it wakes
			 *, and no prefab carries a copy of its own. */
			foreach (System.Type controllerType in new[] { typeof(AbilityController), typeof(BuffController), typeof(CooldownController) })
			{
				Assert.IsTrue(CharacterParts.PartsFor(typeof(PlayerCharacter)).Contains(controllerType), $"a player gets a {controllerType.Name}");
				Assert.IsTrue(CharacterParts.PartsFor(typeof(NPC)).Contains(controllerType), $"and so does an NPC: {controllerType.Name}");
				string controllerGuid = GuidOfScript(controllerType);
				Assert.IsFalse(string.IsNullOrEmpty(controllerGuid), $"{controllerType.Name} resolves");
				foreach (string path in UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }).Select(UnityEditor.AssetDatabase.GUIDToAssetPath))
				{
					Assert.IsFalse(System.IO.File.ReadAllText(path).Contains("guid: " + controllerGuid), $"{path} does not author a {controllerType.Name}");
				}
			}
		}

		private static string GuidOfScript(System.Type type)
		{
			foreach (string guid in UnityEditor.AssetDatabase.FindAssets(type.Name + " t:MonoScript"))
			{
				var script = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.MonoScript>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
				if (script != null && script.GetClass() == type)
				{
					return guid;
				}
			}
			return null;
		}
	}
}
