using System.Collections.Generic;
using Core.Networking;
using LROTech.Network.GameObjects;
using LROTech.Network.StateManagement;
using UnityEngine;
using UnityEngine.Serialization;

namespace LROTech.NetworkBehaviours
{
    [DisallowMultipleComponent]
    public sealed class NetObjectTransformSync : NetObjectBehaviour<NetObjectTransformData>
    {
        [FormerlySerializedAs("m_InterpolateMovement")] [SerializeField, Range(0f, 1f)]
        private float _interpolateMovement = 1f;

        [FormerlySerializedAs("m_InterpolateRotation")] [SerializeField, Range(0f, 1f)]
        private float _interpolateRotation = 1f;

        [FormerlySerializedAs("m_Prediction")] [SerializeField]
        private bool _prediction = true;

        [FormerlySerializedAs("m_PredictionMultiplier")] [FormerlySerializedAs("m_PredictionMultipler")] [SerializeField, Range(0.1f, 2f)]
        private float _predictionMultiplier = 1f;

        [FormerlySerializedAs("m_NestedTransforms")] [SerializeField]
        private List<Transform> _nestedTransforms = new();

        private Vector3 _lastFramePosition;
        private Vector3 _velocity;

#if UNITY_EDITOR
        private void OnValidate()
        {
            for (var i = _nestedTransforms.Count - 1; i >= 0; i--)
            {
                if (_nestedTransforms[i].IsChildOf(transform))
                    continue;

                _nestedTransforms.RemoveAt(i);
                Debug.LogError($"'{_nestedTransforms[i].gameObject.name}' is not a child of '{gameObject.name}'");
            }
        }
#endif

        protected override void OnInitializeViewData()
        {
            base.OnInitializeViewData();
            ResetTemporaryData();
            if (IsLocal)
            {
                Data.Position.Value = transform.position;
                Data.Rotation.Value = transform.rotation;
            }
        }

        private void ResetTemporaryData()
        {
            _lastFramePosition = transform.position;
            _velocity = Vector3.zero;

            if (IsLocal)
                return;

            transform.position = Data.Position.Value;
            transform.rotation = Data.Rotation.Value;
        }

        private void Update()
        {
            if (!Identity.Initialized)
                return;

            var hasParentLocally = transform.parent != transform.root && transform.parent != null;

            _velocity = transform.position - _lastFramePosition;
            _lastFramePosition = transform.position;

            if (IsLocal)
            {
                Data.Position.Value = transform.position;
                Data.Rotation.Value = transform.rotation;

                Data.Velocity.Value = _velocity;
                Data.WorldTime.Value = Identity.World.Time;
                Data.HasParent.Value = hasParentLocally;
            }
            else
            {
                if (!hasParentLocally && !Data.HasParent.Value)
                {
                    // Position interpolation.
                    if (_interpolateMovement > Mathf.Epsilon)
                    {
                        var targetPosition = Data.Position.Value;
                        if (_prediction)
                            targetPosition += Data.Velocity.Value.normalized * ((Identity.World.Time - Data.WorldTime.Value) * _predictionMultiplier);

                        transform.position = Vector3.Lerp(
                            transform.position,
                            targetPosition,
                            Time.deltaTime * _interpolateMovement * 10f);
                    }
                    else
                        transform.position = Data.Position.Value;

                    // Rotation interpolation.
                    if (_interpolateRotation > Mathf.Epsilon)
                    {
                        transform.rotation = Quaternion.Slerp(
                            transform.rotation,
                            Data.Rotation.Value,
                            Time.deltaTime * _interpolateRotation * 10f);
                    }
                    else
                        transform.rotation = Data.Rotation.Value;
                }
            }
        }

        protected override void OnSerialize(NetWriter writer)
        {
            base.OnSerialize(writer);

            foreach (var nestedTransform in _nestedTransforms)
            {
                writer.WriteVector3(nestedTransform.localPosition);
                writer.WriteQuaternion(nestedTransform.localRotation);
            }
        }

        protected override void OnDeserialize(NetReader reader)
        {
            base.OnDeserialize(reader);

            foreach (var nestedTransform in _nestedTransforms)
            {
                nestedTransform.localPosition = reader.ReadVector3();
                nestedTransform.localRotation = reader.ReadQuaternion();
            }
        }
    }
}