namespace LastWard.UI
{
    /// <summary>
    /// What the player reads while the session is being created.
    ///
    /// This is the only teaching surface the game has. There is no tutorial, and several of its
    /// verbs are not guessable — that hiding, using and swinging are three different keys, that the
    /// torch is what damns you on the first floor, that reading a note can cost you your life on the
    /// second. A player who never finds those out is not playing the game as designed, and the wait
    /// for a relay session is the one moment they are guaranteed to be sitting still and reading.
    ///
    /// Four registers, deliberately mixed so the screen never reads as a manual:
    ///
    ///   <b>Tip</b> — a mechanic, stated plainly. Every one of these must be TRUE. A loading screen
    ///   that lies about a binding is worse than a blank one, so if PlayerControls.inputactions
    ///   changes, this file changes with it.
    ///   <b>The Building</b> — what the hospital is, told the way the hospital would tell it.
    ///   <b>Recovered</b> — verbatim fragments of paperwork found in the level. These are drawn from
    ///   notes that genuinely exist in the game, so a player who reads here and then finds the full
    ///   page later gets the click of recognition rather than a contradiction.
    ///   <b>Standing Order</b> — the staff rules. The building's own voice, at its most bureaucratic
    ///   and therefore its worst.
    ///
    /// The three entities are never named and never described physically, here or anywhere else.
    /// That rule is the whole reason they work.
    /// </summary>
    public static class LoadingLore
    {
        public readonly struct Card
        {
            public readonly string Category;
            public readonly string Body;
            public Card(string category, string body) { Category = category; Body = body; }
        }

        public static readonly Card[] Cards =
        {
            // ---------------------------------------------------------------- how it is played
            new Card("TIP",
                "Your torch has five bars, and burning them is the least of what it costs. On the " +
                "first floor, light is the thing it is drawn to. It does not have to see you."),

            new Card("TIP",
                "<b>V</b> holds your breath. Standing still, torch off, breath held is the fastest " +
                "you will ever calm down — and the least you can do about anything else."),

            new Card("TIP",
                "Three verbs, three keys. <b>E</b> uses, <b>Q</b> hides, <b>Left Mouse</b> swings. " +
                "Reaching for the wrong one in the dark is how people die in doorways."),

            new Card("TIP",
                "A weapon is one swing. It buys you a few seconds and then you are carrying nothing."),

            new Card("TIP",
                "The bag is small on purpose. Nobody can hold all three corridor keys — so either " +
                "somebody makes a second trip, or somebody else carries one."),

            new Card("TIP",
                "A crowbar is a third of everything you can hold. Decide before you pick it up, not " +
                "in front of the door you needed the space for."),

            new Card("TIP",
                "<b>C</b> to crouch. It halves the noise you make, and it is the only way to see " +
                "under a bed before you are under it."),

            new Card("TIP",
                "A match takes three seconds on your knees, and the smallest input wastes it. Strike " +
                "it somewhere you can afford to be completely still."),

            new Card("TIP",
                "Ten matches. One box, one team, one candle each and about five minutes of light. " +
                "Nobody is counting them for you."),

            new Card("TIP",
                "Standing in candlelight costs you exactly what sweeping a torch does. The edge of " +
                "the pool is a real place to stand."),

            new Card("TIP",
                "Sprinting is a decision, not a speed. On a floor that hunts sound it is the loudest " +
                "thing you can do while still on your feet."),

            new Card("TIP",
                "Dying does not take you out of the run. You watch through whoever is left, and you " +
                "can ping what you can see and they cannot."),

            new Card("TIP",
                "Reading is not free everywhere. On the second floor it is the most expensive thing " +
                "you can do, and the one that feels most like progress."),

            new Card("TIP",
                "Doors slow it down. Doors have never stopped it."),

            new Card("TIP",
                "<b>G</b> drops what you are holding. Leaving something behind is a move, not a " +
                "failure — most of this building is decided by what you chose not to carry."),

            // ---------------------------------------------------------------- what this place is
            new Card("THE BUILDING",
                "It admitted nine hundred and forty. It has never once recorded a discharge that " +
                "was not also an admission."),

            new Card("THE BUILDING",
                "The east wing burned in March 1974. Everyone processed after that date was " +
                "paperwork, backdated to cover the gap."),

            new Card("THE BUILDING",
                "Three of them work here. One at the desk who cannot see you. One in the halls who " +
                "can. And one that does not care where you are, only what you have learned."),

            new Card("THE BUILDING",
                "The night book takes one name a night. It has never taken two."),

            new Card("THE BUILDING",
                "A discharge is not a release. It is a transfer, and a transfer requires both " +
                "halves, or the register does not balance and he will not sign."),

            new Card("THE BUILDING",
                "One out, one in. If you came here in company then the second half of that " +
                "arrangement is already standing next to you."),

            new Card("THE BUILDING",
                "If you came alone you owe nothing to anybody but the book. The book will only sign " +
                "for someone who can account for the whole of it."),

            new Card("THE BUILDING",
                "The floor is not the same on the way out. Not because you have forgotten it."),

            // ---------------------------------------------------------------- their own words
            new Card("RECOVERED",
                "<i>\"I have signed for nine hundred and forty, and I have never once seen the " +
                "count go down.\"</i>"),

            new Card("RECOVERED",
                "<i>\"Four of us came in. Three signatures on the sheet and the fourth line left " +
                "open, and we spent two days pretending we did not know what the open line was " +
                "for.\"</i>"),

            new Card("RECOVERED",
                "<i>\"Do not stop in a room to get your bearings, and do not turn round to see how " +
                "far you have come. The corridor behind you is the part that is listening.\"</i>"),

            new Card("RECOVERED",
                "<i>\"God forgive me, I read everything I could find, because I thought knowing " +
                "more would get me out.\"</i>"),

            new Card("RECOVERED",
                "<i>\"Below is where they are processed, and below is where they stay.\"</i>"),

            new Card("RECOVERED",
                "<i>\"I have watched a man stand still for eleven seconds. Only the light came " +
                "back.\"</i>"),

            // ---------------------------------------------------------------- the staff rules
            new Card("STANDING ORDER",
                "Staff will not name it. Staff will not agree on what this building was. Staff " +
                "will, where possible, contradict each other in writing."),

            new Card("STANDING ORDER",
                "Beds are counted at seven and again at seven. Any discrepancy is to be entered in " +
                "the book and not discussed on the floor."),

            new Card("STANDING ORDER",
                "Nobody leaves this building unsigned. That is not a rule anyone made up. It is the " +
                "only thing he has ever done."),
        };
    }
}
