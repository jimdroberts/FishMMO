using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// The kind of event a guild log row records.
	/// </summary>
	/// <remarks>
	/// Stored as a small integer and rendered client-side rather than persisted as a sentence.
	/// A log written as prose cannot be filtered, cannot be re-translated, and bakes today's
	/// wording into rows that outlive it. The two character IDs beside it are the actor and the
	/// subject, either of which may be zero when the event has no such party.
	/// </remarks>
	public enum GuildLogEventType : byte
	{
		/// <summary>Unrecognised — rendered as a plain line rather than dropped.</summary>
		Unknown = 0,
		/// <summary>The guild was created. Actor: founder.</summary>
		Created = 1,
		/// <summary>A member joined. Actor: the member.</summary>
		Joined = 2,
		/// <summary>A member left of their own accord. Actor: the member.</summary>
		Left = 3,
		/// <summary>A member was removed. Actor: remover. Target: removed.</summary>
		Kicked = 4,
		/// <summary>A member was promoted. Actor: promoter. Target: promoted.</summary>
		Promoted = 5,
		/// <summary>A member was demoted. Actor: demoter. Target: demoted.</summary>
		Demoted = 6,
		/// <summary>Leadership was transferred. Actor: outgoing leader. Target: new leader.</summary>
		LeadershipTransferred = 7,
		/// <summary>The message of the day was changed. Actor: editor.</summary>
		MessageOfTheDayChanged = 8,
		/// <summary>The notice was changed. Actor: editor.</summary>
		NoticeChanged = 9,
		/// <summary>A rank's name or permissions were edited. Actor: editor. Detail: rank name.</summary>
		RankEdited = 10,
		/// <summary>A rank was created. Actor: creator. Detail: rank name.</summary>
		RankCreated = 11,
		/// <summary>A rank was deleted. Actor: deleter. Detail: rank name.</summary>
		RankDeleted = 12,
		/// <summary>The recruitment advertisement changed. Actor: editor.</summary>
		RecruitmentChanged = 13,
		/// <summary>An application was accepted. Actor: officer. Target: applicant.</summary>
		ApplicationAccepted = 14,
		/// <summary>An application was declined. Actor: officer. Target: applicant.</summary>
		ApplicationDeclined = 15,
		/// <summary>A member note was edited. Actor: editor. Target: subject.</summary>
		NoteChanged = 16,
	}

	/// <summary>
	/// One guild activity log row.
	/// </summary>
	public struct GuildLogData
	{
		/// <summary>Primary key. Zero for a row that has not been written yet.</summary>
		public readonly long ID;

		/// <summary>The guild the event belongs to.</summary>
		public readonly long GuildID;

		/// <summary>What happened.</summary>
		public readonly GuildLogEventType EventType;

		/// <summary>The character who performed the action, or zero.</summary>
		public readonly long ActorCharacterID;

		/// <summary>The character the action was performed on, or zero.</summary>
		public readonly long TargetCharacterID;

		/// <summary>Optional short detail, such as a rank name. May be empty.</summary>
		public readonly string Detail;

		/// <summary>When the event happened (UTC).</summary>
		public readonly DateTime TimeCreated;

		/// <summary>
		/// Initializes a new guild log row.
		/// </summary>
		/// <param name="id">Primary key, or zero when not yet written.</param>
		/// <param name="guildID">The guild the event belongs to.</param>
		/// <param name="eventType">What happened.</param>
		/// <param name="actorCharacterID">The acting character, or zero.</param>
		/// <param name="targetCharacterID">The subject character, or zero.</param>
		/// <param name="detail">Optional short detail.</param>
		/// <param name="timeCreated">When the event happened (UTC).</param>
		public GuildLogData(long id, long guildID, GuildLogEventType eventType, long actorCharacterID, long targetCharacterID, string detail, DateTime timeCreated)
		{
			ID = id;
			GuildID = guildID;
			EventType = eventType;
			ActorCharacterID = actorCharacterID;
			TargetCharacterID = targetCharacterID;
			Detail = detail;
			TimeCreated = timeCreated;
		}
	}
}
