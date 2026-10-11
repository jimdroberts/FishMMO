using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Makes a portal — a same-scene <see cref="Teleporter"/> or a cross-scene
	/// <see cref="SceneTeleporter"/> — dormant until something activates it.
	/// </summary>
	/// <remarks>
	/// <para><b>Jim's rules (2026-10-10).</b> A portal is inactive until activated. A player
	/// activates it by using it while meeting ANY of its <see cref="Conditions"/> (an ability known,
	/// an item held, the placeholder <see cref="HasProfessionCondition"/>) or holding its
	/// <see cref="Key"/>; the key is designer-assigned and generated portals leave it empty. Who it
	/// then opens for is <see cref="Scope"/>, chosen per portal: the activating character only,
	/// everyone for <see cref="DurationSeconds"/>, or everyone for good. The rules are
	/// <see cref="PortalActivationRules"/>; the use is <see cref="PortalGate"/>, called by
	/// <see cref="TeleportAction"/> and <see cref="SceneTeleporter"/> before they move anyone.</para>
	/// <para><b>Identity is (scene, <see cref="PortalIndex"/>).</b> The index is the bit a
	/// character's activation record stores and the key of the world row, so it must be unique in
	/// its scene and never change once the scene ships — the waypoint contract, in its own index
	/// space (a waypoint and a portal may both be 0).</para>
	/// <para><b>Look.</b> On a client, <see cref="ActiveVisual"/> / <see cref="InactiveVisual"/> are
	/// switched from what the server reported (<see cref="PortalClientStates"/>). Both optional.</para>
	/// </remarks>
	[DisallowMultipleComponent]
	public class PortalActivation : MonoBehaviour
	{
		[Tooltip("Index of this portal within its scene: unique per scene and stable forever. It is the bit a character's activation record stores and the key of the world record.")]
		[SerializeField]
		private int portalIndex;

		[Tooltip("Who the portal opens for once activated: the activating character, everyone for a while, or everyone for good.")]
		[SerializeField]
		private PortalActivationScope scope = PortalActivationScope.PerCharacter;

		[Tooltip("How long a world-timed activation keeps the portal open, in seconds.")]
		[SerializeField]
		private float durationSeconds = 3600.0f;

		[Header("Requirements (any one opens it)")]
		[Tooltip("Any ONE passing condition activates the portal. Evaluated on the server against the player using it. Empty, with no key, means the first use activates it.")]
		[SerializeReference, SubclassSelector]
		private List<BaseCondition> conditions = new List<BaseCondition>();

		[Tooltip("An item that also activates the portal when held. Designer-assigned; generated portals leave it empty.")]
		[SerializeField]
		private BaseItemTemplate key;

		[Tooltip("Spend one key when the key is what activated the portal. A player who qualified by a condition keeps theirs.")]
		[SerializeField]
		private bool consumeKey;

		[Tooltip("What a player who cannot activate the portal is told. Uses a generic line when empty.")]
		[SerializeField]
		private string lockedMessage;

		[Header("Look (client)")]
		[Tooltip("Shown while the portal is open for the local player.")]
		[SerializeField]
		private GameObject activeVisual;

		[Tooltip("Shown while the portal is closed for the local player.")]
		[SerializeField]
		private GameObject inactiveVisual;

		/// <summary>The generic refusal when <see cref="LockedMessage"/> is empty.</summary>
		public const string DefaultLockedMessage = "The portal is dormant. Something must awaken it.";

		public int PortalIndex => portalIndex;
		public PortalActivationScope Scope => scope;
		public float DurationSeconds => PortalActivationRules.ClampDuration(durationSeconds);
		public List<BaseCondition> Conditions => conditions;
		public BaseItemTemplate Key => key;
		public bool ConsumeKey => consumeKey;
		public string LockedMessage => string.IsNullOrWhiteSpace(lockedMessage) ? DefaultLockedMessage : lockedMessage;
		public GameObject ActiveVisual => activeVisual;
		public GameObject InactiveVisual => inactiveVisual;

		/// <summary>The scene the portal stands in (the scene asset's name, the same in every instance).</summary>
		public string SceneName => gameObject.scene.name;

		/// <summary>Whether the portal names any condition or a key.</summary>
		public bool HasRequirements
		{
			get
			{
				if (key != null)
				{
					return true;
				}
				if (conditions != null)
				{
					for (int i = 0; i < conditions.Count; ++i)
					{
						if (conditions[i] != null)
						{
							return true;
						}
					}
				}
				return false;
			}
		}

		/// <summary>Whether at least one condition passes for the player. Null entries are skipped.</summary>
		public bool AnyConditionMet(ICharacter character, EventData eventData)
		{
			if (character == null || conditions == null)
			{
				return false;
			}
			for (int i = 0; i < conditions.Count; ++i)
			{
				BaseCondition condition = conditions[i];
				if (condition != null && condition.Check(character, eventData))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>Whether the player carries at least one key in their inventory.</summary>
		public bool HoldsKey(ICharacter character)
		{
			return key != null && character != null &&
				character.TryGet(out IInventoryController inventory) &&
				inventory.GetItemCount(key) > 0;
		}

		/// <summary>
		/// Sets the authored fields. The point-of-interest generator's seam; a designer edits them in
		/// the inspector.
		/// </summary>
		public void Configure(int index, PortalActivationScope activationScope, float timedSeconds)
		{
			portalIndex = Mathf.Clamp(index, 0, WaypointUnlockMask.MaxIndex);
			scope = activationScope;
			durationSeconds = PortalActivationRules.ClampDuration(timedSeconds);
		}

		private void OnEnable()
		{
			PortalRegistry.Register(this);
#if !UNITY_SERVER
			PortalClientStates.Changed += OnClientStatesChanged;
			ApplyVisual();
#endif
		}

		private void OnDisable()
		{
			PortalRegistry.Unregister(this);
#if !UNITY_SERVER
			PortalClientStates.Changed -= OnClientStatesChanged;
			CancelInvoke(nameof(ApplyVisual));
#endif
		}

#if !UNITY_SERVER
		private void OnClientStatesChanged(string sceneName)
		{
			if (string.Equals(sceneName, SceneName, System.StringComparison.Ordinal))
			{
				ApplyVisual();
			}
		}

		/// <summary>
		/// Shows the open or closed look, and wakes again when a timed opening ends so the portal
		/// closes on screen without another message.
		/// </summary>
		private void ApplyVisual()
		{
			CancelInvoke(nameof(ApplyVisual));
			float left = PortalClientStates.SecondsLeft(SceneName, portalIndex);
			bool open = left > 0.0f;
			if (activeVisual != null && activeVisual != gameObject)
			{
				activeVisual.SetActive(open);
			}
			if (inactiveVisual != null && inactiveVisual != gameObject)
			{
				inactiveVisual.SetActive(!open);
			}
			if (open && !float.IsPositiveInfinity(left))
			{
				Invoke(nameof(ApplyVisual), left + 0.05f);
			}
		}
#endif

#if UNITY_EDITOR
		private void OnValidate()
		{
			if (!WaypointUnlockMask.IsValidIndex(portalIndex))
			{
				Debug.LogWarning($"Portal '{name}': index {portalIndex} is outside [0, {WaypointUnlockMask.MaxIndex}]; clamping.", this);
				portalIndex = Mathf.Clamp(portalIndex, 0, WaypointUnlockMask.MaxIndex);
			}
			durationSeconds = PortalActivationRules.ClampDuration(durationSeconds);
		}
#endif
	}
}
