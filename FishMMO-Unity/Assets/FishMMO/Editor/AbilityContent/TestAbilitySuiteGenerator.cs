using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.AddressableAssets;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.AbilityContent
{
	/// <summary>
	/// Generates the healing suite (Lesser Heal, Lesser Heal Self) and the area test suite (Area Lesser Heal, Area Lesser
	/// Fire, Area Minor Armor, Area Minor Movement Speed, Area Knockback): their effects, ability prefabs, events,
	/// templates and premades, the haste effect worn at the ankles while Minor Increase Move Speed lasts, and puts every
	/// one of them on the General Goods merchant.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Balance</b>, against the baseline pools (Health 100, Mana 100, Stamina 100; mana returns at 0.2/s, so a full bar
	/// takes about eight minutes; health does not regenerate at all, which is what makes healing worth its mana):
	/// </para>
	/// <list type="bullet">
	///   <item>Heals mend you, your allies and any non-hostile PLAYER (not wildlife or neutral NPCs); buffs are allies only.</item>
	///   <item>A single-target "lesser" effect moves about a tenth of a pool. Lesser Heal: 12 health for 8 mana (1.5 health
	///   per mana, 144 health per full bar). The self heal is 10: it cannot miss and needs no aim.</item>
	///   <item>An area effect gives about half per target, caps at five, costs about twice as much and cools down longer,
	///   so it is worse than the single-target spell on one body and better from three. Area Lesser Heal: 6 each for 15
	///   mana is 0.4 health per mana alone and 2.0 on five.</item>
	///   <item>Area Lesser Fire deals 5 fire to each of five. Lesser Fireball (3 fire for 5 mana) sits below this budget;
	///   it was left alone. Nothing grants Fire Resistance yet, so fire lands in full.</item>
	///   <item>The area buffs apply the SAME buffs the self versions do (Minor Increase Armor, +5 armor for 120 s; Minor
	///   Increase Move Speed, +30% for 360 s), so the two never stack: an area cast refreshes rather than adds. They are
	///   priced by reach (15 mana, 30 s cooldown) against the self versions' 3 mana.</item>
	///   <item>Armor is a flat 1:1 reduction (CharacterDamageController.ApplyModifiers), so +5 armor erases Punch (5) and
	///   halves Orc Slam (9). Area Knockback's 3 physical is the token damage of a shove, and the shove lands whether or not
	///   the damage is absorbed.</item>
	/// </list>
	/// <para>
	/// <b>Shape.</b> Every area ability is a short-lived, invisible PointBlank object whose spawn chain runs
	/// <see cref="AbilityApplyAreaAction"/> and plays a ground ring the size of its radius. That is what makes it a real blast:
	/// observers are sent the object and every victim, the ring is visible to all, and each victim's hit carries the object
	/// as its centre (<c>IsSplash</c>), which is where Area Knockback pushes from. Hostile area events select anyone but
	/// the caster and leave who may be harmed to the central harm rule (FactionHarm), so wildlife is struck and a neutral
	/// player is not; friendly ones select the caster and allies (party, guild).
	/// </para>
	/// <para>
	/// <b>Effects</b> are copies of the shipped Fire and Explosion effects, recoloured (heal green to sky blue, armor steel
	/// to gold, haste cyan to white, shockwave brown to dust) and, for the area rings, re-emitted as a flat ring that reaches
	/// the edge of the one-metre cube every ability effect is authored to (ParticleAdjuster). PlayFXAction.Size then scales a
	/// ring to exactly twice its radius.
	/// </para>
	/// <para>
	/// Idempotent: everything is overwritten in place, so GUIDs, addressable entries and merchant references survive a
	/// re-run. Run from the FishMMO Dashboard (Maintenance → Ability Content) or headlessly with
	/// <c>-executeMethod FishMMO.AbilityContent.TestAbilitySuiteGenerator.GenerateAll</c>.
	/// </para>
	/// </remarks>
	public static partial class TestAbilitySuiteGenerator
	{
		// ── Where things go ─────────────────────────────────────────────────────────────────────
		public const string FXFolder = "Assets/Prefabs/Client/FX/Abilities";
		public const string HealPrefabFolder = "Assets/Prefabs/Shared/Entity/Abilities/Types/Heal";
		public const string AreaPrefabFolder = "Assets/Prefabs/Shared/Entity/Abilities/Types/Area";
		public const string HealAbilityFolder = "Assets/FishMMO/Modules/Abilities/Templates/Types/Heal";
		public const string AreaAbilityFolder = "Assets/FishMMO/Modules/Abilities/Templates/Types/Area";
		public const string HealHitEventFolder = "Assets/FishMMO/Modules/Abilities/Templates/Events/Hit/Heal";
		public const string AreaHitEventFolder = "Assets/FishMMO/Modules/Abilities/Templates/Events/Hit/Area";
		public const string AreaSpawnEventFolder = "Assets/FishMMO/Modules/Abilities/Templates/Events/Spawn/Area";
		public const string DestroyEventFolder = "Assets/FishMMO/Modules/Abilities/Templates/Events/Destroy";
		public const string PremadeFolder = "Assets/FishMMO/Modules/Commerce/Templates/Premade";

		// ── What is reused ──────────────────────────────────────────────────────────────────────
		private const string PathFireFX = "Assets/Prefabs/Client/FX/Abilities/Fire.prefab";
		private const string PathExplosionFX = "Assets/Prefabs/Client/FX/Abilities/Explosion.prefab";
		private const string PathArmorBuffFX = "Assets/Prefabs/Client/FX/Abilities/TestBuffFX.prefab";
		private const string PathSpeedBuffFX = "Assets/Prefabs/Client/FX/Abilities/TestBuffFX 2.prefab";
		private const string PathFireballPrefab = "Assets/Prefabs/Shared/Entity/Abilities/Types/Fireball/Lesser Fireball.prefab";
		private const string PathProjectileMove = "Assets/FishMMO/Modules/Abilities/Templates/Events/Move/Projectile Forward Move Event.asset";
		private const string PathArmorBuff = "Assets/FishMMO/Modules/Buffs/Templates/Minor Increase Armor.asset";
		private const string PathSpeedBuff = "Assets/FishMMO/Modules/Buffs/Templates/Minor Increase Move Speed.asset";
		private const string PathFireDamage = "Assets/FishMMO/Templates/Entity/CharacterAttributes/Damage/Fire Damage.asset";
		private const string PathPhysicalDamage = "Assets/FishMMO/Templates/Entity/CharacterAttributes/Damage/Physical Damage.asset";
		private const string PathMana = "Assets/FishMMO/Templates/Entity/CharacterAttributes/Resource/Mana.asset";
		private const string PathStamina = "Assets/FishMMO/Templates/Entity/CharacterAttributes/Resource/Stamina.asset";
		private const string PathGeneralGoods = "Assets/FishMMO/Modules/Commerce/Templates/NPCs/Human/Merchants/GeneralGoods/GeneralGoods.asset";

		private const string SharedGroup = "Shared_Static_Permanent";
		private const string ClientFXGroup = "Client_Dynamic";

		/// <summary>Layer ability objects live on (see the ability prefabs' m_Layer).</summary>
		private const int AbilityObjectLayer = 9;
		/// <summary>The Ignore Raycast layer the fireball's visual child uses.</summary>
		private const int VisualLayer = 2;

		/// <summary>What every premade on General Goods costs; the same as Premade Lesser Flame.</summary>
		private const int PremadePrice = 25;

		private static readonly List<string> written = new List<string>();
		private static readonly List<string> problems = new List<string>();

		// Loaded dependencies.
		private static GameObject fireFX, explosionFX, armorBuffFX, speedBuffFX, fireballPrefab;
		private static AbilityOnTickEvent projectileMove;
		private static BaseBuffTemplate armorBuff, speedBuff;
		private static DamageAttributeTemplate fireDamage, physicalDamage;
		private static CharacterAttributeTemplate mana, stamina;

		// ── Palettes: what red becomes (Warm) and what orange-yellow becomes (Hot) ────────────
		private struct Palette
		{
			public Color Warm;
			public Color Hot;
			public Palette(Color warm, Color hot) { Warm = warm; Hot = hot; }
		}
		private static readonly Palette HealPalette = new Palette(new Color(0.15f, 1.0f, 0.35f), new Color(0.40f, 0.85f, 1.0f));
		private static readonly Palette ArmorPalette = new Palette(new Color(0.45f, 0.55f, 0.75f), new Color(1.0f, 0.85f, 0.40f));
		private static readonly Palette HastePalette = new Palette(new Color(0.25f, 0.85f, 1.0f), new Color(0.90f, 1.0f, 0.95f));
		private static readonly Palette EarthPalette = new Palette(new Color(0.45f, 0.33f, 0.22f), new Color(0.85f, 0.78f, 0.62f));

		[DashboardTool(DashboardToolAttribute.Maintenance, "Generate Test Ability Suite", Section = "Ability Content", Order = 1,
			Tooltip = "Generates the heals, the five area abilities, Short Charge, Lesser Teleport and Root with their FX, registers them as addressables and adds them to the General Goods merchant.")]
		public static void GenerateAll()
		{
			written.Clear();
			problems.Clear();
			try
			{
				foreach (string folder in new[] { FXFolder, HealPrefabFolder, AreaPrefabFolder, HealAbilityFolder, AreaAbilityFolder,
					HealHitEventFolder, AreaHitEventFolder, AreaSpawnEventFolder, DestroyEventFolder, PremadeFolder })
				{
					EnsureFolder(folder);
				}

				LoadDependencies();
				if (problems.Count > 0)
				{
					throw new InvalidOperationException("Missing dependencies:\n" + string.Join("\n", problems));
				}

				Effects fx = GenerateEffects();
				Prefabs prefabs = GeneratePrefabs(fx);
				Events events = GenerateEvents(fx);
				List<AbilityTemplate> abilities = GenerateAbilities(prefabs, events);
				List<AbilityEvent> allEvents = new List<AbilityEvent>(events.All());
				// Short Charge, Lesser Teleport and Root (TestAbilitySuiteGenerator.MovementAndControl.cs).
				GenerateMovementAndControl(prefabs, abilities, allEvents);
				// The Blink On Hit components, Shadowstep and Blink Bolt (TestAbilitySuiteGenerator.BlinkOnHit.cs).
				GenerateBlinkOnHit(abilities, allEvents);
				// Dispel Magic and Self Dispel Magic (TestAbilitySuiteGenerator.Dispel.cs).
				GenerateDispel(abilities, allEvents);
				List<PremadeAbilityTemplate> premades = GeneratePremades(abilities);
				WireHasteAnkles(fx);
				WireMerchant(abilities, allEvents, premades);

				AssetDatabase.SaveAssets();
				RegisterAddressables();
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();

				foreach (string path in written)
				{
					Debug.Log($"[TestAbilitySuiteGenerator] asset: {path}");
				}
				foreach (string problem in problems)
				{
					Debug.LogWarning($"[TestAbilitySuiteGenerator] PROBLEM: {problem}");
				}
				Debug.Log($"[TestAbilitySuiteGenerator] DONE assets={written.Count} problems={problems.Count}");
			}
			catch (Exception ex)
			{
				Debug.LogError($"[TestAbilitySuiteGenerator] FAILED: {ex}");
				throw;
			}
		}

		// =======================================================================================
		// Dependencies
		// =======================================================================================

		private static void LoadDependencies()
		{
			fireFX = Require<GameObject>(PathFireFX);
			explosionFX = Require<GameObject>(PathExplosionFX);
			armorBuffFX = Require<GameObject>(PathArmorBuffFX);
			speedBuffFX = Require<GameObject>(PathSpeedBuffFX);
			fireballPrefab = Require<GameObject>(PathFireballPrefab);
			projectileMove = Require<AbilityOnTickEvent>(PathProjectileMove);
			armorBuff = Require<BaseBuffTemplate>(PathArmorBuff);
			speedBuff = Require<BaseBuffTemplate>(PathSpeedBuff);
			fireDamage = Require<DamageAttributeTemplate>(PathFireDamage);
			physicalDamage = Require<DamageAttributeTemplate>(PathPhysicalDamage);
			mana = Require<CharacterAttributeTemplate>(PathMana);
			stamina = Require<CharacterAttributeTemplate>(PathStamina);
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

		// =======================================================================================
		// Effects
		// =======================================================================================

		private sealed class Effects
		{
			public GameObject Heal;          // projectile body (looping), nested in the Lesser Heal projectile
			public GameObject HealBurst;     // impact / self heal / per-target heal
			public GameObject HealNova;      // ground ring
			public GameObject FireNova;
			public GameObject ArmorNova;
			public GameObject HasteNova;
			public GameObject Shockwave;
			public GameObject HasteAnkles;   // worn while Minor Increase Move Speed lasts
		}

		private static Effects GenerateEffects()
		{
			Effects fx = new Effects();
			fx.Heal = BuildEffect(fireFX, "Heal", go => Recolour(go, HealPalette));
			fx.HealBurst = BuildEffect(explosionFX, "Heal Burst", go => Recolour(go, HealPalette));
			fx.HealNova = BuildEffect(explosionFX, "Heal Nova", go => { Recolour(go, HealPalette); MakeGroundRing(go); });
			fx.FireNova = BuildEffect(explosionFX, "Fire Nova", go => MakeGroundRing(go));
			fx.ArmorNova = BuildEffect(explosionFX, "Armor Nova", go => { Recolour(go, ArmorPalette); MakeGroundRing(go); });
			fx.HasteNova = BuildEffect(explosionFX, "Haste Nova", go => { Recolour(go, HastePalette); MakeGroundRing(go); });
			fx.Shockwave = BuildEffect(explosionFX, "Shockwave", go => { Recolour(go, EarthPalette); MakeGroundRing(go); });
			fx.HasteAnkles = BuildHasteAnkles();

			foreach (GameObject ring in new[] { fx.HealNova, fx.FireNova, fx.ArmorNova, fx.HasteNova, fx.Shockwave })
			{
				VerifyGroundRing(ring);
			}
			return fx;
		}

		/// <summary>An unlinked copy of <paramref name="source"/>, changed by <paramref name="modify"/>, saved over the destination.</summary>
		private static GameObject BuildEffect(GameObject source, string name, Action<GameObject> modify)
		{
			GameObject go = UnityEngine.Object.Instantiate(source);
			try
			{
				go.name = name;
				RemoveUnusedChildren(go);
				modify(go);
				return SavePrefab(go, $"{FXFolder}/{name}.prefab");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		/// <summary>Fire carries an inactive, lit "Cube" child (a leftover authoring reference); a copy does not need it.</summary>
		private static void RemoveUnusedChildren(GameObject go)
		{
			for (int i = go.transform.childCount - 1; i >= 0; --i)
			{
				Transform child = go.transform.GetChild(i);
				if (!child.gameObject.activeSelf && child.GetComponent<ParticleSystem>() == null)
				{
					UnityEngine.Object.DestroyImmediate(child.gameObject);
				}
			}
		}

		/// <summary>
		/// The haste effect worn while the movement buff lasts: a root that seats a copy of its emitter at each ankle
		/// (<see cref="FXBoneAttachment"/>), the emitter a small, world-space, rising wisp in the haste colours, so it
		/// trails behind a runner.
		/// </summary>
		private static GameObject BuildHasteAnkles()
		{
			GameObject root = new GameObject("Haste Ankles");
			try
			{
				GameObject emitter = UnityEngine.Object.Instantiate(fireFX);
				emitter.name = "Emitter";
				RemoveUnusedChildren(emitter);
				emitter.transform.SetParent(root.transform, false);
				emitter.transform.localPosition = Vector3.zero;
				emitter.transform.localRotation = Quaternion.identity;
				emitter.transform.localScale = Vector3.one;
				Recolour(emitter, HastePalette);

				foreach (ParticleSystem ps in emitter.GetComponentsInChildren<ParticleSystem>(true))
				{
					var main = ps.main;
					main.loop = true;
					main.prewarm = false;
					main.playOnAwake = true;
					// World space so the wisps are left behind as the feet move: the trail IS the cue.
					main.simulationSpace = ParticleSystemSimulationSpace.World;
					main.scalingMode = ParticleSystemScalingMode.Hierarchy;
					main.stopAction = ParticleSystemStopAction.None;

					var emission = ps.emission;
					emission.rateOverTime = 30f;

					// Rise in the world, whichever way the ankle bone happens to point.
					var force = ps.forceOverLifetime;
					force.enabled = true;
					force.space = ParticleSystemSimulationSpace.World;
					force.x = new ParticleSystem.MinMaxCurve(0f);
					force.y = new ParticleSystem.MinMaxCurve(1.2f);
					force.z = new ParticleSystem.MinMaxCurve(0f);
				}

				FXBoneAttachment attachment = root.AddComponent<FXBoneAttachment>();
				attachment.Bones = new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot };
				attachment.Emitter = emitter.transform;
				attachment.WorldSize = 0.35f;
				attachment.WorldOffset = Vector3.zero;

				return SavePrefab(root, $"{FXFolder}/Haste Ankles.prefab");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(root);
			}
		}

		// ── Recolouring ───────────────────────────────────────────────────────────────────────

		private static void Recolour(GameObject go, Palette palette)
		{
			foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
			{
				var main = ps.main;
				main.startColor = Recolour(main.startColor, palette);

				var colour = ps.colorOverLifetime;
				colour.color = Recolour(colour.color, palette);

				var bySpeed = ps.colorBySpeed;
				bySpeed.color = Recolour(bySpeed.color, palette);

				var trails = ps.trails;
				trails.colorOverLifetime = Recolour(trails.colorOverLifetime, palette);
				trails.colorOverTrail = Recolour(trails.colorOverTrail, palette);

				var lights = ps.lights;
				if (lights.light != null)
				{
					// A light module points at a light ASSET; a recoloured copy would need its own. None of the sources has one.
					problems.Add($"{go.name}: particle lights module references '{lights.light.name}', left uncoloured.");
				}
			}
			foreach (Light light in go.GetComponentsInChildren<Light>(true))
			{
				light.color = Recolour(light.color, palette);
			}
		}

		private static ParticleSystem.MinMaxGradient Recolour(ParticleSystem.MinMaxGradient g, Palette p)
		{
			switch (g.mode)
			{
				case ParticleSystemGradientMode.Color:
					return new ParticleSystem.MinMaxGradient(Recolour(g.color, p));
				case ParticleSystemGradientMode.TwoColors:
					return new ParticleSystem.MinMaxGradient(Recolour(g.colorMin, p), Recolour(g.colorMax, p));
				case ParticleSystemGradientMode.Gradient:
					return new ParticleSystem.MinMaxGradient(Recolour(g.gradient, p));
				case ParticleSystemGradientMode.TwoGradients:
					return new ParticleSystem.MinMaxGradient(Recolour(g.gradientMin, p), Recolour(g.gradientMax, p));
				case ParticleSystemGradientMode.RandomColor:
					ParticleSystem.MinMaxGradient random = new ParticleSystem.MinMaxGradient(Recolour(g.gradient, p));
					random.mode = ParticleSystemGradientMode.RandomColor;
					return random;
				default:
					return g;
			}
		}

		private static Gradient Recolour(Gradient g, Palette p)
		{
			if (g == null)
			{
				return null;
			}
			GradientColorKey[] keys = g.colorKeys;
			for (int i = 0; i < keys.Length; ++i)
			{
				keys[i].color = Recolour(keys[i].color, p);
			}
			Gradient result = new Gradient { mode = g.mode, colorSpace = g.colorSpace };
			result.SetKeys(keys, g.alphaKeys);
			return result;
		}

		/// <summary>
		/// Fire's hues (red 0 through orange-yellow ~0.17) mapped onto the palette, keeping each colour's saturation, value
		/// and alpha: a white core stays white, a dark ember stays dark.
		/// </summary>
		private static Color Recolour(Color c, Palette p)
		{
			Color.RGBToHSV(c, out float h, out float s, out float v);
			if (h > 0.5f)
			{
				h -= 1f; // a magenta-red reads as red
			}
			float t = Mathf.Clamp01(h / 0.17f);
			Color target = Color.Lerp(p.Warm, p.Hot, t);
			Color tinted = Color.Lerp(Color.white, target, s) * v;
			tinted.a = c.a;
			return tinted;
		}

		// ── Ground rings ─────────────────────────────────────────────────────────────────────

		/// <summary>The rotation that lays the Circle shape flat on the ground; checked by <see cref="VerifyGroundRing"/>.</summary>
		private static readonly Vector3 RingRotation = new Vector3(90f, 0f, 0f);

		/// <summary>
		/// Turns a burst into a flat ring that sweeps out from the centre to the edge of the one-metre cube (radius 0.5) and
		/// no further, the ParticleAdjuster contract, so a ring played at Size 2r reaches exactly r.
		/// </summary>
		private static void MakeGroundRing(GameObject go)
		{
			foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
			{
				var main = ps.main;
				float life = main.startLifetime.constantMax > 0f ? main.startLifetime.constantMax : 1f;
				main.loop = false;
				main.prewarm = false;
				main.startLifetime = life;
				main.startSpeed = 0.5f / life;
				main.startSize = 0.12f;
				main.gravityModifier = 0f;
				main.simulationSpace = ParticleSystemSimulationSpace.Local;
				main.scalingMode = ParticleSystemScalingMode.Hierarchy;
				main.maxParticles = Mathf.Max(main.maxParticles, 120);
				main.stopAction = ParticleSystemStopAction.Destroy;

				var emission = ps.emission;
				emission.rateOverTime = 0f;
				emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)120) });

				var shape = ps.shape;
				shape.enabled = true;
				shape.shapeType = ParticleSystemShapeType.Circle;
				shape.radius = 0.02f;
				shape.radiusThickness = 0f;
				shape.arc = 360f;
				shape.position = Vector3.zero;
				shape.rotation = RingRotation;
				shape.scale = Vector3.one;

				var velocity = ps.velocityOverLifetime;
				velocity.enabled = false;
				var force = ps.forceOverLifetime;
				force.enabled = false;
			}
		}

		/// <summary>
		/// Simulates a saved ring and checks it is what it claims: flat on the ground, and reaching the cube's edge (0.5)
		/// without passing it. A ring standing up, or one that falls short, is reported as a problem.
		/// </summary>
		private static void VerifyGroundRing(GameObject prefab)
		{
			if (prefab == null)
			{
				return;
			}
			GameObject probe = UnityEngine.Object.Instantiate(prefab);
			try
			{
				ParticleSystem ps = probe.GetComponentInChildren<ParticleSystem>();
				if (ps == null)
				{
					problems.Add($"{prefab.name}: no particle system to verify.");
					return;
				}
				var main = ps.main;
				main.stopAction = ParticleSystemStopAction.None;
				float life = main.startLifetime.constantMax;
				// Simulate advances by simulation time × simulationSpeed (the Explosion source plays at 3×): ask for 95% of a life.
				ps.Simulate(life * 0.95f / Mathf.Max(0.01f, main.simulationSpeed), true, true, false);
				ParticleSystem.Particle[] particles = new ParticleSystem.Particle[ps.main.maxParticles];
				int count = ps.GetParticles(particles);
				float maxHeight = 0f, maxReach = 0f;
				for (int i = 0; i < count; ++i)
				{
					Vector3 local = particles[i].position;
					maxHeight = Mathf.Max(maxHeight, Mathf.Abs(local.y));
					maxReach = Mathf.Max(maxReach, new Vector2(local.x, local.z).magnitude);
				}
				Debug.Log($"[TestAbilitySuiteGenerator] ring '{prefab.name}': {count} particles, reach {maxReach:F3} (want ~0.5), height {maxHeight:F3} (want ~0).");
				if (count == 0 || maxReach < 0.4f || maxReach > 0.55f || maxHeight > 0.05f)
				{
					problems.Add($"{prefab.name}: ring is not flat at the cube's edge (particles {count}, reach {maxReach:F3}, height {maxHeight:F3}).");
				}
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(probe);
			}
		}

		// =======================================================================================
		// Ability object prefabs
		// =======================================================================================

		private sealed class Prefabs
		{
			public GameObject LesserHealProjectile;
			public GameObject AreaPulse;
		}

		private static Prefabs GeneratePrefabs(Effects fx)
		{
			return new Prefabs
			{
				LesserHealProjectile = BuildFireballLikeProjectile(fx.Heal, "Heal", $"{HealPrefabFolder}/Lesser Heal Projectile.prefab"),
				AreaPulse = BuildAreaPulse(),
			};
		}

		/// <summary>The Lesser Fireball's object (collider, kinematic body, AbilityObject) with another effect as its body.</summary>
		private static GameObject BuildFireballLikeProjectile(GameObject bodyFX, string bodyName, string path)
		{
			GameObject root = UnityEngine.Object.Instantiate(fireballPrefab);
			try
			{
				root.name = System.IO.Path.GetFileNameWithoutExtension(path);

				// The fireball's visual child: its placement is kept, its effect replaced.
				Vector3 position = Vector3.zero, scale = Vector3.one;
				Quaternion rotation = Quaternion.identity;
				float duration = -1f;
				for (int i = root.transform.childCount - 1; i >= 0; --i)
				{
					Transform child = root.transform.GetChild(i);
					ParticleSystem visual = child.GetComponent<ParticleSystem>();
					if (visual == null)
					{
						continue;
					}
					position = child.localPosition;
					rotation = child.localRotation;
					scale = child.localScale;
					duration = visual.main.duration;
					UnityEngine.Object.DestroyImmediate(child.gameObject);
				}

				GameObject body = (GameObject)PrefabUtility.InstantiatePrefab(bodyFX, root.transform);
				body.name = bodyName;
				body.transform.localPosition = position;
				body.transform.localRotation = rotation;
				body.transform.localScale = scale;
				SetLayerRecursively(body, VisualLayer);
				if (duration > 0f)
				{
					foreach (ParticleSystem ps in body.GetComponentsInChildren<ParticleSystem>(true))
					{
						var main = ps.main;
						main.duration = duration;
					}
				}

				return SavePrefab(root, path);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(root);
			}
		}

		/// <summary>
		/// The invisible object an area ability is: a body to carry the blast's centre and its spawn chain. Its collider is a
		/// 5 cm sphere at the caster's feet, inside the caster's own capsule, so its sweep never strikes anyone the area query
		/// already does; the area is the query's radius, not this.
		/// </summary>
		private static GameObject BuildAreaPulse()
		{
			GameObject root = new GameObject("Area Pulse");
			try
			{
				root.layer = AbilityObjectLayer;
				SphereCollider collider = root.AddComponent<SphereCollider>();
				collider.radius = 0.05f;
				Rigidbody body = root.AddComponent<Rigidbody>();
				body.isKinematic = true;
				body.useGravity = false;
				body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
				root.AddComponent<AbilityObject>();
				return SavePrefab(root, $"{AreaPrefabFolder}/Area Pulse.prefab");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(root);
			}
		}

		private static void SetLayerRecursively(GameObject go, int layer)
		{
			go.layer = layer;
			foreach (Transform child in go.transform)
			{
				SetLayerRecursively(child.gameObject, layer);
			}
		}

		// =======================================================================================
		// Events
		// =======================================================================================

		private sealed class Events
		{
			public AbilityOnHitEvent LesserHealImpact;
			public AbilityOnHitEvent LesserHealSelf;
			public AbilityOnDestroyEvent HealImpactFX;

			public AbilityOnHitEvent AreaHeal, AreaFire, AreaArmor, AreaSpeed, AreaKnockback;
			public AbilityOnSpawnEvent AreaHealBurst, AreaFireBurst, AreaArmorBurst, AreaSpeedBurst, AreaKnockbackBurst;

			public IEnumerable<AbilityEvent> All()
			{
				yield return LesserHealImpact;
				yield return LesserHealSelf;
				yield return HealImpactFX;
				yield return AreaHeal;
				yield return AreaFire;
				yield return AreaArmor;
				yield return AreaSpeed;
				yield return AreaKnockback;
				yield return AreaHealBurst;
				yield return AreaFireBurst;
				yield return AreaArmorBurst;
				yield return AreaSpeedBurst;
				yield return AreaKnockbackBurst;
			}
		}

		// Area tuning: radius (m) and the most bodies one cast reaches.
		private const float HealRadius = 8f, FireRadius = 6f, BuffRadius = 10f, KnockbackRadius = 5f;
		private const int HealMaxHits = 5, FireMaxHits = 5, BuffMaxHits = 5, KnockbackMaxHits = 6;

		private static Events GenerateEvents(Effects fx)
		{
			Events e = new Events();

			// ── Lesser Heal (projectile): heals the friend it strikes; an enemy it strikes is spared. ──
			e.LesserHealImpact = SaveEvent<AbilityOnHitEvent>(HealHitEventFolder, "Lesser Heal Impact", ev =>
			{
				ev.TargetSelector = Healable(new EventTargetSelector { FallbackToInitiator = false });
				ev.OnConditionsMetActions.Add(new ApplyHealAction { HealValue = new ConstantValue { Amount = 12 } });
			});
			// The burst plays where the projectile ends, struck or spent, as the fireball's impact does.
			e.HealImpactFX = SaveEvent<AbilityOnDestroyEvent>(DestroyEventFolder, "Heal Impact FX Event", ev =>
			{
				ev.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = fx.HealBurst, Size = 1.5f });
			});

			// ── Lesser Heal Self ──
			e.LesserHealSelf = SaveEvent<AbilityOnHitEvent>(HealHitEventFolder, "Lesser Heal Self Event", ev =>
			{
				ev.TargetSelector = new InitiatorTargetSelector();
				ev.OnConditionsMetActions.Add(new ApplyHealAction { HealValue = new ConstantValue { Amount = 10 } });
				ev.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = fx.HealBurst, Size = 1.5f, HeightOffset = 1f });
			});

			// ── Area hit events: run once per body the area query found (IsSplash, the object as centre). ──
			e.AreaHeal = SaveEvent<AbilityOnHitEvent>(AreaHitEventFolder, "Area Lesser Heal Event", ev =>
			{
				ev.TargetSelector = Healable(new EventTargetSelector { FallbackToInitiator = false });
				ev.OnConditionsMetActions.Add(new ApplyHealAction { HealValue = new ConstantValue { Amount = 6 } });
				ev.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = fx.HealBurst, Size = 1.2f, HeightOffset = 1f });
			});
			e.AreaFire = SaveEvent<AbilityOnHitEvent>(AreaHitEventFolder, "Area Lesser Fire Event", ev =>
			{
				ev.TargetSelector = Hostiles(new EventTargetSelector { FallbackToInitiator = false });
				ev.OnConditionsMetActions.Add(Damage(5, fireDamage));
			});
			e.AreaArmor = SaveEvent<AbilityOnHitEvent>(AreaHitEventFolder, "Area Minor Armor Event", ev =>
			{
				ev.TargetSelector = Friends(new EventTargetSelector { FallbackToInitiator = false });
				ev.OnConditionsMetActions.Add(Buff(armorBuff));
				ev.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = armorBuffFX });
			});
			e.AreaSpeed = SaveEvent<AbilityOnHitEvent>(AreaHitEventFolder, "Area Minor Movement Speed Event", ev =>
			{
				ev.TargetSelector = Friends(new EventTargetSelector { FallbackToInitiator = false });
				ev.OnConditionsMetActions.Add(Buff(speedBuff));
				ev.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = speedBuffFX });
			});
			e.AreaKnockback = SaveEvent<AbilityOnHitEvent>(AreaHitEventFolder, "Area Knockback Event", ev =>
			{
				ev.TargetSelector = Hostiles(new EventTargetSelector { FallbackToInitiator = false });
				ev.OnConditionsMetActions.Add(Damage(3, physicalDamage));
				ev.OnConditionsMetActions.Add(new KnockbackHitAction
				{
					ForceValue = new ConstantFloatValue { Amount = 8f },
					Direction = KnockbackDirection.Auto, // a blast: away from the centre
				});
			});

			// ── Area spawn events: the blast itself and its ring. ──
			e.AreaHealBurst = AreaBurst("Area Lesser Heal Burst", HealRadius, HealMaxHits, fx.HealNova);
			e.AreaFireBurst = AreaBurst("Area Lesser Fire Burst", FireRadius, FireMaxHits, fx.FireNova);
			e.AreaArmorBurst = AreaBurst("Area Minor Armor Burst", BuffRadius, BuffMaxHits, fx.ArmorNova);
			e.AreaSpeedBurst = AreaBurst("Area Minor Movement Speed Burst", BuffRadius, BuffMaxHits, fx.HasteNova);
			e.AreaKnockbackBurst = AreaBurst("Area Knockback Burst", KnockbackRadius, KnockbackMaxHits, fx.Shockwave);
			return e;
		}

		private static AbilityOnSpawnEvent AreaBurst(string name, float radius, int maxHits, GameObject ring)
		{
			return SaveEvent<AbilityOnSpawnEvent>(AreaSpawnEventFolder, name, ev =>
			{
				ev.OnConditionsMetActions.Add(new PlayFXAction { FXPrefab = ring, Size = radius * 2f });
				ev.OnConditionsMetActions.Add(new AbilityApplyAreaAction
				{
					RadiusValue = new ConstantFloatValue { Amount = radius },
					MaxHitsValue = new ConstantValue { Amount = maxHits },
					TargetLayerMask = ~0,
				});
			});
		}

		// =======================================================================================
		// Abilities
		// =======================================================================================

		private static List<AbilityTemplate> GenerateAbilities(Prefabs prefabs, Events e)
		{
			List<AbilityTemplate> abilities = new List<AbilityTemplate>();

			abilities.Add(SaveAbility(HealAbilityFolder, "Lesser Heal", a =>
			{
				a.Description = "Hurls a mote of healing light that restores 12 health to the ally or friendly player it strikes. An enemy it strikes is spared.";
				a.AbilityObjectPrefab = prefabs.LesserHealProjectile;
				a.AbilitySpawnTarget = AbilitySpawnTarget.SpawnerWithCameraRotation;
				a.Type = AbilityType.GroundedMagic;
				a.ActivationTime = 1.0f;
				a.Speed = 14f;
				a.LifeTime = 2.5f; // 35 m
				a.Cooldown = 2.5f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(mana, 8));
				a.OnTickEvents.Add(projectileMove);
				a.OnHitEvents.Add(e.LesserHealImpact);
				a.OnDestroyEvents.Add(e.HealImpactFX);
			}));

			abilities.Add(SaveAbility(HealAbilityFolder, "Lesser Heal Self", a =>
			{
				a.Description = "Restores 10 health to yourself.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.Type = AbilityType.GroundedMagic;
				a.ActivationTime = 1.0f;
				a.Cooldown = 2.5f;
				a.HitCount = 1;
				a.ActivationConditions.Add(Cost(mana, 8));
				a.OnHitEvents.Add(e.LesserHealSelf);
			}));

			abilities.Add(Area("Area Lesser Heal",
				$"Restores 6 health to you and up to {HealMaxHits - 1} allies or friendly players within {HealRadius:0} m.",
				AbilityType.GroundedMagic, 1.5f, 8f, mana, 15, e.AreaHealBurst, e.AreaHeal));
			abilities.Add(Area("Area Lesser Fire",
				$"A ring of flame bursts from you, dealing 5 fire damage to up to {FireMaxHits} foes within {FireRadius:0} m.",
				AbilityType.GroundedMagic, 0.8f, 6f, mana, 12, e.AreaFireBurst, e.AreaFire));
			abilities.Add(Area("Area Minor Armor",
				$"Grants Minor Increase Armor to you and up to {BuffMaxHits - 1} allies within {BuffRadius:0} m.",
				AbilityType.GroundedMagic, 1.0f, 30f, mana, 15, e.AreaArmorBurst, e.AreaArmor));
			abilities.Add(Area("Area Minor Movement Speed",
				$"Grants Minor Increase Move Speed to you and up to {BuffMaxHits - 1} allies within {BuffRadius:0} m.",
				AbilityType.GroundedMagic, 1.0f, 30f, mana, 15, e.AreaSpeedBurst, e.AreaSpeed));
			abilities.Add(Area("Area Knockback",
				$"Stamps the ground, dealing 3 physical damage to up to {KnockbackMaxHits} foes within {KnockbackRadius:0} m and hurling them away from you.",
				AbilityType.GroundedPhysical, 0.4f, 10f, stamina, 15, e.AreaKnockbackBurst, e.AreaKnockback));

			return abilities;

			AbilityTemplate Area(string name, string description, AbilityType type, float castTime, float cooldown,
				CharacterAttributeTemplate resource, int cost, AbilityOnSpawnEvent burst, AbilityOnHitEvent hit)
			{
				return SaveAbility(AreaAbilityFolder, name, a =>
				{
					a.Description = description;
					a.AbilityObjectPrefab = prefabs.AreaPulse;
					a.AbilitySpawnTarget = AbilitySpawnTarget.PointBlank;
					a.Type = type;
					a.ActivationTime = castTime;
					a.Speed = 0f;
					// Long enough for an observer's copy to be drawn (and its ring released) before it ends.
					a.LifeTime = 0.5f;
					a.Cooldown = cooldown;
					a.HitCount = 1;
					a.ActivationConditions.Add(Cost(resource, cost));
					a.OnSpawnEvents.Add(burst);
					a.OnHitEvents.Add(hit);
				});
			}
		}

		private static List<PremadeAbilityTemplate> GeneratePremades(List<AbilityTemplate> abilities)
		{
			List<PremadeAbilityTemplate> premades = new List<PremadeAbilityTemplate>();
			foreach (AbilityTemplate ability in abilities)
			{
				PremadeAbilityTemplate premade = ScriptableObject.CreateInstance<PremadeAbilityTemplate>();
				premade.Ability = ability;
				premade.Events = new List<AbilityEvent>();
				premade.Price = PremadePrice;
				if (!premade.Validate(out string reason))
				{
					problems.Add($"Premade {ability.name}: {reason}");
				}
				premades.Add(Save(premade, PremadeFolder, $"Premade {ability.name}"));
			}
			return premades;
		}

		// =======================================================================================
		// Wiring
		// =======================================================================================

		/// <summary>
		/// The movement buff wears the haste effect at the ankles for as long as it lasts, on the owner and on every
		/// observer (BuffController instantiates a buff's FX under the model and removes it with the buff).
		/// </summary>
		private static void WireHasteAnkles(Effects fx)
		{
			string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(fx.HasteAnkles));
			if (speedBuff.FXPrefabReference != null && speedBuff.FXPrefabReference.AssetGUID == guid)
			{
				return;
			}
			speedBuff.FXPrefabReference = new AssetReferenceGameObject(guid);
			EditorUtility.SetDirty(speedBuff);
			written.Add(PathSpeedBuff);
		}

		private static void WireMerchant(List<AbilityTemplate> abilities, List<AbilityEvent> events, List<PremadeAbilityTemplate> premades)
		{
			MerchantTemplate merchant = AssetDatabase.LoadAssetAtPath<MerchantTemplate>(PathGeneralGoods);
			if (merchant == null)
			{
				problems.Add($"No merchant at '{PathGeneralGoods}'.");
				return;
			}
			merchant.Abilities ??= new List<AbilityTemplate>();
			merchant.AbilityEvents ??= new List<AbilityEvent>();
			merchant.PremadeAbilities ??= new List<PremadeAbilityTemplate>();
			AddMissing(merchant.Abilities, abilities);
			AddMissing(merchant.AbilityEvents, events);
			AddMissing(merchant.PremadeAbilities, premades);
			EditorUtility.SetDirty(merchant);
			written.Add(PathGeneralGoods);
		}

		private static void AddMissing<T>(List<T> list, IEnumerable<T> items) where T : UnityEngine.Object
		{
			foreach (T item in items)
			{
				if (item != null && !list.Contains(item))
				{
					list.Add(item);
				}
			}
		}

		/// <summary>
		/// Templates, events, premades and ability prefabs in <see cref="SharedGroup"/>; effects in
		/// <see cref="ClientFXGroup"/>, beside the effects they were copied from. Address = file name, label = group name.
		/// </summary>
		private static void RegisterAddressables()
		{
			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null)
			{
				problems.Add("No Addressable settings: nothing registered.");
				return;
			}
			foreach (string path in written)
			{
				if (path == PathGeneralGoods || path == PathSpeedBuff)
				{
					continue; // already registered; their entries must not move
				}
				string group = path.StartsWith(FXFolder + "/") ? ClientFXGroup : SharedGroup;
				Register(settings, path, group);
			}
		}

		private static void Register(AddressableAssetSettings settings, string path, string groupName)
		{
			AddressableAssetGroup group = settings.FindGroup(groupName);
			if (group == null)
			{
				problems.Add($"Addressable group '{groupName}' not found for '{path}'.");
				return;
			}
			string guid = AssetDatabase.AssetPathToGUID(path);
			AddressableAssetEntry entry = settings.FindAssetEntry(guid);
			if (entry == null || entry.parentGroup != group)
			{
				entry = settings.CreateOrMoveEntry(guid, group, false, false);
			}
			if (entry == null)
			{
				problems.Add($"Could not register '{path}'.");
				return;
			}
			entry.address = Path.GetFileNameWithoutExtension(path);
			entry.SetLabel(groupName, true, true, false);
			EditorUtility.SetDirty(group);
		}

		// =======================================================================================
		// Authoring helpers
		// =======================================================================================

		private static ApplyDamageAction Damage(int amount, DamageAttributeTemplate type)
		{
			return new ApplyDamageAction { DamageValue = new ConstantValue { Amount = amount }, DamageAttributeTemplate = type };
		}

		private static ApplyBuffAction Buff(BaseBuffTemplate template)
		{
			return new ApplyBuffAction { StacksValue = new ConstantValue { Amount = 1 }, BuffTemplate = template };
		}

		private static HasResourceCondition Cost(CharacterAttributeTemplate template, int amount)
		{
			return new HasResourceCondition { Template = template, RequiredAmount = amount };
		}

		/// <summary>You and your allies (party, guild), alive.</summary>
		private static T Friends<T>(T selector) where T : TargetSelector
		{
			selector.Conditions.Add(new TargetAllianceCondition { ApplyToSelf = true, ApplyToEnemy = false, ApplyToNeutral = false, ApplyToAllies = true });
			selector.Conditions.Add(new IsCharacterAliveCondition());
			return selector;
		}

		/// <summary>
		/// Who a heal mends: you, your allies, and any player or NPC that is not hostile to you — never wildlife or someone
		/// else's pet, even an ally's (Jim, 2026-11-21: owners maintain their own pets) (Jim, 2026-11-21: neutral NPCs too, "it will be helpful later on"). Buffs stay with <see cref="Friends"/>:
		/// allies only.
		/// </summary>
		private static T Healable<T>(T selector) where T : TargetSelector
		{
			selector.Conditions.Add(new TargetAllianceCondition
			{
				ApplyToSelf = true,
				ApplyToEnemy = false,
				ApplyToNeutral = true,
				NeutralPlayersOnly = false,
				NeutralExcludesWildlifeAndPets = true,
				ExcludeOthersPets = true,
				ApplyToAllies = true,
			});
			selector.Conditions.Add(new IsCharacterAliveCondition());
			return selector;
		}

		/// <summary>
		/// Anyone but you and your allies, alive. Neutral is included on purpose: wildlife stands outside the factions and
		/// reads as neutral, and the central harm rule (FactionHarm) is what refuses a neutral player or townsperson.
		/// </summary>
		private static T Hostiles<T>(T selector) where T : TargetSelector
		{
			selector.Conditions.Add(new TargetAllianceCondition { ApplyToSelf = false, ApplyToEnemy = true, ApplyToNeutral = true, ApplyToAllies = false });
			selector.Conditions.Add(new IsCharacterAliveCondition());
			return selector;
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
		/// Carries the icon already on <paramref name="existing"/> onto the fresh <paramref name="instance"/> when the
		/// generator gave it none, so an overwrite does not blank an icon assigned by hand or by
		/// FishMMO-Art/Icons/generate_ability_icons.py. Covers both the addressable <c>icon</c> of ability and buff
		/// templates and the plain sprite <c>icon</c> of ability events.
		/// </summary>
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
				return; // the generator chose an icon itself
			}
			to.CopyFromSerializedProperty(kept);
			to.ApplyModifiedPropertiesWithoutUndo();
		}

		/// <summary>Overwrites an existing asset of the same type in place (keeping its GUID), else creates it.</summary>
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

		/// <summary>Saves over an existing prefab (its GUID survives) or creates it.</summary>
		private static GameObject SavePrefab(GameObject go, string path)
		{
			GameObject saved = PrefabUtility.SaveAsPrefabAsset(go, path, out bool success);
			if (!success || saved == null)
			{
				throw new InvalidOperationException($"Could not save prefab '{path}'.");
			}
			written.Add(path);
			return saved;
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
