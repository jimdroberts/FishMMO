#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using FishMMO.Server.Implementation.World.SceneServer.AI;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Makes the mounts' content (issue #109): the five mounts (Cow, Llama, Horse, Frog, Dragon) and their saddles,
	/// the Mount ability and its premade, the Dismount effect, the Stablemaster's stock and the Stablemaster NPC.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Idempotent.</b> Each asset is written over in place (its GUID, and so every reference and addressable entry,
	/// survives), keeping an icon already assigned by FishMMO-Art/Icons; the Stablemaster prefab is made once and then kept.
	/// Everything is registered in <see cref="SharedGroup"/>.
	/// </para>
	/// <para>
	/// The roster's numbers give each mount distinct traits, starting points to tune: Cow +30% (the cheap starter);
	/// Llama +40% and walks slopes up to 75°; Horse +70%; Frog +20% and leaps 2.5× high and far; Dragon +40% on the ground and
	/// flies at 15 m/s for 3 stamina a second, gliding down when it runs out.
	/// </para>
	/// </remarks>
	public static class MountContentBuilder
	{
		public const string Root = "Assets/FishMMO/Modules/Mounts/Templates";
		public const string MountFolder = Root + "/Mounts";
		public const string SaddleFolder = Root + "/Saddles";
		public const string AbilityFolder = Root + "/Abilities";
		public const string EventFolder = Root + "/Events";
		public const string PremadeFolder = Root + "/Premade";
		public const string MerchantFolder = Root + "/NPCs";
		public const string MerchantPath = MerchantFolder + "/Stablemaster.asset";
		public const string PrefabFolder = "Assets/FishMMO/Modules/Mounts/Prefabs";
		/// <summary>The Stablemaster NPC: a copy of the general merchant selling the Stablemaster's stock.</summary>
		public const string StablemasterPrefabPath = PrefabFolder + "/a stablemaster.prefab";
		public const string MerchantSourcePath = "Assets/Prefabs/Shared/Entity/NPCs/Interactables/Human/Merchants/GeneralGoods/HumanGeneralMerchant.prefab";
		public const string WildlifeMaterialPath = "Assets/Prefabs/Shared/Entity/NPCs/Wildlife/Wildlife.mat";
		public const string SharedGroup = "Shared_Static_Permanent";

		public const string MountAbilityName = "Mount";
		public const string DismountEffectName = "Dismount";
		public const int MountPremadePrice = 40;
		public const int DismountEffectPrice = 60;

		/// <summary>One mount: its buff, its saddle and the saddle's price.</summary>
		public sealed class MountSpec
		{
			public string Name;
			public string Saddle;
			public int Price;
			public string Description;
			public WildlifeBodySpec Body;
			public Action<MountBuffTemplate> Motion;
		}

		/// <summary>The roster (see remarks).</summary>
		public static readonly MountSpec[] Roster =
		{
			new MountSpec
			{
				Name = "Cow", Saddle = "Cow Saddle", Price = 50,
				Description = "Riding a cow: steady, patient and not very fast.",
				Body = new WildlifeBodySpec
				{
					Length = 1.6f, Depth = 0.75f, Width = 0.62f, LegLength = 0.66f, LegThickness = 0.1f,
					NeckLength = 0.35f, NeckRaise = 22f, HeadLength = 0.5f, HeadSize = 0.27f, EarSize = 0.13f,
					Horns = WildlifeHorns.Horns, HornSize = 0.26f, TailLength = 0.7f, TailRaise = 5f,
					Back = new Color(0.86f, 0.82f, 0.74f), Belly = new Color(0.93f, 0.9f, 0.84f), Accent = new Color(0.22f, 0.17f, 0.15f),
				},
				Motion = m => { m.GroundSpeedMultiplier = 1.3f; },
			},
			new MountSpec
			{
				Name = "Llama", Saddle = "Llama Saddle", Price = 120,
				Description = "Riding a llama: sure-footed on slopes a horse would refuse.",
				Body = new WildlifeBodySpec
				{
					Length = 1.2f, Depth = 0.5f, Width = 0.42f, LegLength = 0.86f, LegThickness = 0.075f,
					NeckLength = 0.85f, NeckRaise = 80f, HeadLength = 0.32f, HeadSize = 0.17f, EarSize = 0.19f,
					Horns = WildlifeHorns.None, TailLength = 0.22f, TailRaise = 45f,
					Back = new Color(0.84f, 0.76f, 0.62f), Belly = new Color(0.92f, 0.86f, 0.74f), Accent = new Color(0.3f, 0.24f, 0.2f),
				},
				Motion = m => { m.GroundSpeedMultiplier = 1.4f; m.MaxSlopeDegrees = 75f; },
			},
			new MountSpec
			{
				Name = "Horse", Saddle = "Horse Saddle", Price = 250,
				Description = "Riding a horse: the fastest thing on four legs.",
				Body = new WildlifeBodySpec
				{
					Length = 1.65f, Depth = 0.62f, Width = 0.46f, LegLength = 0.9f, LegThickness = 0.09f,
					NeckLength = 0.78f, NeckRaise = 55f, HeadLength = 0.56f, HeadSize = 0.22f, EarSize = 0.13f,
					Horns = WildlifeHorns.None, TailLength = 0.65f, TailRaise = 15f,
					Back = new Color(0.45f, 0.28f, 0.16f), Belly = new Color(0.38f, 0.24f, 0.14f), Accent = new Color(0.12f, 0.1f, 0.09f),
				},
				Motion = m => { m.GroundSpeedMultiplier = 1.7f; m.JumpHeightMultiplier = 1.3f; },
			},
			new MountSpec
			{
				Name = "Giant Frog", Saddle = "Frog Saddle", Price = 300,
				Description = "Riding a giant frog: slow on its feet, but one leap clears a wall and the field beyond it.",
				Body = new WildlifeBodySpec
				{
					Length = 1.15f, Depth = 0.55f, Width = 0.95f, LegLength = 0.36f, LegThickness = 0.17f,
					NeckLength = 0.06f, NeckRaise = 5f, HeadLength = 0.55f, HeadSize = 0.5f, EarSize = 0f,
					Horns = WildlifeHorns.None, TailLength = 0f,
					Back = new Color(0.32f, 0.52f, 0.2f), Belly = new Color(0.82f, 0.84f, 0.55f), Accent = new Color(0.18f, 0.28f, 0.1f),
				},
				Motion = m => { m.GroundSpeedMultiplier = 1.2f; m.JumpHeightMultiplier = 2.5f; m.LeapSpeed = 6f; },
			},
			new MountSpec
			{
				Name = "Dragon", Saddle = "Dragon Saddle", Price = 2000,
				Description = "Riding a dragon: jump to take off, hold jump to climb, crouch to descend. Flight costs stamina; spent, it glides down.",
				Body = new WildlifeBodySpec
				{
					Length = 2.4f, Depth = 0.8f, Width = 0.72f, LegLength = 0.95f, LegThickness = 0.15f,
					NeckLength = 1.1f, NeckRaise = 45f, HeadLength = 0.72f, HeadSize = 0.3f, EarSize = 0.15f,
					Horns = WildlifeHorns.Horns, HornSize = 0.38f, TailLength = 2.0f, TailRaise = 5f,
					WingSpan = 6.0f, HoverHeight = 0f,
					Back = new Color(0.5f, 0.12f, 0.1f), Belly = new Color(0.85f, 0.6f, 0.35f), Accent = new Color(0.15f, 0.1f, 0.1f),
				},
				Motion = m =>
				{
					m.GroundSpeedMultiplier = 1.4f;
					m.JumpHeightMultiplier = 1.2f;
					m.Flies = true;
					m.FlySpeed = 15f;
					m.FlySprintMultiplier = 2f;
					m.FlightStaminaPerSecond = 3f;
					m.GlideSinkSpeed = 3f;
					m.GlideSpeedShare = 0.6f;
				},
			},
		};

		private static readonly List<string> written = new List<string>();

		[DashboardTool(DashboardToolAttribute.Maintenance, "Build mounts", Section = "Mounts", Order = 1,
			Tooltip = "Makes the five mounts and their saddles, the Mount ability and premade, the Dismount effect, the Stablemaster's stock and the Stablemaster NPC (issue #109). Idempotent.")]
		public static void BuildFromDashboard() => Debug.Log("[Mounts]\n  " + string.Join("\n  ", Build()));

		/// <summary><c>-executeMethod FishMMO.Shared.MountContentBuilder.BuildBatch</c>: exits 0 unless something failed.</summary>
		public static void BuildBatch()
		{
			List<string> notes = Build();
			Debug.Log("[Mounts]\n  " + string.Join("\n  ", notes));
			if (Application.isBatchMode)
			{
				EditorApplication.Exit(notes.Exists(n => n.StartsWith("FAILED")) ? 1 : 0);
			}
		}

		public static List<string> Build()
		{
			var notes = new List<string>();
			written.Clear();
			foreach (string folder in new[] { MountFolder, SaddleFolder, AbilityFolder, EventFolder, PremadeFolder, MerchantFolder, PrefabFolder })
			{
				EnsureFolder(folder);
			}
			Material material = AssetDatabase.LoadAssetAtPath<Material>(WildlifeMaterialPath);
			if (material == null)
			{
				notes.Add($"FAILED: no wildlife material at {WildlifeMaterialPath}.");
				return notes;
			}

			// ── The mounts and their saddles ─────────────────────────────────────────────────
			var saddles = new List<BaseItemTemplate>();
			foreach (MountSpec spec in Roster)
			{
				MountBuffTemplate mount = ScriptableObject.CreateInstance<MountBuffTemplate>();
				mount.Description = spec.Description + " Casting, a hit, deep water, a ladder or a roof overhead gets you off; click it off to dismount.";
				mount.Body = spec.Body;
				mount.Material = material;
				mount.MaxStacks = 1u;
				mount.IsDebuff = false;
				mount.IsPermanent = false;
				// Casting any ability dismounts.
				mount.BreaksOnAction = true;
				spec.Motion?.Invoke(mount);
				mount.GaitRunSpeed = Constants.Character.SprintSpeed * Mathf.Max(1f, mount.GroundSpeedMultiplier);
				mount = Save(mount, MountFolder, spec.Name);

				SaddleTemplate saddle = ScriptableObject.CreateInstance<SaddleTemplate>();
				saddle.Slot = ItemSlot.MountSaddle;
				saddle.Mount = mount;
				saddle.Price = spec.Price;
				saddle.MaxStackSize = 1;
				saddle.Attributes = new List<ItemAttributeTemplate>();
				saddles.Add(Save(saddle, SaddleFolder, spec.Saddle));
			}

			// ── The Mount ability and its premade ────────────────────────────────────────────
			AbilityOnHitEvent mountEvent = SaveEvent<AbilityOnHitEvent>(EventFolder, "Mount Event", ev =>
			{
				ev.TargetSelector = new InitiatorTargetSelector();
				ev.OnConditionsMetActions.Add(new MountAction());
			});
			AbilityTemplate mountAbility = SaveAbility(AbilityFolder, MountAbilityName, a =>
			{
				a.Description = "Call the mount your saddle names and climb on. Needs a saddle; not in combat, in water, indoors or underground. Use it again to get off.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.Type = AbilityType.GroundedMagic;
				a.ActivationTime = 1.5f;
				a.Cooldown = 3f;
				a.HitCount = 1;
				a.ActivationConditions.Add(new CanMountCondition());
				a.OnHitEvents.Add(mountEvent);
			});
			PremadeAbilityTemplate premade = ScriptableObject.CreateInstance<PremadeAbilityTemplate>();
			premade.Ability = mountAbility;
			premade.Events = new List<AbilityEvent>();
			premade.Price = MountPremadePrice;
			if (!premade.Validate(out string premadeReason))
			{
				notes.Add($"FAILED: Premade Mount: {premadeReason}");
			}
			premade = Save(premade, PremadeFolder, "Premade Mount");

			// ── The Dismount effect (knock a rider off, for PvP) ────────────────────────
			AbilityOnHitEvent dismount = SaveEvent<AbilityOnHitEvent>(EventFolder, DismountEffectName, ev =>
			{
				var selector = new EventTargetSelector { FallbackToInitiator = false };
				selector.Conditions.Add(new TargetAllianceCondition { ApplyToSelf = false, ApplyToEnemy = true, ApplyToNeutral = true, ApplyToAllies = false });
				selector.Conditions.Add(new IsCharacterAliveCondition());
				ev.TargetSelector = selector;
				ev.Price = DismountEffectPrice;
				ev.OnConditionsMetActions.Add(new DismountAction());
			});

			// ── The Stablemaster's stock ─────────────────────────────────────────────────────
			MerchantTemplate merchant = AssetDatabase.LoadAssetAtPath<MerchantTemplate>(MerchantPath);
			bool fresh = merchant == null;
			if (fresh)
			{
				merchant = ScriptableObject.CreateInstance<MerchantTemplate>();
			}
			merchant.Description = "Stablemaster";
			merchant.Items = new List<BaseItemTemplate>(saddles);
			merchant.Abilities ??= new List<AbilityTemplate>();
			merchant.AbilityEvents = new List<AbilityEvent> { dismount };
			merchant.PremadeAbilities = new List<PremadeAbilityTemplate> { premade };
			merchant.BuysItems = true;
			if (fresh)
			{
				AssetDatabase.CreateAsset(merchant, MerchantPath);
			}
			EditorUtility.SetDirty(merchant);
			written.Add(MerchantPath);

			AssetDatabase.SaveAssets();
			Register(notes);
			AssetDatabase.SaveAssets();

			// ── The Stablemaster NPC ─────────────────────────────────────────────────────────
			EnsureStablemaster(merchant, notes);

			notes.Add($"Wrote {written.Count} asset(s): {Roster.Length} mounts and saddles, Mount + premade, Dismount, the Stablemaster's stock.");
			return notes;
		}

		/// <summary>The Stablemaster prefab, made from the general merchant's recipe with the Stablemaster's stock (once).</summary>
		private static void EnsureStablemaster(MerchantTemplate merchant, List<string> notes)
		{
			if (AssetDatabase.LoadAssetAtPath<GameObject>(StablemasterPrefabPath) != null)
			{
				notes.Add($"Kept {StablemasterPrefabPath}.");
				return;
			}
			GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(MerchantSourcePath);
			NPCRecipe recipe = source != null ? NPCPrefabFactory.RecipeFrom(source) : null;
			if (recipe == null)
			{
				notes.Add($"FAILED: {MerchantSourcePath} is not an NPC prefab.");
				return;
			}
			recipe.Name = Path.GetFileNameWithoutExtension(StablemasterPrefabPath);
			recipe.Folder = PrefabFolder;
			recipe.Interaction = NPCInteraction.Merchant;
			recipe.MerchantTemplate = merchant;
			recipe.IsAggressive = false;
			recipe.IsCharmable = false;
			recipe.LootTable = null;
			recipe.RegisterAddressable = true;
			var problems = new List<string>();
			if (!NPCPrefabFactory.Validate(recipe, problems))
			{
				notes.Add($"FAILED: {StablemasterPrefabPath}: {string.Join("; ", problems)}");
				return;
			}
			GameObject created = NPCPrefabFactory.Create(recipe);
			notes.Add(created != null ? $"Made {StablemasterPrefabPath}." : $"FAILED: could not make {StablemasterPrefabPath}.");
		}

		// ── Helpers ─────────────────────────────────────────────────────────────────────────

		private static void Register(List<string> notes)
		{
			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			AddressableAssetGroup group = settings != null ? settings.FindGroup(SharedGroup) : null;
			if (group == null)
			{
				notes.Add($"FAILED: Addressable group '{SharedGroup}' not found: nothing registered.");
				return;
			}
			foreach (string path in written)
			{
				string guid = AssetDatabase.AssetPathToGUID(path);
				AddressableAssetEntry entry = settings.FindAssetEntry(guid);
				if (entry == null || entry.parentGroup != group)
				{
					entry = settings.CreateOrMoveEntry(guid, group, false, false);
				}
				if (entry == null)
				{
					notes.Add($"FAILED: could not register '{path}'.");
					continue;
				}
				entry.address = Path.GetFileNameWithoutExtension(path);
				entry.SetLabel(SharedGroup, true, true, false);
			}
			EditorUtility.SetDirty(group);
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

		/// <summary>Overwrites an existing asset of the same type in place (GUID and icon kept), else creates it.</summary>
		private static T Save<T>(T instance, string folder, string assetName) where T : ScriptableObject
		{
			string path = $"{folder}/{assetName}.asset";
			instance.name = assetName;
			ScriptableObject existing = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
			if (existing != null && existing.GetType() == typeof(T))
			{
				KeepIcon(existing, instance);
				EditorUtility.CopySerialized(instance, existing);
				EditorUtility.SetDirty(existing);
				UnityEngine.Object.DestroyImmediate(instance);
				written.Add(path);
				return (T)existing;
			}
			if (existing != null)
			{
				AssetDatabase.DeleteAsset(path);
			}
			AssetDatabase.CreateAsset(instance, path);
			EditorUtility.SetDirty(instance);
			written.Add(path);
			return instance;
		}

		/// <summary>Carries an icon already assigned (by hand or by the icon generators) onto the fresh copy that has none.</summary>
		private static void KeepIcon(ScriptableObject existing, ScriptableObject instance)
		{
			SerializedObject from = new SerializedObject(existing);
			SerializedProperty kept = from.FindProperty("icon");
			SerializedObject to = new SerializedObject(instance);
			SerializedProperty fresh = to.FindProperty("icon");
			if (kept == null || fresh == null)
			{
				return;
			}
			bool freshHasIcon = fresh.propertyType == SerializedPropertyType.ObjectReference
				? fresh.objectReferenceValue != null
				: !string.IsNullOrEmpty(fresh.FindPropertyRelative("m_AssetGUID")?.stringValue);
			if (freshHasIcon)
			{
				return;
			}
			to.CopyFromSerializedProperty(kept);
			to.ApplyModifiedPropertiesWithoutUndo();
		}

		private static void EnsureFolder(string folder)
		{
			if (AssetDatabase.IsValidFolder(folder))
			{
				return;
			}
			string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
			if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
			{
				EnsureFolder(parent);
			}
			AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
		}
	}
}
#endif
