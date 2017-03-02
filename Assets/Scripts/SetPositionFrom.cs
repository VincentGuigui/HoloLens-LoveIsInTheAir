using HoloToolkit.Unity.SpatialMapping;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SetPositionFrom : MonoBehaviour
{

    // Use this for initialization
    public HandDraggableWithAnchor PositionComponent;
    public string PositionContainer;
    private System.Reflection.FieldInfo propinfo;
    void Start()
    {
        propinfo = typeof(HandDraggableWithAnchor).GetField(PositionContainer);
    }

    // Update is called once per frame
    void Update()
    {
        Vector3 position = (Vector3)propinfo.GetValue(PositionComponent);
        this.gameObject.transform.position = position;
    }
}
