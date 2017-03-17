using HoloToolkit.Unity.InputModule;
using UnityEngine;
using UnityEngine.Events;

public class TapToEvent : MonoBehaviour, IInputClickHandler
{
    [System.Serializable]
    public class TapCallback : UnityEvent<GameObject> { }

    public TapCallback OnTap = new TapCallback();

    public void OnInputClicked(InputEventData eventData)
    {
        OnTap.Invoke(this.gameObject);
    }
}
