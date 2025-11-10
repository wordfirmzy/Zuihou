using System.Collections.Generic;
using UnityEngine;

public class DeckManager : MonoBehaviour {
    readonly Stack<Card> drawPile = new();
    readonly Stack<Card> discardPile = new();

    public int Count => drawPile.Count;
    public Card Top => discardPile.Count > 0 ? discardPile.Peek() : null;

    public void Init(List<Card> cards) {
        var list = new List<Card>(cards);
        for (int i = 0; i < list.Count; i++) {
            int j = Random.Range(i, list.Count);
            (list[i], list[j]) = (list[j], list[i]);
        }
        drawPile.Clear(); discardPile.Clear();
        for (int i = 0; i < list.Count; i++) drawPile.Push(list[i]);
    }

    public Card Draw() {
        if (drawPile.Count == 0) return null;
        return drawPile.Pop();
    }

    public void Play(Card c) {
        if (c != null) discardPile.Push(c);
    }
}
