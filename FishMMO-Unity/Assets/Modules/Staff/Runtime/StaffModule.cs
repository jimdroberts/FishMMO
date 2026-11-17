using FishMMO.Core;

[assembly: GameModule(typeof(FishMMO.Shared.StaffModule))]

namespace FishMMO.Shared
{
	/// <summary>
	/// The Staff and Support module: the <c>/gm</c> and <c>/admin</c> command sets, the staff console, game master
	/// presence, and the player support commands and report window. See the module's README.
	/// </summary>
	public sealed class StaffModule : GameModule
	{
		/// <summary>The module's id.</summary>
		public const string ModuleId = "staff";

		/// <inheritdoc />
		public override string Id => ModuleId;

		/// <inheritdoc />
		public override void Install(ModuleBuilder builder)
		{
		}
	}
}
