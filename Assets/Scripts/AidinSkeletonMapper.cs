using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using RosMessageTypes.Sensor;

public class AidinSkeletonMapper : HandSkeletonMapperBase
{
    public override void Publish(XRHand leftHand, Transform leftAnchor,
                                 XRHand rightHand, Transform rightAnchor)
    {
        // 왼손
        if (leftAnchor && leftHand.isTracked)
        {
            FillJointState(leftHand, true, leftMsg);
            ros.Publish(leftJointTopic, leftMsg);
        }
        // 오른손
        if (rightAnchor && rightHand.isTracked)
        {
            FillJointState(rightHand, false, rightMsg);
            ros.Publish(rightJointTopic, rightMsg);
        }
    }

    void FillJointState(XRHand hand, bool isLeft, JointStateMsg msg)
    {
        var names = new List<string>(16);
        var pos   = new List<double>(16);

        Vector3 palmN = HandKinematics.EstimatePalmNormal(hand);
        string prefix = isLeft ? "left" : "right";

        // 공통 4지: index, middle, ring, little(baby)
        MapThreeDOFFinger(hand, XRHandJointID.IndexMetacarpal, XRHandJointID.IndexProximal,
                                  XRHandJointID.IndexIntermediate, XRHandJointID.IndexDistal,
                                  XRHandJointID.IndexTip, $"{prefix}_index", palmN, names, pos);

        MapThreeDOFFinger(hand, XRHandJointID.MiddleMetacarpal, XRHandJointID.MiddleProximal,
                                  XRHandJointID.MiddleIntermediate, XRHandJointID.MiddleDistal,
                                  XRHandJointID.MiddleTip, $"{prefix}_middle", palmN, names, pos);

        MapThreeDOFFinger(hand, XRHandJointID.RingMetacarpal, XRHandJointID.RingProximal,
                                  XRHandJointID.RingIntermediate, XRHandJointID.RingDistal,
                                  XRHandJointID.RingTip, $"{prefix}_ring", palmN, names, pos);

        MapThreeDOFFinger(hand, XRHandJointID.LittleMetacarpal, XRHandJointID.LittleProximal,
                                  XRHandJointID.LittleIntermediate, XRHandJointID.LittleDistal,
                                  XRHandJointID.LittleTip, $"{prefix}_baby", palmN, names, pos);

        // 엄지
        if (TryThumbAngles(hand, palmN, out float t1, out float t2))
        {
            names.Add($"{prefix}_thumb_joint1"); pos.Add(t1);
            names.Add($"{prefix}_thumb_joint2"); pos.Add(t2);
            names.Add($"{prefix}_thumb_joint3"); pos.Add(0.0); // 필요시 외전/내전 추정값으로 대체
        }

        msg.name = names.ToArray();
        msg.position = pos.ToArray();
        msg.velocity = System.Array.Empty<double>();
        msg.effort   = System.Array.Empty<double>();
        // msg.header.frame_id = isLeft ? "hand_left" : "hand_right"; // 원하면 지정
    }

    static void MapThreeDOFFinger(
        XRHand hand,
        XRHandJointID meta, XRHandJointID prox, XRHandJointID inter, XRHandJointID dist, XRHandJointID tip,
        string namePrefix, Vector3 palmN,
        List<string> outNames, List<double> outPos)
    {
        if (!HandKinematics.TryGetJointPos(hand, meta,  out var pMeta))  return;
        if (!HandKinematics.TryGetJointPos(hand, prox,  out var pProx))  return;
        if (!HandKinematics.TryGetJointPos(hand, inter, out var pInter)) return;
        if (!HandKinematics.TryGetJointPos(hand, dist,  out var pDist))  return;
        if (!HandKinematics.TryGetJointPos(hand, tip,   out var pTip))   return;

        float j1 = HandKinematics.SignedFlexionAngle(pMeta,  pProx,  pInter, palmN); // proximal
        float j2 = HandKinematics.SignedFlexionAngle(pProx,  pInter, pDist,  palmN); // intermediate
        float j3 = HandKinematics.SignedFlexionAngle(pInter, pDist,  pTip,   palmN); // distal

        outNames.Add($"{namePrefix}_joint1"); outPos.Add(j1);
        outNames.Add($"{namePrefix}_joint2"); outPos.Add(j2);
        outNames.Add($"{namePrefix}_joint3"); outPos.Add(j3);
    }

    static bool TryThumbAngles(XRHand hand, Vector3 palmN, out float j1, out float j2)
    {
        j1 = j2 = 0f;
        if (!HandKinematics.TryGetJointPos(hand, XRHandJointID.ThumbMetacarpal, out var tMeta)) return false;
        if (!HandKinematics.TryGetJointPos(hand, XRHandJointID.ThumbProximal,   out var tProx)) return false;
        if (!HandKinematics.TryGetJointPos(hand, XRHandJointID.ThumbDistal,     out var tDist)) return false;
        if (!HandKinematics.TryGetJointPos(hand, XRHandJointID.ThumbTip,        out var tTip )) return false;

        j1 = HandKinematics.SignedFlexionAngle(tMeta, tProx, tDist, palmN); // joint1
        j2 = HandKinematics.SignedFlexionAngle(tProx, tDist, tTip,  palmN); // joint2
        return true;
    }
}
