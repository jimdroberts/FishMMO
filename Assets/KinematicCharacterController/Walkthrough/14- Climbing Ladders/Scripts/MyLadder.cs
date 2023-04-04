using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace KinematicCharacterController.Walkthrough.ClimbingLadders
{
    public class MyLadder : MonoBehaviour
    {
        // Ladder segment
        public Vector3 LadderSegmentBottom;
        public float LadderSegmentLength;

        // Points to move to when reaching one of the extremities and moving off of the ladder
        public Transform BottomReleasePoint;
        public Transform TopReleasePoint;

        // Gets the position of the bottom point of the ladder segment
        public Vector3 BottomAnchorPoint
        {
            get
            {
                return transform.position + transform.TransformVector(LadderSegmentBottom);
            }
        }

        // Gets the position of the top point of the ladder segment
        public Vector3 TopAnchorPoint
        {
            get
            {
                return transform.position + transform.TransformVector(LadderSegmentBottom) + (transform.up * LadderSegmentLength);
            }
        }

        public Vector3 ClosestPointOnLadderSegment(Vector3 fromPoint, out float onSegmentState)
        {
            Vector3 segment = TopAnchorPoint - BottomAnchorPoint;            
            Vector3 segmentPoint1ToPoint = fromPoint - BottomAnchorPoint;
            float pointProjectionLength = Vector3.Dot(segmentPoint1ToPoint, segment.normalized);

            // When higher than bottom point
            if(pointProjectionLength > 0)
            {
                // If we are not higher than top point
                if (pointProjectionLength <= segment.magnitude)
                {
                    onSegmentState = 0;
                    return BottomAnchorPoint + (segment.normalized * pointProjectionLength);
                }
                // If we are higher than top point
                else
                {
                    onSegmentState = pointProjectionLength - segment.magnitude;
                    return TopAnchorPoint;
                }
            }
            // When lower than bottom point
            else
            {
                onSegmentState = pointProjectionLength;
                return BottomAnchorPoint;
            }
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(BottomAnchorPoint, TopAnchorPoint);
        }
    }
}