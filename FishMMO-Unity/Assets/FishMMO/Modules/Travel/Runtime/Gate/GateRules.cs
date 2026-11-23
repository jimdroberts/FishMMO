using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>Who may step through a gate (issue #241). Chosen by the Gate spell's template; default <see cref="Anyone"/>.</summary>
	public enum GateAccess : byte
	{
		/// <summary>Anyone who walks into it.</summary>
		Anyone = 0,

		/// <summary>The caster and the members of the caster's party or guild.</summary>
		PartyAndGuild = 1,

		/// <summary>The caster and the members of the caster's party.</summary>
		Party = 2,

		/// <summary>The caster alone.</summary>
		CasterOnly = 3,
	}

	/// <summary>Why a Gate Stone choice did not open a gate.</summary>
	public enum GateChoiceRefusal : byte
	{
		None = 0,

		/// <summary>No Gate is waiting for a choice (it timed out, was cancelled, or was never cast).</summary>
		NotChoosing = 1,

		/// <summary>The scene or the stone is not in this build's world.</summary>
		UnknownStone = 2,

		/// <summary>The waypoint chosen is a fast-travel point, not a Gate Stone.</summary>
		NotAGateStone = 3,

		/// <summary>The caster has not discovered the stone.</summary>
		NotDiscovered = 4,

		/// <summary>Dead, incapacitated, unloaded or already teleporting.</summary>
		CannotAct = 5,

		/// <summary>The caster now stands in an arena, an instance or a <see cref="NoTravelZone"/>.</summary>
		CannotOpenHere = 6,
	}

	/// <summary>
	/// The Gate spell's rules (issue #241), as pure functions: who may enter, where the portal stands, where travellers land, and
	/// whether a Gate Stone choice opens a gate.
	/// </summary>
	public static class GateRules
	{
		/// <summary>Shortest and longest a gate may stay open, in seconds.</summary>
		public const float MinLifetimeSeconds = 5.0f, MaxLifetimeSeconds = 600.0f;

		/// <summary>Shortest and longest the caster may take to choose a stone, in seconds.</summary>
		public const float MinChooseSeconds = 5.0f, MaxChooseSeconds = 300.0f;

		/// <summary>Bounds on the portal's walk-in radius, in metres.</summary>
		public const float MinRadius = 0.5f, MaxRadius = 3.0f;

		/// <summary>How far above or below the portal's foot a traveller still walks into it, in metres.</summary>
		public const float EntryHeight = 2.5f;

		/// <summary>How many arrival spots surround a Gate Stone's arrival point (the point itself is the first).</summary>
		public const int ArrivalSpots = 7;

		/// <summary>The ring the spots after the first stand on, in metres from the arrival point.</summary>
		public const float ArrivalRingMetres = 1.6f;

		public static float ClampLifetime(float seconds) => Mathf.Clamp(float.IsNaN(seconds) ? 30.0f : seconds, MinLifetimeSeconds, MaxLifetimeSeconds);

		public static float ClampChoose(float seconds) => Mathf.Clamp(float.IsNaN(seconds) ? 60.0f : seconds, MinChooseSeconds, MaxChooseSeconds);

		public static float ClampRadius(float metres) => Mathf.Clamp(float.IsNaN(metres) ? 1.2f : metres, MinRadius, MaxRadius);

		/// <summary>Whether a character may step through a gate of <paramref name="access"/>.</summary>
		public static bool CanEnter(GateAccess access, bool isCaster, bool sameParty, bool sameGuild)
		{
			if (isCaster)
			{
				return true;
			}
			switch (access)
			{
				case GateAccess.Anyone:
					return true;
				case GateAccess.PartyAndGuild:
					return sameParty || sameGuild;
				case GateAccess.Party:
					return sameParty;
				default:
					return false;
			}
		}

		/// <summary>Where the portal stands: <paramref name="forward"/> metres ahead of the caster, facing the caster.</summary>
		public static Vector3 PortalPosition(Vector3 caster, float casterYaw, float forward)
		{
			return caster + Heading(casterYaw) * Mathf.Max(0.0f, forward);
		}

		/// <summary>Whether <paramref name="point"/> stands inside the portal's walk-in cylinder.</summary>
		public static bool IsInside(Vector3 portal, float radius, Vector3 point)
		{
			float dx = point.x - portal.x;
			float dz = point.z - portal.z;
			return dx * dx + dz * dz <= radius * radius && Mathf.Abs(point.y - portal.y) <= EntryHeight;
		}

		/// <summary>
		/// The <paramref name="slot"/>th spot around a stone's arrival point: the point itself, then a ring, so a party arriving
		/// together does not stand in one another. Only spreads people out; nothing checks a spot.
		/// </summary>
		public static Vector3 ArrivalSpot(Vector3 arrival, float yaw, int slot)
		{
			int s = ((slot % ArrivalSpots) + ArrivalSpots) % ArrivalSpots;
			if (s == 0)
			{
				return arrival;
			}
			float angle = yaw + (s - 1) * (360.0f / (ArrivalSpots - 1));
			return arrival + Heading(angle) * ArrivalRingMetres;
		}

		/// <summary>The unit vector along the ground a yaw (degrees about up, 0 = +z) faces: Unity's forward, in plain maths.</summary>
		public static Vector3 Heading(float yawDegrees)
		{
			double radians = yawDegrees * (Math.PI / 180.0);
			return new Vector3((float)Math.Sin(radians), 0.0f, (float)Math.Cos(radians));
		}

		/// <summary>The first rule a Gate Stone choice fails, in order, or <see cref="GateChoiceRefusal.None"/>.</summary>
		public static GateChoiceRefusal DecideChoice(bool isChoosing, bool stoneKnown, bool isGateStone, bool discovered,
			bool canAct, bool canOpenHere)
		{
			if (!isChoosing)
			{
				return GateChoiceRefusal.NotChoosing;
			}
			if (!stoneKnown)
			{
				return GateChoiceRefusal.UnknownStone;
			}
			if (!isGateStone)
			{
				return GateChoiceRefusal.NotAGateStone;
			}
			if (!discovered)
			{
				return GateChoiceRefusal.NotDiscovered;
			}
			if (!canAct)
			{
				return GateChoiceRefusal.CannotAct;
			}
			if (!canOpenHere)
			{
				return GateChoiceRefusal.CannotOpenHere;
			}
			return GateChoiceRefusal.None;
		}

		/// <summary>What the caster is told for a refused choice, or null for none.</summary>
		public static string Describe(GateChoiceRefusal refusal)
		{
			switch (refusal)
			{
				case GateChoiceRefusal.NotChoosing:
					return "The Gate spell is no longer waiting for a destination.";
				case GateChoiceRefusal.UnknownStone:
					return "That Gate Stone is not part of this world.";
				case GateChoiceRefusal.NotAGateStone:
					return "Gate opens only to a Gate Stone.";
				case GateChoiceRefusal.NotDiscovered:
					return "You have not discovered that Gate Stone.";
				case GateChoiceRefusal.CannotOpenHere:
					return "A gate cannot be opened here.";
				default:
					return null;
			}
		}

		/// <summary>
		/// The channel to ask for when a traveller goes to a stone in <paramref name="stoneScene"/>: the traveller's own when it is
		/// the open-world scene it stands in (a local move, no loading screen), any otherwise (0).
		/// </summary>
		public static long StoneChannel(string currentScene, long currentSceneID, bool inInstance, string stoneScene)
		{
			return !inInstance && string.Equals(currentScene, stoneScene, StringComparison.Ordinal) ? currentSceneID : 0L;
		}
	}
}
