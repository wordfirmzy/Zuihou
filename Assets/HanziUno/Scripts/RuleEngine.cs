using System;
using System.Collections.Generic;

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

        public static MatchContext Fail(string why) => new MatchContext { ok = false, by = "", reason = why };
        public static MatchContext Start() => new MatchContext { ok = true, by = "start", reason = "No top card; any card can start." };
        public static MatchContext Compound(string top, string play) => new MatchContext { ok = true, by = "compound", reason = $"{top}+{play} is a legal compound." };
        public static MatchContext Ok(string rule, string msg, Reading tr = null, Reading pr = null, string terminal = null)
            => new MatchContext { ok = true, by = rule, reason = msg, topReading = tr, playReading = pr, terminalSymbol = terminal };
    }

    public static MatchContext CanPlayOn(Card top, Card play)
    {
        if (play == null) return MatchContext.Fail("No card selected.");
        if (top == null)  return MatchContext.Start();

        // EFFECT CARDS
        if (play.type == CardType.Effect)
        {
            switch (play.effect)
            {
                case EffectType.DrawTwoToneLinked:
                    // must match tone with top's any reading, and top must be Hanzi
                    if (top.type != CardType.Hanzi) return MatchContext.Fail("Draw-2 requires a Hanzi card on top.");
                    foreach (var tr in top.AllReadings())
                    foreach (var pr in play.AllReadings())
                        if (pr.tone != 0 && tr.tone == pr.tone)
                            return MatchContext.Ok("effect", $"Draw 2 (tone-linked: {pr.tone}).", tr, pr);
                    return MatchContext.Fail("Draw-2 must match the top card's tone.");

                case EffectType.WildToneSetter:
                    // always legal
                    return MatchContext.Ok("effect", "Wild tone setter.");
            }
        }

        // HANZI RULES
        if (top.type == CardType.Hanzi && play.type == CardType.Hanzi)
        {
            // COMPOUNDS: accept if play+top or top+play equals any listed compound
            // on EITHER card (A+B or B+A).
            if (CompoundMatch(top, play))
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

                // Finals terminal (e.g. ㄧㄠ ends with ㄠ)
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

    /// <summary>
    /// Returns the last zhuyin symbol in a final string, skipping tone marks and separators.
    /// Used for terminal-final matches (e.g. ㄧㄠ ends with ㄠ).
    /// </summary>
    public static string LastZhuyinSymbol(string zhuyin)
    {
        if (string.IsNullOrWhiteSpace(zhuyin)) return null;
        for (int i = zhuyin.Length - 1; i >= 0; i--)
        {
            char c = zhuyin[i];
            if (char.IsWhiteSpace(c) || c == '-' || c == '/' || c == '·') continue;
            // tone marks
            if (c == '\u02D9' || c == '\u02CA' || c == '\u02C7' || c == '\u02CB') continue;
            // zhuyin range
            if (c >= '\u3105' && c <= '\u312F') return c.ToString();
        }
        return null;
    }

    /// <summary>
    /// Helper for tone locks.
    /// </summary>
    public static bool HasTone(Card c, int tone)
    {
        if (c == null || tone <= 0) return false;
        foreach (var r in c.AllReadings()) if (r.tone == tone) return true;
        return false;
    }

    // ===== COMPOUND HELPERS =====

    /// <summary>
    /// Returns true if the two cards form a legal compound according to either card's
    /// compound list. Checks both play+top and top+play (A+B and B+A).
    /// 
    /// Examples:
    ///   top = "们", play = "你"
    ///   play+top = "你们"  -> matches if "你们" is listed in either card's compounds.
    /// </summary>
    private static bool CompoundMatch(Card top, Card play)
    {
        if (top == null || play == null) return false;
        if (top.compounds == null && play.compounds == null) return false;

        var a = (play.hanzi ?? string.Empty).Trim();
        var b = (top.hanzi  ?? string.Empty).Trim();
        if (a.Length == 0 || b.Length == 0) return false;

        // Both possible combined forms
        string playPlusTop = NormalizeHanzi(a + b); // A+B, e.g. "你们"
        string topPlusPlay = NormalizeHanzi(b + a); // B+A, e.g. "们你"

        // Check against each card's compound list; either card may list the compound.
        if (play.compounds != null)
        {
            if (Listed(playPlusTop, play.compounds) || Listed(topPlusPlay, play.compounds))
                return true;
        }

        if (top.compounds != null)
        {
            if (Listed(playPlusTop, top.compounds) || Listed(topPlusPlay, top.compounds))
                return true;
        }

        return false;
    }

    private static bool Listed(string candidate, IEnumerable<string> list)
    {
        candidate = NormalizeHanzi(candidate);
        foreach (var s in list)
        {
            if (NormalizeHanzi(s) == candidate)
                return true;
        }
        return false;
    }

    private static string NormalizeHanzi(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        // Remove spaces and trim; if you ever add other normalizations (full-width, punctuation),
        // this is the place.
        return s.Replace(" ", "").Trim();
    }
}
