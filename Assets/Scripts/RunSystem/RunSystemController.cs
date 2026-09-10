using UnityEngine;
using Unity.XR.CoreUtils;

public class RunSystemController : MonoBehaviour
{
    [SerializeField] private Transform guidedSpawnPoint;

    public void StartGuidedRun()
    {
        XROrigin xrOrigin = FindFirstObjectByType<XROrigin>();

        if (xrOrigin == null)
        {
            Debug.LogError("RunSystemController could not find the XR Origin.", this);
            return;
        }

        if (guidedSpawnPoint == null)
        {
            Debug.LogError("RunSystemController has no guided spawn point assigned.", this);
            return;
        }

        CharacterController characterController =
            xrOrigin.GetComponent<CharacterController>();

        if (characterController != null)
            characterController.enabled = false;

        xrOrigin.transform.SetPositionAndRotation(
            guidedSpawnPoint.position,
            Quaternion.Euler(0f, guidedSpawnPoint.eulerAngles.y, 0f)
        );

        if (characterController != null)
            characterController.enabled = true;
    }
}
