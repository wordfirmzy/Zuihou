using System;
using UnityEngine;

public class TonePickerUI : MonoBehaviour
{
    Action<int> _onPicked;

    public void ShowPick(Action<int> onPicked)
    {
        _onPicked = onPicked;
        gameObject.SetActive(true);
    }

    public void Hide() => gameObject.SetActive(false);

    // Wire each button to this with the int parameter set to 1,2,3,4
    public void PickTone(int tone)
    {
        _onPicked?.Invoke(tone);
        _onPicked = null;
        Hide();
    }
}
