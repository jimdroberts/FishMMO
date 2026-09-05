using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Plot access grant data transfer object.
	/// </summary>
	/// <remarks>
	/// Carries the permission mask as the integer the row stores rather than as a resolved set,
	/// because this assembly cannot reference the shared types where <c>PlotPermission</c> lives.
	/// Callers mask it through <c>FishMMO.Shared.PlotAccess.Sanitize</c>, which is also where a bit
	/// this build does not recognise is dropped.
	/// </remarks>
	public struct PlotAccessData
	{
		/// <summary>Primary key.</summary>
		public readonly long ID;

		/// <summary>The plot the grant is about.</summary>
		public readonly long PlotID;

		/// <summary>The character granted access.</summary>
		public readonly long CharacterID;

		/// <summary>What they may do, as a permission bitmask.</summary>
		public readonly int Permissions;

		/// <summary>Who granted it.</summary>
		public readonly long GrantedByCharacterID;

		/// <summary>When the grant was made or last changed (UTC).</summary>
		public readonly DateTime TimeGranted;

		public PlotAccessData(long id, long plotID, long characterID, int permissions, long grantedByCharacterID, DateTime timeGranted)
		{
			ID = id;
			PlotID = plotID;
			CharacterID = characterID;
			Permissions = permissions;
			GrantedByCharacterID = grantedByCharacterID;
			TimeGranted = timeGranted;
		}
	}
}
