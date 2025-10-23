using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Collections;
using TMPro;
public class StartButton : MonoBehaviour
{
    [SerializeField] private Button _button;
    [SerializeField] private TextMeshPro _text;


    public void OnButtonClick()
    {
        _text.text = "Stop";
    }
}
