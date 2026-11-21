using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.AbilityContent
{
	/// <summary>
	/// Short Charge, Lesser Teleport and Root: the movement and crowd-control half of the test ability suite.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Short Charge</b> (hold to charge, <see cref="AbilityTemplate.ChargeStages"/> 3, 1 s to full): a tap rushes 3 m in
	/// 0.2 s, from half a charge 6 m in 0.33 s, a full charge 9 m in 0.45 s (15, 18, 20 m/s), and strikes the first foe in its
	/// path for 4 / 7 / 10 physical. The rush is the character's own (reconciled) movement, so what is seen is the body
	/// running flat out and the cast's punch: no spell effect, by request. 12 stamina, 6 s.
	/// </para>
	/// <para>
	/// <b>Lesser Teleport</b> (instant): up to 5 m the way the caster faces, stopping short of walls, with an arcane burst
	/// where it leaves and the burst drawn inward where it arrives. 10 mana, 10 s.
	/// </para>
	/// <para>
	/// <b>Root</b> (0.6 s cast): a bolt of living wood that roots the first foe it strikes for 4 s — no walking, running or
	/// jumping (players, NPCs and wildlife alike), though it may still turn and cast — with vines clinging to its ankles for
	/// as long as it lasts. 8 mana, 12 s, so a third of the time at most.
	/// </para>
	/// </remarks>
	public static partial class TestAbilitySuiteGenerator
	{
		public const string MovementAbilityFolder = "Assets/FishMMO/Modules/Abilities/Templates/Types/Movement";
		public const string ControlAbilityFolder = "Assets/FishMMO/Modules/Abilities/Templates/Types/Control";
		public const string MovementPrefabFolder = "Assets/Prefabs/Shared/Entity/Abilities/Types/Movement";
		public const string ControlPrefabFolder = "Assets/Prefabs/Shared/Entity/Abilities/Types/Control";
		public const string MovementEventFolder = "Assets/FishMMO/Modules/Abilities/Templates/Events/Movement";
		public const string ControlEventFolder = "Assets/FishMMO/Modules/Abilities/Templates/Events/Control";
		public const string BuffFolder = "Assets/FishMMO/Modules/Buffs/Templates";

		private static readonly Palette ArcanePalette = new Palette(new Color(0.55f, 0.25f, 1.0f), new Color(0.55f, 0.90f, 1.0f));
		private static readonly Palette RootPalette = new Palette(new Color(0.22f, 0.45f, 0.10f), new Color(0.62f, 0.46f, 0.22f));

		private static void GenerateMovementAndControl(Prefabs prefabs, List<AbilityTemplate> abilities, List<AbilityEvent> events)
		{
			foreach (string folder in new[] { MovementAbilityFolder, ControlAbilityFolder, MovementPrefabFolder, ControlPrefabFolder,
				MovementEventFolder, ControlEventFolder, BuffFolder })
			{
				EnsureFolder(folder);
			}

			// ── Effects ─────────────────────────────────────────────────────────────────────────
			GameObject teleportOut = BuildEffect(explosionFX, "Teleport Out", go => Recolour(go, ArcanePalette));
			GameObject teleportIn = BuildEffect(explosionFX, "Teleport In", go => { Recolour(go, ArcanePalette); MakeImplosion(go); });
			GameObject rootBolt = BuildEffect(fireFX, "Root Bolt", go => Recolour(go, RootPalette));
			GameObject rootBurst = BuildEffect(explosionFX, "Root Burst", go => Recolour(go, RootPalette));
			GameObject rootAnkles = BuildRootAnkles();

			// ── Ability objects ─────────────────────────────────────────────────────────────────
			GameObject chargeVolume = BuildChargeVolume();
			GameObject rootProjectile = BuildFireballLikeProjectile(rootBolt, "Root Bolt", $"{ControlPrefabFolder}/Root Bolt Projectile.prefab");

			// ── The root itself ─────────────────────────────────────────────────────────────────
			StateBuffTemplate root = ScriptableObject.CreateInstance<StateBuffTemplate>();
			root.Description = "Rooted: cannot move or jump. Can still turn and cast.";
			root.Duration = 4f;
			root.IsDebuff = true;
			root.MaxStacks = 1u;
			root.Flag = CharacterFlags.IsRooted;
			root.FXPrefabReference = new AssetReferenceGameObject(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(rootAnkles)));
			root = Save(root, BuffFolder, "Root");

			// ── Short Charge ────────────────────────────────────────────────────────────────────
			AbilityOnSpawnEvent chargeRush = SaveEvent<AbilityOnSpawnEvent>(MovementEventFolder, "Short Charge Rush Event", ev =>
			{
				ev.OnConditionsMetActions.Add(new CasterRushAction
				{
					StageDistances = new[] { 3f, 6f, 9f },
					StageSeconds = new[] { 0.2f, 0.33f, 0.45f },
				});
			});
			AbilityOnTickEvent chargeFollow = SaveEvent<AbilityOnTickEvent>(MovementEventFolder, "Short Charge Follow Event", ev =>
			{
				ev.OnConditionsMetActions.Add(new FollowCasterAction { ForwardOffset = 0.7f, HeightOffset = 0.9f, EndWithCasterRush = true });
			});
			AbilityOnHitEvent chargeImpact = SaveEvent<AbilityOnHitEvent>(MovementEventFolder, "Short Charge Impact", ev =>
			{
				ev.TargetSelector = Hostiles(new EventTargetSelector { FallbackToInitiator = false });
				ev.OnConditionsMetActions.Add(new ApplyDamageAction
				{
					DamageValue = new ChargeStageValue { PerStage = new[] { 4, 7, 10 } },
					DamageAttributeTemplate = physicalDamage,
				});
			});
			// Whatever it runs into (friend or foe), the charge stops there.
			AbilityOnHitEvent chargeStop = SaveEvent<AbilityOnHitEvent>(MovementEventFolder, "Short Charge Stop Event", ev =>
			{
				ev.OnConditionsMetActions.Add(new EndCasterRushAction());
			});
			abilities.Add(SaveAbility(MovementAbilityFolder, "Short Charge", a =>
			{
				a.Description = "Hold to charge, release to rush and strike the first foe in your path. " +
					"A tap rushes 3 m (4 damage), half a charge 6 m (7), a full charge 9 m (10).";
				a.AbilityObjectPrefab = chargeVolume;
				a.AbilitySpawnTarget = AbilitySpawnTarget.Forward;
				a.Type = AbilityType.GroundedPhysical;
				a.ChargeStages = 3;
				a.ActivationTime = 1.0f; // the full charge; half of it reaches stage 2
				a.Speed = 0f;            // FollowCasterAction carries it: no path of its own
				a.LifeTime = 0.6f;       // outlasts the longest rush; ends with the rush
				a.Cooldown = 6f;
				a.HitCount = 1;
				// The volume skims the ground it rushes over: rising ground ahead must not count as running into something.
				a.StrikesCharactersOnly = true;
				a.ActivationConditions.Add(Cost(stamina, 12));
				a.OnSpawnEvents.Add(chargeRush);
				a.OnTickEvents.Add(chargeFollow);
				a.OnHitEvents.Add(chargeImpact);
				a.OnHitEvents.Add(chargeStop);
			}));
			events.Add(chargeRush);
			events.Add(chargeFollow);
			events.Add(chargeImpact);
			events.Add(chargeStop);

			// ── Lesser Teleport ─────────────────────────────────────────────────────────────────
			AbilityOnSpawnEvent teleport = SaveEvent<AbilityOnSpawnEvent>(MovementEventFolder, "Lesser Teleport Event", ev =>
			{
				// The departure first, at the object: it stays where the caster stood.
				ev.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = teleportOut, Size = 2f, HeightOffset = 1f });
				ev.OnConditionsMetActions.Add(new TeleportCasterAction
				{
					MaxDistance = 5f,
					ArriveFX = teleportIn,
					ArriveFXSize = 2f,
					ArriveFXHeight = 1f,
				});
			});
			abilities.Add(SaveAbility(MovementAbilityFolder, "Lesser Teleport", a =>
			{
				a.Description = "Blink up to 5 m the way you face. Walls stop you short.";
				a.AbilityObjectPrefab = prefabs.AreaPulse; // invisible, at the caster's feet: carries the cast to observers
				a.AbilitySpawnTarget = AbilitySpawnTarget.PointBlank;
				a.Type = AbilityType.Magic;              // not Grounded: a blink may be cast in the air
				a.ActivationTime = 0f;
				a.Speed = 0f;
				a.LifeTime = 0.5f;
				a.Cooldown = 10f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(mana, 10));
				a.OnSpawnEvents.Add(teleport);
			}));
			events.Add(teleport);

			// ── Root ───────────────────────────────────────────────────────────────────────────
			AbilityOnHitEvent rootImpact = SaveEvent<AbilityOnHitEvent>(ControlEventFolder, "Root Impact", ev =>
			{
				ev.TargetSelector = Hostiles(new EventTargetSelector { FallbackToInitiator = false });
				ev.OnConditionsMetActions.Add(Buff(root));
			});
			AbilityOnDestroyEvent rootImpactFX = SaveEvent<AbilityOnDestroyEvent>(ControlEventFolder, "Root Impact FX Event", ev =>
			{
				ev.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = rootBurst, Size = 1.5f });
			});
			abilities.Add(SaveAbility(ControlAbilityFolder, "Root", a =>
			{
				a.Description = "Hurls a bolt of living wood. The first foe it strikes is rooted for 4 seconds: it cannot move or jump, but can still turn and cast.";
				a.AbilityObjectPrefab = rootProjectile;
				a.AbilitySpawnTarget = AbilitySpawnTarget.SpawnerWithCameraRotation;
				a.Type = AbilityType.GroundedMagic;
				a.ActivationTime = 0.6f;
				a.Speed = 16f;
				a.LifeTime = 1.5f; // 24 m
				a.Cooldown = 12f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(mana, 8));
				a.OnTickEvents.Add(projectileMove);
				a.OnHitEvents.Add(rootImpact);
				a.OnDestroyEvents.Add(rootImpactFX);
			}));
			events.Add(rootImpact);
			events.Add(rootImpactFX);
		}

		/// <summary>
		/// The arrival half of a teleport: the departure burst drawn inward, its particles starting on the edge of the
		/// one-metre cube and closing on the centre over their life.
		/// </summary>
		private static void MakeImplosion(GameObject go)
		{
			foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
			{
				var main = ps.main;
				float life = main.startLifetime.constantMax > 0f ? main.startLifetime.constantMax : 1f;
				main.startLifetime = life;
				main.startSpeed = -0.5f / life; // from radius 0.5 to the centre
				main.scalingMode = ParticleSystemScalingMode.Hierarchy;

				var shape = ps.shape;
				shape.enabled = true;
				shape.shapeType = ParticleSystemShapeType.Sphere;
				shape.radius = 0.5f;
				shape.radiusThickness = 0f; // from the surface
				var velocity = ps.velocityOverLifetime;
				velocity.enabled = false;
			}
		}

		/// <summary>
		/// What a rooted character wears for as long as the root lasts: at each ankle (<see cref="FXBoneAttachment"/>) a ring
		/// of bark and leaf motes that rises a little and holds to the foot (local space: the feet are not going anywhere).
		/// </summary>
		private static GameObject BuildRootAnkles()
		{
			GameObject root = new GameObject("Root Ankles");
			try
			{
				GameObject emitter = UnityEngine.Object.Instantiate(fireFX);
				emitter.name = "Emitter";
				RemoveUnusedChildren(emitter);
				emitter.transform.SetParent(root.transform, false);
				emitter.transform.localPosition = Vector3.zero;
				emitter.transform.localRotation = Quaternion.identity;
				emitter.transform.localScale = Vector3.one;
				Recolour(emitter, RootPalette);

				foreach (ParticleSystem ps in emitter.GetComponentsInChildren<ParticleSystem>(true))
				{
					var main = ps.main;
					main.loop = true;
					main.prewarm = true;
					main.playOnAwake = true;
					main.simulationSpace = ParticleSystemSimulationSpace.Local;
					main.scalingMode = ParticleSystemScalingMode.Hierarchy;
					main.stopAction = ParticleSystemStopAction.None;
					main.startSpeed = 0.05f;

					var emission = ps.emission;
					emission.rateOverTime = 40f;

					// A ring around the ankle, kept level by FXBoneAttachment.KeepUpright whichever way the bone points.
					var shape = ps.shape;
					shape.enabled = true;
					shape.shapeType = ParticleSystemShapeType.Circle;
					shape.radius = 0.4f;
					shape.radiusThickness = 0.2f;
					shape.arc = 360f;
					shape.rotation = RingRotation; // the Circle lies in XY; this lays it on XZ

					var force = ps.forceOverLifetime;
					force.enabled = true;
					force.space = ParticleSystemSimulationSpace.World;
					force.x = new ParticleSystem.MinMaxCurve(0f);
					force.y = new ParticleSystem.MinMaxCurve(0.25f);
					force.z = new ParticleSystem.MinMaxCurve(0f);
				}

				FXBoneAttachment attachment = root.AddComponent<FXBoneAttachment>();
				attachment.Bones = new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot };
				attachment.Emitter = emitter.transform;
				attachment.WorldSize = 0.45f;
				attachment.WorldOffset = Vector3.zero;
				attachment.KeepUpright = true; // the ring stays level however the foot turns
				return SavePrefab(root, $"{FXFolder}/Root Ankles.prefab");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(root);
			}
		}

		/// <summary>
		/// The striking edge of a charge: an invisible box a body wide, carried in front of the charger
		/// (<see cref="FollowCasterAction"/>). Layer, body and component are those every ability object has.
		/// </summary>
		private static GameObject BuildChargeVolume()
		{
			GameObject volume = new GameObject("Charge Volume");
			try
			{
				volume.layer = AbilityObjectLayer;
				BoxCollider box = volume.AddComponent<BoxCollider>();
				box.size = new Vector3(1.0f, 1.6f, 0.6f);
				Rigidbody body = volume.AddComponent<Rigidbody>();
				body.isKinematic = true;
				body.useGravity = false;
				body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
				volume.AddComponent<AbilityObject>();
				return SavePrefab(volume, $"{MovementPrefabFolder}/Charge Volume.prefab");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(volume);
			}
		}
	}
}
