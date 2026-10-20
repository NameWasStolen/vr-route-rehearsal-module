using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace VRTutorial
{
    /// <summary>
    /// Adds "Start Again" to the pause menu, with a confirmation page, and restarts the tutorial
    /// from the beginning.
    ///
    /// Starting again reloads the tutorial scene behind a fade: every lesson, timer and trigger
    /// comes back in its authored state and the participant is placed on the spawn point. The
    /// lasting button tints and tooltip badges are cleared as well, so those buttons are taught
    /// again rather than looking already learnt. Settings - chosen hand, text size, volume - are
    /// saved separately and are untouched.
    ///
    /// Confirmation is a page inside the pause menu rather than a second panel, keeping to "never
    /// two panels at once". "No, go back" sits near the top where Resume normally is, and "Yes"
    /// is in a different place from the Start Again button, so a double press cannot confirm by
    /// accident.
    ///
    /// The button and the page are built at runtime by copying the menu's own Resume button and
    /// title, so they match the menu's look and its text-size scaling with nothing to lay out by
    /// hand. Put this component on the PauseController object in Tutorial.unity.
    /// </summary>
    [DisallowMultipleComponent]
    public class TutorialResetController : MonoBehaviour
    {
        [Header("Links (found automatically if empty)")]
        [SerializeField] private PauseController pauseController;
        [Tooltip("Only used to record which lesson the participant was on.")]
        [SerializeField] private TutorialFlow flow;

        [Header("Wording")]
        [SerializeField] private string startAgainLabel = "Start Again";
        [SerializeField] private string confirmQuestion = "Start the tutorial again?";
        [TextArea]
        [SerializeField] private string confirmDetail = "You will go back to the beginning.\nYour settings will stay the same.";
        [SerializeField] private string yesLabel = "Yes, start again";
        [SerializeField] private string noLabel = "No, go back";

        [Header("Layout (menu canvas units)")]
        [Tooltip("The free slot beside Get Help.")]
        [SerializeField] private Vector2 startAgainPosition = new Vector2(300f, -130f);
        [SerializeField] private Vector2 questionPosition = new Vector2(0f, 330f);
        [SerializeField] private Vector2 detailPosition = new Vector2(0f, 190f);
        [SerializeField] private Vector2 noPosition = new Vector2(0f, 10f);
        [SerializeField] private Vector2 yesPosition = new Vector2(0f, -210f);
        [SerializeField] private Vector2 confirmButtonSize = new Vector2(600f, 130f);

        [Header("Logging")]
        [SerializeField] private string logEventName = "tutorial_reset";

        private GameObject _menuRoot;
        private Button _startAgainButton;
        private GameObject _confirmPage;
        private Button _yesButton;
        private Button _noButton;
        private readonly List<KeyValuePair<GameObject, bool>> _hiddenForConfirm =
            new List<KeyValuePair<GameObject, bool>>();
        private bool _confirmShowing;
        private bool _resetting;

        private void Awake()
        {
            if (pauseController == null) pauseController = GetComponent<PauseController>();
            if (pauseController == null) pauseController = FindInScene<PauseController>();
            if (flow == null) flow = FindInScene<TutorialFlow>();

            if (pauseController == null || pauseController.MenuRoot == null)
            {
                Debug.LogWarning("[TutorialResetController] No PauseController with a menu found; " +
                                 "Start Again was not added.", this);
                return;
            }

            _menuRoot = pauseController.MenuRoot;
            Build();
            pauseController.onClosed.AddListener(HideConfirm);
        }

        private void OnDestroy()
        {
            if (pauseController != null) pauseController.onClosed.RemoveListener(HideConfirm);
        }

        // ------------------------------------------------------------------ building

        private void Build()
        {
            Transform root = _menuRoot.transform;
            Button template = FindButtonTemplate(root);
            TMP_Text title = root.Find("MenuText") != null ? root.Find("MenuText").GetComponent<TMP_Text>() : null;

            if (template == null)
            {
                Debug.LogWarning("[TutorialResetController] No button in the pause menu to copy; " +
                                 "Start Again was not added.", this);
                return;
            }

            _startAgainButton = CloneButton(template, root, "StartAgainButton", startAgainPosition,
                                            null, startAgainLabel, ShowConfirm);

            _confirmPage = new GameObject("ResetConfirmPage", typeof(RectTransform));
            var pageRect = (RectTransform)_confirmPage.transform;
            pageRect.SetParent(root, false);
            pageRect.anchorMin = Vector2.zero;
            pageRect.anchorMax = Vector2.one;
            pageRect.offsetMin = Vector2.zero;
            pageRect.offsetMax = Vector2.zero;

            if (title != null)
            {
                CloneText(title, pageRect, "Question", questionPosition, new Vector2(1000f, 120f), confirmQuestion, 1.15f);
                CloneText(title, pageRect, "Detail", detailPosition, new Vector2(1000f, 160f), confirmDetail, 0.8f);
            }

            _noButton = CloneButton(template, pageRect, "NoButton", noPosition, confirmButtonSize, noLabel, HideConfirm);
            _yesButton = CloneButton(template, pageRect, "YesButton", yesPosition, confirmButtonSize, yesLabel, ConfirmReset);

            _confirmPage.SetActive(false);
        }

        private static Button FindButtonTemplate(Transform root)
        {
            Transform resume = root.Find("ResumeButton");
            if (resume != null && resume.GetComponent<Button>() != null) return resume.GetComponent<Button>();
            return root.GetComponentInChildren<Button>(true);
        }

        private static Button CloneButton(Button template, Transform parent, string name, Vector2 position,
                                          Vector2? size, string label, UnityEngine.Events.UnityAction onClick)
        {
            GameObject go = Instantiate(template.gameObject, parent, false);
            go.name = name;
            go.SetActive(true);

            // The copy must not carry anything the template does when pressed - Resume closes the
            // menu, and another button might be Back to Menu.
            var returnToMenu = go.GetComponent<ReturnToMainMenu>();
            if (returnToMenu != null) Destroy(returnToMenu);

            var rect = (RectTransform)go.transform;
            rect.anchoredPosition = position;
            if (size.HasValue) rect.sizeDelta = size.Value;

            var button = go.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(onClick);
            button.interactable = true;

            TMP_Text text = go.GetComponentInChildren<TMP_Text>(true);
            if (text != null) text.text = label;
            return button;
        }

        private static void CloneText(TMP_Text template, Transform parent, string name, Vector2 position,
                                      Vector2 size, string content, float sizeFactor)
        {
            GameObject go = Instantiate(template.gameObject, parent, false);
            go.name = name;
            go.SetActive(true);
            var rect = (RectTransform)go.transform;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var text = go.GetComponent<TMP_Text>();
            text.text = content;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize *= sizeFactor;
        }

        // ------------------------------------------------------------------ page

        public void ShowConfirm()
        {
            if (_confirmPage == null || _confirmShowing || _resetting) return;
            _confirmShowing = true;

            _hiddenForConfirm.Clear();
            foreach (Transform child in _menuRoot.transform)
            {
                if (child.gameObject == _confirmPage) continue;
                if (child.name == "Background") continue;
                _hiddenForConfirm.Add(new KeyValuePair<GameObject, bool>(child.gameObject, child.gameObject.activeSelf));
                child.gameObject.SetActive(false);
            }

            _confirmPage.SetActive(true);
        }

        public void HideConfirm()
        {
            if (!_confirmShowing || _resetting) return;
            _confirmShowing = false;

            _confirmPage.SetActive(false);
            foreach (KeyValuePair<GameObject, bool> entry in _hiddenForConfirm)
                if (entry.Key != null) entry.Key.SetActive(entry.Value);
            _hiddenForConfirm.Clear();
        }

        // ------------------------------------------------------------------ reset

        public void ConfirmReset()
        {
            if (_resetting) return;
            _resetting = true;
            SetConfirmButtonsInteractable(false);

            SessionLog.Record(logEventName, CurrentStepName());

            // The tints and badges live on the rig in Bootstrap, which the reload does not touch.
            foreach (ControllerHand hand in new[] { ControllerHand.Left, ControllerHand.Right })
            {
                foreach (ControllerButton button in new[] { ControllerButton.Secondary, ControllerButton.Primary })
                {
                    ControllerButtonHighlight highlight = ControllerButtonHighlight.For(hand, button);
                    if (highlight != null)
                    {
                        highlight.StopPulsing();
                        highlight.ClearMark();
                    }
                    ControllerTooltip.Hide(hand, button);
                }
            }

            string sceneName = gameObject.scene.name;

            if (SceneTransitionController.Instance != null)
            {
                if (!SceneTransitionController.Instance.ReloadScene(sceneName))
                {
                    // A transition is already running - leave the page usable rather than stuck.
                    _resetting = false;
                    SetConfirmButtonsInteractable(true);
                }
            }
            else
            {
                // Scene opened standalone for testing, with no Bootstrap: a plain reload.
                SceneManager.LoadScene(sceneName);
            }
        }

        private void SetConfirmButtonsInteractable(bool interactable)
        {
            if (_yesButton != null) _yesButton.interactable = interactable;
            if (_noButton != null) _noButton.interactable = interactable;
        }

        private string CurrentStepName()
        {
            if (flow == null) return string.Empty;
            TutorialStep step = flow.CurrentStep;
            return step != null ? step.name : (flow.IsDismissed ? "dismissed" : string.Empty);
        }

        private T FindInScene<T>() where T : Component
        {
            foreach (T c in FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (c.gameObject.scene == gameObject.scene) return c;
            return null;
        }
    }
}
