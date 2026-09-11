using UnityEngine;
using Unity.XR.CoreUtils;

public static class XRPlayerTeleport
{
    public static bool MoveToStandingPoint(XROrigin xrOrigin, Transform standingPoint)
    {
        if (xrOrigin == null || standingPoint == null)
            return false;

        CharacterController characterController =
            xrOrigin.GetComponent<CharacterController>();

        if (characterController != null)
            characterController.enabled = false;

        xrOrigin.transform.rotation = Quaternion.Euler(
            0f,
            standingPoint.eulerAngles.y,
            0f
        );

        Vector3 cameraOffset = xrOrigin.Camera.transform.position - xrOrigin.transform.position;
        Vector3 horizontalOffset = new Vector3(cameraOffset.x, 0f, cameraOffset.z);
        xrOrigin.transform.position = standingPoint.position - horizontalOffset;

        if (characterController != null)
            characterController.enabled = true;

        return true;
    }
}
