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
        [SerializeField, Range(0f, 1f)]
        private float m_InterpolateMovement = 1f;

        [SerializeField, Range(0f, 1f)]
        private float m_InterpolateRotation = 1f;

        [SerializeField]
        private bool m_Prediction = true;

        [FormerlySerializedAs("m_PredictionMultipler")] 
        [SerializeField, Range(0.1f, 2f)]
        private float m_PredictionMultiplier = 1f;

        [SerializeField]
        private List<Transform> m_NestedTransforms = new ();

        private Vector3 m_LastFramePosition;
        private Vector3 m_Velocity;

#if UNITY_EDITOR
        private void OnValidate()
        {
            for (var i = m_NestedTransforms.Count - 1; i >= 0; i--)
            {
                if (m_NestedTransforms[i].IsChildOf(transform))
                    continue;

                m_NestedTransforms.RemoveAt(i);
                Debug.LogError($"'{m_NestedTransforms[i].gameObject.name}' is not a child of '{gameObject.name}'");
            }
        }
#endif

        protected override void OnInitializeViewData()
        {
            base.OnInitializeViewData();
            ResetTemporaryData();
            if (IsLocal)
            {
                Data.Position.Set(transform.position);
                Data.Rotation.Set(transform.rotation);
            }
        }

        private void ResetTemporaryData()
        {
            m_LastFramePosition = transform.position;
            m_Velocity = Vector3.zero;

            if (IsLocal) 
                return;
        
            transform.position = Data.Position.Value;
            transform.rotation = Data.Rotation.Value;
        }

        private void FixedUpdate()
        {
            if (!Identity.Initialized)
                return;

            var hasParentLocally = transform.parent != transform.root && transform.parent != null;

            m_Velocity = transform.position - m_LastFramePosition;
            m_LastFramePosition = transform.position;

            if (IsLocal)
            {
                Data.Position.Set(transform.position);
                Data.Rotation.Set(transform.rotation);

                Data.Velocity.Set(m_Velocity);
                Data.WorldTime.Set(Identity.World.Time);
                Data.HasParent.Set(hasParentLocally);
            }
            else
            {
                if (!hasParentLocally && !Data.HasParent.Value)
                {
                    // Position interpolation.
                    if (m_InterpolateMovement > Mathf.Epsilon)
                    {
                        var targetPosition = Data.Position.Value;
                        if (m_Prediction)
                            targetPosition += Data.Velocity.Value.normalized * ((Identity.World.Time - Data.WorldTime.Value) * m_PredictionMultiplier);

                        transform.position = Vector3.Lerp(
                            transform.position,
                            targetPosition,
                            Time.fixedDeltaTime * m_InterpolateMovement * 10);
                    }
                    else
                        transform.position = Data.Position.Value;

                    // Rotation interpolation.
                    if (m_InterpolateRotation > Mathf.Epsilon)
                    {
                        transform.rotation = Quaternion.Slerp(
                            transform.rotation,
                            Data.Rotation.Value,
                            Time.fixedDeltaTime * m_InterpolateRotation * 10);
                    }
                    else
                        transform.rotation = Data.Rotation.Value;
                }
            }
        }

        protected override void OnSerialize(NetWriter writer)
        {
            base.OnSerialize(writer);

            foreach (var nestedTransform in m_NestedTransforms)
            {
                writer.WriteVector3(nestedTransform.localPosition);
                writer.WriteQuaternion(nestedTransform.localRotation);
            }
        }

        protected override void OnDeserialize(NetReader reader, bool firstTime)
        {
            base.OnDeserialize(reader, firstTime);

            foreach (var nestedTransform in m_NestedTransforms)
            {
                nestedTransform.localPosition = reader.ReadVector3();
                nestedTransform.localRotation = reader.ReadQuaternion();
            }
        }
    }
}
