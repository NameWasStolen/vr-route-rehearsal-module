using UnityEngine;
using UnityEngine.Serialization;
using Unity.XR.CoreUtils;

public class RunSystemController : MonoBehaviour
{
	[FormerlySerializedAs("guidedSpawnPoint")]
	[SerializeField] private Transform runStartPoint;

	public void StartRun()
	{
		XROrigin xrOrigin = FindFirstObjectByType<XROrigin>();

		if (xrOrigin == null)
		{
			Debug.LogError("RunSystemController could not find the XR Origin.", this);
			return;
		}

		if (runStartPoint == null)
		{
			Debug.LogError("RunSystemController has no run start point assigned.", this);
			return;
		}

		CharacterController characterController =
			xrOrigin.GetComponent<CharacterController>();

		if (characterController != null)
			characterController.enabled = false;

		xrOrigin.transform.SetPositionAndRotation(
			runStartPoint.position,
			Quaternion.Euler(0f, runStartPoint.eulerAngles.y, 0f)
		);

		if (characterController != null)
			characterController.enabled = true;
	}
}
