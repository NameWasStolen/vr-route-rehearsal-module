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

	[Tooltip("Only used if Bootstrap has no SceneTransitionController: seconds to fade to black " +
	         "and back when the run hands back to the main menu. Normally the transition " +
	         "controller's own timings are used, the same as the tutorial's.")]
	[SerializeField] private float menuFadeSeconds = 0.5f;

	private TimerController timerController;
	private PlayerPositionTracker positionTracker;
	private RouteProgressTracker routeTracker;
	private RunGuidance guidance;
	private AssistanceController assistanceController;
	private PostRunSurvey survey;
	private readonly List<WrongTurnController> wrongTurnControllers = new List<WrongTurnController>();
	private XROrigin xrOrigin;
	private bool isEndingRun;
	private string runType = "run";
	private float runStartTime;
	private System.DateTime runStartedAt;
	private string participantId = StudySession.Unset;
	private int runIndex = 1;

	private void OnEnable()
	{
		Debug.Log($"RunSystemController enabled in scene '{gameObject.scene.name}'.", this);
		timerController = FindFirstObjectByType<TimerController>();
		positionTracker = GetComponent<PlayerPositionTracker>();
		if (positionTracker == null)
			positionTracker = gameObject.AddComponent<PlayerPositionTracker>();

		// Follows the participant along the route: detours, decision points, road crossings.
		routeTracker = GetComponent<RouteProgressTracker>();
		if (routeTracker == null)
			routeTracker = gameObject.AddComponent<RouteProgressTracker>();
		positionTracker.RouteTracker = routeTracker;

		// Guided vs Unguided: the route line, the tap and the "Turn around" signal.
		guidance = GetComponent<RunGuidance>();
		if (guidance == null)
			guidance = gameObject.AddComponent<RunGuidance>();

		// The tutorial's pause and help badges on the controllers, for the run. Added here so it
		// needs no scene setup; add a RunControllerTooltips by hand to change its settings.
		if (FindInThisScene<RunControllerTooltips>() == null)
			gameObject.AddComponent<RunControllerTooltips>();

		// Only the participant's selected controller is shown during a run, as in the tutorial:
		// the other one's model, pointer ray and badges are hidden (its pause and help buttons
		// still work). Scene-scoped, so both controllers come back at the main menu.
		if (FindInThisScene<SelectedControllerOnly>() == null)
			gameObject.AddComponent<SelectedControllerOnly>();

		// The stress and confidence questions shown at the end zone of some modules. Added here
		// so it needs no scene setup; add a PostRunSurvey by hand to change which runs get it or
		// its wording.
		survey = FindInThisScene<PostRunSurvey>();
		if (survey == null)
			survey = gameObject.AddComponent<PostRunSurvey>();

		// The help button for runs lives in this scene (added by Tools > VR Full Route > Add Help and
		// Pause Menu to RunSystem). Prefer that one over any other that happens to be loaded.
		assistanceController = FindInThisScene<AssistanceController>();
		if (assistanceController != null)
			assistanceController.onRequested.AddListener(HandleAssistanceRequested);
		else
			Debug.LogWarning("RunSystemController found no AssistanceController in this scene, so the " +
			                 "participant cannot ask for help during the run. Run Tools > VR Full Route > " +
			                 "Add Help and Pause Menu to RunSystem.", this);

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
		// Who and which run, for the CSVs. Set with Tools > VR Study > Participant ID.
		participantId = StudySession.ParticipantId;
		runIndex = StudySession.NextRunIndex(participantId);
		if (!StudySession.HasParticipant)
			Debug.LogWarning("No participant ID is set, so this run is saved as 'unset'. Set one with " +
			                 "Tools > VR Study > Participant ID before the next run.", this);
		SessionLog.Record("run_loaded", $"{runType}, participant {participantId}, run {runIndex}");

		// Armed as the run loads, so a Guided participant can already tap for the way at the bus stop.
		guidance?.Configure(runType, assistanceController, routeTracker, positionTracker);

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
		runStartedAt = System.DateTime.Now;

		// Route first, so the very first sample already has its route columns.
		routeTracker?.Begin(xrOrigin.Camera.transform);
		WarnAboutUnknownDecisionZones();
		positionTracker.StartTracking(xrOrigin.Camera.transform, settings, runType, participantId, runIndex);
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
		routeTracker?.NoteHelpRequest();
	}

	private void HandleWrongTurnRecorded(string triggerName)
	{
		positionTracker?.RecordError();
	}

	/// <summary>
	/// The summary has columns for the six decision points only (fixed, so the file layout never
	/// changes). Says so if the baked route has a decision zone the summary does not know.
	/// </summary>
	private void WarnAboutUnknownDecisionZones()
	{
		if (routeTracker == null || routeTracker.Route == null)
			return;
		foreach (RouteDefinition.Zone z in routeTracker.Route.DecisionZones)
		{
			string node = z.name.Replace("CP_Decision_", "");
			if (System.Array.IndexOf(RunSummaryWriter.DecisionNodes, node) < 0)
				Debug.LogWarning($"Decision zone {z.name} has no columns in run_summaries.csv; its time and " +
				                 "head scan are only in the session log.", this);
		}
	}

	private IEnumerator ReturnToMainMenu(float elapsedTime, bool completed)
	{
		isEndingRun = true;
		guidance?.End();
		routeTracker?.End(completed, elapsedTime);

		// Per-sample CSV, then this run's row in run_summaries.csv. Nothing is written for a run
		// that never started (left from the pause menu before leaving the bus stop).
		bool wasTracking = positionTracker != null && positionTracker.IsTracking;
		string sampleFile = positionTracker?.StopTracking(completed);
		if (wasTracking)
			RunSummaryWriter.Write(positionTracker, routeTracker, runStartedAt, completed, elapsedTime, sampleFile);

		// The post-run survey, for runs that reached the end zone in the modules that have one.
		// Its answers go to survey_responses.csv, after the run's own data is already saved.
		if (completed && survey != null && survey.AppliesTo(runType))
			yield return survey.Run(runType, participantId, runIndex);

		Debug.Log($"Returning to main menu after a {elapsedTime:F2} second run.");

		MenuController menuController =
			FindFirstObjectByType<MenuController>(FindObjectsInactive.Include);
		Scene runSystemScene = gameObject.scene;

		// Back to the menu behind the same fade as the tutorial: dark, show the menu and move the
		// participant to it, unload this scene, then fade in. Handed to SceneTransitionController
		// (Bootstrap) because this scene is unloaded part-way through.
		SceneTransitionController transition = SceneTransitionController.Instance;
		if (transition != null && transition.RunInDark(BackToMenu(menuController, runSystemScene)))
			yield break;

		// Fallback with no transition controller: a plain fade on the fader.
		ScreenFader fader = ScreenFader.Instance;
		if (fader != null)
			yield return fader.FadeTo(1f, menuFadeSeconds);

		if (menuController != null)
			menuController.ShowMainMenu();

		if (fader != null)
			fader.FadeIn(menuFadeSeconds);

		if (runSystemScene.IsValid() && runSystemScene.isLoaded)
			yield return SceneManager.UnloadSceneAsync(runSystemScene);
	}

	/// <summary>
	/// The return to the menu, run in the dark by SceneTransitionController. Static and given
	/// everything it needs, because the object that started it is unloaded halfway through.
	/// </summary>
	private static IEnumerator BackToMenu(MenuController menuController, Scene runSystemScene)
	{
		if (menuController != null)
			menuController.ShowMainMenu();
		else
			Debug.LogWarning("RunSystemController found no MenuController to return to.");

		if (runSystemScene.IsValid() && runSystemScene.isLoaded)
		{
			AsyncOperation unload = SceneManager.UnloadSceneAsync(runSystemScene);
			while (unload != null && !unload.isDone)
				yield return null;
		}
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
