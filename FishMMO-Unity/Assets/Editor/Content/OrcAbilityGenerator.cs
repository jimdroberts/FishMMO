using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Editor.Content
{
	/// <summary>
	/// Corrects the shipped ability templates and authors the orc ability suite.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Runs idempotently: existing assets are edited in place (GUIDs and addressable entries
	/// survive), new ones are created once. Menu: <c>FishMMO/Content/Generate Orc Abilities</c>;
	/// headless: <c>-executeMethod FishMMO.Editor.Content.OrcAbilityGenerator.GenerateAll</c>.
	/// </para>
	/// <para>
	/// What was wrong with the shipped set (issue #220 follow-up, 2026-09-03):
	/// </para>
	/// <list type="bullet">
	///   <item>"Lesser Fireball" had Speed 0 and no tick-move event, so the "fireball" was a
	///   stationary ball at the caster's hand for five seconds. Movement is an ECA tick action
	///   (<see cref="AbilityMoveTransformAction"/>), not something Speed does on its own.</item>
	///   <item>Its hit event carried the mana cost as a hit CONDITION with no target selector.
	///   <c>HasResourceCondition</c> checks the event's target, which on a hit is the victim: the
	///   damage only landed on victims holding 5 mana. The cost belongs in the ability's
	///   activation conditions.</item>
	///   <item>The same hit event played its FX by instantiating the fireball ABILITY prefab —
	///   an orphan AbilityObject per hit. It now plays the fire VFX prefab.</item>
	///   <item>"Lesser Flame" had no events of any kind: it spawned, sat there, and did nothing.</item>
	/// </list>
	/// </remarks>
	public static class OrcAbilityGenerator
	{
		private const string AddressableGroupName = "Shared_Static_Permanent";

		private const string TypesFolder = "Assets/Templates/Entity/Abilities/Types";
		private const string OrcFolder = TypesFolder + "/Orc";
		private const string FireHitFolder = "Assets/Templates/Entity/Abilities/Events/Hit/Fire Damage";
		private const string PhysicalHitFolder = "Assets/Templates/Entity/Abilities/Events/Hit/Physical Damage";
		private const string MoveFolder = "Assets/Templates/Entity/Abilities/Events/Move";
		private const string DestroyFolder = "Assets/Templates/Entity/Abilities/Events/Destroy";
		private const string PrefabFolder = "Assets/Prefabs/Shared/Entity/Abilities/Types";

		private const string PathPhysicalDamage = "Assets/Templates/Entity/CharacterAttributes/Damage/Physical Damage.asset";
		private const string PathFireDamage = "Assets/Templates/Entity/CharacterAttributes/Damage/Fire Damage.asset";
		private const string PathMana = "Assets/Templates/Entity/CharacterAttributes/Resource/Mana.asset";
		private const string PathFireballPrefab = PrefabFolder + "/Fireball/Lesser Fireball.prefab";
		private const string PathFlamePrefab = PrefabFolder + "/Flame/Flame.prefab";
		private const string PathPunchPrefab = PrefabFolder + "/Punch/Punch.prefab";
		private const string PathSlamPrefab = PrefabFolder + "/Slam/Orc Slam.prefab";
		private const string PathLesserFireball = TypesFolder + "/Lesser Fireball.asset";
		private const string PathLesserFlame = TypesFolder + "/Lesser Flame.asset";
		private const string PathPunch = TypesFolder + "/Punch.asset";
		private const string PathLesserFireDamage = FireHitFolder + "/Lesser Fire Damage.asset";
		private const string PathMinorArmorEvent = "Assets/Templates/Entity/Abilities/Events/Buff/Event/Minor Increase Armor Event.asset";
		/// <summary>The pure VFX prefab nested inside the fireball ability prefab.</summary>
		private const string FireVfxGuid = "422ac1a06602ba841bd45b8b1c616098";

		private const string PathOrc = "Assets/Prefabs/Shared/Entity/NPCs/Monsters/Orcs/an orc.prefab";
		private const string PathOrcWarrior = "Assets/Prefabs/Shared/Entity/NPCs/Monsters/Orcs/an orc warrior.prefab";
		private const string PathOrcMage = "Assets/Prefabs/Shared/Entity/NPCs/Monsters/Orcs/an orc mage.prefab";

		private const int AbilityObjectLayer = 9;

		private static readonly List<string> touched = new List<string>();
		private static readonly List<string> problems = new List<string>();

		[MenuItem("FishMMO/Content/Generate Orc Abilities")]
		public static void GenerateAll()
		{
			touched.Clear();
			problems.Clear();

			DamageAttributeTemplate physical = Require<DamageAttributeTemplate>(PathPhysicalDamage);
			DamageAttributeTemplate fire = Require<DamageAttributeTemplate>(PathFireDamage);
			CharacterAttributeTemplate mana = Require<CharacterAttributeTemplate>(PathMana);
			GameObject fireballPrefab = Require<GameObject>(PathFireballPrefab);
			GameObject flamePrefab = Require<GameObject>(PathFlamePrefab);
			GameObject punchPrefab = Require<GameObject>(PathPunchPrefab);
			AbilityOnHitEvent lesserFireDamage = Require<AbilityOnHitEvent>(PathLesserFireDamage);
			AbilityOnHitEvent minorArmor = Require<AbilityOnHitEvent>(PathMinorArmorEvent);
			AbilityTemplate lesserFireball = Require<AbilityTemplate>(PathLesserFireball);
			AbilityTemplate lesserFlame = Require<AbilityTemplate>(PathLesserFlame);
			AbilityTemplate punch = Require<AbilityTemplate>(PathPunch);
			string fireVfxPath = AssetDatabase.GUIDToAssetPath(FireVfxGuid);
			GameObject fireVfx = string.IsNullOrEmpty(fireVfxPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(fireVfxPath);
			if (fireVfx == null)
			{
				problems.Add($"Fire VFX prefab {FireVfxGuid} not found.");
			}

			if (problems.Count > 0)
			{
				Debug.LogError("[OrcAbilityGenerator] Aborting:\n" + string.Join("\n", problems));
				return;
			}

			foreach (string folder in new[] { OrcFolder, MoveFolder, DestroyFolder, PrefabFolder + "/Slam" })
			{
				EnsureFolder(folder);
			}

			// ---- Corrections to shipped events -------------------------------------------------

			/* Lesser Fire Damage: unconditional 3 fire on the hit target, VFX from the pure VFX
			 * prefab. The mana cost moves to the abilities that use this event. */
			lesserFireDamage.Conditions.Clear();
			lesserFireDamage.OnConditionsMetActions.Clear();
			lesserFireDamage.OnConditionsMetActions.Add(Damage(3, fire));
			lesserFireDamage.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = fireVfx });
			Touch(lesserFireDamage);

			// ---- New events ----------------------------------------------------------------------

			AbilityOnTickEvent projectileMove = SaveEvent<AbilityOnTickEvent>(MoveFolder, "Projectile Forward Move Event", e =>
			{
				e.OnConditionsMetActions.Add(new AbilityMoveTransformAction { MoveDirection = Vector3.forward });
			});

			AbilityOnHitEvent minorFireDamage = SaveEvent<AbilityOnHitEvent>(FireHitFolder, "Minor Fire Damage", e =>
			{
				e.OnConditionsMetActions.Add(Damage(2, fire));
				e.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = fireVfx });
			});

			AbilityOnHitEvent slamDamage = SaveEvent<AbilityOnHitEvent>(PhysicalHitFolder, "Orc Slam Damage", e =>
			{
				e.OnConditionsMetActions.Add(Damage(9, physical));
			});

			AbilityOnDestroyEvent fireImpact = SaveEvent<AbilityOnDestroyEvent>(DestroyFolder, "Fire Impact FX Event", e =>
			{
				e.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = fireVfx });
			});

			// ---- New prefab ----------------------------------------------------------------------

			GameObject slamPrefab = BuildSlamPrefab();

			// ---- Corrections to shipped abilities ------------------------------------------------

			/* Lesser Fireball: a projectile at last. 12 m/s for 2.5 s = 30 m reach, which is
			 * what the Caster archetype's 22 m preferred distance was always written against. */
			lesserFireball.Description = "Hurls a ball of flame that bursts on the first thing it strikes.";
			lesserFireball.AbilityObjectPrefab = fireballPrefab;
			lesserFireball.AbilitySpawnTarget = AbilitySpawnTarget.SpawnerWithCameraRotation;
			lesserFireball.ActivationTime = 0.8f;
			lesserFireball.LifeTime = 2.5f;
			lesserFireball.Speed = 12f;
			lesserFireball.Cooldown = 2.5f;
			lesserFireball.HitCount = 1;
			lesserFireball.RequiresTarget = false;
			SetActivationCost(lesserFireball, mana, 5);
			SetEvents(lesserFireball, tick: projectileMove, hit: lesserFireDamage, destroy: fireImpact);
			Touch(lesserFireball);

			/* Lesser Flame: a short burst of fire at the caster's hand. The caster's answer to
			 * something that has closed to melee — it reaches about a metre. */
			lesserFlame.Description = "A burst of flame in front of the caster. Burns whatever is standing in it.";
			lesserFlame.AbilityObjectPrefab = flamePrefab;
			lesserFlame.AbilitySpawnTarget = AbilitySpawnTarget.SpawnerWithCameraRotation;
			lesserFlame.ActivationTime = 0.3f;
			lesserFlame.LifeTime = 0.5f;
			lesserFlame.Speed = 0f;
			lesserFlame.Cooldown = 4f;
			lesserFlame.HitCount = 2;
			lesserFlame.RequiresTarget = false;
			SetActivationCost(lesserFlame, mana, 3);
			SetEvents(lesserFlame, tick: null, hit: lesserFireDamage, destroy: null);
			Touch(lesserFlame);

			// Punch is sound as shipped; only make sure the fields the contract test reads are explicit.
			if (punch.HitCount < 1)
			{
				punch.HitCount = 1;
				Touch(punch);
			}

			// ---- Orc suite -----------------------------------------------------------------------

			AbilityTemplate firebolt = SaveAbility(OrcFolder, "Orc Firebolt", a =>
			{
				a.Description = "A quick, cheap bolt of fire. Less damage than a fireball, far faster to throw.";
				a.AbilityObjectPrefab = fireballPrefab;
				a.AbilitySpawnTarget = AbilitySpawnTarget.SpawnerWithCameraRotation;
				a.Type = AbilityType.Magic;
				a.ActivationTime = 0.4f;
				a.LifeTime = 1.5f;
				a.Speed = 18f;
				a.Cooldown = 1.5f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(mana, 3));
				a.OnTickEvents.Add(projectileMove);
				a.OnHitEvents.Add(minorFireDamage);
				a.OnDestroyEvents.Add(fireImpact);
			});

			AbilityTemplate slam = SaveAbility(OrcFolder, "Orc Slam", a =>
			{
				a.Description = "A heavy two-handed slam that hits everything in front of the orc.";
				a.AbilityObjectPrefab = slamPrefab;
				a.AbilitySpawnTarget = AbilitySpawnTarget.Forward;
				a.Type = AbilityType.Physical;
				a.ActivationTime = 0.6f;
				a.LifeTime = 0.1f;
				a.Speed = 0f;
				a.Cooldown = 6f;
				a.HitCount = 3;
				a.OnHitEvents.Add(slamDamage);
			});

			AbilityTemplate warCry = SaveAbility(OrcFolder, "Orc War Cry", a =>
			{
				a.Description = "The orc bellows and braces itself, hardening its hide for a time.";
				a.AbilityObjectPrefab = null;
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.Type = AbilityType.Physical;
				a.ActivationTime = 0.5f;
				a.LifeTime = 0f;
				a.Speed = 0f;
				a.Cooldown = 20f;
				a.HitCount = 1;
				a.OnHitEvents.Add(minorArmor);
			});

			// ---- Kits ----------------------------------------------------------------------------

			AssignKit(PathOrc, punch, slam);
			AssignKit(PathOrcWarrior, punch, slam, warCry);
			AssignKit(PathOrcMage, lesserFireball, firebolt, lesserFlame);

			AssetDatabase.SaveAssets();
			RegisterAddressables();
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();

			Debug.Log($"[OrcAbilityGenerator] Done. {touched.Count} asset(s) written:\n" + string.Join("\n", touched));
		}

		// =======================================================================================

		private static GameObject BuildSlamPrefab()
		{
			/* Same anatomy as the Punch prefab: a half-scale cube on the ability layer with a
			 * kinematic continuous rigidbody, a box collider sized for the swing, and the
			 * AbilityObject driver. Wider and lower than a punch. */
			GameObject root = new GameObject("Orc Slam");
			try
			{
				root.layer = AbilityObjectLayer;
				root.transform.localScale = Vector3.one * 0.5f;

				MeshFilter filter = root.AddComponent<MeshFilter>();
				filter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
				MeshRenderer renderer = root.AddComponent<MeshRenderer>();
				renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

				BoxCollider box = root.AddComponent<BoxCollider>();
				box.size = new Vector3(3f, 2f, 3f);
				box.center = Vector3.zero;

				AbilityObject abilityObject = root.AddComponent<AbilityObject>();
				Rigidbody body = root.AddComponent<Rigidbody>();
				body.useGravity = false;
				body.isKinematic = true;
				body.collisionDetectionMode = CollisionDetectionMode.Continuous;
				abilityObject.CachedRigidBody = null;

				GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PathSlamPrefab, out bool success);
				if (!success || saved == null)
				{
					problems.Add($"Failed to save prefab at {PathSlamPrefab}");
				}
				else
				{
					touched.Add(PathSlamPrefab);
				}
				return saved;
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(root);
			}
		}

		private static void AssignKit(string prefabPath, params AbilityTemplate[] abilities)
		{
			GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
			try
			{
				NPC npc = root.GetComponent<NPC>();
				if (npc == null)
				{
					problems.Add($"{prefabPath} has no NPC component.");
					return;
				}
				npc.Abilities = new List<AbilityTemplate>(abilities);
				PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
				touched.Add(prefabPath);
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(root);
			}
		}

		private static void SetActivationCost(AbilityTemplate ability, CharacterAttributeTemplate resource, int amount)
		{
			ability.ActivationConditions.RemoveAll(c => c is HasResourceCondition);
			ability.ActivationConditions.Add(Cost(resource, amount));
		}

		private static void SetEvents(AbilityTemplate ability, AbilityOnTickEvent tick, AbilityOnHitEvent hit, AbilityOnDestroyEvent destroy)
		{
			ability.OnTickEvents.Clear();
			ability.OnHitEvents.Clear();
			ability.OnDestroyEvents.Clear();
			if (tick != null) ability.OnTickEvents.Add(tick);
			if (hit != null) ability.OnHitEvents.Add(hit);
			if (destroy != null) ability.OnDestroyEvents.Add(destroy);
		}

		private static ApplyDamageAction Damage(int amount, DamageAttributeTemplate type)
		{
			return new ApplyDamageAction
			{
				DamageValue = new ConstantValue { Amount = amount },
				DamageAttributeTemplate = type,
			};
		}

		private static HasResourceCondition Cost(CharacterAttributeTemplate template, int amount)
		{
			return new HasResourceCondition { Template = template, RequiredAmount = amount };
		}

		private static T SaveEvent<T>(string folder, string assetName, Action<T> configure) where T : AbilityEvent
		{
			T instance = ScriptableObject.CreateInstance<T>();
			configure(instance);
			return Save(instance, folder, assetName);
		}

		private static AbilityTemplate SaveAbility(string folder, string assetName, Action<AbilityTemplate> configure)
		{
			AbilityTemplate instance = ScriptableObject.CreateInstance<AbilityTemplate>();
			configure(instance);
			return Save(instance, folder, assetName);
		}

		/// <summary>
		/// Writes an asset, overwriting in place so an existing GUID (and its addressable entry) survives.
		/// </summary>
		private static T Save<T>(T instance, string folder, string assetName) where T : ScriptableObject
		{
			string path = $"{folder}/{assetName}.asset";
			instance.name = assetName;

			ScriptableObject existing = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
			if (existing != null && existing.GetType() == typeof(T))
			{
				EditorUtility.CopySerialized(instance, existing);
				EditorUtility.SetDirty(existing);
				UnityEngine.Object.DestroyImmediate(instance);
				touched.Add(path);
				return (T)existing;
			}

			if (existing != null)
			{
				AssetDatabase.DeleteAsset(path);
			}
			AssetDatabase.CreateAsset(instance, path);
			EditorUtility.SetDirty(instance);
			touched.Add(path);
			return instance;
		}

		private static void Touch(UnityEngine.Object asset)
		{
			EditorUtility.SetDirty(asset);
			touched.Add(AssetDatabase.GetAssetPath(asset));
		}

		private static T Require<T>(string path) where T : UnityEngine.Object
		{
			T asset = AssetDatabase.LoadAssetAtPath<T>(path);
			if (asset == null)
			{
				problems.Add($"Missing {typeof(T).Name} at '{path}'.");
			}
			return asset;
		}

		private static void EnsureFolder(string folder)
		{
			if (AssetDatabase.IsValidFolder(folder))
			{
				return;
			}
			Directory.CreateDirectory(folder);
			AssetDatabase.Refresh();
		}

		/// <summary>
		/// Puts every asset this generator wrote into the shared static addressable group, which
		/// is what makes a template reachable by ID at runtime and a prefab loadable on a client.
		/// </summary>
		private static void RegisterAddressables()
		{
			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null)
			{
				problems.Add("No Addressable settings found.");
				return;
			}
			AddressableAssetGroup group = settings.FindGroup(AddressableGroupName);
			if (group == null)
			{
				problems.Add($"Addressable group '{AddressableGroupName}' not found.");
				return;
			}

			int count = 0;
			foreach (string path in new HashSet<string>(touched))
			{
				if (!path.EndsWith(".asset") && !path.EndsWith(".prefab"))
				{
					continue;
				}
				// NPC prefabs are FishNet spawnables, registered elsewhere; only ability content here.
				if (path.Contains("/NPCs/"))
				{
					continue;
				}
				string guid = AssetDatabase.AssetPathToGUID(path);
				if (string.IsNullOrEmpty(guid))
				{
					continue;
				}
				AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group, false, false);
				if (entry == null)
				{
					continue;
				}
				entry.address = Path.GetFileNameWithoutExtension(path);
				entry.SetLabel(AddressableGroupName, true, true, false);
				++count;
			}
			EditorUtility.SetDirty(settings);
			Debug.Log($"[OrcAbilityGenerator] Registered {count} addressable entries in '{AddressableGroupName}'.");
		}
	}
}
