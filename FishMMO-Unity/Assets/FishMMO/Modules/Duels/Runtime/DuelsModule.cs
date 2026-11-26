using FishMMO.Core;

[assembly: GameModule(typeof(FishMMO.Shared.DuelsModule))]

namespace FishMMO.Shared
{
	/// <summary>
	/// The Duels module (issue #250): one-on-one fights in the open world. A player challenges another from the context
	/// menu; on acceptance both are set down facing each other inside a small duel ground, a countdown runs, and they
	/// fight until one is beaten (left on their last point of health, never killed), steps out of the ground, yields or
	/// leaves. Results feed the duel rating and the Duel Rating and Duel Wins leaderboards. See the module's README.
	/// </summary>
	public sealed class DuelsModule : GameModule
	{
		/// <summary>The module's id.</summary>
		public const string ModuleId = "duels";

		/// <inheritdoc />
		public override string Id => ModuleId;

		/// <inheritdoc />
		public override void Install(ModuleBuilder builder)
		{
		}
	}
}
