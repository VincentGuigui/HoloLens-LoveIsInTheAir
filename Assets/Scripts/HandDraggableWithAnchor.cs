using HoloToolkit.Unity.InputModule;
using UnityEngine;
using UnityEngine.Events;
using System;
using HoloToolkit.Unity;

namespace HoloToolkit.Unity.SpatialMapping
{
    /// <summary>
    /// Component that allows dragging an object with your hand on HoloLens.
    /// Dragging is done by calculating the angular delta and z-delta between the current and previous hand positions,
    /// and then repositioning the object based on that.
    /// also adds a WorldAnchor component to enable persistence.
    /// </summary>
    public class HandDraggableWithAnchor : MonoBehaviour,
                                     IFocusable,
                                     IInputHandler,
                                     IInputClickHandler,
                                     ISourceStateHandler
    {

        public enum AlignementOptions
        {
            Free,
            FreeAndSpatialMapping,
            ForceSpatialMapping,
        }

        public enum DraggingMethods
        {
            DragByHand,
            TapToPlaceWithGaze,
            TapToPlaceWithGazePlusPreciseHandDrag
        }

        public AlignementOptions Placement = AlignementOptions.Free;
        public DraggingMethods DraggingMethod = DraggingMethods.DragByHand;

        [Tooltip("Used in FreeAndSpatialMapping alignment mode")]
        public float SpatialMappingAlignmentDistance = 1f;

        public float FloatingDistanceWhenNoSpatialMappingHit = 2f;

        [Tooltip("Should the object be kept upright as it is being dragged?")]
        public bool IsKeepUpright = false;

        [Tooltip("Should the object be oriented towards the user as it is being dragged?")]
        public bool IsFacingUser = true;

        public bool IsDraggingEnabled = true;

        public enum IsBeingPlacedStates
        {
            No,
            ByHand,
            ByGaze,
        }
        /// <summary>
        /// Keeps track of if the user is moving the object or not.
        /// Setting this to true will enable the user to move and place the object in the scene.
        /// Useful when you want to place an object immediately.
        /// </summary>
        [Tooltip("Setting this to true will enable the user to move and place the object in the scene without needing to tap on the object. Useful when you want to place an object immediately.")]
        public IsBeingPlacedStates IsBeingPlaced = IsBeingPlacedStates.No;

        [Tooltip("Parent game object will be dragged instead of current game object.")]
        public bool ObjectToPlaceIsParent;

        [Tooltip("Specify a parent game object that will be dragged instead of current game object. Defaults to the current game object.")]
        public GameObject ObjectToPlace;


        /// <summary>
        /// Controls spatial mapping.  In this script we access spatialMappingManager
        /// to control rendering and to access the physics layer mask.
        /// </summary>
        protected SpatialMappingManager spatialMappingManager;

        #region Persistence
        [Tooltip("Enable persistence in the WorldAnchorStore.")]
        public bool EnablePersistenceInWorldAnchor = false;

        /// <summary>
        /// Specifies whether or not the SpatialMapping meshes are to be rendered.
        /// </summary>
        public bool DrawVisualMeshes = true;

        [Tooltip("Supply a friendly name for the anchor as the key name for the WorldAnchorStore.")]
        public string SavedAnchorFriendlyName = String.Empty;


        /// <summary>
        /// Manages persisted anchors.
        /// </summary>
        protected WorldAnchorManager anchorManager;

        #endregion

        #region HandDragging
        /// <summary>
        /// Event triggered when dragging starts.
        /// </summary>
        public event Action StartedDragging;

        /// <summary>
        /// Event triggered when dragging stops.
        /// </summary>
        public event Action StoppedDragging;
        [Tooltip("Scale by which hand movement in z is multipled to move the dragged object.")]
        public float DistanceScale = 2f;
        #endregion

        private Camera mainCamera;
        private bool isGazed;
        private IsBeingPlacedStates previousPlacingMethod = IsBeingPlacedStates.No;
        private Vector3 objRefForward;
        private float objRefDistance;
        private Quaternion gazeAngularOffset;
        private float handRefDistance;
        private Vector3 objRefGrabPoint;

        private Vector3 draggingPosition;

        private IInputSource currentInputSource = null;
        private uint currentInputSourceId;

        private void Start()
        {

            DetermineParent();
            if (SavedAnchorFriendlyName == string.Empty)
                SavedAnchorFriendlyName = this.ObjectToPlace.name;

            mainCamera = Camera.main;

            if (EnablePersistenceInWorldAnchor)
            {
                // Make sure we have all the components in the scene we need.
                anchorManager = WorldAnchorManager.Instance;
                if (anchorManager == null)
                {
                    Debug.LogError("This script expects that you have a WorldAnchorManager component in your scene.");
                }

                spatialMappingManager = SpatialMappingManager.Instance;
                if (spatialMappingManager == null)
                {
                    Debug.LogError("This script expects that you have a SpatialMappingManager component in your scene.");
                }

                if (anchorManager != null && spatialMappingManager != null)
                {
                    anchorManager.AttachAnchor(gameObject, SavedAnchorFriendlyName);
                }
                else
                {
                    // If we don't have what we need to proceed, we may as well remove ourselves.
                    Destroy(this);
                }
            }
        }

        private void OnDestroy()
        {
            if (IsBeingPlaced != IsBeingPlacedStates.No)
            {
                StopDragging();
            }

            if (isGazed)
            {
                OnFocusExit();
            }
        }

        private void Update()
        {
            if (IsDraggingEnabled && IsBeingPlaced != IsBeingPlacedStates.No)
            {
                UpdateDragging();
            }
        }

        /// <summary>
        /// Starts dragging the object.
        /// </summary>
        public void StartDragging(IsBeingPlacedStates isBeingPlacedBy)
        {
            if (!IsDraggingEnabled)
                return;

            // if already dragging with the same method
            if (IsBeingPlaced == isBeingPlacedBy)
                return;

            // if start dragging
            if (IsBeingPlaced == IsBeingPlacedStates.No)
                // Add self as a modal input handler, to get all inputs during the manipulation
                InputManager.Instance.PushModalInputHandler(gameObject);

            IsBeingPlaced = isBeingPlacedBy;

            Vector3 handPosition;
            if (DraggingMethod != DraggingMethods.TapToPlaceWithGaze
                && currentInputSource != null
                && currentInputSource.TryGetPosition(currentInputSourceId, out handPosition))
            {
                Vector3 gazeHitPosition = GazeManager.Instance.HitInfo.point;

                Vector3 handPivotPosition = GetHandPivotPosition();
                handRefDistance = Vector3.Magnitude(handPosition - handPivotPosition);
                objRefDistance = Vector3.Magnitude(gazeHitPosition - handPivotPosition);

                Vector3 objForward = ObjectToPlace.transform.forward;

                // Store where the object was grabbed from
                objRefGrabPoint = mainCamera.transform.InverseTransformDirection(ObjectToPlace.transform.position - gazeHitPosition);

                Vector3 objDirection = Vector3.Normalize(gazeHitPosition - handPivotPosition);
                Vector3 handDirection = Vector3.Normalize(handPosition - handPivotPosition);

                objForward = mainCamera.transform.InverseTransformDirection(objForward);       // in camera space
                objDirection = mainCamera.transform.InverseTransformDirection(objDirection);   // in camera space
                handDirection = mainCamera.transform.InverseTransformDirection(handDirection); // in camera space

                objRefForward = objForward;

                // Store the initial offset between the hand and the object, so that we can consider it when dragging
                gazeAngularOffset = Quaternion.FromToRotation(handDirection, objDirection);
                draggingPosition = gazeHitPosition;
            }

            StartedDragging.RaiseEvent();

            if (Placement == AlignementOptions.ForceSpatialMapping)
            {
                if (spatialMappingManager != null)
                    spatialMappingManager.DrawVisualMeshes = DrawVisualMeshes;
            }
            if (EnablePersistenceInWorldAnchor)
            {
                Debug.Log(gameObject.name + " : Removing existing world anchor if any.");
                anchorManager.RemoveAnchor(gameObject);
            }
        }

        /// <summary>
        /// Gets the pivot position for the hand, which is approximated to the base of the neck.
        /// </summary>
        /// <returns>Pivot position for the hand.</returns>
        private Vector3 GetHandPivotPosition()
        {
            Vector3 pivot = mainCamera.transform.position + new Vector3(0, -0.2f, 0) - mainCamera.transform.forward * 0.2f; // a bit lower and behind
            return pivot;
        }

        /// <summary>
        /// Enables or disables dragging.
        /// </summary>
        /// <param name="isEnabled">Indicates whether dragging shoudl be enabled or disabled.</param>
        public void SetDragging(bool isEnabled)
        {
            if (IsDraggingEnabled == isEnabled)
            {
                return;
            }

            IsDraggingEnabled = isEnabled;

            if (IsBeingPlaced != IsBeingPlacedStates.No)
            {
                StopDragging();
            }
        }

        /// <summary>
        /// Update the position of the object being dragged.
        /// </summary>
        private void UpdateDragging()
        {
            Vector3 userPosition, targetDirection;
            Vector3 handPosition = Vector3.zero, handPivotPosition = Vector3.zero;
            if (IsBeingPlaced == IsBeingPlacedStates.ByHand
                && currentInputSource != null
                && currentInputSource.TryGetPosition(currentInputSourceId, out handPosition))
            {
                handPivotPosition = GetHandPivotPosition();
                userPosition = Vector3.Normalize(handPosition - handPivotPosition);
                Debug.Log("UpdateDragging with hand " + gameObject.name + " : " + handPosition.ToString());

                //Rotation
                userPosition = mainCamera.transform.InverseTransformDirection(userPosition); // in camera space
                targetDirection = Vector3.Normalize(gazeAngularOffset * userPosition);
                targetDirection = mainCamera.transform.TransformDirection(targetDirection); // back to world space
            }
            else // Gaze if selected or no hand available
            {
                Debug.Log("UpdateDragging with gaze " + gameObject.name);
                userPosition = mainCamera.transform.position;
                targetDirection = mainCamera.transform.forward;
            }

            RaycastHit hitInfo;
            if (CanBePlaceOnSpatialMapping(userPosition, targetDirection, out hitInfo))
            {
                // Move this object to where the raycast hit the Spatial Mapping mesh.
                // Here is where you might consider adding intelligence
                // to how the object is placed.  For example, consider
                // placing based on the bottom of the object's
                // collider so it sits properly on surfaces.
                ObjectToPlace.transform.position = hitInfo.point + mainCamera.transform.TransformDirection(objRefGrabPoint);

                // Orient the object at the normal to spatial mapping
                ObjectToPlace.transform.rotation = Quaternion.LookRotation(-hitInfo.normal);
            }
            else // Manual placement
            {
                if (IsBeingPlaced == IsBeingPlacedStates.ByHand)
                {
                    //Position
                    float currenthandDistance = Vector3.Magnitude(handPosition - handPivotPosition);
                    float distanceRatio = currenthandDistance / handRefDistance;
                    float distanceOffset = distanceRatio > 0 ? (distanceRatio - 1f) * DistanceScale : 0;
                    float targetDistance = objRefDistance + distanceOffset;
                    draggingPosition = handPivotPosition + (targetDirection * targetDistance);
                    ObjectToPlace.transform.position = draggingPosition + mainCamera.transform.TransformDirection(objRefGrabPoint);
                    previousPlacingMethod = IsBeingPlacedStates.ByHand;
                }
                else // by gaze
                {
                    // put this object at 2m in the gaze direction
                    ObjectToPlace.transform.position = userPosition + targetDirection * FloatingDistanceWhenNoSpatialMappingHit;
                    previousPlacingMethod = IsBeingPlacedStates.ByGaze;
                }
            }
            if (IsFacingUser)
            {

                if (IsBeingPlaced == IsBeingPlacedStates.ByHand)
                {
                    // Rotate based on hand pivot
                    Quaternion draggingRotation = Quaternion.LookRotation(ObjectToPlace.transform.position - handPivotPosition);
                    ObjectToPlace.transform.rotation = draggingRotation;
                }
                else
                    // Rotate this object to face the camera.
                    ObjectToPlace.transform.rotation = mainCamera.transform.localRotation;

            }

            if (IsKeepUpright)
            {
                Quaternion upRotation = Quaternion.FromToRotation(ObjectToPlace.transform.up, Vector3.up);
                ObjectToPlace.transform.rotation = upRotation * ObjectToPlace.transform.rotation;
            }
        }

        private bool CanBePlaceOnSpatialMapping(Vector3 userPosition, Vector3 targetDirection, out RaycastHit hitInfo)
        {
            if (spatialMappingManager != null
                && (Placement == AlignementOptions.ForceSpatialMapping
                || Placement == AlignementOptions.FreeAndSpatialMapping))
            {
                if (Physics.Raycast(userPosition, targetDirection, out hitInfo,
                    30.0f,
                    spatialMappingManager.LayerMask))
                {
                    if (Placement == AlignementOptions.ForceSpatialMapping)
                        return true;
                    else // Placement == PlacementOptions.FreeDragAndSpatialMappingAlignment
                    {
                        Vector3 heading = hitInfo.point - ObjectToPlace.transform.position;
                        float sqrMagnitude = heading.sqrMagnitude;
                        float sqrSpatialMappingAlignmentDistance = SpatialMappingAlignmentDistance * SpatialMappingAlignmentDistance;
                        // if object is close to spatial mapping mesh, show mesh
                        if (sqrMagnitude < 2 * sqrSpatialMappingAlignmentDistance)
                            spatialMappingManager.DrawVisualMeshes = DrawVisualMeshes;
                        // if object is very close to spatial mapping mesh, declare placement possible
                        if (sqrMagnitude < sqrSpatialMappingAlignmentDistance)
                            return true;
                    }
                }
            }
            hitInfo = new RaycastHit();
            return false;
        }

        private void DetermineParent()
        {
            if (ObjectToPlaceIsParent)
            {
                if (gameObject.transform.parent == null)
                {
                    Debug.LogError("The selected GameObject has no parent.");
                    ObjectToPlaceIsParent = false;
                }
                else
                {
                    Debug.LogError("Using immediate parent: " + gameObject.transform.parent.gameObject.name);
                    ObjectToPlace = gameObject.transform.parent.gameObject;
                }
            }
            // if Host is not specified, apply transform directly to the current object
            if (ObjectToPlace == null)
            {
                ObjectToPlace = this.gameObject;
            }
        }

        /// <summary>
        /// Stops dragging the object.
        /// </summary>
        public void StopDragging()
        {
            if (IsBeingPlaced == IsBeingPlacedStates.No)
                return;

            // Remove self as a modal input handler
            InputManager.Instance.PopModalInputHandler();

            IsBeingPlaced = IsBeingPlacedStates.No;
            previousPlacingMethod = IsBeingPlacedStates.No;

            currentInputSource = null;
            StoppedDragging.RaiseEvent();

            if (spatialMappingManager != null)
            {
                spatialMappingManager.DrawVisualMeshes = false;
            }
            if (EnablePersistenceInWorldAnchor)
            {
                // Add world anchor when object placement is done.
                anchorManager.AttachAnchor(gameObject, SavedAnchorFriendlyName);
            }

        }

        public void OnFocusEnter()
        {
            if (!IsDraggingEnabled)
            {
                return;
            }

            if (isGazed)
            {
                return;
            }

            isGazed = true;
        }

        public void OnFocusExit()
        {
            if (!IsDraggingEnabled)
            {
                return;
            }

            if (!isGazed)
            {
                return;
            }

            isGazed = false;
        }

        public void OnInputUp(InputEventData eventData)
        {
            // event not used in TapToPlace
            if (DraggingMethod == DraggingMethods.TapToPlaceWithGaze)
                return;

            if (IsBeingPlaced == IsBeingPlacedStates.ByHand
                && currentInputSource != null
                && eventData.SourceId == currentInputSourceId)
            {
                StopDragging();
            }
        }

        public void OnInputDown(InputEventData eventData)
        {
            // event not used in TapToPlace
            if (DraggingMethod == DraggingMethods.TapToPlaceWithGaze)
                return;

            if (IsBeingPlaced == IsBeingPlacedStates.ByHand)
            {
                // We're already handling drag input, so we can't start a new drag operation.
                return;
            }

            if (!eventData.InputSource.SupportsInputInfo(eventData.SourceId, SupportedInputInfo.Position))
            {
                // The input source must provide positional data for this script to be usable
                return;
            }

            currentInputSource = eventData.InputSource;
            currentInputSourceId = eventData.SourceId;
            StartDragging(IsBeingPlacedStates.ByHand);
        }

        public void OnSourceDetected(SourceStateEventData eventData)
        {
            // Nothing to do
        }

        public void OnSourceLost(SourceStateEventData eventData)
        {
            // event not used in TapToPlace
            if (DraggingMethod == DraggingMethods.TapToPlaceWithGaze)
                return;

            if (currentInputSource != null && eventData.SourceId == currentInputSourceId)
            {
                if (IsBeingPlaced == IsBeingPlacedStates.ByHand)
                {
                    // if hand is lost, replace by gaze
                    if (DraggingMethod == DraggingMethods.TapToPlaceWithGazePlusPreciseHandDrag)
                        IsBeingPlaced = IsBeingPlacedStates.ByGaze;
                    else
                        StopDragging();
                }
            }
        }

        public void OnInputClicked(InputEventData eventData)
        {
            if (DraggingMethod == DraggingMethods.TapToPlaceWithGaze
                || DraggingMethod == DraggingMethods.TapToPlaceWithGazePlusPreciseHandDrag)
            {
                // On each tap gesture, toggle whether the user is in placing mode.
                if (IsBeingPlaced == IsBeingPlacedStates.ByGaze
                    // Handles the case where the inputDown event is raising a ByHand and loosing the Gaze state
                    // AirTap must be quick and clean with no UpdateDragging
                    || (IsBeingPlaced == IsBeingPlacedStates.ByHand && previousPlacingMethod == IsBeingPlacedStates.ByGaze))
                    StopDragging();
                else
                    StartDragging(IsBeingPlacedStates.ByGaze);
            }
        }
    }
}

// Free, DragByHand OK
// Free+Spatial, Gaze (IHeartU) OK
// Free, Gaze&Precise: (Beauty) OK
// Free+Spatial, DragByHand: problem avec le heart (offset quand il jump sur le mur)
