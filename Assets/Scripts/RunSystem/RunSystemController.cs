using UnityEngine;
using UnityEngine.Serialization;
using Unity.XR.CoreUtils;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;
using VRTutorial;

public class RunSystemController : MonoBehaviour
{
	[FormerlySerializedAs("guidedSpawnPoint")]
	[SerializeField] private Transform runStartPoint;

	private TimerController timerController;
	private PlayerPositionTracker positionTracker;
	private AssistanceController assistanceController;
	private readonly List<WrongTurnController> wrongTurnControllers = new List<WrongTurnController>();
	private XROrigin xrOrigin;
	private bool isEndingRun;
	private string runType = "run";
	private float runStartTime;

	private void OnEnable()
	{
		Debug.Log($"RunSystemController enabled in scene '{gameObject.scene.name}'.", this);
		timerController = FindFirstObjectByType<TimerController>();
		positionTracker = GetComponent<PlayerPositionTracker>();
		if (positionTracker == null)
			positionTracker = gameObject.AddComponent<PlayerPositionTracker>();

		// The help button for runs lives in this scene (added by Tools > VR Full Route > Add Help
		// Button to RunSystem). Prefer that one over any other that happens to be loaded.
		assistanceController = FindInThisScene<AssistanceController>();
		if (assistanceController != null)
			assistanceController.onRequested.AddListener(HandleAssistanceRequested);
		else
			Debug.LogWarning("RunSystemController found no AssistanceController in this scene, so the " +
			                 "participant cannot ask for help during the run. Run Tools > VR Full Route > " +
			                 "Add Help Button to RunSystem.", this);

		// Every ACTIVE wrong-turn controller in this scene. The old test map (Map1) has its own,
		// but it is switched off - listening to it would miss every wrong turn on the route.
		wrongTurnControllers.Clear();
		foreach (WrongTurnController w in FindObjectsByType<WrongTurnController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
		{
			if (w.gameObject.scene != gameObject.scene)
				continue;
			w.WrongTurnRecorded += HandleWrongTurnRecorded;
			wrongTurnControllers.Add(w);
		}
		if (wrongTurnControllers.Count == 0)
			Debug.LogWarning("RunSystemController found no active WrongTurnController in this scene; " +
			                 "wrong turns will not be counted.", this);

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

		foreach (WrongTurnController w in wrongTurnControllers)
			if (w != null)
				w.WrongTurnRecorded -= HandleWrongTurnRecorded;
		wrongTurnControllers.Clear();
	}

	/// <summary>
	/// First component of type T in this controller's own scene (active or not), falling back
	/// to any loaded scene.
	/// </summary>
	private T FindInThisScene<T>() where T : Component
	{
		T fallback = null;
		foreach (T c in FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None))
		{
			if (c.gameObject.scene == gameObject.scene)
				return c;
			if (fallback == null)
				fallback = c;
		}
		return fallback;
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

		// One session log per run, opened as the run loads (so a help request made before the
		// participant leaves the bus stop still lands in it), with positions taken from the
		// headset. Without this, run events went into whatever file the tutorial had opened.
		SessionLog.BeginSession();
		if (xrOrigin.Camera != null)
			SessionLog.SetPositionSource(xrOrigin.Camera.transform);
		SessionLog.Record("run_loaded", runType);

		XRPlayerTeleport.MoveToStandingPoint(
			xrOrigin,
			runStartPoint,
			this
		);
	}

	private void HandleRunStarted()
	{
		runStartTime = Time.time;

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

		SessionLog.Record("run_started", runType);

		positionTracker.StartTracking(xrOrigin.Camera.transform, settings, runType);
	}

	private void Update()
	{
		if (Keyboard.current == null)
			return;

		// Desktop testing: 1 places a help request at once, the same as the pause menu's Get help.
		// It goes through the controller only - onRequested then records it - so it is counted
		// once. (Holding H also works, exactly like holding the controller button.)
		if (Keyboard.current[Key.Digit1].wasPressedThisFrame)
		{
			if (assistanceController != null)
				assistanceController.Request();
			else
				Debug.LogWarning("No AssistanceController in the run, so there is nothing to request help from.", this);
		}
	}

	/// <summary>
	/// Ends the run early and returns to the main menu. Wire the run pause menu's Exit button
	/// here (not to ReturnToMainMenu, which is for the tutorial scene). The run's data is still
	/// written, with "_incomplete" in the file name.
	/// </summary>
	public void ExitRun()
	{
		if (isEndingRun)
			return;

		float elapsed = timerController != null && timerController.IsRunning
			? Time.time - runStartTime
			: 0f;
		SessionLog.Record("run_exited", elapsed.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "s");
		StartCoroutine(ReturnToMainMenu(elapsed, false));
	}

	private void HandleRunEnded(float elapsedTime)
	{
		if (!isEndingRun)
		{
			SessionLog.Record("run_ended", elapsedTime.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "s");
			StartCoroutine(ReturnToMainMenu(elapsedTime, true));
		}
	}

	private void HandleAssistanceRequested()
	{
		positionTracker?.RecordAssistance();
	}

	private void HandleWrongTurnRecorded(string triggerName)
	{
		positionTracker?.RecordError();
	}

	private IEnumerator ReturnToMainMenu(float elapsedTime, bool completed)
	{
		isEndingRun = true;
		positionTracker?.StopTracking(completed);
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
