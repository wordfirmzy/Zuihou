using System;
using System.Collections.Generic;
using UnityEngine;

public static class CardsDatabase
{
    const string RESOURCE_PATH = "cards"; // Resources/cards.json

    [Serializable]
    class CardsFile { public List<CardDTO> cards = new(); }

    [Serializable]
    class CardDTO
    {
        public string hanzi;

        // Type/effect as strings for JSON convenience
        public string type;    // "Hanzi" | "Effect" (optional, default Hanzi)
        public string effect;  // "DrawTwoToneLinked" | "WildToneSetter"

        // Legacy single-reading fields (optional)
        public string initial;
        public string final;
        public int tone;

        // New multi-readings
        public List<Reading> readings;
        public List<string> compounds;

        // Frequency controls
        public int count = 1;      // copies to include (>=1)
        public int weight = 0;     // alias for count; if >0, used instead of count

        // For effect convenience: auto-generate tone-linked clones
        public List<int> tones;    // e.g., [1,2,3,4]
    }

    public static List<Card> Load()
    {
        var text = Resources.Load<TextAsset>(RESOURCE_PATH);
        if (text == null)
        {
            Debug.LogError("[CardsDatabase] Resources/cards.json not found.");
            return new List<Card>();
        }

        CardsFile file;
        try
        {
            file = JsonUtility.FromJson<CardsFile>(text.text);
        }
        catch (Exception e)
        {
            Debug.LogError($"[CardsDatabase] JSON parse error: {e.Message}");
            return new List<Card>();
        }
        if (file == null || file.cards == null) return new List<Card>();

        var result = new List<Card>();

        foreach (var dto in file.cards)
        {
            int copies = dto.weight > 0 ? dto.weight : Mathf.Max(1, dto.count);

            // Determine type/effect
            var type = ParseType(dto.type);
            var effect = ParseEffect(dto.effect);

            // EFFECT CARDS
            if (type == CardType.Effect)
            {
                // Convenience: auto-generate clones for tone-linked draw2
                if (effect == EffectType.DrawTwoToneLinked && dto.tones != null && dto.tones.Count > 0)
                {
                    foreach (var t in dto.tones)
                    {
                        for (int c = 0; c < copies; c++)
                            result.Add(BuildEffectCard(dto.hanzi, effect, t));
                    }
                    continue;
                }

                // Otherwise use readings/tone provided (or tone 0 if omitted)
                int tone = dto.tone;
                if (dto.readings != null && dto.readings.Count > 0)
                    tone = dto.readings[0]?.tone ?? tone;

                for (int c = 0; c < copies; c++)
                    result.Add(BuildEffectCard(dto.hanzi, effect, tone));
                continue;
            }

            // HANZI CARDS
            var template = new Card
            {
                hanzi = dto.hanzi,
                type = CardType.Hanzi,
                effect = EffectType.None,
                compounds = dto.compounds != null ? new List<string>(dto.compounds) : new List<string>()
            };

            // readings: prefer explicit list; else legacy single fields
            if (dto.readings != null && dto.readings.Count > 0)
            {
                template.readings = new List<Reading>();
                foreach (var r in dto.readings)
                    template.readings.Add(new Reading(r.initial ?? "", r.final ?? "", r.tone));
            }
            else
            {
                template.initial = dto.initial ?? "";
                template.final   = dto.final ?? "";
                template.tone    = dto.tone;
            }

            for (int c = 0; c < copies; c++)
                result.Add(CloneCard(template));
        }

        return Shuffle(result);
    }

    static CardType ParseType(string s)
        => string.IsNullOrEmpty(s) ? CardType.Hanzi :
           EnumTry<CardType>(s, CardType.Hanzi);

    static EffectType ParseEffect(string s)
        => string.IsNullOrEmpty(s) ? EffectType.None :
           EnumTry<EffectType>(s, EffectType.None);

    static T EnumTry<T>(string s, T fallback) where T : struct
        => Enum.TryParse<T>(s, true, out var v) ? v : fallback;

    static Card BuildEffectCard(string label, EffectType effect, int tone)
    {
        var card = new Card
        {
            hanzi = string.IsNullOrWhiteSpace(label) ? effect.ToString() : label,
            type = CardType.Effect,
            effect = effect,
            readings = new List<Reading>()
        };

        // For DrawTwoToneLinked we store the linking tone in readings[0].tone
        // Wild ignores tone legality, but we can still store a tone 0/1 safely.
        int safeTone = Mathf.Clamp(tone, 0, 4);
        card.readings.Add(new Reading("", "", safeTone));
        return card;
    }

    static Card CloneCard(Card src)
    {
        var c = new Card
        {
            hanzi = src.hanzi,
            type = src.type,
            effect = src.effect,
            initial = src.initial,
            final = src.final,
            tone = src.tone,
            readings = new List<Reading>(),
            compounds = src.compounds != null ? new List<string>(src.compounds) : new List<string>()
        };
        if (src.readings != null)
            foreach (var r in src.readings)
                c.readings.Add(new Reading(r.initial, r.final, r.tone));
        return c;
    }

    static List<Card> Shuffle(List<Card> list)
    {
        // Simple Fisher–Yates
        for (int i = 0; i < list.Count - 1; i++)
        {
            int j = UnityEngine.Random.Range(i, list.Count);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }
}
