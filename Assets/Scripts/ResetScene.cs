using HoloToolkit.Unity.InputModule;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

public class ResetScene : MonoBehaviour, IInputClickHandler
{
    public void OnInputClicked(InputEventData eventData)
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name, LoadSceneMode.Single);
    }
}
