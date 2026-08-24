using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Transporting;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit pet control panel. Shows the pet's name and health, and issues
	/// follow/stay/summon/release commands.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The pet's health is a live value, so this panel subscribes to the pet's health attribute
	/// rather than sampling it once at summon time. It previously read the attribute exactly twice
	/// — on summon and on character set — and then never again, so the bar showed the pet's health
	/// at the moment it appeared for the pet's whole life.
	/// </para>
	/// <para>
	/// The fill fraction was also inverted (<c>FinalValue / CurrentValue</c>), which reads as a
	/// full bar at any health at all and an over-full one below half, and the name block set the
	/// GUILD label active inside a check on the NAME label.
	/// </para>
	/// </remarks>
	public class UITKPetControl : UITKCharacterControl
	{
		/// <summary>Draw order tier for this panel. See <see cref="UITKPanelLayer"/>.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.Window;

		/// <summary>Name of the pet name label element.</summary>
		private const string NAME_LABEL_NAME = "pet-name";

		/// <summary>Name of the pet health fill element.</summary>
		private const string HEALTH_FILL_NAME = "pet-health-fill";

		/// <summary>Name of the pet health value label element.</summary>
		private const string HEALTH_TEXT_NAME = "pet-health-text";

		/// <summary>Name of the pet status badge element.</summary>
		private const string STATUS_LABEL_NAME = "pet-status";

		/// <summary>Name of the follow command button.</summary>
		private const string FOLLOW_BUTTON_NAME = "pet-follow";

		/// <summary>Name of the stay command button.</summary>
		private const string STAY_BUTTON_NAME = "pet-stay";

		/// <summary>Name of the summon command button.</summary>
		private const string SUMMON_BUTTON_NAME = "pet-summon";

		/// <summary>Name of the release command button.</summary>
		private const string RELEASE_BUTTON_NAME = "pet-release";

		/// <summary>Name of the attack command button.</summary>
		private const string ATTACK_BUTTON_NAME = "pet-attack";

		/// <summary>Name of the passive stance button.</summary>
		private const string STANCE_PASSIVE_NAME = "pet-stance-passive";

		/// <summary>Name of the defensive stance button.</summary>
		private const string STANCE_DEFENSIVE_NAME = "pet-stance-defensive";

		/// <summary>Name of the aggressive stance button.</summary>
		private const string STANCE_AGGRESSIVE_NAME = "pet-stance-aggressive";

		/// <summary>Class applied to the stance button matching the pet's current stance.</summary>
		private const string STANCE_ACTIVE_CLASS = "pet-stance--active";

		/// <summary>Cached reference to the pet name label element.</summary>
		private Label nameLabel;
		/// <summary>Cached reference to the pet health fill element.</summary>
		private VisualElement healthFill;
		/// <summary>Cached reference to the pet health value label element.</summary>
		private Label healthText;
		/// <summary>Cached reference to the pet status badge element.</summary>
		private Label statusLabel;

		/// <summary>Cached reference to the passive stance button.</summary>
		private Button passiveButton;
		/// <summary>Cached reference to the defensive stance button.</summary>
		private Button defensiveButton;
		/// <summary>Cached reference to the aggressive stance button.</summary>
		private Button aggressiveButton;

		/// <summary>
		/// The stance currently shown. Model, not view — the panel rebuilds its tree on show, so
		/// the highlighted button has to be reapplied from here rather than left on the elements.
		/// </summary>
		private PetStance petStance = PetStance.Defensive;

		/// <summary>The movement order currently shown.</summary>
		private PetMovementOrder petMovementOrder = PetMovementOrder.Follow;

		/// <summary>
		/// The pet whose health attribute this panel is currently subscribed to.
		/// </summary>
		/// <remarks>
		/// Held so the subscription can be released against the SAME pet it was taken on. Reading
		/// the controller again at unsubscribe time can hand back a different pet (or none), which
		/// leaves the old attribute holding a handler that writes into this panel forever.
		/// </remarks>
		private Pet boundPet;

		/// <summary>The pet name currently displayed. Model, not view — survives a tree rebuild.</summary>
		private string petName = "Pet";
		/// <summary>The pet health fraction currently displayed.</summary>
		private float petHealthFraction;
		/// <summary>The pet health text currently displayed.</summary>
		private string petHealthText = string.Empty;

		/// <summary>
		/// Queries elements and wires up the command buttons.
		/// </summary>
		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			nameLabel = root.Q<Label>(NAME_LABEL_NAME);
			healthFill = root.Q(HEALTH_FILL_NAME);
			healthText = root.Q<Label>(HEALTH_TEXT_NAME);
			statusLabel = root.Q<Label>(STATUS_LABEL_NAME);

			Button follow = root.Q<Button>(FOLLOW_BUTTON_NAME);
			if (follow != null)
			{
				follow.clicked += OnFollowPet;
			}

			Button stay = root.Q<Button>(STAY_BUTTON_NAME);
			if (stay != null)
			{
				stay.clicked += OnStayPet;
			}

			Button summon = root.Q<Button>(SUMMON_BUTTON_NAME);
			if (summon != null)
			{
				summon.clicked += OnSummonPet;
			}

			Button release = root.Q<Button>(RELEASE_BUTTON_NAME);
			if (release != null)
			{
				release.clicked += OnReleasePet;
			}

			Button attack = root.Q<Button>(ATTACK_BUTTON_NAME);
			if (attack != null)
			{
				attack.clicked += OnAttackWithPet;
			}

			passiveButton = root.Q<Button>(STANCE_PASSIVE_NAME);
			if (passiveButton != null)
			{
				passiveButton.clicked += OnStancePassive;
			}

			defensiveButton = root.Q<Button>(STANCE_DEFENSIVE_NAME);
			if (defensiveButton != null)
			{
				defensiveButton.clicked += OnStanceDefensive;
			}

			aggressiveButton = root.Q<Button>(STANCE_AGGRESSIVE_NAME);
			if (aggressiveButton != null)
			{
				aggressiveButton.clicked += OnStanceAggressive;
			}

			/* Static events, and OnStarting re-runs on every tree rebuild. Removing first makes
			 * the pair idempotent; a bare += would stack a handler per rebuild. */
			IPetController.OnPetSummoned -= PetController_OnPetSummoned;
			IPetController.OnPetSummoned += PetController_OnPetSummoned;
			IPetController.OnPetDestroyed -= PetController_OnPetDestroyed;
			IPetController.OnPetDestroyed += PetController_OnPetDestroyed;
			IPetController.OnPetOrdersChanged -= PetController_OnPetOrdersChanged;
			IPetController.OnPetOrdersChanged += PetController_OnPetOrdersChanged;
		}

		/// <summary>
		/// Releases the static pet subscriptions and the pet health subscription.
		/// </summary>
		public override void OnDestroying()
		{
			IPetController.OnPetSummoned -= PetController_OnPetSummoned;
			IPetController.OnPetDestroyed -= PetController_OnPetDestroyed;
			IPetController.OnPetOrdersChanged -= PetController_OnPetOrdersChanged;

			UnbindPetHealth();

			base.OnDestroying();
		}

		/// <summary>
		/// Binds the panel to whatever pet the character already has.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			if (Character.TryGet(out IPetController petController))
			{
				BindPet(petController.Pet);
			}
		}

		/// <summary>
		/// Releases the outgoing character's pet binding before a new character is applied.
		/// </summary>
		public override void OnPreSetCharacter()
		{
			UnbindPetHealth();
		}

		/// <summary>
		/// Releases the pet binding and resets the panel.
		/// </summary>
		public override void OnPreUnsetCharacter()
		{
			base.OnPreUnsetCharacter();

			UnbindPetHealth();

			petName = "Pet";
			petHealthFraction = 0.0f;
			petHealthText = string.Empty;
			petStance = PetStance.Defensive;
			petMovementOrder = PetMovementOrder.Follow;
			ApplyPetState();

			Hide();
		}

		/// <inheritdoc />
		protected override void OnAfterShow()
		{
			ApplyPetState();
		}

		/// <inheritdoc />
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();

			ApplyPetState();
		}

		/// <summary>
		/// Writes the tracked pet state into the current visual tree.
		/// </summary>
		/// <remarks>
		/// Called from both <see cref="OnAfterShow"/> and <see cref="OnAfterStarting"/>: on the
		/// very first open <c>hasStarted</c> is still false so the tree-replacement path bails out
		/// and only <c>OnAfterShow</c> runs, while on later shows the tree may genuinely have been
		/// replaced. Applying the same state from both is idempotent.
		/// </remarks>
		private void ApplyPetState()
		{
			if (nameLabel != null)
			{
				nameLabel.text = petName;
			}
			if (healthFill != null)
			{
				healthFill.style.width = Length.Percent(UnityEngine.Mathf.Clamp01(petHealthFraction) * 100.0f);
			}
			if (healthText != null)
			{
				healthText.text = petHealthText;
			}
			if (statusLabel != null)
			{
				statusLabel.text = boundPet != null ? DescribeOrders() : string.Empty;
			}

			ApplyStanceHighlight();
		}

		/// <summary>
		/// Short badge text describing what the pet has been told to do.
		/// </summary>
		/// <returns>A label such as "Defensive · Stay".</returns>
		private string DescribeOrders()
		{
			return petMovementOrder == PetMovementOrder.Stay
				? petStance + " \u00B7 Stay"
				: petStance.ToString();
		}

		/// <summary>
		/// Marks the button matching the current stance and clears the other two.
		/// </summary>
		private void ApplyStanceHighlight()
		{
			SetStanceActive(passiveButton, petStance == PetStance.Passive);
			SetStanceActive(defensiveButton, petStance == PetStance.Defensive);
			SetStanceActive(aggressiveButton, petStance == PetStance.Aggressive);
		}

		/// <summary>
		/// Adds or removes the active-stance class on a button.
		/// </summary>
		/// <param name="button">The stance button, which may be null before the tree is built.</param>
		/// <param name="active">Whether this is the current stance.</param>
		private static void SetStanceActive(Button button, bool active)
		{
			if (button == null)
			{
				return;
			}
			button.EnableInClassList(STANCE_ACTIVE_CLASS, active);
		}

		/// <summary>
		/// Refreshes the panel when the server confirms a stance or movement order change.
		/// </summary>
		/// <param name="pet">The pet whose orders changed.</param>
		public void PetController_OnPetOrdersChanged(Pet pet)
		{
			/* Static event: it fires for every pet on this client. Only react to ours. */
			if (Character == null || !Character.TryGet(out IPetController petController))
			{
				return;
			}
			if (pet != null && !ReferenceEquals(petController.Pet, pet))
			{
				return;
			}

			petStance = petController.Stance;
			petMovementOrder = petController.MovementOrder;
			ApplyPetState();
		}

		/// <summary>
		/// Binds this panel to a pet: shows its name, subscribes to its health and reveals the panel.
		/// </summary>
		/// <param name="pet">The pet to bind, or null to unbind.</param>
		private void BindPet(Pet pet)
		{
			UnbindPetHealth();

			if (pet == null)
			{
				petName = "Pet";
				petHealthFraction = 0.0f;
				petHealthText = string.Empty;
				ApplyPetState();
				Hide();
				return;
			}

			boundPet = pet;
			petName = pet.GameObject != null ? pet.GameObject.name.Replace("(Clone)", string.Empty) : "Pet";
			petStance = pet.Stance;
			petMovementOrder = pet.MovementOrder;

			if (pet.CharacterNameLabel != null)
			{
				pet.CharacterNameLabel.gameObject.SetActive(true);
			}
			if (pet.CharacterGuildLabel != null)
			{
				pet.CharacterGuildLabel.gameObject.SetActive(true);
			}

			if (pet.TryGet(out ICharacterAttributeController attributeController) &&
				attributeController.TryGetHealthAttribute(out CharacterResourceAttribute health))
			{
				health.OnAttributeUpdated += PetHealth_OnAttributeUpdated;
				ApplyHealth(health);
			}
			else
			{
				petHealthFraction = 0.0f;
				petHealthText = string.Empty;
			}

			// Show() re-clones the tree, so the state above has to be applied AFTER it, which is
			// what OnAfterShow does. Applying it again here covers the already-visible case.
			if (!Visible)
			{
				Show();
			}
			else
			{
				ApplyPetState();
			}
		}

		/// <summary>
		/// Releases the health subscription held on the currently bound pet.
		/// </summary>
		private void UnbindPetHealth()
		{
			if (boundPet == null)
			{
				return;
			}

			if (boundPet.TryGet(out ICharacterAttributeController attributeController) &&
				attributeController.TryGetHealthAttribute(out CharacterResourceAttribute health))
			{
				health.OnAttributeUpdated -= PetHealth_OnAttributeUpdated;
			}

			boundPet = null;
		}

		/// <summary>
		/// Refreshes the pet health bar whenever the pet's health attribute changes.
		/// </summary>
		/// <param name="attribute">The updated attribute.</param>
		private void PetHealth_OnAttributeUpdated(CharacterAttribute attribute)
		{
			if (attribute is CharacterResourceAttribute resource)
			{
				ApplyHealth(resource);
				ApplyPetState();
			}
		}

		/// <summary>
		/// Recomputes the pet health fraction and label from a resource attribute.
		/// </summary>
		/// <param name="health">The pet's health resource attribute.</param>
		private void ApplyHealth(CharacterResourceAttribute health)
		{
			// CurrentValue / FinalValue. The original was FinalValue / CurrentValue, which is the
			// reciprocal — a pet at 1/100 health showed a bar clamped to full.
			petHealthFraction = health.FinalValueAsFloat > 0.0f
				? health.CurrentValue / health.FinalValueAsFloat
				: 0.0f;
			petHealthText = UnityEngine.Mathf.RoundToInt(health.CurrentValue) + "/" + health.FinalValue;
		}

		/// <summary>
		/// Updates the panel when a pet is summoned and makes it visible.
		/// </summary>
		/// <param name="pet">The summoned pet.</param>
		public void PetController_OnPetSummoned(Pet pet)
		{
			/* Static event: it fires for EVERY pet summoned anywhere on this client, including
			 * other players'. Only bind the one belonging to this panel's character. */
			if (pet != null &&
				Character != null &&
				Character.TryGet(out IPetController petController) &&
				!ReferenceEquals(petController.Pet, pet))
			{
				return;
			}

			BindPet(pet);
		}

		/// <summary>
		/// Hides the panel when the pet is destroyed.
		/// </summary>
		public void PetController_OnPetDestroyed()
		{
			UnbindPetHealth();

			petName = "Pet";
			petHealthFraction = 0.0f;
			petHealthText = string.Empty;
			petStance = PetStance.Defensive;
			petMovementOrder = PetMovementOrder.Follow;
			ApplyPetState();

			Hide();
		}

		/// <summary>
		/// Returns whether the character currently has a pet.
		/// </summary>
		/// <returns>True if a pet exists.</returns>
		private bool HasPet()
		{
			return Character != null &&
				Character.TryGet(out IPetController petController) &&
				petController.Pet != null;
		}

		/// <summary>
		/// Sends a follow command to the pet.
		/// </summary>
		public void OnFollowPet()
		{
			if (!HasPet())
			{
				return;
			}
			Client.Broadcast(new PetFollowBroadcast(), Channel.Reliable);
		}

		/// <summary>
		/// Sends a stay command to the pet.
		/// </summary>
		public void OnStayPet()
		{
			if (!HasPet())
			{
				return;
			}
			Client.Broadcast(new PetStayBroadcast(), Channel.Reliable);
		}

		/// <summary>
		/// Sends a summon command to the pet.
		/// </summary>
		public void OnSummonPet()
		{
			if (!HasPet())
			{
				return;
			}
			Client.Broadcast(new PetSummonBroadcast(), Channel.Reliable);
		}

		/// <summary>
		/// Orders the pet to attack the player's current target.
		/// </summary>
		/// <remarks>
		/// The target is not sent: the server reads the player's own target controller, so the
		/// client cannot nominate something it is not actually targeting.
		/// </remarks>
		public void OnAttackWithPet()
		{
			if (!HasPet())
			{
				return;
			}
			Client.Broadcast(new PetAttackBroadcast(), Channel.Reliable);
		}

		/// <summary>Requests the passive stance.</summary>
		public void OnStancePassive()
		{
			RequestStance(PetStance.Passive);
		}

		/// <summary>Requests the defensive stance.</summary>
		public void OnStanceDefensive()
		{
			RequestStance(PetStance.Defensive);
		}

		/// <summary>Requests the aggressive stance.</summary>
		public void OnStanceAggressive()
		{
			RequestStance(PetStance.Aggressive);
		}

		/// <summary>
		/// Asks the server to change the pet's stance.
		/// </summary>
		/// <remarks>
		/// The panel does not paint the new stance here. It waits for the server's confirming
		/// <see cref="PetStanceBroadcast"/>, so the highlighted button always reflects what the
		/// pet is really doing rather than what was last clicked.
		/// </remarks>
		/// <param name="stance">The requested stance.</param>
		private void RequestStance(PetStance stance)
		{
			if (!HasPet())
			{
				return;
			}
			Client.Broadcast(new PetStanceBroadcast() { Stance = stance }, Channel.Reliable);
		}

		/// <summary>
		/// Sends a release command to the pet.
		/// </summary>
		public void OnReleasePet()
		{
			if (!HasPet())
			{
				return;
			}
			Client.Broadcast(new PetReleaseBroadcast(), Channel.Reliable);
		}
	}
}
