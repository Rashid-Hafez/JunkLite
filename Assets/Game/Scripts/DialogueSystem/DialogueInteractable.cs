/*using junklite;
using System;
using UnityEngine;

public class DialogueInteractable : MonoBehaviour
{
    public DialogueSequence sequence;
    private void OnTriggerEnter(Collider other)
    {
        if (other.GetComponent<Character2D5Controller>())
        {
            if (!DialogueManager.instance.dialogueText.IsActive())
            {
                DialogueManager.instance.StartDialogue(sequence);
            }
            
        }
        
    }
}
*/

using UnityEngine;
using TMPro;
using DG.Tweening;

namespace junklite
{
    public class DialogueInteractable : MonoBehaviour
    {
        #region Fields

        [Header("Dialogue")]
        [SerializeField] private DialogueSequence dialogue;

        [Header("Proximity")]
        [SerializeField] private float interactRadius = 2.5f;
        [SerializeField] private string playerTag = "Player";

        [Header("Prompt (Child Object)")]
        [SerializeField] private GameObject promptRoot;
        [SerializeField] private TMP_Text speakerText;
        [SerializeField] private TMP_Text interactHintText;
        [SerializeField] private string interactKeyLabel = "[E]";

        [Header("Animation")]
        [SerializeField] private float popDuration = 0.25f;
        [SerializeField] private Ease popInEase = Ease.OutBack;
        [SerializeField] private Ease popOutEase = Ease.InBack;

        private CanvasGroup canvasGroup;
        private Tween activeTween;

        #endregion

        #region Static

        public static DialogueInteractable Current { get; private set; }

        #endregion

        #region Unity

        private void Awake()
        {
            CreateProximityZone();

            if (promptRoot != null)
            {
                canvasGroup = promptRoot.GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                    canvasGroup = promptRoot.AddComponent<CanvasGroup>();
            }

            HideInstant();
        }

        private void OnEnable()
        {
            RefreshPromptText();
            HideInstant();
        }

        private void OnDisable()
        {
            if (Current == this)
            {
                Current = null;
                KillTween();
                HideInstant();
            }
        }

        private void OnDestroy() => KillTween();

        private void Update()
        {
            // Handle interaction input
            if (Current == this && Input.GetKeyDown(KeyCode.E))
            {
                TryStartDialogue();
            }
        }

        private void LateUpdate()
        {
            if (promptRoot == null || !promptRoot.activeSelf) return;

            Camera cam = Camera.main;
            if (cam != null)
                promptRoot.transform.forward = cam.transform.forward;
        }

        #endregion

        #region Proximity Zone

        private void CreateProximityZone()
        {
            var zone = new GameObject("ProximityZone");
            zone.transform.SetParent(transform, false);
            zone.layer = gameObject.layer;

            var col = zone.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = interactRadius;

            var relay = zone.AddComponent<DialogueProximityRelay>();
            relay.Init(this);
        }

        internal void OnPlayerEnter(Collider other)
        {
            if (!other.CompareTag(playerTag)) return;

            if (Current != null && Current != this)
                Current.Deactivate();

            Current = this;
            RefreshPromptText();
            PopIn();
        }

        internal void OnPlayerExit(Collider other)
        {
            if (!other.CompareTag(playerTag)) return;
            if (Current != this) return;

            Deactivate();
        }

        private void Deactivate()
        {
            if (Current == this)
                Current = null;

            PopOut();
        }

        #endregion

        #region Interaction

        private void TryStartDialogue()
        {
            if (dialogue == null) return;

            // Optional: prevent starting if already in dialogue
            if (DialogueManager.instance == null) return;

            DialogueManager.instance.StartDialogue(dialogue);

            // Hide prompt while dialogue is active
            Deactivate();
        }

        #endregion

        #region Prompt

        private void RefreshPromptText()
        {
            if (speakerText != null)
                speakerText.text = dialogue != null ? dialogue.dialogueLines[0].speakerName : "Dialogue";

            if (interactHintText != null)
                interactHintText.text = $"{interactKeyLabel} Talk";
        }

        #endregion

        #region Animation

        private void PopIn()
        {
            if (promptRoot == null) return;

            KillTween();
            promptRoot.SetActive(true);
            promptRoot.transform.localScale = Vector3.zero;
            if (canvasGroup != null) canvasGroup.alpha = 0f;

            activeTween = DOTween.Sequence()
                .Join(promptRoot.transform.DOScale(Vector3.one, popDuration).SetEase(popInEase))
                .Join(canvasGroup != null
                    ? canvasGroup.DOFade(1f, popDuration * 0.6f)
                    : DOTween.Sequence())
                .SetUpdate(true)
                .SetLink(promptRoot);
        }

        private void PopOut()
        {
            if (promptRoot == null || !promptRoot.activeSelf) return;

            KillTween();

            activeTween = DOTween.Sequence()
                .Join(promptRoot.transform.DOScale(Vector3.zero, popDuration * 0.7f).SetEase(popOutEase))
                .Join(canvasGroup != null
                    ? canvasGroup.DOFade(0f, popDuration * 0.5f)
                    : DOTween.Sequence())
                .SetUpdate(true)
                .SetLink(promptRoot)
                .OnComplete(() => promptRoot.SetActive(false));
        }

        private void HideInstant()
        {
            if (promptRoot == null) return;

            KillTween();
            promptRoot.transform.localScale = Vector3.zero;
            if (canvasGroup != null) canvasGroup.alpha = 0f;
            promptRoot.SetActive(false);
        }

        private void KillTween()
        {
            if (activeTween != null && activeTween.IsActive())
                activeTween.Kill();

            activeTween = null;
        }

        #endregion
    }

    internal class DialogueProximityRelay : MonoBehaviour
    {
        private DialogueInteractable owner;

        public void Init(DialogueInteractable parent) => owner = parent;

        private void OnTriggerEnter(Collider other) => owner?.OnPlayerEnter(other);
        private void OnTriggerExit(Collider other) => owner?.OnPlayerExit(other);
    }
}