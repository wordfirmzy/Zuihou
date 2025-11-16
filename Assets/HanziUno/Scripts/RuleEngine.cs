using System;

public static class RuleEngine
{
    [Serializable]
    public class MatchContext
    {
        public bool ok;
        public string by;            // "initial" | "final" | "terminal" | "tone" | "compound" | "effect" | "start"
        public string reason;

        public Reading topReading;
        public Reading playReading;
        public string terminalSymbol;

        public static MatchContext Fail(string why) =>
            new MatchContext { ok = false, by = "", reason = why };

        public static MatchContext Start() =>
            new MatchContext
            {
                ok = true,
                by = "start",
                reason = "No top card; any card can start."
            };

        public static MatchContext Compound(string top, string play) =>
            new MatchContext
            {
                ok = true,
                by = "compound",
                reason = $"{top}+{play} is a legal compound."
            };

        public static MatchContext Ok(string rule, string msg, Reading tr = null, Reading pr = null, string terminal = null) =>
            new MatchContext
            {
                ok = true,
                by = rule,
                reason = msg,
                topReading = tr,
                playReading = pr,
                terminalSymbol = terminal
            };
    }

    /// <summary>
    /// Core matching rules for playing <paramref name="play"/> on top of <paramref name="top"/>.
    /// This encodes:
    /// - Effect rules (Draw-2 tone-linked, Wild always legal)
    /// - Hanzi rules: compound, initial, final, terminal-final, tone
    /// </summary>
    public static MatchContext CanPlayOn(Card top, Card play)
    {
        if (play == null)
            return MatchContext.Fail("No card selected.");

        // No card on the pile yet -> any card may start.
        if (top == null)
            return MatchContext.Start();

        // EFFECT RULES
        if (play.type == CardType.Effect)
        {
            switch (play.effect)
            {
                case EffectType.DrawTwoToneLinked:
                    // Must match tone with top's any reading.
                    // Top can be Hanzi or an Effect card that also has readings
                    // (e.g., starter Draw-2).
                    if (top.type != CardType.Hanzi && top.type != CardType.Effect)
                        return MatchContext.Fail("Draw-2 requires a readable card on top.");

                    foreach (var tr in top.AllReadings())
                    foreach (var pr in play.AllReadings())
                    {
                        if (pr.tone != 0 && tr.tone == pr.tone)
                            return MatchContext.Ok("effect", $"Draw-2 (tone-linked: {pr.tone}).", tr, pr);
                    }

                    return MatchContext.Fail("Draw-2 must match the top card's tone.");

                case EffectType.WildToneSetter:
                    // Always legal. Tone choice is handled by TurnManager.
                    return MatchContext.Ok("effect", "Wild tone setter.");

                default:
                    return MatchContext.Fail("Unknown effect card.");
            }
        }

        // HANZI RULES
        // Normal matching is defined for Hanzi vs Hanzi.
        // We also allow matching *onto* an effect card when it is the only
        // card in the discard pile (e.g., starter card is Draw-2 or Wild).
        // In those cases the effect card still has readings, so we treat it
        // like a Hanzi for purposes of initials/finals/tones.
        if ((top.type == CardType.Hanzi || top.type == CardType.Effect) && play.type == CardType.Hanzi)
        {
            // Compound (bidirectional)
            bool compound =
                (play.compounds != null && play.compounds.Contains(top.hanzi)) ||
                (top.compounds  != null && top.compounds.Contains(play.hanzi));
            if (compound)
                return MatchContext.Compound(top.hanzi, play.hanzi);

            foreach (var tr in top.AllReadings())
            foreach (var pr in play.AllReadings())
            {
                // Initials exact
                if (!string.IsNullOrWhiteSpace(tr.initial) &&
                    !string.IsNullOrWhiteSpace(pr.initial) &&
                    tr.initial == pr.initial)
                    return MatchContext.Ok("initial", $"Initials match ({pr.initial}).", tr, pr);

                // Finals exact
                if (!string.IsNullOrWhiteSpace(tr.final) &&
                    !string.IsNullOrWhiteSpace(pr.final) &&
                    tr.final == pr.final)
                    return MatchContext.Ok("final", $"Finals match ({pr.final}).", tr, pr);

                // Finals terminal
                var tTail = LastZhuyinSymbol(tr.final);
                var pTail = LastZhuyinSymbol(pr.final);
                if (!string.IsNullOrEmpty(tTail) && tTail == pTail)
                    return MatchContext.Ok("terminal", $"Finals share terminal ({pTail}).", tr, pr, terminal: pTail);

                // Tone equal
                if (tr.tone == pr.tone)
                    return MatchContext.Ok("tone", $"Tones match ({pr.tone}).", tr, pr);
            }

            return MatchContext.Fail("Must match by INITIAL, FINAL, terminal FINAL, TONE, or be a listed COMPOUND.");
        }

        // Default prohibition for mixing Hanzi/effect in odd ways
        return MatchContext.Fail("Illegal move.");
    }

    public static string LastZhuyinSymbol(string zhuyin)
    {
        if (string.IsNullOrWhiteSpace(zhuyin)) return null;
        for (int i = zhuyin.Length - 1; i >= 0; i--)
        {
            char c = zhuyin[i];
            if (char.IsWhiteSpace(c) || c == '-' || c == '/' || c == '·') continue;
            if (c == '\u02D9' || c == '\u02CA' || c == '\u02C7' || c == '\u02CB') continue; // tone marks
            if (c >= '\u3105' && c <= '\u312F') return c.ToString();
        }
        return null;
    }

    // Helper for tone locks
    public static bool HasTone(Card c, int tone)
    {
        if (c == null || tone <= 0) return false;
        foreach (var r in c.AllReadings())
            if (r.tone == tone)
                return true;
        return false;
    }
}
