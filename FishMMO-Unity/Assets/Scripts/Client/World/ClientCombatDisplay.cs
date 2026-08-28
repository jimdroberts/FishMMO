using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using KinematicCharacterController;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Client
{
	/// <summary>
	/// Handles client-side display of combat events: floating damage/heal numbers,
	/// death dialog, resurrect dialog hide, and achievement popups.
	/// Extracted from Client.cs.
	/// </summary>
	/// <remarks>
	/// Every method here runs on the hottest path the client has — one invocation per damage or
	/// heal event, on every character in view, which in an AoE fight is dozens per frame. What it
	/// used to do per invocation was a <c>GetComponent&lt;Collider&gt;</c> (a native call that walks
	/// the GameObject's component list), a config dictionary probe with a string key, and an
	/// <c>int.ToString()</c>. The first two are cached below; the third is served from a table for
	/// the small values that make up almost all combat numbers.
	/// </remarks>
	public class ClientCombatDisplay
	{
		/// <summary>
		/// Cached label height per character GameObject, keyed by instance ID.
		/// </summary>
		/// <remarks>
		/// The height comes from the collider's dimensions or the capsule height, neither of which
		/// changes over a character's life, so resolving it once per character is enough. Keyed by
		/// instance ID rather than by the object so a destroyed character cannot be kept alive by
		/// this dictionary; the entries are cleared on scene change and when the cache grows past
		/// its bound, which is what keeps it from accumulating dead ids over a long session.
		/// </remarks>
		private readonly Dictionary<int, float> displayHeights = new Dictionary<int, float>();

		/// <summary>
		/// Maximum height cache entries before the whole cache is dropped.
		/// </summary>
		/// <remarks>
		/// A flat clear rather than an eviction policy: the cost of a miss is one
		/// <c>GetComponent</c>, so being occasionally cold is cheap and an LRU would cost more to
		/// maintain than it saves. The bound exists to stop the dictionary growing without limit
		/// on a busy server, not to maximise hit rate.
		/// </remarks>
		private const int MaxHeightCacheEntries = 512;

		/// <summary>
		/// Pre-rendered decimal strings for the values combat numbers almost always take.
		/// </summary>
		/// <remarks>
		/// <c>int.ToString()</c> allocates a fresh string every call, and every one of them is
		/// garbage a frame later. Damage and heal values below this bound are served from the table
		/// instead; larger hits still allocate, which is correct — they are rare.
		/// </remarks>
		private static readonly string[] SmallNumberCache = BuildSmallNumberCache(2048);

		/// <summary>
		/// False until the cached values below were read from real settings.
		/// </summary>
		/// <remarks>
		/// <see cref="Initialize"/> runs from <c>Client.Initialize</c>, which happens while the
		/// postboot Addressable batch is completing — <c>Configuration.GlobalSettings</c> does not
		/// exist yet at that point. Reading the three flags per event, as this used to, hid that:
		/// the first damage event arrived long after settings had loaded. Caching them at startup
		/// is the right optimisation but it moved the read in front of its dependency, so the
		/// cache is now filled on first use if startup was too early, and the flags stay at their
		/// safe default of false until then.
		/// </remarks>
		private bool configLoaded;

		/// <summary>Cached "ShowDamage" config value.</summary>
		private bool showDamage;
		/// <summary>Cached "ShowHeals" config value.</summary>
		private bool showHeals;
		/// <summary>Cached "ShowAchievementCompletion" config value.</summary>
		private bool showAchievements;

		/// <summary>Subscribes to damage/heal/kill/resurrect/achievement events. Call during startup.</summary>
		public void Initialize()
		{
			RefreshConfig();

			/* Without this the cache below is never invalidated: the values were read once at
			 * start-up and the options panel had no way to reach them, so turning damage numbers
			 * off did nothing until the client was restarted. */
			ClientSettings.OnGameplayChanged += RefreshConfig;

			/* Numbers come from the server's combat report, not from the local OnDamaged/OnHealed
			 * events. Those fire wherever the arithmetic runs: on the server for every ability hit,
			 * which reaches no client at all, and on the owning client for its own predicted
			 * damage-over-time ticks, which repeat on every reconcile replay. The report fires once,
			 * on every client that can see the target, carrying the amount that actually landed. */
			CharacterDamageController.OnCombatEventReceived += OnCombatEvent;
			ICharacterDamageController.OnKilled += OnKilled;
			ICharacterDamageController.OnResurrected += OnResurrected;
			IAchievementController.OnCompleteAchievement += OnAchievement;

			SceneManager.activeSceneChanged += OnActiveSceneChanged;
		}

		/// <summary>Unsubscribes from all combat/achievement events. Call during teardown.</summary>
		public void Shutdown()
		{
			ClientSettings.OnGameplayChanged -= RefreshConfig;

			CharacterDamageController.OnCombatEventReceived -= OnCombatEvent;
			ICharacterDamageController.OnKilled -= OnKilled;
			ICharacterDamageController.OnResurrected -= OnResurrected;
			IAchievementController.OnCompleteAchievement -= OnAchievement;

			SceneManager.activeSceneChanged -= OnActiveSceneChanged;

			displayHeights.Clear();
		}

		/// <summary>
		/// Re-reads the display toggles from the global settings.
		/// </summary>
		/// <remarks>
		/// Public so the options panel can call it after a settings change. The values were
		/// previously read out of the settings dictionary on every single damage event, by string
		/// key, which is a dictionary probe and a hash of a literal per hit.
		/// </remarks>
		public void RefreshConfig()
		{
			if (Configuration.GlobalSettings == null)
			{
				// Too early. EnsureConfig retries on the first event that needs a value.
				configLoaded = false;
				return;
			}

			/* Through ClientSettings, which supplies the default declared alongside the toggle.
			 * Reading these with an implicit default of false — which a bare TryGetBool gives —
			 * meant a fresh install showed no damage numbers while its own options screen showed
			 * the box ticked, and ticking it off and on again was the only way to reconcile them. */
			showDamage = ClientSettings.GetGameplayToggle(ClientSettings.ShowDamageKey);
			showHeals = ClientSettings.GetGameplayToggle(ClientSettings.ShowHealsKey);
			showAchievements = ClientSettings.GetGameplayToggle(ClientSettings.ShowAchievementsKey);
			configLoaded = true;
		}

		/// <summary>
		/// Fills the config cache if <see cref="RefreshConfig"/> ran before settings existed.
		/// </summary>
		/// <remarks>
		/// Costs one boolean test per event once loaded, which is the whole point of the cache;
		/// before then it costs the dictionary probe this used to pay on every hit anyway.
		/// </remarks>
		private void EnsureConfig()
		{
			if (!configLoaded)
			{
				RefreshConfig();
			}
		}

		/// <summary>
		/// Drops the per-character height cache when the scene changes.
		/// </summary>
		private void OnActiveSceneChanged(Scene from, Scene to)
		{
			displayHeights.Clear();
		}

		/// <summary>
		/// Returns the vertical offset a floating label should sit at above a character.
		/// </summary>
		/// <param name="character">The character the label belongs to.</param>
		private float GetDisplayHeight(ICharacter character)
		{
			GameObject go = character.GameObject;
			if (go == null)
			{
				return 1f;
			}

			int key = go.GetInstanceID();
			if (displayHeights.TryGetValue(key, out float cached))
			{
				return cached;
			}

			float h = 1f;
			var col = go.GetComponent<Collider>();
			if (col != null) { col.TryGetDimensions(out h, out _); }
			else if (character is IPlayerCharacter pc) { h = pc.CharacterController.FullCapsuleHeight; }

			if (displayHeights.Count >= MaxHeightCacheEntries)
			{
				displayHeights.Clear();
			}
			displayHeights[key] = h;
			return h;
		}

		/// <summary>
		/// Builds the small-integer string table.
		/// </summary>
		/// <param name="count">Number of entries (values 0..count-1).</param>
		private static string[] BuildSmallNumberCache(int count)
		{
			string[] cache = new string[count];
			for (int i = 0; i < count; ++i)
			{
				cache[i] = i.ToString();
			}
			return cache;
		}

		/// <summary>
		/// Returns a decimal string for a combat value, without allocating for common magnitudes.
		/// </summary>
		/// <param name="value">The value to render.</param>
		private static string ToDisplayString(int value)
		{
			if (value >= 0 && value < SmallNumberCache.Length)
			{
				return SmallNumberCache[value];
			}
			return value.ToString();
		}

		/// <summary>Routes one server-reported combat event to the right floating number.</summary>
		private void OnCombatEvent(ICharacter source, ICharacter target, int amount, DamageAttributeTemplate dmg, CombatEventKind kind)
		{
			if (kind == CombatEventKind.Heal)
			{
				OnHealed(source, target, amount);
				return;
			}
			OnDamaged(source, target, amount, dmg);
		}

		private void OnDamaged(ICharacter attacker, ICharacter target, int amount, DamageAttributeTemplate dmg)
		{
			EnsureConfig();
			if (target == null || !showDamage) return;
			var pos = target.Transform.position;
			pos.y += GetDisplayHeight(target);
			int fx = 0; fx.EnableBit(LabelEffect.FloatRandom); fx.EnableBit(LabelEffect.FadeOut);
			/* Typeless damage still gets a number. Environmental and "true" damage carry no damage
			 * attribute, and the report is free to arrive before this client has resolved the
			 * template, so the colour falls back rather than the number being skipped. */
			Color damageColor = dmg != null ? dmg.DisplayColor : new TinyColor(255, 64, 64).ToUnityColor();
			UITKLabelMaker.Display3D(ToDisplayString(amount), pos, damageColor, 2.0f, 1.0f, false, fx);
		}

		private void OnHealed(ICharacter healer, ICharacter healed, int amount)
		{
			EnsureConfig();
			if (healed == null || !showHeals) return;
			var pos = healed.Transform.position;
			pos.y += GetDisplayHeight(healed);
			int fx = 0; fx.EnableBit(LabelEffect.FloatUp); fx.EnableBit(LabelEffect.FadeOut);
			UITKLabelMaker.Display3D(ToDisplayString(amount), pos, new TinyColor(64, 64, 255).ToUnityColor(), 4.0f, 1.0f, false, fx);
		}

		private void OnKilled(ICharacter killer, ICharacter victim)
		{
			if (victim == null || !victim.NetworkObject.IsOwner) return;
			if (UIManager.TryGetTK("UIDeathDialog", out UITKDeathDialog d)) d.ShowDeathDialog();
		}

		private void OnResurrected(ICharacter resurrector, ICharacter resurrected)
		{
			if (resurrected == null || !resurrected.NetworkObject.IsOwner) return;
			if (UIManager.TryGetTK("UIDeathDialog", out UITKDeathDialog d)) d.Hide();
		}

		private void OnAchievement(ICharacter character, AchievementTemplate template, AchievementTier tier)
		{
			EnsureConfig();
			if (character == null || template == null || !showAchievements) return;
			var pos = character.Transform.position;
			pos.y += GetDisplayHeight(character);
			int fx = 0; fx.EnableBit(LabelEffect.FadeIn); fx.EnableBit(LabelEffect.FadeOut); fx.EnableBit(LabelEffect.Bounce);
			UITKLabelMaker.Display3D("Achievement: " + template.Name + "\r\n" + tier.TierCompleteMessage, pos, Color.yellow, 2.0f, 4.0f, false, fx);
		}
	}
}
