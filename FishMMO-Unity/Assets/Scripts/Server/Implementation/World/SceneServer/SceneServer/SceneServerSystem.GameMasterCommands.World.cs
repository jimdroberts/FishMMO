using System.Collections.Generic;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// <c>/gm weather</c> and <c>/gm clock</c>: read-only views of the world.
	/// </summary>
	/// <remarks>
	/// Weather changes gameplay, so a game master may look but not touch; the changing commands are
	/// <c>/admin weather</c> and <c>/admin climate</c>. Nothing, for anyone, sets the clock.
	/// </remarks>
	public partial class SceneServerSystem
	{
		/// <summary>The world rows of the <c>/gm</c> table.</summary>
		private IEnumerable<OperatorCommand> BuildGameMasterWorldCommands()
		{
			return new List<OperatorCommand>
			{
				new OperatorCommand
				{
					Name = "weather", Category = "World",
					Summary = "Reports this scene's weather: layers, nearby storm cells and the weather where you stand.",
					Run = (c, a) => ReportWeather(c),
				},
				new OperatorCommand
				{
					Name = "clock", Category = "World",
					Summary = "Reports the world clock, the date and your local time. Nothing can set it.",
					Run = (c, a) => ReportClock(c),
				},
			};
		}
	}
}
