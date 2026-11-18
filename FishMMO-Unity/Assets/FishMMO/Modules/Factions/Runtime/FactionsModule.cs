using FishMMO.Core;

[assembly: GameModule(typeof(FishMMO.Shared.FactionsModule))]
// Every character (players, NPCs, pets, wildlife) carries its standings, added at wake rather than authored on prefabs.
[assembly: FishMMO.Shared.CharacterPart(typeof(FishMMO.Shared.FactionController), typeof(FishMMO.Shared.BaseCharacter))]

namespace FishMMO.Shared
{
	/// <summary>
	/// The Factions module (Phase 2): reputation with factions, the alliances it implies, and the aggression toggle.
	/// </summary>
	/// <remarks>
	/// <para>Here: <see cref="FactionController"/> (a character part implementing the core <c>IFactionController</c> and this
	/// module's <see cref="IFactionStandings"/>), the faction templates and matrix (with each race's starting faction, see
	/// <see cref="FactionTemplate.StartingRaces"/>), the broadcasts, the faction ECA pieces, the harm relation
	/// (<see cref="FactionHarmRelation"/>), the scene server's <c>FactionSystem</c>, the save participant over
	/// <c>character_factions</c>, the starting standings at character creation, and the Factions window.</para>
	/// <para>Without it: no standings, the harm rule falls to its other relations (arena, wildlife, companions) and otherwise
	/// allows, and AI sees no enemies but the aggressive NPCs. The <c>character_factions</c> table and its rows stay.</para>
	/// </remarks>
	public sealed class FactionsModule : GameModule
	{
		public const string ModuleId = "factions";

		public override string Id => ModuleId;

		public override void Install(ModuleBuilder builder)
		{
		}
	}
}
