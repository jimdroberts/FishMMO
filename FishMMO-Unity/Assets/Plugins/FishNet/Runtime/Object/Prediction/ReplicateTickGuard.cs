namespace FishNet.Object.Prediction
{
    /* FISHMMO EDIT: a bound on how far the tick an owner stamps on its replicates may run ahead.
     *
     * The tick on a replicate is the owner's own counter, and everything a game times against the replicate tick (a
     * cooldown, the length of a debuff, a regeneration pulse) runs on it. FishNet only refuses a tick that is not newer
     * than the last one read. An owner that stamps each replicate far ahead of the last fast-forwards all of those timers
     * on the server, and both peers agree, so nothing corrects it.
     *
     * An honest owner's counter runs at real time. Two facts follow, and the guard is built on nothing else:
     *
     *   1. FROM THE START. The counter cannot have advanced further since the first input than real time has since the
     *      server gave the owner the object (Begin). This is the rule while the stream settles (SettleSeconds after the
     *      first read), and it is what makes world entry safe: the client's first frames after loading are long, it
     *      catches their ticks up at once, and the first input read is the OLDEST of that burst. Any rule anchored on
     *      that first input refused everything newer than it by more than the window, and the owner stood at the spawn
     *      point, unable to move, pulled back there by every correction, for as long as the burst was over the window
     *      times fifty. This one never refuses a burst: its span is real time the owner really spent.
     *   2. AFTER. The OFFSET (an input's tick less the server tick it is read on) is close to constant. It goes DOWN for
     *      every input read late (a delayed packet, the old end of a burst, an owner whose client froze and caught its
     *      ticks up) and that is never refused: a counter run slow only makes a character move and act slower. It goes
     *      UP only by small amounts (the connection getting faster, the owner's timing nudging its clock), or by the
     *      length of a server stall whose backlog is read before the server's own ticks catch up. The reference is the
     *      HIGHEST offset seen (the settling period's, then rising by one tick each CreepIntervalTicks, never falling:
     *      a reference that followed a long spell of high latency down refused the owner when its latency recovered),
     *      and an input more than the window above it is refused. A counter can be run ahead by the window once, and
     *      by that rate after it; a server stall longer than the window loses the newest part of its backlog, once.
     *
     * Pure: a struct with no clock of its own, so it is tested on its own. */
    public struct ReplicateTickGuard
    {
        /// <summary>How far above the reference a replicate's offset may be, in seconds of ticks.</summary>
        public const int WindowSeconds = 1;
        /// <summary>Server ticks for the reference to move one tick toward what the owner reports: the sustained rate a counter may be run fast (2%).</summary>
        public const uint CreepIntervalTicks = 50;
        /// <summary>How long after the first read the stream is held only to rule 1 (real time since Begin), in seconds of ticks.</summary>
        public const int SettleSeconds = 1;
        /// <summary>Replicates refused since the server started, for diagnostics.</summary>
        public static long Refused;

        private bool _begun;
        /// <summary>True when Begin was told the tick the owner got the object; false when the first read stands in for it.</summary>
        private bool _begunExplicitly;
        /// <summary>The server tick the owner was given the object on (or of the first read, when nobody said).</summary>
        private uint _beginTick;
        private bool _set;
        private uint _firstInputTick;
        private uint _firstReadTick;
        private bool _settled;
        /// <summary>The reference: an input's tick less the server tick it was read on.</summary>
        private long _offset;
        /// <summary>The server tick the reference was last moved on.</summary>
        private uint _creepTick;

        /// <summary>The reference offset, for tests and diagnostics.</summary>
        public long Offset => _offset;

        /// <summary>Forgets the owner: the next replicate starts a new stream.</summary>
        public void Reset()
        {
            this = default;
        }

        /// <summary>The owner was given the object on server tick <paramref name = "serverTick"/>: its counter starts no earlier.</summary>
        public void Begin(uint serverTick)
        {
            this = default;
            _begun = true;
            _begunExplicitly = true;
            _beginTick = serverTick;
        }

        /// <summary>
        /// True when a replicate stamped <paramref name = "inputTick"/>, read on <paramref name = "serverTick"/>, may be queued.
        /// </summary>
        /// <param name = "windowTicks">How far ahead of what rules 1 and 2 allow an input may be, in ticks.</param>
        /// <param name = "creepIntervalTicks">Server ticks for the reference to move one tick; 0 to never move it.</param>
        /// <param name = "settleTicks">How long after the first read only rule 1 applies.</param>
        public bool Accept(uint inputTick, uint serverTick, uint windowTicks, uint creepIntervalTicks, uint settleTicks)
        {
            long offset = (long)inputTick - serverTick;
            if (!_set)
            {
                _set = true;
                if (!_begun)
                {
                    _begun = true;
                    _beginTick = serverTick;
                }
                _firstInputTick = inputTick;
                _firstReadTick = serverTick;
                _offset = offset;
                _creepTick = serverTick;
                return true;
            }

            if (!_settled)
            {
                /* Nobody said when the owner got the object, so the first read stands in for it: everything read on that same
                 * tick is one burst, and the newest of it is the owner's now. */
                if (!_begunExplicitly && serverTick == _firstReadTick)
                {
                    if (offset > _offset)
                        _offset = offset;
                    // Rule 1 then runs from the newest of the burst: the owner's counter as it stood when the read began.
                    if (unchecked((int)(inputTick - _firstInputTick)) > 0)
                        _firstInputTick = inputTick;
                    return true;
                }
                if (unchecked(serverTick - _firstReadTick) < settleTicks)
                {
                    // Rule 1: no further ahead of the first input than real time since Begin, and the window.
                    long advanced = unchecked((int)(inputTick - _firstInputTick));
                    long elapsed = unchecked((int)(serverTick - _beginTick));
                    if (elapsed < 0)
                        elapsed = 0;
                    if (advanced > elapsed + windowTicks)
                        return false;
                    if (offset > _offset)
                        _offset = offset;
                    return true;
                }
                _settled = true;
                _creepTick = serverTick;
            }

            // Rule 2: the reference rises toward what the owner reports, slowly, and never falls.
            if (creepIntervalTicks > 0)
            {
                uint steps = unchecked(serverTick - _creepTick) / creepIntervalTicks;
                if (steps > 0)
                {
                    _creepTick += steps * creepIntervalTicks;
                    long difference = offset - _offset;
                    if (difference > 0)
                        _offset += difference > steps ? steps : difference;
                }
            }

            return offset - _offset <= windowTicks;
        }
    }
}
