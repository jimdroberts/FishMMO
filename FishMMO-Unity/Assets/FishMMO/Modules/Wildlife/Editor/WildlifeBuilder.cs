using System;
using System.Collections.Generic;
using System.IO;
using FishMMO.Server.Implementation.World.SceneServer.AI;
using FishMMO.Server.Implementation.World.SceneServer.Spawner;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using FishNet.Component.Observing;
using FishNet.Object;
using FishNet.Observing;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Makes the world's wildlife: the species templates, the wildlife AI states and archetypes, the shared material, one NPC
	/// prefab per species (cloned from the orc and given a procedural body), and the catalogue the cut places their spawners
	/// from (WildlifeSpawnerCut). Re-runnable: the species' numbers are rewritten every run (this builder owns them), anything else is made only
	/// when missing.
	/// </summary>
	public static class WildlifeBuilder
	{
		private const string TemplateFolder = "Assets/FishMMO/Modules/Wildlife/Templates/Wildlife";
		private const string StateFolder = "Assets/FishMMO/Modules/NPCs/Templates/AI/States/Wildlife";
		private const string ArchetypeFolder = "Assets/FishMMO/Modules/NPCs/Templates/AI/Archetypes";
		private const string PrefabFolder = "Assets/Prefabs/Shared/Entity/NPCs/Wildlife";
		private const string MaterialPath = PrefabFolder + "/Wildlife.mat";
		private const string BasePrefabPath = "Assets/Prefabs/Shared/Entity/NPCs/Monsters/Orcs/an orc.prefab";
		private const string MeleeStatePath = "Assets/FishMMO/Modules/NPCs/Templates/AI/States/Attack/Melee Attack State.asset";
		private const string BaseWanderPath = "Assets/FishMMO/Modules/NPCs/Templates/AI/States/Movement/Basic Wander State.asset";
		private const string PreyBaseArchetypePath = ArchetypeFolder + "/Enemy - Pathetic Critter.asset";
		private const string BeastBaseArchetypePath = ArchetypeFolder + "/Enemy - Raging Beast.asset";
		private const string BiteAbilityPath = "Assets/FishMMO/Modules/Abilities/Templates/Types/Punch.asset";
		private const string WildlifeConditionPath = "Assets/Settings/ObserverConditions/WildlifeDistanceCondition.asset";
		private const string CataloguePath = "Assets/Prefabs/Server/SceneServer/Wildlife Catalogue.asset";
		private const string BeastRaceFolder = "Assets/FishMMO/Templates/Entity/Races/Beast";

		/// <summary>One species as the builder authors it.</summary>
		private sealed class Species
		{
			public string Name;          // template asset name
			public string PrefabName;    // the NPC's name in the world ("a deer")
			public string Race;          // beast race asset (naming only; it has no model or faction)
			public WildlifeRole Role;
			public float Walk, Run, Safe = 40f, Leg = 14f, Corpse = 300f;
			public WildlifeBodySpec Body;
			public BiomeHabitat Habitats;
			public float Density;
			public int GroupMin, GroupMax;
			public POIType[] Dens = Array.Empty<POIType>();
			public bool CaveDens;
			public bool CaveColonies;
			public WildlifeShelter Shelter = WildlifeShelter.Open;
			public SurfaceMask Surfaces = SurfaceMask.Any;
			public float RespawnMin = 180f, RespawnMax = 420f;
		}

		private static readonly BiomeHabitat Woods = BiomeHabitat.Forest | BiomeHabitat.Woodland;

		/// <summary>Grazers' ground: grass, forest floor, earth, mud and snow over them; never bare rock, scree, sand or ice.</summary>
		private const SurfaceMask Grazing = SurfaceMask.Grass | SurfaceMask.Leaves | SurfaceMask.Dirt | SurfaceMask.Mud | SurfaceMask.Snow;

		private static readonly Species[] Roster =
		{
			new Species
			{
				Name = "Deer", PrefabName = "a deer", Race = "Great Elk", Role = WildlifeRole.Prey, Walk = 1.4f, Run = 10f, Safe = 45f, Leg = 16f,
				Body = WildlifeBodySpec.Deer,
				Habitats = Woods | BiomeHabitat.Grassland | BiomeHabitat.Farmland | BiomeHabitat.Scrub,
				Density = 0.9f, GroupMin = 2, GroupMax = 5, Surfaces = Grazing,
			},
			new Species
			{
				Name = "Elk", PrefabName = "an elk", Race = "Great Elk", Role = WildlifeRole.Prey, Walk = 1.5f, Run = 9f, Safe = 50f, Leg = 18f,
				Body = new WildlifeBodySpec
				{
					Length = 1.9f, Depth = 0.65f, Width = 0.55f, LegLength = 0.85f, LegThickness = 0.1f, NeckLength = 0.6f, NeckRaise = 50f,
					HeadLength = 0.5f, HeadSize = 0.24f, EarSize = 0.16f, Horns = WildlifeHorns.BroadAntlers, HornSize = 0.75f,
					TailLength = 0.12f, TailRaise = 30f,
					Back = new Color(0.42f, 0.3f, 0.2f), Belly = new Color(0.75f, 0.62f, 0.45f), Accent = new Color(0.12f, 0.1f, 0.09f),
				},
				Habitats = Woods | BiomeHabitat.Grassland | BiomeHabitat.Mountain | BiomeHabitat.Tundra,
				Density = 0.5f, GroupMin = 3, GroupMax = 7, Surfaces = Grazing,
			},
			new Species
			{
				Name = "Fox", PrefabName = "a fox", Race = "Dire Hound", Role = WildlifeRole.Predator, Walk = 1.3f, Run = 8f,
				Body = new WildlifeBodySpec
				{
					Length = 0.65f, Depth = 0.22f, Width = 0.18f, LegLength = 0.25f, LegThickness = 0.045f, NeckLength = 0.16f, NeckRaise = 35f,
					HeadLength = 0.22f, HeadSize = 0.12f, EarSize = 0.09f, Horns = WildlifeHorns.None, HornSize = 0f,
					TailLength = 0.42f, TailRaise = 70f,
					Back = new Color(0.78f, 0.38f, 0.14f), Belly = new Color(0.92f, 0.88f, 0.8f), Accent = new Color(0.1f, 0.08f, 0.07f),
				},
				Habitats = Woods | BiomeHabitat.Grassland | BiomeHabitat.Farmland | BiomeHabitat.Scrub | BiomeHabitat.Tundra,
				Density = 0.5f, GroupMin = 1, GroupMax = 2,
			},
			new Species
			{
				Name = "Wolf", PrefabName = "a wolf", Race = "Dire Wolf", Role = WildlifeRole.Predator, Walk = 1.5f, Run = 9.5f,
				Body = new WildlifeBodySpec
				{
					Length = 1.05f, Depth = 0.38f, Width = 0.3f, LegLength = 0.52f, LegThickness = 0.07f, NeckLength = 0.28f, NeckRaise = 30f,
					HeadLength = 0.32f, HeadSize = 0.16f, EarSize = 0.1f, Horns = WildlifeHorns.None, HornSize = 0f,
					TailLength = 0.42f, TailRaise = 25f,
					Back = new Color(0.45f, 0.43f, 0.4f), Belly = new Color(0.78f, 0.75f, 0.7f), Accent = new Color(0.12f, 0.11f, 0.1f),
				},
				Habitats = Woods | BiomeHabitat.Mountain | BiomeHabitat.Tundra | BiomeHabitat.Grassland,
				Density = 0.35f, GroupMin = 3, GroupMax = 6,
				Dens = new[] { POIType.WolfDen, POIType.WinterWolfDen },
			},
			new Species
			{
				Name = "Bear", PrefabName = "a bear", Race = "Cave Bear", Role = WildlifeRole.Predator, Walk = 1.3f, Run = 7.5f, Corpse = 420f,
				Body = new WildlifeBodySpec
				{
					Length = 1.6f, Depth = 0.75f, Width = 0.75f, LegLength = 0.55f, LegThickness = 0.17f, NeckLength = 0.25f, NeckRaise = 15f,
					HeadLength = 0.42f, HeadSize = 0.32f, EarSize = 0.08f, Horns = WildlifeHorns.None, HornSize = 0f,
					TailLength = 0.06f, TailRaise = 20f,
					Back = new Color(0.3f, 0.2f, 0.13f), Belly = new Color(0.38f, 0.27f, 0.18f), Accent = new Color(0.12f, 0.09f, 0.07f),
				},
				Habitats = Woods | BiomeHabitat.Mountain | BiomeHabitat.Tundra,
				Density = 0.12f, GroupMin = 1, GroupMax = 1, CaveDens = true, Shelter = WildlifeShelter.Anywhere,
				RespawnMin = 420f, RespawnMax = 900f,
			},
			new Species
			{
				Name = "Boar", PrefabName = "a boar", Race = "Boar", Role = WildlifeRole.Territorial, Walk = 1.2f, Run = 7f,
				Body = new WildlifeBodySpec
				{
					Length = 1.0f, Depth = 0.5f, Width = 0.42f, LegLength = 0.32f, LegThickness = 0.08f, NeckLength = 0.12f, NeckRaise = 10f,
					HeadLength = 0.4f, HeadSize = 0.26f, EarSize = 0.1f, Horns = WildlifeHorns.Tusks, HornSize = 0.14f,
					TailLength = 0.18f, TailRaise = 15f,
					Back = new Color(0.3f, 0.25f, 0.21f), Belly = new Color(0.38f, 0.32f, 0.27f), Accent = new Color(0.15f, 0.12f, 0.11f),
				},
				Habitats = Woods | BiomeHabitat.Scrub | BiomeHabitat.Wetland | BiomeHabitat.Farmland,
				Density = 0.45f, GroupMin = 2, GroupMax = 4,
				Surfaces = SurfaceMask.Grass | SurfaceMask.Leaves | SurfaceMask.Dirt | SurfaceMask.Mud,
			},
			// 2026-11-21 (Jim: "add giant bats for cave wildlife"): a predator colony deep in the caves, drawn flying over the floor.
			new Species
			{
				Name = "Giant Bat", PrefabName = "a giant bat", Race = "Giant Bat", Role = WildlifeRole.Predator, Walk = 2.2f, Run = 8f,
				Body = new WildlifeBodySpec
				{
					Length = 0.55f, Depth = 0.32f, Width = 0.3f, LegLength = 0.16f, LegThickness = 0.04f, NeckLength = 0.06f, NeckRaise = 20f,
					HeadLength = 0.22f, HeadSize = 0.17f, EarSize = 0.2f, Horns = WildlifeHorns.None, HornSize = 0f,
					TailLength = 0.08f, TailRaise = 10f, WingSpan = 2.6f, HoverHeight = 1.8f,
					Back = new Color(0.22f, 0.18f, 0.17f), Belly = new Color(0.36f, 0.27f, 0.24f), Accent = new Color(0.1f, 0.08f, 0.08f),
				},
				// The biome is never asked of a cave dweller (WildlifeRules.Lives); placed only in colonies (WildlifePlanner).
				Habitats = BiomeHabitat.None, Density = 0f, GroupMin = 3, GroupMax = 6,
				Shelter = WildlifeShelter.Cave, CaveColonies = true, RespawnMin = 240f, RespawnMax = 480f,
			},
		};

		[DashboardTool(DashboardToolAttribute.Maintenance, "Build wildlife", Section = "World", Order = 20,
			Tooltip = "Makes the wildlife species (templates, AI, prefabs) and the catalogue the scene cut places their spawners from.")]
		public static void BuildFromDashboard() => Debug.Log("[Wildlife]\n  " + string.Join("\n  ", Build()));

		/// <summary>Batch entry point: -executeMethod FishMMO.Shared.WildlifeBuilder.BuildBatch. Exits 1 on any failure.</summary>
		public static void BuildBatch()
		{
			bool ok = false;
			try
			{
				List<string> notes = Build();
				Debug.Log("[Wildlife]\n  " + string.Join("\n  ", notes));
				ok = !notes.Exists(n => n.StartsWith("FAILED", StringComparison.Ordinal));
			}
			catch (Exception ex)
			{
				Debug.LogError($"[Wildlife] {ex}");
			}
			EditorApplication.Exit(ok ? 0 : 1);
		}

		public static List<string> Build()
		{
			var notes = new List<string>();
			EnsureFolder(TemplateFolder);
			EnsureFolder(StateFolder);
			EnsureFolder(PrefabFolder);

			Material material = EnsureMaterial(notes);
			var melee = AssetDatabase.LoadAssetAtPath<BaseAttackingState>(MeleeStatePath);
			var baseWander = AssetDatabase.LoadAssetAtPath<WanderState>(BaseWanderPath);
			if (material == null || melee == null || baseWander == null)
			{
				notes.Add($"FAILED: missing the material, {MeleeStatePath} or {BaseWanderPath}.");
				return notes;
			}

			WanderState wander = EnsureWander(baseWander, notes);
			BaseAttackingState flee = EnsureFlee(melee, notes);
			BaseAttackingState hunt = EnsureAttack(melee, "Wildlife Predator Attack State", 22f, 60f, 120f, notes);
			BaseAttackingState charge = EnsureAttack(melee, "Wildlife Territorial Attack State", 7f, 12f, 25f, notes);
			AIArchetypeTemplate prey = EnsureArchetype("Wildlife - Prey", PreyBaseArchetypePath, flee, wander, clearRetreat: true,
				"Wild prey: grazes and wanders, never fights, and flees from anything that is not prey for as long as it is near.", notes);
			AIArchetypeTemplate predator = EnsureArchetype("Wildlife - Predator", BeastBaseArchetypePath, hunt, wander, clearRetreat: true,
				"Wild predator: hunts everything but its own pack (players, NPCs, prey, other packs) within its senses.", notes);
			AIArchetypeTemplate territorial = EnsureArchetype("Wildlife - Territorial", BeastBaseArchetypePath, charge, wander, clearRetreat: true,
				"Wild territorial animal: never hunts or flees, charges whatever comes too close or hits it, then goes back.", notes);
			if (prey == null || predator == null || territorial == null)
			{
				notes.Add("FAILED: an archetype could not be made.");
				return notes;
			}
			/* Fighters keep a target: the beast base's Raging personality rolls a 60% random retarget every few seconds, so a
			 * wolf bounced between a player and their pet. Aggressive Melee targets by threat and switches on a clear lead. */
			SetPersonality(predator, FocusedPersonalityPath, notes);
			SetPersonality(territorial, FocusedPersonalityPath, notes);

			var condition = AssetDatabase.LoadAssetAtPath<ObserverCondition>(WildlifeConditionPath);
			var bite = AssetDatabase.LoadAssetAtPath<AbilityTemplate>(BiteAbilityPath);
			var prefabs = new List<NetworkObject>();
			foreach (Species species in Roster)
			{
				WildlifeTemplate template = EnsureTemplate(species, material, notes);
				AIArchetypeTemplate archetype = species.Role == WildlifeRole.Prey ? prey
					: species.Role == WildlifeRole.Predator ? predator : territorial;
				GameObject prefab = EnsurePrefab(species, template, archetype, species.Role == WildlifeRole.Prey ? null : bite, notes);
				if (prefab == null)
				{
					continue;
				}
				Convert(prefab, template, condition, notes);
				if (prefab.TryGetComponent(out NetworkObject nob))
				{
					prefabs.Add(nob);
				}
			}

			EnsureCatalogue(prefabs, notes);
			AssetDatabase.SaveAssets();
			return notes;
		}

		private static Material EnsureMaterial(List<string> notes)
		{
			var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
			if (material != null)
			{
				return material;
			}
			Shader shader = Shader.Find("FishMMO/Wildlife");
			if (shader == null)
			{
				notes.Add("FAILED: the FishMMO/Wildlife shader is not imported.");
				return null;
			}
			material = new Material(shader) { name = "Wildlife", enableInstancing = false };
			AssetDatabase.CreateAsset(material, MaterialPath);
			notes.Add($"Made {MaterialPath}.");
			return material;
		}

		private static WanderState EnsureWander(WanderState source, List<string> notes)
		{
			string path = StateFolder + "/Wildlife Wander State.asset";
			var wander = AssetDatabase.LoadAssetAtPath<WanderState>(path);
			if (wander == null)
			{
				wander = UnityEngine.Object.Instantiate(source);
				AssetDatabase.CreateAsset(wander, path);
				notes.Add($"Made {path}.");
			}
			var so = new SerializedObject(wander);
			SetFloat(so, "WanderRadius", 30f);
			SetFloat(so, "IdleChance", 0.6f);
			SetFloat(so, "MinLeashRange", 200f);
			SetFloat(so, "MaxLeashRange", 400f);
			so.ApplyModifiedPropertiesWithoutUndo();
			return wander;
		}

		private static BaseAttackingState EnsureFlee(BaseAttackingState melee, List<string> notes)
		{
			string path = StateFolder + "/Wildlife Flee State.asset";
			var flee = AssetDatabase.LoadAssetAtPath<WildlifeFleeState>(path);
			if (flee == null)
			{
				flee = ScriptableObject.CreateInstance<WildlifeFleeState>();
				AssetDatabase.CreateAsset(flee, path);
				notes.Add($"Made {path}.");
			}
			flee.EnemyLayers = melee.EnemyLayers;
			flee.DetectionRadius = 24f;
			// Prey is never leashed home: it settles wherever its flight ends.
			flee.MinLeashRange = 5000f;
			flee.MaxLeashRange = 10000f;
			// It runs from the target, so it keeps it (also as the prey's RetreatState).
			flee.KeepsCombatTarget = true;
			var so = new SerializedObject(flee);
			SetFloat(so, "updateRate", 0.3f);
			so.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(flee);
			return flee;
		}

		private static BaseAttackingState EnsureAttack(BaseAttackingState melee, string name, float detect, float minLeash, float maxLeash, List<string> notes)
		{
			string path = StateFolder + "/" + name + ".asset";
			var state = AssetDatabase.LoadAssetAtPath<BaseAttackingState>(path);
			if (state == null)
			{
				state = UnityEngine.Object.Instantiate(melee);
				AssetDatabase.CreateAsset(state, path);
				notes.Add($"Made {path}.");
			}
			state.DetectionRadius = detect;
			state.MinLeashRange = minLeash;
			state.MaxLeashRange = maxLeash;
			EditorUtility.SetDirty(state);
			return state;
		}

		private static AIArchetypeTemplate EnsureArchetype(string name, string basePath, BaseAIState attacking, WanderState wander, bool clearRetreat, string description, List<string> notes)
		{
			string path = ArchetypeFolder + "/" + name + ".asset";
			var archetype = AssetDatabase.LoadAssetAtPath<AIArchetypeTemplate>(path);
			if (archetype == null)
			{
				var source = AssetDatabase.LoadAssetAtPath<AIArchetypeTemplate>(basePath);
				if (source == null)
				{
					notes.Add($"FAILED: no {basePath} to start from.");
					return null;
				}
				archetype = UnityEngine.Object.Instantiate(source);
				AssetDatabase.CreateAsset(archetype, path);
				notes.Add($"Made {path}.");
			}
			var so = new SerializedObject(archetype);
			SetObject(so, "AttackingState", attacking);
			SetObject(so, "InitialState", wander);
			SetObject(so, "WanderState", wander);
			if (clearRetreat)
			{
				// Prey's flight is its retreat too (its personality flees when hurt); predators fight to the death.
				SetObject(so, "RetreatState", attacking is WildlifeFleeState ? attacking : null);
			}
			SetString(so, "Description", description);
			so.ApplyModifiedPropertiesWithoutUndo();
			return archetype;
		}

		private const string FocusedPersonalityPath = "Assets/FishMMO/Modules/NPCs/Templates/AI/Personalities/Aggressive Melee Personality.asset";

		private static void SetPersonality(AIArchetypeTemplate archetype, string personalityPath, List<string> notes)
		{
			var personality = AssetDatabase.LoadAssetAtPath<ScriptableObject>(personalityPath);
			if (personality == null)
			{
				notes.Add($"FAILED: no {personalityPath}; {archetype.name} keeps its personality.");
				return;
			}
			var so = new SerializedObject(archetype);
			SetObject(so, "Personality", personality);
			so.ApplyModifiedPropertiesWithoutUndo();
		}

		private static WildlifeTemplate EnsureTemplate(Species species, Material material, List<string> notes)
		{
			string path = TemplateFolder + "/" + species.Name + ".asset";
			var template = AssetDatabase.LoadAssetAtPath<WildlifeTemplate>(path);
			if (template == null)
			{
				template = ScriptableObject.CreateInstance<WildlifeTemplate>();
				AssetDatabase.CreateAsset(template, path);
				notes.Add($"Made {path}.");
			}
			template.Role = species.Role;
			template.WalkSpeed = species.Walk;
			template.RunSpeed = species.Run;
			template.SafeMetres = species.Safe;
			template.FleeLegMetres = species.Leg;
			template.CorpseSeconds = species.Corpse;
			template.Body = species.Body;
			template.Material = material;
			template.Habitats = species.Habitats;
			template.GroupsPerKm2 = species.Density;
			template.GroupMin = species.GroupMin;
			template.GroupMax = species.GroupMax;
			template.DenKinds = new List<POIType>(species.Dens);
			template.DensInCaveMouths = species.CaveDens;
			template.ColoniesInCaves = species.CaveColonies;
			template.Shelter = species.Shelter;
			template.Surfaces = species.Surfaces;
			template.RespawnMinSeconds = species.RespawnMin;
			template.RespawnMaxSeconds = species.RespawnMax;
			EditorUtility.SetDirty(template);
			return template;
		}

		private static GameObject EnsurePrefab(Species species, WildlifeTemplate template, AIArchetypeTemplate archetype, AbilityTemplate bite, List<string> notes)
		{
			string path = $"{PrefabFolder}/{species.PrefabName}.prefab";
			var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
			if (existing != null)
			{
				AIBrainCatalogueEditorUtility.SetArchetype(existing, archetype);
				return existing;
			}
			var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePrefabPath);
			NPCRecipe recipe = NPCPrefabFactory.RecipeFrom(basePrefab);
			if (recipe == null)
			{
				notes.Add($"FAILED: {BasePrefabPath} is not an NPC prefab.");
				return null;
			}
			recipe.Name = species.PrefabName;
			recipe.Folder = PrefabFolder;
			recipe.Interaction = NPCInteraction.None;
			recipe.MerchantTemplate = null;
			recipe.Archetype = archetype;
			recipe.Race = AssetDatabase.LoadAssetAtPath<RaceTemplate>($"{BeastRaceFolder}/{species.Race}.asset") ?? recipe.Race;
			recipe.IsAggressive = false;
			recipe.IsCharmable = false;
			recipe.LootTable = null;
			recipe.Abilities.Clear();
			if (bite != null)
			{
				recipe.Abilities.Add(bite);
			}
			recipe.RegisterAddressable = true;
			GameObject created = NPCPrefabFactory.Create(recipe);
			notes.Add(created != null ? $"Made {path}." : $"FAILED: could not make {path}.");
			return created;
		}

		/// <summary>Turns the cloned humanoid into the animal: body, species, visibility class and collider.</summary>
		private static void Convert(GameObject prefab, WildlifeTemplate template, ObserverCondition condition, List<string> notes)
		{
			string path = AssetDatabase.GetAssetPath(prefab);
			GameObject root = PrefabUtility.LoadPrefabContents(path);
			try
			{
				foreach (CharacterAnimationController humanoid in root.GetComponentsInChildren<CharacterAnimationController>(true))
				{
					UnityEngine.Object.DestroyImmediate(humanoid, true);
				}
				if (root.GetComponent<WildlifeBody>() == null)
				{
					root.AddComponent<WildlifeBody>();
				}
				NPC npc = root.GetComponent<NPC>();
				if (npc != null)
				{
					npc.Wildlife = template;
					if (npc.MeshRoot != null)
					{
						for (int i = npc.MeshRoot.childCount - 1; i >= 0; --i)
						{
							GameObject child = npc.MeshRoot.GetChild(i).gameObject;
							if (child.name.Contains("Labels") || child.name.Contains("FollowPoint") ||
								child.name.Contains("SpawnPoint") || child.name == "EquipmentRoot")
							{
								continue;
							}
							UnityEngine.Object.DestroyImmediate(child, true);
						}
					}
				}

				WildlifeBodySpec body = template.Body;
				CapsuleCollider capsule = root.GetComponent<CapsuleCollider>();
				if (capsule != null)
				{
					// Wings widen it; a flyer's is where it is drawn, over the ground it stands on (WildlifeBodySpec.HoverHeight).
					float across = body.WingSpan > 0f ? body.WingSpan * 0.4f : Mathf.Max(body.Width, body.Length * 0.3f);
					float radius = Mathf.Clamp(across * 0.5f, 0.15f, 0.55f);
					float height = Mathf.Max(body.ShoulderHeight + body.HeadSize, radius * 2f + 0.05f);
					capsule.direction = 1;
					capsule.radius = radius;
					capsule.height = height;
					capsule.center = body.HoverHeight > 0f
						? new Vector3(0f, body.HoverHeight + body.LegLength + body.Depth * 0.5f, 0f)
						: new Vector3(0f, height * 0.5f, 0f);
				}

				NetworkObserver observer = root.GetComponent<NetworkObserver>();
				if (observer != null && condition != null)
				{
					var so = new SerializedObject(observer);
					SerializedProperty conditions = so.FindProperty("_observerConditions");
					for (int i = 0; conditions != null && i < conditions.arraySize; ++i)
					{
						SerializedProperty element = conditions.GetArrayElementAtIndex(i);
						if (element.objectReferenceValue is DistanceCondition)
						{
							element.objectReferenceValue = condition;
						}
					}
					so.ApplyModifiedPropertiesWithoutUndo();
				}
				PrefabUtility.SaveAsPrefabAsset(root, path);
				notes.Add($"Converted {path} into a {template.Role} ({template.name}).");
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(root);
			}
		}

		private static void EnsureCatalogue(List<NetworkObject> prefabs, List<string> notes)
		{
			var catalogue = AssetDatabase.LoadAssetAtPath<WildlifeCatalogue>(CataloguePath);
			if (catalogue == null)
			{
				catalogue = ScriptableObject.CreateInstance<WildlifeCatalogue>();
				AssetDatabase.CreateAsset(catalogue, CataloguePath);
				notes.Add($"Made {CataloguePath}.");
			}
			catalogue.Species = prefabs;
			EditorUtility.SetDirty(catalogue);

			notes.Add($"{prefabs.Count} species in the catalogue.");
		}

		private static void EnsureFolder(string path)
		{
			if (AssetDatabase.IsValidFolder(path))
			{
				return;
			}
			string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
			if (!string.IsNullOrEmpty(parent))
			{
				EnsureFolder(parent);
			}
			AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
		}

		private static void SetFloat(SerializedObject so, string field, float value)
		{
			SerializedProperty p = so.FindProperty(field);
			if (p != null)
			{
				p.floatValue = value;
			}
		}

		private static void SetObject(SerializedObject so, string field, UnityEngine.Object value)
		{
			SerializedProperty p = so.FindProperty(field);
			if (p != null)
			{
				p.objectReferenceValue = value;
			}
		}

		private static void SetString(SerializedObject so, string field, string value)
		{
			SerializedProperty p = so.FindProperty(field);
			if (p != null)
			{
				p.stringValue = value;
			}
		}
	}
}
