using HoloToolkit.Examples.ColorPicker;
using HoloToolkit.Unity.InputModule;
using UnityEngine;
using UnityEngine.Events;

public class SetColorFromColorPicker : MonoBehaviour, IInputClickHandler
{
    [Tooltip("Optional. Will use any existing GazeableColorPicker in the scene")]
    public GazeableColorPicker GazeableColorPicker;

    public void OnInputClicked(InputEventData eventData)
    {
        if (GazeableColorPicker == null)
            GazeableColorPicker = FindObjectOfType<GazeableColorPicker>();
        if (GazeableColorPicker != null && GazeableColorPicker.IsColoring)
        {
            Color col = GazeableColorPicker.PickedColor;
            GetComponentInChildren<Renderer>().material.SetColor("_Color", GazeableColorPicker.PickedColor);
            GazeableColorPicker.IsColoring = false;
        }
    }
}
