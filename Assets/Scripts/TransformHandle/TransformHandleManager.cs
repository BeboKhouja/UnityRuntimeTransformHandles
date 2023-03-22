using System;
using System.Collections.Generic;
using TransformHandle.Utils;
using UnityEngine;
using UnityEngine.Serialization;

namespace TransformHandle
{
    public class TransformHandleManager : Singleton<TransformHandleManager>
    {
        public Camera mainCamera;
        
        [SerializeField] private GameObject transformHandlePrefab;
        [SerializeField] private GameObject ghostPrefab;
        
        [SerializeField] private LayerMask layerMask;
        
        [SerializeField] private Color highlightColor = Color.white;
        
        private RaycastHit[] _rayHits;
        
        private Vector3 _previousMousePosition;
        private Vector3 _handleHitPoint;

        private HandleBase _previousAxis;
        private HandleBase _draggingHandle;
        private HandleBase _hoveredHandle;
        
        private Ghost _interactedGhost;
        private Handle _interactedHandle;
        
        private HashSet<Transform> _transformHashSet;
        private Dictionary<Handle, TransformGroup> _handleGroupMap;
        private Dictionary<Ghost, TransformGroup> _ghostGroupMap;
        
        private bool _handleActive;

        protected override void Awake()
        {
            base.Awake();
            
            mainCamera = mainCamera == null ? Camera.main : mainCamera;
            
            _handleGroupMap = new Dictionary<Handle, TransformGroup>();
            _ghostGroupMap = new Dictionary<Ghost, TransformGroup>();
            _transformHashSet = new HashSet<Transform>();
        }

        public Handle CreateHandle(Transform target)
        {
            if (_transformHashSet.Contains(target)) { Debug.LogWarning($"{target} already has a handle."); return null; }
            
            var ghost = Instantiate(ghostPrefab).GetComponent<Ghost>();
            ghost.Initialize();

            var transformHandle = Instantiate(transformHandlePrefab).GetComponent<Handle>();
            transformHandle.Enable(ghost.transform);            
            
            var group = new TransformGroup(ghost, transformHandle);
            
            _handleGroupMap.Add(transformHandle, group);
            _ghostGroupMap.Add(ghost, group);
            
            AddTarget(target, transformHandle);
            
            _handleActive = true;

            return transformHandle;
        }
        
        public Handle CreateHandleFromList(List<Transform> targets)
        {
            if(targets.Count == 0) { Debug.LogWarning("List is empty."); return null; }
            
            var ghost = Instantiate(ghostPrefab).GetComponent<Ghost>();
            ghost.Initialize();

            var transformHandle = Instantiate(transformHandlePrefab).GetComponent<Handle>();
            transformHandle.Enable(ghost.transform);      
            
            var group = new TransformGroup(ghost, transformHandle);
            _handleGroupMap.Add(transformHandle, group);
            _ghostGroupMap.Add(ghost, group);

            foreach (var target in targets)
            {
                if (_transformHashSet.Contains(target))
                {
                    Debug.LogWarning($"{target} already has a handle."); 
                    DestroyHandle(transformHandle);
                    return null;
                }
                AddTarget(target, transformHandle);
            }

            _handleActive = true;

            return transformHandle;
        }
        
        public void DestroyHandle(Handle handle)
        {
            if(handle == null) { Debug.LogError("Handle is already null"); return;}
            
            var group = _handleGroupMap[handle];
            _handleGroupMap.Remove(handle);
            _ghostGroupMap.Remove(group.GroupGhost);
            
            handle.Disable();
            group.GroupGhost.Terminate();
            
            DestroyImmediate(handle.gameObject);
            
            if (_handleGroupMap.Count == 0) _handleActive = false;
        }
        
        public void DestroyAllHandles()
        {
            foreach (var handle in _handleGroupMap.Keys)
            {
                DestroyHandle(handle);
            }
        }
        
        public void AddTarget(Transform target, Handle handle)
        {
            if (_transformHashSet.Contains(target)) { Debug.LogWarning($"{target} already has a handle."); return; }
            if(handle == null) { Debug.LogError("Handle is null"); return;}

            var group = _handleGroupMap[handle];
            var targetAdded = group.AddTransform(target);
            if(!targetAdded) { Debug.LogWarning($"{target} is relative to the selected ones."); return; }
            
            var averagePosRotScale = group.GetAveragePosRotScale();
            group.GroupGhost.UpdateGhostTransform(averagePosRotScale);
            
            _transformHashSet.Add(target);
        }

        public void RemoveTarget(Transform target, Handle handle)
        {
            if (!_transformHashSet.Contains(target)) { Debug.LogWarning($"{target} doesn't have a handle."); return;}
            if(handle == null) { Debug.LogError("Handle is null"); return;}

            _transformHashSet.Remove(target);
            
            var group = _handleGroupMap[handle];
            var groupElementsRemoved = group.RemoveTransform(target);
            if (groupElementsRemoved) { DestroyHandle(handle); return; }
            
            var averagePosRotScale = group.GetAveragePosRotScale();
            group.GroupGhost.UpdateGhostTransform(averagePosRotScale);
        }
        
        protected virtual void Update()
        {
            if (!_handleActive) return;
            
            _hoveredHandle = null;
            _handleHitPoint = Vector3.zero;
            
            GetHandle(ref _hoveredHandle, ref _handleHitPoint);
            
            HandleOverEffect(_hoveredHandle);

            MouseInput();
            KeyboardInput();
        }
        
        private void GetHandle(ref HandleBase handle, ref Vector3 hitPoint)
        {
            _rayHits = new RaycastHit[16];

            var ray = mainCamera.ScreenPointToRay(Input.mousePosition);
            var size = Physics.RaycastNonAlloc(ray, _rayHits, 1000, layerMask);
                
            if (size == 0)
            {
                if (_hoveredHandle == null) return;
                return;
            }
            
            Array.Sort(_rayHits, (x,y) => x.distance.CompareTo(y.distance));

            foreach (var hit in _rayHits)
            {
                var hitCollider = hit.collider;
                if (hitCollider == null) continue;
                handle = hit.collider.gameObject.GetComponentInParent<HandleBase>();

                if (handle == null) continue;
                hitPoint = hit.point;
                return;
            }
        }

        private void HandleOverEffect(HandleBase handleBase)
        {
            if (_draggingHandle == null && _previousAxis != null && _previousAxis != handleBase)
            {
                _previousAxis.SetDefaultColor();
            }

            if (handleBase != null && _draggingHandle == null)
            {
                handleBase.SetColor(highlightColor);
            }

            _previousAxis = handleBase;
        }

        private void MouseInput()
        {
            if (Input.GetMouseButton(0) && _draggingHandle != null)
            {
                _draggingHandle.Interact(_previousMousePosition);
                OnInteraction();
            }

            if (Input.GetMouseButtonDown(0) && _hoveredHandle != null)
            {
                _draggingHandle = _hoveredHandle;
                _draggingHandle.StartInteraction(_handleHitPoint);
                OnInteractionStart();
            }

            if (Input.GetMouseButtonUp(0) && _draggingHandle != null)
            {
                _draggingHandle.EndInteraction();
                _draggingHandle = null;
                OnInteractionEnd();
            }

            _previousMousePosition = Input.mousePosition;
        }
        
        private void KeyboardInput()
        {
            if (Input.GetKeyDown(KeyCode.W))
            {
                foreach (var handle in _handleGroupMap.Keys)
                {
                    ChangeHandleType(handle, HandleType.Position);
                }
            }
            
            if (Input.GetKeyDown(KeyCode.E))
            {
                foreach (var handle in _handleGroupMap.Keys)
                {
                    ChangeHandleType(handle, HandleType.Rotation);
                }
            }
            
            if (Input.GetKeyDown(KeyCode.R))
            {
                foreach (var handle in _handleGroupMap.Keys)
                {
                    ChangeHandleType(handle, HandleType.Scale);
                }
            }
            
            if (Input.GetKeyDown(KeyCode.Q))
            {
                foreach (var handle in _handleGroupMap.Keys)
                {
                    ChangeHandleType(handle, HandleType.All);
                }
            }

            if (Input.GetKeyDown(KeyCode.X))
            {
                foreach (var handle in _handleGroupMap.Keys)
                {
                    ChangeHandleSpace(handle, handle.space == Space.World ? Space.Self : Space.World);
                }
            }

            if (Input.GetKeyDown(KeyCode.Z))
            {
                foreach (var group in _handleGroupMap.Values)
                {
                    ChangeHandlePivot(group, !group.IsOriginOnCenter);
                }
            }
        }

        private void OnInteractionStart()
        {
            _interactedHandle = _draggingHandle.GetComponentInParent<Handle>();
            _interactedGhost = _handleGroupMap[_interactedHandle].GroupGhost;
            _interactedGhost.OnInteractionStart();
        }

        private void OnInteraction()
        {
            _interactedGhost.OnInteraction(_interactedHandle.type);
        }

        private void OnInteractionEnd()
        {
            var group = _handleGroupMap[_interactedHandle];
            group.UpdateBounds();

            var averagePosRotScale = group.GetAveragePosRotScale();
            _interactedGhost.UpdateGhostTransform(averagePosRotScale);
        }
        
        public void ChangeHandleType(Handle handle, HandleType type)
        {
            if(handle == null) { Debug.LogError("Handle is null"); return;}
            handle.ChangeHandleType(type);
        }

        public void ChangeHandleSpace(Handle handle, Space space)
        {
            if(handle == null) { Debug.LogError("Handle is null"); return;}
            handle.ChangeHandleSpace(space);
            
            var group = _handleGroupMap[handle];
            group.GroupGhost.UpdateGhostTransform(group.GetAveragePosRotScale());
        }

        public void ChangeHandlePivot(TransformGroup group, bool originToCenter)
        {
            if(group == null) { Debug.LogError("Group is null"); return;}
            group.IsOriginOnCenter = originToCenter;
            group.GroupGhost.UpdateGhostTransform(group.GetAveragePosRotScale());
        }
        
        public void GroupPositionUpdate(Ghost ghost, Vector3 positionChange)
        {
            var group = _ghostGroupMap[ghost];
            group.UpdatePositions(positionChange);
        }
        
        public void GroupRotationUpdate(Ghost ghost, Quaternion rotationChange)
        {
            var group = _ghostGroupMap[ghost];
            group.UpdateRotations(rotationChange);
        }
        
        public void GroupScaleUpdate(Ghost ghost, Vector3 scaleChange)
        {
            var group = _ghostGroupMap[ghost];
            group.UpdateScales(scaleChange);
        }
    }
    
    public class TransformGroup
    {
        public Ghost GroupGhost {get; private set;}
        public Handle GroupHandle {get; private set;}
        
        public bool IsOriginOnCenter;

        public HashSet<Transform> TransformHashSet;
        private Dictionary<Transform, MeshRenderer> _transformRendererMap;
        private Dictionary<Transform, Bounds> _transformBoundsMap;

        public TransformGroup(Ghost groupGhost, Handle groupHandle)
        {
            GroupGhost = groupGhost;
            GroupHandle = groupHandle;
            
            TransformHashSet = new HashSet<Transform>();
            _transformRendererMap = new Dictionary<Transform, MeshRenderer>();
            _transformBoundsMap = new Dictionary<Transform, Bounds>();
        }

        public bool AddTransform(Transform tElement)
        {
            if (IsTargetRelativeToSelectedOnes(tElement)) return false;
            
            var meshRenderer = tElement.GetComponent<MeshRenderer>();
            
            TransformHashSet.Add(tElement);
            _transformRendererMap.Add(tElement, meshRenderer);
            _transformBoundsMap.Add(tElement, meshRenderer != null ? meshRenderer.bounds : tElement.GetBounds());

            return true;
        }
        
        public bool RemoveTransform(Transform transform)
        {
            TransformHashSet.Remove(transform);
            _transformRendererMap.Remove(transform);
            _transformBoundsMap.Remove(transform);
            
            return TransformHashSet.Count == 0;
        }

        public void UpdateBounds()
        {
            foreach (var (tElement, meshRenderer) in _transformRendererMap)
            {
                var bounds = meshRenderer ? meshRenderer.bounds : tElement.GetBounds();
                _transformBoundsMap[tElement] = bounds;
            }
        }
        
        public void UpdatePositions(Vector3 positionChange)
        {
            foreach (var tElement in _transformRendererMap.Keys)
            {
                tElement.position += positionChange;
            }
        }

        public void UpdateRotations(Quaternion rotationChange)
        {
            var ghostPosition = GroupGhost.transform.position;
            var rotationAxis = rotationChange.normalized.eulerAngles;
            var rotationChangeMagnitude = rotationChange.eulerAngles.magnitude;
            foreach (var tElement in _transformRendererMap.Keys)
            {
                if (GroupHandle.space == Space.Self)
                {
                    tElement.position = rotationChange * (tElement.position - ghostPosition) + ghostPosition;
                    tElement.rotation = rotationChange * tElement.rotation;
                }
                else
                {
                    tElement.RotateAround(ghostPosition, rotationAxis, rotationChangeMagnitude);
                }
            }
        }

        public void UpdateScales(Vector3 scaleChange)
        {
            foreach (var (tElement, meshRenderer) in _transformRendererMap)
            {
                if (IsOriginOnCenter)
                {
                    if (meshRenderer != null)
                    {
                        var oldCenter = meshRenderer.bounds.center;

                        tElement.localScale += scaleChange;
            
                        // ReSharper disable once Unity.InefficientPropertyAccess
                        var newCenter =  meshRenderer.bounds.center;

                        var change = newCenter - oldCenter;
                
                        tElement.position += change * -1;
                    }
                    else
                    {
                        tElement.localScale += scaleChange;
                    }
                }
                else
                {
                    tElement.localScale += scaleChange;
                }
            }
        }

        private Vector3 GetCenterPoint(Transform tElement)
        {
            return IsOriginOnCenter ? _transformBoundsMap[tElement].center : tElement.position;
        }
        
        public PosRotScale GetAveragePosRotScale()
        {
            var space = GroupHandle.space;
            
            var averagePosRotScale = new PosRotScale();
            
            var centerPositions = new List<Vector3>();
            var sumQuaternion = Quaternion.identity;

            var transformsCount = TransformHashSet.Count;

            foreach (var tElement in TransformHashSet)
            {
                var centerPoint = GetCenterPoint(tElement);
                centerPositions.Add(centerPoint);
                
                if (space == Space.World) continue;
                sumQuaternion *= tElement.rotation;
            }

            var averagePosition = Vector3.zero;
            foreach (var centerPosition in centerPositions)
            {
                averagePosition += centerPosition;
            }
            averagePosition /= transformsCount;

            averagePosRotScale.Position = averagePosition;
            averagePosRotScale.Rotation = sumQuaternion;
            averagePosRotScale.Scale = Vector3.one;

            return averagePosRotScale;
        }
        
        private bool IsTargetRelativeToSelectedOnes(Transform newTarget)
        {
            foreach (var transformInHash in TransformHashSet)
            {
                if (transformInHash.IsDeepParentOf(newTarget)) return true;

                if (!newTarget.IsDeepParentOf(transformInHash)) continue;
                RemoveTransform(transformInHash);
                return false;
            }

            return false;
        }
    }
}