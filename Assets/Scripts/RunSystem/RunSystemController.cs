using UnityEngine;
using UnityEngine.Serialization;
using Unity.XR.CoreUtils;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using System.Collections;
using VRTutorial;

public class RunSystemController : MonoBehaviour
{
	[FormerlySerializedAs("guidedSpawnPoint")]
	[SerializeField] private Transform runStartPoint;

	private TimerController timerController;
	private PlayerPositionTracker positionTracker;
	private AssistanceController assistanceController;
	private WrongTurnController wrongTurnController;
	private XROrigin xrOrigin;
	private bool isEndingRun;
	private string runType = "run";

	private void OnEnable()
	{
		Debug.Log($"RunSystemController enabled in scene '{gameObject.scene.name}'.", this);
		timerController = FindFirstObjectByType<TimerController>();
		positionTracker = GetComponent<PlayerPositionTracker>();
		if (positionTracker == null)
			positionTracker = gameObject.AddComponent<PlayerPositionTracker>();

		assistanceController = FindFirstObjectByType<AssistanceController>(FindObjectsInactive.Include);
		if (assistanceController != null)
			assistanceController.onRequested.AddListener(HandleAssistanceRequested);

		wrongTurnController = FindFirstObjectByType<WrongTurnController>(FindObjectsInactive.Include);
		if (wrongTurnController != null)
			wrongTurnController.WrongTurnRecorded += HandleWrongTurnRecorded;

		if (timerController != null)
		{
			timerController.RunStarted += HandleRunStarted;
			timerController.RunEnded += HandleRunEnded;
		}
		else
			Debug.LogError("RunSystemController could not find TimerController.", this);
	}

	private void OnDisable()
	{
		if (timerController != null)
		{
			timerController.RunStarted -= HandleRunStarted;
			timerController.RunEnded -= HandleRunEnded;
		}

		if (assistanceController != null)
			assistanceController.onRequested.RemoveListener(HandleAssistanceRequested);

		if (wrongTurnController != null)
			wrongTurnController.WrongTurnRecorded -= HandleWrongTurnRecorded;
	}

	public void StartRun()
	{
		StartRun("run");
	}

	public void StartRun(string selectedRunType)
	{
		runType = string.IsNullOrWhiteSpace(selectedRunType)
			? "run"
			: selectedRunType;
		xrOrigin = FindFirstObjectByType<XROrigin>();

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

		XRPlayerTeleport.MoveToStandingPoint(
			xrOrigin,
			runStartPoint,
			this
		);
	}

	private void HandleRunStarted()
	{
		if (positionTracker == null || xrOrigin == null)
			return;

		if (xrOrigin.Camera == null)
		{
			Debug.LogError("RunSystemController could not find the XR camera.", this);
			return;
		}

		SettingsController settingsController =
			FindFirstObjectByType<SettingsController>(FindObjectsInactive.Include);
		RunSettingsSnapshot settings =
			RunSettingsSnapshot.Capture(settingsController);

		positionTracker.StartTracking(xrOrigin.Camera.transform, settings, runType);
	}

	private void Update()
	{
		if (Keyboard.current == null)
			return;

		if (Keyboard.current[Key.Digit1].wasPressedThisFrame)
		{
			if (assistanceController != null)
				assistanceController.Request();
			HandleAssistanceRequested();
		}
	}

	private void HandleRunEnded(float elapsedTime)
	{
		if (!isEndingRun)
			StartCoroutine(ReturnToMainMenu(elapsedTime));
	}

	private void HandleAssistanceRequested()
	{
		positionTracker?.RecordAssistance();
	}

	private void HandleWrongTurnRecorded()
	{
		positionTracker?.RecordError();
	}

	private IEnumerator ReturnToMainMenu(float elapsedTime)
	{
		isEndingRun = true;
		positionTracker?.StopTracking();
		Debug.Log($"Returning to main menu after a {elapsedTime:F2} second run.");

		MenuController menuController =
			FindFirstObjectByType<MenuController>(FindObjectsInactive.Include);

		if (menuController != null)
			menuController.ShowMainMenu();

		Scene runSystemScene = gameObject.scene;

		if (runSystemScene.IsValid() && runSystemScene.isLoaded)
			yield return SceneManager.UnloadSceneAsync(runSystemScene);
	}
}

public static class XRPlayerTeleport
{
	public static bool MoveToStandingPoint(
		XROrigin xrOrigin,
		Transform standingPoint,
		Object context)
	{
		if (xrOrigin == null)
		{
			Debug.LogError("Could not find the XR Origin.", context);
			return false;
		}

		if (standingPoint == null)
		{
			Debug.LogError("No standing point has been assigned.", context);
			return false;
		}

		CharacterController characterController =
			xrOrigin.GetComponent<CharacterController>();

		if (characterController != null)
			characterController.enabled = false;

		xrOrigin.transform.rotation = Quaternion.Euler(
			0f,
			standingPoint.eulerAngles.y,
			0f
		);

		Transform cameraTransform = xrOrigin.Camera.transform;
		float cameraHeight = cameraTransform.position.y - xrOrigin.transform.position.y;
		Vector3 desiredCameraPosition =
			standingPoint.position + Vector3.up * cameraHeight;
		Vector3 cameraCorrection =
			desiredCameraPosition - cameraTransform.position;

		xrOrigin.transform.position += cameraCorrection;

		if (characterController != null)
			characterController.enabled = true;

		return true;
	}
}
