// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using HoloToolkit.Unity.InputModule;
using UnityEngine;
using UnityEngine.Events;

namespace HoloToolkit.Examples.ColorPicker
{
    public class GazeableColorPicker : MonoBehaviour, IFocusable, IInputClickHandler
    {
        public Renderer rendererComponent;

        public bool IsColoring { get; set; }
        public Color GazedColor { get; set; }
        public Color PickedColor { get; set; }

        [System.Serializable]
        public class PickedColorCallback : UnityEvent<Color> { }

        public PickedColorCallback OnGazedColor = new PickedColorCallback();
        public PickedColorCallback OnPickedColor = new PickedColorCallback();

        private bool gazing = false;

        void Update()
        {
            if (gazing == false) return;
            UpdatePickedColor(OnGazedColor);
        }

        void UpdatePickedColor(PickedColorCallback cb)
        {
            RaycastHit hit = GazeManager.Instance.HitInfo;
            if (hit.transform.gameObject != rendererComponent.gameObject) return;
            
            Texture2D texture = rendererComponent.material.mainTexture as Texture2D;
            Vector2 pixelUV = hit.textureCoord;
            pixelUV.x *= texture.width;
            pixelUV.y *= texture.height;

            GazedColor = texture.GetPixel((int)pixelUV.x, (int)pixelUV.y);
            cb.Invoke(GazedColor);
        }

        public void OnFocusEnter()
        {
            gazing = true;
        }

        public void OnFocusExit()
        {
            gazing = false;
        }

        public void OnInputClicked(InputEventData eventData)
        {
            PickedColor = GazedColor;
            UpdatePickedColor(OnPickedColor);
        }
    }
}