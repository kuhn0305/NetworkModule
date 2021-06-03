using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
/// <summary>
/// OVRCameraRig에 component로 넣어서 centerEyeAnchor만 넣어주고 사용.
/// HMD를 착용했을 때 자동으로 씬 안의 OVRCameraRig 설정된 위치로 뷰 재설정.
/// </summary>
public class AutomaticViewReset : MonoBehaviour
{
    public Transform _centerEyeAnchor;

    private Vector3 movingPosition;
    private Vector3 movingRotation;
    private Vector3 originPosition;
    private Vector3 originRotation;

    private void Awake()
    {
        originPosition = transform.position;
        originRotation = transform.eulerAngles;
    }

    private void OnEnable()
    {
        OVRManager.HMDMounted += ResetViewToOrigin;
        OVRManager.HMDUnmounted += ResetCameraRigToOrigin;
    }

    private void ResetViewToOrigin()
    {
        movingRotation = _centerEyeAnchor.transform.eulerAngles;
        transform.eulerAngles = new Vector3(0, -movingRotation.y, 0);
        movingPosition = _centerEyeAnchor.transform.position;
        transform.position = -movingPosition;
    }

    private void ResetCameraRigToOrigin()
    {
        transform.position = originPosition;
        transform.eulerAngles = originRotation;
    }
}
