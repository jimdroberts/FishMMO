using System;
using FishMMO.Shared.Weather;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace FishMMO.Shared
{
	/// <summary>What a character is doing this frame, as its body is animated from it.</summary>
	public struct CharacterMotion
	{
		/// <summary>How far it really went this frame over the frame's length (not how fast it was asked to go).</summary>
		public Vector3 Velocity;
		/// <summary>On walkable ground.</summary>
		public bool Grounded;
		/// <summary>Crouched.</summary>
		public bool Crouching;
		/// <summary>In the water, floating or diving.</summary>
		public bool Swimming;
		/// <summary>The still water's surface over it while swimming (world metres), or NaN to have the body look it up.</summary>
		public float StillWater;
		/// <summary>On a ladder or a rope.</summary>
		public bool Climbing;
	}

	/// <summary>
	/// A character model, animated: the clips of its <see cref="CharacterAnimationSet"/> blended in a PlayableGraph by what
	/// the character is doing, at the speed it is really doing it, and the figure set where it belongs (its soles on the
	/// ground, its seat on its seat, its head over the waves). The one body behind every character in the game
	/// (<see cref="CharacterAnimationController"/>) and the World Sim walker.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A graph, not an AnimatorController.</b> One mixer holds every whole-body state (standing, walking, running, the
	/// air, crouching, treading, the two strokes, climbing, sitting, the roll, the death), its weights eased toward the
	/// state's and always summing to one. An arms-only layer over it plays the punches, the cast and the guard, so a cast
	/// thrown walking keeps the legs walking. There is no controller asset to keep in step with parameters.
	/// </para>
	/// <para>
	/// <b>Speed-matched.</b> Under the walk clip's own speed idle fades into walk; between walk and run the two are blended
	/// with their strides locked to one phase; past the run clip's speed the run plays faster. The strokes play at the
	/// swim's pace and the climb is stepped by the height climbed.
	/// </para>
	/// <para>
	/// <b>In the water as a swimmer is</b> (<see cref="SwimPose"/>): the movement floats the capsule on still water, so at
	/// the surface the figure is set by its head, held just over the drawn sea, riding and tipping with each wave, and it
	/// swims head up; only under the surface does it lie flat along its swim.
	/// </para>
	/// <para>
	/// Client only in use: the server never builds one (the controller's body is compiled out of a server build).
	/// </para>
	/// </remarks>
	public sealed class CharacterBody : IDisposable
	{
		// The whole-body mixer's inputs.
		private const int Idle = 0, Walk = 1, Run = 2, JumpUp = 3, MidAir = 4, Fall = 5, CrouchIdle = 6, CrouchWalk = 7,
			Tread = 8, Surface = 9, Under = 10, Climb = 11, SitGround = 12, SitSeat = 13, Rolling = 14, Dying = 15, Inputs = 16;
		// The arms layer's inputs.
		private const int PunchRight = 0, PunchLeft = 1, CastArms = 2, BlockArms = 3, ArmInputs = 4;

		/// <summary>How quickly the blend follows the character's speed and footing, per second.</summary>
		private const float BlendRate = 10f;
		/// <summary>How quickly it sits down, gets up, dies and rises again, per second: a body's pace, not a gait's.</summary>
		private const float SlowRate = 3.5f;
		/// <summary>Seconds off the ground before it is in the air: a step down a kerb is not a jump.</summary>
		private const float AirDelay = 0.15f;
		/// <summary>The fastest the run is played, as a multiple of its own pace.</summary>
		private const float MaxRunRate = 2f;
		/// <summary>Seconds an arms action takes to come up from the arms' own pose.</summary>
		private const float ArmsIn = 0.06f;
		/// <summary>How quickly the feet are eased onto the ground, per second.</summary>
		private const float GroundRate = 12f;
		/// <summary>The most the body is ever raised or lowered to find the ground, metres.</summary>
		private const float MaxLift = 0.5f;
		/// <summary>How far above and below a sole the ground is looked for, metres.</summary>
		private const float GroundReach = 0.6f;
		/// <summary>How quickly it follows the waves, per second.</summary>
		private const float WaveRate = 8f;
		/// <summary>How far in from a seat's front edge the hip joints come down, metres (on the keyed figure).</summary>
		private const float SeatInset = 0.1f;
		/// <summary>The capsule height the swim float line is measured against (every playable prefab's).</summary>
		public const float CapsuleHeight = 1.6f;

		private readonly CharacterAnimationSet set;
		private readonly Animator animator;
		private readonly Transform shown;
		private readonly Vector3 shownRest;
		private readonly Quaternion shownRestRotation;
		private readonly int groundMask;
		private readonly Renderer[] renderers;
		private PlayableGraph graph;
		private AnimationMixerPlayable mixer;
		private AnimationLayerMixerPlayable layers;
		private AnimationMixerPlayable arms;
		private readonly AnimationClipPlayable[] clips = new AnimationClipPlayable[Inputs];
		private readonly AnimationClipPlayable[] armClips = new AnimationClipPlayable[ArmInputs];
		private readonly bool[] hasClip = new bool[Inputs];
		private readonly bool[] hasArmClip = new bool[ArmInputs];
		private readonly float[] weights = new float[Inputs];
		private readonly float[] targets = new float[Inputs];
		private AvatarMask armsMask;

		private readonly Transform[] feet = new Transform[2];
		private readonly Transform[] toes = new Transform[2];
		private readonly float[] ankleRest = new float[2];
		private readonly float[] toeRest = new float[2];
		private readonly Transform head;
		private readonly float restHeight;

		private readonly float walkSpeed, runSpeed, crouchWalkSpeed, walkLength, runLength;
		private float speed;
		private float phase;
		private float climbPhase;
		private float airborne;
		private bool grounded;
		private bool crouching;
		private bool swimming;
		private bool climbing;
		private float stillWater = float.NaN;
		private float surfaceShare;
		private float pitch;
		private float tiltPitch;
		private float tiltRoll;
		private float lift;

		private bool sitting;
		private bool onSeat;
		private float seatHeight;
		private float seatBack;
		private Vector3 seatOffset;

		private enum ArmsAction { None, Punch, Cast, Block }
		private ArmsAction armsAction;
		private int armsInput;
		private float armsTime = -1f;
		private float armsLength;
		private bool blocking;
		private int punchSide;
		private float armsWeight;

		private float rollTime = -1f;
		private bool dead;
		private float deathTime;

		/// <summary>The figure's height over its capsule's feet, metres (the swell it rides, the ground it finds).</summary>
		public float Lift => lift;

		/// <summary>1 floating at the surface, 0 well under it (or not swimming).</summary>
		public float SurfaceShare => surfaceShare;

		/// <summary>Lying dead (its last pose held).</summary>
		public bool IsDead => dead;

		/// <summary>The model's height standing at rest, metres.</summary>
		public float RestHeight => restHeight;

		/// <summary>The seat height its seated clip fits, metres, on this model (the set's, scaled to the model's height).</summary>
		public float KeyedSeatHeight => set.SeatHeight * (set.KeyedHeight > 0f ? restHeight / set.KeyedHeight : 1f);

		/// <summary>
		/// Animates <paramref name="animator"/> (a model with a Humanoid avatar) with <paramref name="set"/>, or returns null
		/// (and says why) when it cannot: no avatar, not a humanoid, no idle.
		/// </summary>
		public static CharacterBody Create(Animator animator, CharacterAnimationSet set, string owner)
		{
			if (animator == null || set == null || set.Idle == null)
			{
				Debug.LogWarning($"[CharacterBody] {owner}: no animator or no animation set (with an idle); its model is not animated.");
				return null;
			}
			if (animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
			{
				Debug.LogWarning($"[CharacterBody] {owner}: '{animator.name}' has no valid Humanoid avatar; its model is not animated.");
				return null;
			}
			return new CharacterBody(animator, set, owner);
		}

		private CharacterBody(Animator animator, CharacterAnimationSet set, string owner)
		{
			this.animator = animator;
			this.set = set;
			shown = animator.transform;
			shownRest = shown.localPosition;
			shownRestRotation = shown.localRotation;
			renderers = shown.GetComponentsInChildren<Renderer>(true);
			/* Its sky light as the ground's and the rocks' is: from the light probes where the scene has some, and from the
			 * scene's ambient where it has none. A skinned mesh blends probes by default and the world scenes carry none: its
			 * shaded side took no sky at all, and with the sun behind it the figure stood black against lit ground. */
			LightProbes probes = LightmapSettings.lightProbes;
			UnityEngine.Rendering.LightProbeUsage usage = probes != null && probes.count > 0
				? UnityEngine.Rendering.LightProbeUsage.BlendProbes
				: UnityEngine.Rendering.LightProbeUsage.Off;
			foreach (Renderer renderer in renderers)
			{
				renderer.lightProbeUsage = usage;
			}
			int solid = LayerMask.GetMask("Default", "Ground");
			groundMask = (solid != 0 ? solid : Physics.DefaultRaycastLayers) & ~(1 << shown.gameObject.layer);
			int playerLayer = LayerMask.NameToLayer("Player");
			if (playerLayer >= 0)
			{
				groundMask &= ~(1 << playerLayer);
			}

			// Measured now, while the model stands in its rest pose (it has just been made, and nothing has played yet).
			restHeight = RestExtent(shown, out float bottom, out float top) ? top - bottom : CapsuleHeight;
			float sole = shown.position.y;
			feet[0] = Bone(HumanBodyBones.LeftFoot);
			feet[1] = Bone(HumanBodyBones.RightFoot);
			toes[0] = Bone(HumanBodyBones.LeftToes);
			toes[1] = Bone(HumanBodyBones.RightToes);
			head = Bone(HumanBodyBones.Head);
			for (int i = 0; i < 2; i++)
			{
				ankleRest[i] = feet[i] != null ? feet[i].position.y - sole : 0f;
				toeRest[i] = toes[i] != null ? toes[i].position.y - sole : 0f;
			}
			/* At rest the two feet stand alike. When they do not, the model came in posed: Unity takes a model's default
			 * pose from its file's first take and builds its Humanoid avatar from it too, so every clip is retargeted
			 * through that pose. A climb's first frame did that once (a knee up), and the grounding held the figure a
			 * quarter metre up. */
			if (feet[0] != null && feet[1] != null && Mathf.Abs(ankleRest[0] - ankleRest[1]) > 0.03f)
			{
				Debug.LogWarning($"[CharacterBody] {owner}: '{animator.name}' is not at rest as imported (its feet stand {ankleRest[0]:F2} and {ankleRest[1]:F2} m up): its first take must be its rest pose.");
			}

			animator.applyRootMotion = false;
			animator.runtimeAnimatorController = null;
			graph = PlayableGraph.Create($"Character Body ({owner})");
			graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
			mixer = AnimationMixerPlayable.Create(graph, Inputs);
			layers = AnimationLayerMixerPlayable.Create(graph, 2);
			graph.Connect(mixer, 0, layers, 0);
			layers.SetInputWeight(0, 1f);
			AnimationPlayableOutput.Create(graph, "Body", animator).SetSourcePlayable(layers);

			// A missing clip is stood in for by the nearest there is.
			AnimationClip stand = set.Idle;
			AnimationClip walk = set.Walk != null ? set.Walk : stand;
			AnimationClip midAir = set.MidAir != null ? set.MidAir : set.Fall != null ? set.Fall : stand;
			AnimationClip crouch = set.CrouchIdle != null ? set.CrouchIdle : stand;
			Connect(Idle, stand, true);
			Connect(Walk, walk, set.Walk != null);
			Connect(Run, set.Run != null ? set.Run : walk, set.Run != null);
			Connect(JumpUp, set.JumpUp != null ? set.JumpUp : midAir, set.JumpUp != null);
			Connect(MidAir, midAir, set.MidAir != null);
			Connect(Fall, set.Fall != null ? set.Fall : midAir, set.Fall != null);
			Connect(CrouchIdle, crouch, set.CrouchIdle != null);
			Connect(CrouchWalk, set.CrouchWalk != null ? set.CrouchWalk : crouch, set.CrouchWalk != null);
			Connect(Tread, set.Tread != null ? set.Tread : stand, set.Tread != null);
			Connect(Surface, set.SwimSurface != null ? set.SwimSurface : walk, set.SwimSurface != null);
			Connect(Under, set.SwimUnder != null ? set.SwimUnder : set.SwimSurface != null ? set.SwimSurface : walk, set.SwimUnder != null);
			Connect(Climb, set.Climb != null ? set.Climb : walk, set.Climb != null);
			Connect(SitGround, set.SitGround != null ? set.SitGround : crouch, set.SitGround != null);
			Connect(SitSeat, set.SitSeat != null ? set.SitSeat : stand, set.SitSeat != null);
			Connect(Rolling, set.Roll != null ? set.Roll : crouch, set.Roll != null);
			Connect(Dying, set.Death != null ? set.Death : stand, set.Death != null);
			// Stepped by hand: the shared stride, the height climbed, the one-shots.
			clips[Walk].SetSpeed(0d);
			clips[Run].SetSpeed(0d);
			clips[Climb].SetSpeed(0d);
			clips[Rolling].SetSpeed(0d);
			clips[Dying].SetSpeed(0d);
			clips[JumpUp].SetSpeed(0d);

			arms = AnimationMixerPlayable.Create(graph, ArmInputs);
			ConnectArms(PunchRight, set.PunchRight);
			ConnectArms(PunchLeft, set.PunchLeft);
			ConnectArms(CastArms, set.Cast);
			ConnectArms(BlockArms, set.Block);
			graph.Connect(arms, 0, layers, 1);
			// The arms only: the legs and the body go on with whatever they were doing.
			armsMask = new AvatarMask { name = "Character arms" };
			for (AvatarMaskBodyPart part = 0; part < AvatarMaskBodyPart.LastBodyPart; part++)
			{
				armsMask.SetHumanoidBodyPartActive(part, part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm
					|| part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers);
			}
			layers.SetLayerMaskFromAvatarMask(1, armsMask);
			layers.SetInputWeight(1, 0f);

			float scale = animator.humanScale;
			walkSpeed = NaturalSpeed(set.Walk, scale, 1.5f);
			runSpeed = Mathf.Max(NaturalSpeed(set.Run, scale, 4f), walkSpeed + 0.5f);
			crouchWalkSpeed = NaturalSpeed(set.CrouchWalk != null ? set.CrouchWalk : set.Walk, scale, 1.2f);
			walkLength = Mathf.Max(walk.length, 0.1f);
			runLength = Mathf.Max((set.Run != null ? set.Run : walk).length, 0.1f);
			weights[Idle] = 1f;
			mixer.SetInputWeight(Idle, 1f);
			graph.Play();
		}

		/// <summary>The graph runs, or stops (a hidden character's), with the GameObject.</summary>
		public void SetPlaying(bool playing)
		{
			if (!graph.IsValid())
			{
				return;
			}
			if (playing)
			{
				graph.Play();
			}
			else
			{
				graph.Stop();
			}
		}

		public void Dispose()
		{
			if (graph.IsValid())
			{
				graph.Destroy();
			}
			if (armsMask != null)
			{
				UnityEngine.Object.Destroy(armsMask);
				armsMask = null;
			}
			if (shown != null)
			{
				shown.localPosition = shownRest;
				shown.localRotation = shownRestRotation;
			}
		}

		private void Connect(int input, AnimationClip clip, bool own)
		{
			AnimationClipPlayable playable = AnimationClipPlayable.Create(graph, clip);
			graph.Connect(playable, 0, mixer, input);
			mixer.SetInputWeight(input, 0f);
			clips[input] = playable;
			hasClip[input] = own;
		}

		private void ConnectArms(int input, AnimationClip clip)
		{
			hasArmClip[input] = clip != null;
			AnimationClipPlayable playable = AnimationClipPlayable.Create(graph, clip != null ? clip : set.Idle);
			playable.SetSpeed(0d);
			graph.Connect(playable, 0, arms, input);
			arms.SetInputWeight(input, 0f);
			armClips[input] = playable;
		}

		private float Length(int input) => Mathf.Max(0.05f, clips[input].GetAnimationClip().length);

		// ── Doing ──

		/// <summary>Throws a punch, the right and the left by turns (not while the hands are busy swimming, climbing or sitting).</summary>
		public void Punch()
		{
			if (!ArmsFree() || !hasArmClip[PunchRight] || !hasArmClip[PunchLeft]
				|| (armsAction == ArmsAction.Punch && armsTime >= 0f && armsTime < armsLength * 0.55f))
			{
				return;
			}
			punchSide = 1 - punchSide;
			StartArms(ArmsAction.Punch, punchSide == 0 ? PunchRight : PunchLeft);
		}

		/// <summary>Casts: both hands gather and thrust.</summary>
		public void Cast()
		{
			if (ArmsFree() && hasArmClip[CastArms])
			{
				StartArms(ArmsAction.Cast, CastArms);
			}
		}

		/// <summary>Raises the guard, or lowers it.</summary>
		public void SetBlocking(bool block)
		{
			blocking = block;
		}

		/// <summary>Rolls forward once.</summary>
		public void Roll()
		{
			if (!dead && hasClip[Rolling] && !swimming && !climbing)
			{
				rollTime = 0f;
				sitting = false;
			}
		}

		/// <summary>
		/// Dies: falls and lies. <paramref name="already"/> lies at once, for a character that was dead before this client saw
		/// it (a corpse walked up to, a reconnect).
		/// </summary>
		public void Die(bool already = false)
		{
			if (dead && !already)
			{
				return;
			}
			dead = true;
			deathTime = already ? Length(Dying) : 0f;
			rollTime = -1f;
			armsTime = -1f;
			blocking = false;
			sitting = false;
			if (already)
			{
				Array.Clear(weights, 0, Inputs);
				weights[Dying] = 1f;
			}
		}

		/// <summary>Rises again (revived, or a death that was mispredicted).</summary>
		public void Revive()
		{
			dead = false;
		}

		/// <summary>
		/// Sits down where it stands: on the ground, or, with <paramref name="seat"/>, on a seat that high (metres over its
		/// feet) whose front edge is <paramref name="back"/> metres behind it (its back already turned to it).
		/// </summary>
		public void SitDown(bool seat, float height, float back)
		{
			sitting = true;
			onSeat = seat;
			seatHeight = height;
			seatBack = back;
			armsTime = -1f;
		}

		/// <summary>Gets up again (at the sit's pace, from wherever the sit has got to).</summary>
		public void StandUp()
		{
			sitting = false;
		}

		/// <summary>Stood still where it was put down, so a jump across the map does not arrive running.</summary>
		public void Settle()
		{
			speed = 0f;
			airborne = 0f;
			lift = 0f;
			pitch = 0f;
			armsTime = -1f;
			climbPhase = 0f;
			rollTime = -1f;
			sitting = false;
			seatOffset = Vector3.zero;
			surfaceShare = 0f;
			tiltPitch = 0f;
			tiltRoll = 0f;
			Array.Clear(weights, 0, Inputs);
			weights[dead ? Dying : Idle] = 1f;
		}

		private bool ArmsFree() => !dead && !swimming && !climbing && !sitting && rollTime < 0f;

		private void StartArms(ArmsAction action, int input)
		{
			armsAction = action;
			armsInput = input;
			armsTime = 0f;
			armsLength = Mathf.Max(0.1f, armClips[input].GetAnimationClip().length);
		}

		// ── Each frame ──

		/// <summary>One frame of the character's motion: picks and blends the clips. Call before the animation updates (Update).</summary>
		public void Pose(in CharacterMotion motion, float deltaTime)
		{
			if (!graph.IsValid() || deltaTime <= 0f)
			{
				return;
			}
			bool slow = dead || sitting || weights[SitGround] + weights[SitSeat] + weights[Dying] > 0.02f;
			float follow = 1f - Mathf.Exp(-(slow ? SlowRate : BlendRate) * deltaTime);
			if (dead && deathTime < Length(Dying))
			{
				// The fall itself is a gait's pace; only rising from it is slow.
				follow = 1f - Mathf.Exp(-BlendRate * deltaTime);
			}
			Vector3 velocity = motion.Velocity;
			swimming = motion.Swimming;
			climbing = motion.Climbing;
			grounded = motion.Grounded;
			crouching = motion.Crouching;
			float flat = new Vector2(velocity.x, velocity.z).magnitude;
			float measure = climbing ? Mathf.Abs(velocity.y) : swimming ? velocity.magnitude : flat;
			speed = Mathf.Lerp(speed, measure, follow);

			stillWater = float.NaN;
			if (swimming)
			{
				stillWater = motion.StillWater;
				if (float.IsNaN(stillWater))
				{
					Vector3 at = Feet;
					stillWater = SurfaceWater.TryGetSurfaceAt(at.x, at.z, out float level) ? level : float.NaN;
				}
			}
			float targetShare = swimming ? SwimPose.SurfaceShare(stillWater, Feet.y, CapsuleHeight) : 0f;
			surfaceShare = Mathf.Lerp(surfaceShare, targetShare, follow);

			float swimPitch = 0f;
			float stroke = SwimPose.Stroke(speed);
			if (swimming && !dead)
			{
				float underPitch = stroke * SwimPose.AlongSwim(SwimPose.Elevation(velocity));
				swimPitch = SwimPose.Lean(surfaceShare, SwimPose.Stroke(flat), underPitch);
			}
			pitch = Mathf.Lerp(pitch, swimPitch, 1f - Mathf.Exp(-0.5f * BlendRate * deltaTime));
			airborne = grounded || swimming || climbing ? 0f : airborne + deltaTime;
			if (sitting && (swimming || climbing || airborne > AirDelay))
			{
				sitting = false;
			}

			Array.Clear(targets, 0, Inputs);
			if (dead)
			{
				targets[Dying] = 1f;
				deathTime = Mathf.Min(deathTime + deltaTime, Length(Dying));
				clips[Dying].SetTime(deathTime);
			}
			else if (rollTime >= 0f)
			{
				rollTime += deltaTime;
				if (rollTime >= Length(Rolling))
				{
					rollTime = -1f;
				}
				else
				{
					targets[Rolling] = 1f;
					clips[Rolling].SetTime(rollTime);
				}
			}
			if (targets[Dying] == 0f && targets[Rolling] == 0f)
			{
				Locomote(velocity, flat, stroke, deltaTime);
			}

			// Every weight eases toward its state's, and together they always come to one.
			float sum = 0f;
			for (int i = 0; i < Inputs; i++)
			{
				// A one-shot comes in at once and goes out at the gait's pace.
				float rate = (i == Rolling && targets[i] > weights[i]) ? 1f - Mathf.Exp(-4f * BlendRate * deltaTime) : follow;
				weights[i] = Mathf.Lerp(weights[i], targets[i], rate);
				sum += weights[i];
			}
			for (int i = 0; i < Inputs; i++)
			{
				mixer.SetInputWeight(i, sum > 1e-5f ? weights[i] / sum : (i == Idle ? 1f : 0f));
			}
			Vector3 seatTarget = sitting && onSeat ? new Vector3(0f, 0f, -(seatBack + SeatInset * SeatScale)) : Vector3.zero;
			seatOffset = Vector3.Lerp(seatOffset, seatTarget, follow);
			ArmsStep(deltaTime);
		}

		private float SeatScale => set.KeyedHeight > 0f ? restHeight / set.KeyedHeight : 1f;

		private Vector3 Feet => shown.parent != null ? shown.parent.position : shown.position;

		private void Locomote(Vector3 velocity, float flat, float stroke, float deltaTime)
		{
			if (sitting)
			{
				targets[onSeat ? SitSeat : SitGround] = 1f;
			}
			else if (climbing)
			{
				// Stepped by the height climbed, so the hands keep to the rungs and a climber holding still holds still.
				targets[Climb] = 1f;
				climbPhase = Mathf.Repeat(climbPhase + speed * deltaTime / Mathf.Max(0.1f, set.ClimbCycleMetres * SeatScale), 1f);
				clips[Climb].SetTime(climbPhase * Length(Climb));
			}
			else if (swimming)
			{
				// Treading water, into the stroke as it gets going: head up at the surface, flat under it.
				targets[Tread] = 1f - stroke;
				targets[Surface] = stroke * surfaceShare;
				targets[Under] = stroke * (1f - surfaceShare);
				float rate = Mathf.Clamp(speed / Mathf.Max(0.1f, set.StrokePace), 0.6f, 1.6f);
				clips[Surface].SetSpeed(rate);
				clips[Under].SetSpeed(rate);
			}
			else if (airborne > AirDelay)
			{
				AirShares(velocity.y, out float up, out float top, out float down);
				targets[JumpUp] = up;
				targets[MidAir] = top;
				targets[Fall] = down;
				// The rise is held at its own climb, a still of the take-off rather than a replay of it.
				clips[JumpUp].SetTime(Length(JumpUp) * 0.5f);
			}
			else if (crouching)
			{
				float share = Mathf.Clamp01(speed / crouchWalkSpeed);
				targets[CrouchIdle] = 1f - share;
				targets[CrouchWalk] = share;
				clips[CrouchWalk].SetSpeed(Mathf.Clamp(speed / crouchWalkSpeed, 0.5f, 2f));
			}
			else
			{
				Gait(speed, walkSpeed, runSpeed, out float idle, out float walk, out float run, out float rate);
				targets[Idle] = idle;
				targets[Walk] = walk;
				targets[Run] = run;
				float running = run > 0f ? Mathf.Clamp01(run / Mathf.Max(1e-4f, run + walk)) : 0f;
				// One stride shared by both clips, its length the blend of theirs.
				phase = Mathf.Repeat(phase + deltaTime * rate / Mathf.Lerp(walkLength, runLength, running), 1f);
				clips[Walk].SetTime(phase * walkLength);
				clips[Run].SetTime(phase * runLength);
			}
		}

		/// <summary>
		/// How a ground speed splits between idle, walk and run, and how fast the run plays: idle into walk under the walk
		/// clip's own speed, walk into run up to the run clip's, then the run faster (to <see cref="MaxRunRate"/>).
		/// </summary>
		public static void Gait(float speed, float walkSpeed, float runSpeed, out float idle, out float walk, out float run, out float rate)
		{
			idle = walk = run = 0f;
			rate = 1f;
			if (speed < walkSpeed)
			{
				walk = Mathf.Clamp01(speed / Mathf.Max(1e-4f, walkSpeed));
				idle = 1f - walk;
			}
			else if (speed < runSpeed)
			{
				run = (speed - walkSpeed) / Mathf.Max(1e-4f, runSpeed - walkSpeed);
				walk = 1f - run;
			}
			else
			{
				run = 1f;
				rate = Mathf.Min(speed / Mathf.Max(1e-4f, runSpeed), MaxRunRate);
			}
		}

		/// <summary>How a body in the air splits between rising, the top and coming down, by its vertical speed (m/s).</summary>
		public static void AirShares(float verticalSpeed, out float up, out float top, out float down)
		{
			up = Mathf.Clamp01(verticalSpeed / 2f);
			down = Mathf.Clamp01(-verticalSpeed / 3f);
			top = Mathf.Max(0f, 1f - up - down);
		}

		private void ArmsStep(float deltaTime)
		{
			float target = 0f;
			if (!ArmsFree())
			{
				armsTime = -1f;
			}
			else if (armsTime >= 0f)
			{
				armsTime += deltaTime;
				if (armsTime >= armsLength)
				{
					armsTime = -1f;
				}
				else
				{
					SoloArms(armsInput);
					armClips[armsInput].SetTime(armsTime);
					// Up from the arms' own pose at once, and back into it over the last quarter.
					target = Mathf.Clamp01(armsTime / ArmsIn) * Mathf.Clamp01((armsLength - armsTime) / (armsLength * 0.25f));
					armsWeight = target;
					layers.SetInputWeight(1, armsWeight);
					return;
				}
			}
			if (blocking && ArmsFree() && hasArmClip[BlockArms])
			{
				SoloArms(BlockArms);
				AnimationClipPlayable guard = armClips[BlockArms];
				guard.SetTime(Mathf.Repeat((float)guard.GetTime() + deltaTime, Mathf.Max(0.1f, guard.GetAnimationClip().length)));
				target = 1f;
			}
			// The guard comes up and goes down at a gait's pace; a one-shot sets its own envelope above.
			armsWeight = Mathf.Lerp(armsWeight, target, 1f - Mathf.Exp(-BlendRate * deltaTime));
			layers.SetInputWeight(1, armsWeight);
		}

		private void SoloArms(int input)
		{
			for (int i = 0; i < ArmInputs; i++)
			{
				arms.SetInputWeight(i, i == input ? 1f : 0f);
			}
		}

		/// <summary>
		/// Sets the figure where it belongs, after the animation has posed it: its feet on the ground, its seat on its seat,
		/// its head over the waves. Call after the animation updates (LateUpdate).
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>On land,</b> placing the model by its bind pose is not enough: a humanoid clip keeps the hip height of the body
		/// it was captured on, the capsule hovers its skin width above what it stands on, and on a slope the capsule's lowest
		/// point is above the ground under its middle. So the lower of the two soles is found against the ground beneath it
		/// and the body eased up or down to meet it, while the figure is on screen. Sat on the ground the soles are on it too,
		/// and the same search seats it; on a seat it is raised or lowered by the seat's height against the one the clip was
		/// keyed for. In the air it holds where it was, so a landing does not pop.
		/// </para>
		/// <para>
		/// <b>In the water</b> the capsule floats on the still surface. At the surface the figure is set by its head instead:
		/// the head joint held <see cref="SwimPose.HeadAboveWater"/> over the drawn sea, so the water stands at its chin
		/// whether it treads or leans into its stroke, it heaves with every wave, and it tips with the wave's slope. Going
		/// under, it eases back onto the capsule, which the controller steers.
		/// </para>
		/// </remarks>
		public void Place(float deltaTime)
		{
			if (shown == null || deltaTime <= 0f)
			{
				return;
			}
			float ease = 1f - Mathf.Exp(-GroundRate * deltaTime);
			Transform parent = shown.parent;
			float baseY = Feet.y;
			if (sitting && onSeat)
			{
				lift = Mathf.Lerp(lift, seatHeight - KeyedSeatHeight, 1f - Mathf.Exp(-SlowRate * deltaTime));
			}
			else if (dead)
			{
				// Its death was keyed lying on the ground it fell on.
				lift = Mathf.Lerp(lift, 0f, ease);
			}
			else if (grounded && feet[0] != null && feet[1] != null && Visible())
			{
				PhysicsScene physics = shown.gameObject.scene.GetPhysicsScene();
				float gap = float.MaxValue;
				for (int i = 0; i < 2; i++)
				{
					Vector3 foot = feet[i].position;
					float sole = foot.y - ankleRest[i];
					if (toes[i] != null)
					{
						sole = Mathf.Min(sole, toes[i].position.y - toeRest[i]);
					}
					if (physics.Raycast(new Vector3(foot.x, sole + GroundReach, foot.z), Vector3.down, out RaycastHit hit, GroundReach * 2f, groundMask, QueryTriggerInteraction.Ignore))
					{
						gap = Mathf.Min(gap, sole - hit.point.y);
					}
				}
				if (gap < float.MaxValue)
				{
					lift = Mathf.Lerp(lift, Mathf.Clamp(lift - gap, -MaxLift, MaxLift), ease);
				}
			}

			Vector3 pivot = Vector3.up * SwimPose.PivotMetres;
			if (swimming || climbing)
			{
				// The wave under the figure: its slope tips it, its height sets its head.
				float wave = 0f;
				float slopePitch = 0f;
				float slopeRoll = 0f;
				Vector3 forward = parent != null ? parent.forward : shown.forward;
				Vector3 right = parent != null ? parent.right : shown.right;
				if (swimming && surfaceShare > 0.01f)
				{
					Vector3 at = head != null ? head.position : Feet + Vector3.up * restHeight;
					wave = SurfaceWater.WaveOffsetAt(at.x, at.z);
					Vector3 chest = Feet + forward * 0.3f;
					float ahead = SurfaceWater.WaveOffsetAt(chest.x + forward.x * 0.6f, chest.z + forward.z * 0.6f);
					float behind = SurfaceWater.WaveOffsetAt(chest.x - forward.x * 0.6f, chest.z - forward.z * 0.6f);
					float toRight = SurfaceWater.WaveOffsetAt(chest.x + right.x * 0.35f, chest.z + right.z * 0.35f);
					float toLeft = SurfaceWater.WaveOffsetAt(chest.x - right.x * 0.35f, chest.z - right.z * 0.35f);
					SwimPose.WaveTilt(ahead, behind, 1.2f, toRight, toLeft, 0.7f, surfaceShare, out slopePitch, out slopeRoll);
				}
				float waveEase = 1f - Mathf.Exp(-WaveRate * deltaTime);
				tiltPitch = Mathf.Lerp(tiltPitch, slopePitch, waveEase);
				tiltRoll = Mathf.Lerp(tiltRoll, slopeRoll, waveEase);

				float target = 0f;
				if (swimming && !float.IsNaN(stillWater) && head != null && surfaceShare > 0.01f)
				{
					// Where the head joint would be with the figure turned as it is about to be and not lifted, against where
					// the water wants it.
					Quaternion turnTo = Quaternion.Euler(tiltPitch, 0f, tiltRoll) * Quaternion.Euler(pitch, 0f, 0f);
					Vector3 inFigure = Vector3.Scale(shown.localScale, shown.InverseTransformPoint(head.position));
					float headAt = baseY + (shownRest + pivot - turnTo * pivot + turnTo * (shownRestRotation * inFigure)).y;
					target = Mathf.Clamp(SwimPose.HeadLift(headAt, stillWater, wave, surfaceShare), -2f * MaxLift, 2f * MaxLift + Mathf.Abs(wave));
				}
				lift = Mathf.Lerp(lift, target, swimming ? waveEase : ease);
			}
			else
			{
				tiltPitch = Mathf.Lerp(tiltPitch, 0f, ease);
				tiltRoll = Mathf.Lerp(tiltRoll, 0f, ease);
			}
			// Turned about the chest, not the soles, so lying along a swim keeps it in the water.
			Quaternion turn = Quaternion.Euler(tiltPitch, 0f, tiltRoll) * Quaternion.Euler(pitch, 0f, 0f);
			shown.localRotation = turn * shownRestRotation;
			shown.localPosition = shownRest + seatOffset + Vector3.up * lift + pivot - turn * pivot;
		}

		private bool Visible()
		{
			for (int i = 0; i < renderers.Length; i++)
			{
				if (renderers[i] != null && renderers[i].isVisible)
				{
					return true;
				}
			}
			return false;
		}

		// ── The model ──

		/// <summary>
		/// A bone by the avatar's own map of it (its human description), not by <see cref="Animator.GetBoneTransform"/>:
		/// that answers null until the Animator is initialised, which one with no controller and no graph yet is not.
		/// </summary>
		private Transform Bone(HumanBodyBones bone)
		{
			string human = HumanTrait.BoneName[(int)bone];
			foreach (HumanBone mapped in animator.avatar.humanDescription.human)
			{
				if (mapped.humanName == human)
				{
					Transform found = Named(shown, mapped.boneName);
					if (found != null)
					{
						return found;
					}
				}
			}
			return animator.isInitialized ? animator.GetBoneTransform(bone) : null;
		}

		private static Transform Named(Transform parent, string name)
		{
			if (parent.name == name)
			{
				return parent;
			}
			for (int i = 0; i < parent.childCount; i++)
			{
				Transform found = Named(parent.GetChild(i), name);
				if (found != null)
				{
					return found;
				}
			}
			return null;
		}

		/// <summary>
		/// The lowest and highest world height of a figure at rest: each mesh's own bounds (its vertices as modelled), placed
		/// by its renderer's transform. Never the renderers' bounds: the importer sizes a skinned renderer's bounds to hold
		/// every clip in its file (arms overhead, a roll, lying dead).
		/// </summary>
		public static bool RestExtent(Transform shape, out float bottom, out float top)
		{
			bottom = float.MaxValue;
			top = float.MinValue;
			foreach (Renderer renderer in shape.GetComponentsInChildren<Renderer>())
			{
				Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh
					: renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
				if (mesh == null)
				{
					continue;
				}
				Bounds local = mesh.bounds;
				Matrix4x4 toWorld = renderer.transform.localToWorldMatrix;
				for (int corner = 0; corner < 8; corner++)
				{
					Vector3 point = local.center + Vector3.Scale(local.extents,
						new Vector3((corner & 1) != 0 ? 1f : -1f, (corner & 2) != 0 ? 1f : -1f, (corner & 4) != 0 ? 1f : -1f));
					float y = toWorld.MultiplyPoint3x4(point).y;
					bottom = Mathf.Min(bottom, y);
					top = Mathf.Max(top, y);
				}
			}
			return bottom < top;
		}

		/// <summary>
		/// The ground speed a clip was captured at, in metres per second on this body. A humanoid clip's root travel is
		/// stored for a body of human scale one, so it is scaled by this one's.
		/// </summary>
		private static float NaturalSpeed(AnimationClip clip, float humanScale, float fallback)
		{
			if (clip == null)
			{
				return fallback;
			}
			Vector3 average = clip.averageSpeed;
			float flat = new Vector2(average.x, average.z).magnitude * humanScale;
			return flat > 0.1f ? flat : fallback;
		}
	}
}
