using UnityEngine;
using UnityEngine.XR.Hands;

public static class HandKinematics
{
    public static float SignedFlexionAngle(Vector3 A, Vector3 B, Vector3 C, Vector3 palmNormal)
    {
        Vector3 v1 = (B - A).normalized;
        Vector3 v2 = (C - B).normalized;

        float dot = Mathf.Clamp(Vector3.Dot(v1, v2), -1f, 1f);
        float angle = Mathf.Acos(dot); // 0..pi

        Vector3 cross = Vector3.Cross(v1, v2);
        float sign = Mathf.Sign(Vector3.Dot(cross, palmNormal.normalized));
        return angle * sign;
    }

    public static bool TryGetJointPos(XRHand hand, XRHandJointID id, out Vector3 pos)
    {
        pos = default;
        var j = hand.GetJoint(id);
        if (!j.TryGetPose(out var p)) return false;
        pos = p.position;
        return true;
    }

    public static Vector3 EstimatePalmNormal(XRHand hand)
    {
        if (TryGetJointPos(hand, XRHandJointID.Palm, out var palm) &&
            TryGetJointPos(hand, XRHandJointID.Wrist, out var wrist) &&
            TryGetJointPos(hand, XRHandJointID.MiddleMetacarpal, out var midMeta))
        {
            var a = (midMeta - palm).normalized;
            var b = (wrist   - palm).normalized;
            return Vector3.Cross(a, b).normalized;
        }
        return Vector3.up;
    }
}
