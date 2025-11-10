using System;
using System.Collections.Generic;
using System.Linq;

public enum CardType { Hanzi, Effect }
public enum EffectType { None, DrawTwoToneLinked, WildToneSetter }

[Serializable]
public class Reading
{
    public string initial;   // Zhuyin initial (e.g., ㄌ)
    public string final;     // Zhuyin final (e.g., ㄧㄠ)
    public int tone;         // 1..4 (0/5 = neutral if used)

    public Reading() {}
    public Reading(string ini, string fin, int t) { initial = ini ?? ""; final = fin ?? ""; tone = t; }
}

[Serializable]
public class Card
{
    public string hanzi;

    // Card kind
    public CardType type = CardType.Hanzi;
    public EffectType effect = EffectType.None;

    // Legacy single-reading fields (kept for backward compatibility)
    public string initial;  // legacy
    public string final;    // legacy
    public int tone;        // legacy

    // New multi-reading field
    public List<Reading> readings = new();

    // Legal compounds (hanzi that can be paired)
    public List<string> compounds = new();

    public IEnumerable<Reading> AllReadings()
    {
        bool legacyPresent = !string.IsNullOrWhiteSpace(initial) ||
                             !string.IsNullOrWhiteSpace(final)   ||
                             (tone != 0);
        if (readings != null && readings.Count > 0)
            foreach (var r in readings) yield return r;

        if (legacyPresent)
            yield return new Reading(initial ?? "", final ?? "", tone);
    }

    public (List<string> initials, List<string> finals, List<int> tones) DistinctReadingSets()
    {
        var ini = new HashSet<string>();
        var fin = new HashSet<string>();
        var ton = new HashSet<int>();
        foreach (var r in AllReadings())
        {
            if (!string.IsNullOrWhiteSpace(r.initial)) ini.Add(r.initial.Trim());
            if (!string.IsNullOrWhiteSpace(r.final))   fin.Add(r.final.Trim());
            ton.Add(r.tone);
        }
        return (ini.Where(s=>!string.IsNullOrWhiteSpace(s)).ToList(),
                fin.Where(s=>!string.IsNullOrWhiteSpace(s)).ToList(),
                ton.ToList());
    }

    public override string ToString()
    {
        if (type == CardType.Effect) return $"[{effect}]";
        var sets = DistinctReadingSets();
        string i = sets.initials.Count > 0 ? string.Join("/", sets.initials) : "-";
        string f = sets.finals.Count   > 0 ? string.Join("/", sets.finals)   : "-";
        string t = sets.tones.Count    > 0 ? string.Join("/", sets.tones)    : "-";
        return $"{hanzi} (i:{i} f:{f} t:{t})";
    }
}
