using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;

namespace junklite
{
    // Built-in LevelEvent steps. Add new ones by subclassing LevelEventAction; the Inspector finds them.

    [Serializable, LevelEventActionMenu("Objectives/Start Objective")]
    public sealed class StartObjectiveAction : LevelEventAction
    {
        public ObjectiveDefinition objective;

        public override IEnumerator Run(LevelEvent context)
        {
            if (ObjectiveTracker.Instance != null) ObjectiveTracker.Instance.Begin(objective);
            yield break;
        }

        public override string Summary => $"Start objective: {(objective != null ? objective.Title : "(none)")}";
        public override string Validate() => objective == null ? "No objective set." : null;
    }

    [Serializable, LevelEventActionMenu("Objectives/Complete Objective")]
    public sealed class CompleteObjectiveAction : LevelEventAction
    {
        public ObjectiveDefinition objective;

        public override IEnumerator Run(LevelEvent context)
        {
            if (ObjectiveTracker.Instance != null) ObjectiveTracker.Instance.Complete(objective);
            yield break;
        }

        public override string Summary => $"Complete objective: {(objective != null ? objective.Title : "(none)")}";
        public override string Validate() => objective == null ? "No objective set." : null;
    }

    [Serializable, LevelEventActionMenu("Story/Play Dialogue")]
    public sealed class PlayDialogueAction : LevelEventAction
    {
        public DialogueSequence sequence;

        public override IEnumerator Run(LevelEvent context)
        {
            DialogueManager dialogue = DialogueManager.instance;
            if (sequence == null || dialogue == null) yield break;

            // Never cut into another conversation.
            while (dialogue.IsInDialogue) yield return null;

            bool ended = false;
            void OnEnded() => ended = true;
            dialogue.OnDialogueEnded += OnEnded;
            dialogue.StartDialogue(sequence);
            while (!ended && dialogue != null) yield return null;
            if (dialogue != null) dialogue.OnDialogueEnded -= OnEnded;
        }

        public override string Summary => $"Dialogue: {(sequence != null ? sequence.name : "(none)")}";
        public override string Validate() => sequence == null ? "No dialogue sequence set." : null;
    }

    [Serializable, LevelEventActionMenu("Story/Play Timeline")]
    public sealed class PlayTimelineAction : LevelEventAction
    {
        public PlayableDirector director;
        [Tooltip("Lock input and movement while the timeline plays.")]
        public bool lockPlayer = true;
        [Tooltip("Wait for the timeline to end before the next step.")]
        public bool waitUntilDone = true;

        public override IEnumerator Run(LevelEvent context)
        {
            if (director == null) yield break;

            var locks = new List<IDisposable>();
            PlayerCharacter player = context.Player;
            if (lockPlayer && player != null)
            {
                if (player.State != null) locks.Add(player.State.AcquireInputLock());
                if (player.Controller != null) locks.Add(player.Controller.AcquireMovementLock());
                foreach (IDisposable l in locks) context.AddLease(l);
            }

            director.time = 0;
            director.Play();
            if (waitUntilDone)
            {
                // Hold-mode timelines stay "Playing" at the end, so also stop at the last frame.
                while (director != null && director.state == PlayState.Playing && director.time < director.duration)
                    yield return null;
            }

            foreach (IDisposable l in locks) l.Dispose();
        }

        public override string Summary => $"Timeline: {(director != null ? director.name : "(none)")}{(waitUntilDone ? "" : " (no wait)")}";
        public override string Validate() => director == null ? "No PlayableDirector set." :
            director.playableAsset == null ? "Director has no timeline asset." : null;
    }

    [Serializable, LevelEventActionMenu("Player/Lock Player")]
    public sealed class LockPlayerAction : LevelEventAction
    {
        [Tooltip("Also stop movement (off = input only, gravity and momentum still apply).")]
        public bool freezeMovement = true;

        public override IEnumerator Run(LevelEvent context)
        {
            context.LockPlayer(freezeMovement);
            yield break;
        }

        public override string Summary => freezeMovement ? "Lock player (input + movement)" : "Lock player input";
    }

    [Serializable, LevelEventActionMenu("Player/Unlock Player")]
    public sealed class UnlockPlayerAction : LevelEventAction
    {
        public override IEnumerator Run(LevelEvent context)
        {
            context.ReleaseLeases();
            yield break;
        }

        public override string Summary => "Unlock player";
    }

    [Serializable, LevelEventActionMenu("Player/Teleport Player")]
    public sealed class TeleportPlayerAction : LevelEventAction
    {
        public Transform target;

        public override IEnumerator Run(LevelEvent context)
        {
            PlayerCharacter player = context.Player;
            if (target != null && player != null && player.Controller != null)
                player.Controller.TeleportTo(target.position);
            yield break;
        }

        public override string Summary => $"Teleport player to {(target != null ? target.name : "(none)")}";
        public override string Validate() => target == null ? "No target set." : null;
    }

    [Serializable, LevelEventActionMenu("Player/Wait For Landing")]
    public sealed class WaitForLandingAction : LevelEventAction
    {
        [Min(0.1f)] public float timeout = 4f;
        [Tooltip("Optional. Where to put the player if they have not landed by the timeout.")]
        public Transform fallback;

        public override IEnumerator Run(LevelEvent context)
        {
            PlayerCharacter player = context.Player;
            if (player == null || player.Controller == null) yield break;

            // First wait to leave the ground (the floor may vanish a frame later), then wait to land.
            for (float t = 0f; t < 1f && player != null && player.Controller.IsGrounded; t += Time.deltaTime)
                yield return new WaitForFixedUpdate();
            for (float t = 0f; t < timeout; t += Time.deltaTime)
            {
                if (player == null) yield break;
                if (player.Controller.IsGrounded) yield break;
                yield return null;
            }
            if (fallback != null && player != null) player.Controller.TeleportTo(fallback.position);
        }

        public override string Summary => $"Wait for landing (max {timeout:0.#}s)";
    }

    [Serializable, LevelEventActionMenu("Player/Grant Item")]
    public sealed class GrantKeyAction : LevelEventAction
    {
        [Tooltip("Keyring item, e.g. Grapple Hook or Z Key.")]
        public string keyTag = PlayerKeyring.GrappleHook;

        public override IEnumerator Run(LevelEvent context)
        {
            PlayerCharacter player = context.Player;
            PlayerKeyring keyring = player != null ? player.GetComponentInParent<PlayerKeyring>() : null;
            if (keyring != null && !keyring.Has(keyTag)) keyring.Add(keyTag);
            yield break;
        }

        public override string Summary => $"Grant item: {keyTag}";
        public override string Validate() => string.IsNullOrEmpty(keyTag) ? "No item set." : null;
    }

    [Serializable, LevelEventActionMenu("Progression/Grant Upgrade Points")]
    public sealed class GrantUpgradePointsAction : LevelEventAction
    {
        [Min(1)] public int points = 1;
        [Tooltip("Optional profile key so the grant happens once per save (e.g. \"service-floor/brute-bonus\"). Empty grants every time the event runs.")]
        public string onceId;

        public override IEnumerator Run(LevelEvent context)
        {
            PlayerUpgrades upgrades = PlayerUpgrades.Instance;
            if (upgrades != null)
            {
                if (string.IsNullOrEmpty(onceId)) upgrades.AddBonusPoints(points);
                else upgrades.TryCollectPoints(onceId, points);
            }
            yield break;
        }

        public override string Summary => $"Grant {points} upgrade point{(points == 1 ? "" : "s")}{(string.IsNullOrEmpty(onceId) ? "" : $" once ({onceId})")}";
        public override string Validate() => points < 1 ? "Points must be at least 1." : null;
    }

    [Serializable, LevelEventActionMenu("UI/Show Hint")]
    public sealed class ShowHintAction : LevelEventAction
    {
        [Tooltip("Scene prompt that shows the hint.")]
        public TutorialPromptHUD prompt;
        [Tooltip("Input action path for the key chip, e.g. Player/GrappleAim.")]
        public string inputAction = "Player/GrappleAim";
        public string label = "Aim grapple";
        public string keyboardOverride;
        public string gamepadOverride;

        public override IEnumerator Run(LevelEvent context)
        {
            if (prompt != null)
                prompt.Show(new TutorialPromptHUD.Entry(inputAction, label,
                    keyboardOverride: string.IsNullOrEmpty(keyboardOverride) ? null : keyboardOverride,
                    gamepadOverride: string.IsNullOrEmpty(gamepadOverride) ? null : gamepadOverride));
            yield break;
        }

        public override string Summary => $"Show hint: [{inputAction}] {label}";
        public override string Validate() => prompt == null ? "No TutorialPromptHUD set." : null;
    }

    [Serializable, LevelEventActionMenu("UI/Hide Hint")]
    public sealed class HideHintAction : LevelEventAction
    {
        public TutorialPromptHUD prompt;
        [Tooltip("Flash the hint as done before hiding it.")]
        public bool markComplete = true;

        public override IEnumerator Run(LevelEvent context)
        {
            if (prompt != null)
            {
                if (markComplete) prompt.Complete();
                else prompt.Hide();
            }
            yield break;
        }

        public override string Summary => markComplete ? "Complete hint" : "Hide hint";
        public override string Validate() => prompt == null ? "No TutorialPromptHUD set." : null;
    }

    [Serializable, LevelEventActionMenu("World/Set Active")]
    public sealed class SetActiveAction : LevelEventAction
    {
        public List<GameObject> targets = new();
        public bool active = true;

        public override IEnumerator Run(LevelEvent context)
        {
            foreach (GameObject target in targets)
                if (target != null) target.SetActive(active);
            yield break;
        }

        public override string Summary => $"{(active ? "Show" : "Hide")} {targets.Count} object(s)";
    }

    [Serializable, LevelEventActionMenu("World/Camera Shake")]
    public sealed class CameraShakeAction : LevelEventAction
    {
        public CinemachineImpulseSource source;
        public float force = 1f;

        public override IEnumerator Run(LevelEvent context)
        {
            if (source != null) source.GenerateImpulseWithForce(force);
            else FeedbackManager.Instance?.DoCameraShake(force);
            yield break;
        }

        public override string Summary => $"Camera shake ({force:0.##})";
    }

    [Serializable, LevelEventActionMenu("World/Switch Camera")]
    public sealed class SwitchCameraAction : LevelEventAction
    {
        [Tooltip("Cinemachine camera to use from now on (e.g. a pulled-back room camera, or PosZCam to go back).")]
        public CinemachineCamera camera;

        public override IEnumerator Run(LevelEvent context)
        {
            if (camera == null) yield break;
            camera.gameObject.SetActive(true);
            // CameraManager binds the player as the follow target and tracks it as the active camera.
            if (CameraManager.Instance != null) CameraManager.Instance.SetActiveCamera(camera);
            camera.Prioritize();
        }

        public override string Summary => $"Switch camera: {(camera != null ? camera.name : "(none)")}";
        public override string Validate() => camera == null ? "No camera set." : null;
    }

    [Serializable, LevelEventActionMenu("Flow/Wait")]
    public sealed class WaitAction : LevelEventAction
    {
        [Min(0f)] public float seconds = 1f;

        public override IEnumerator Run(LevelEvent context)
        {
            if (seconds > 0f) yield return new WaitForSeconds(seconds);
        }

        public override string Summary => $"Wait {seconds:0.##}s";
    }

    [Serializable, LevelEventActionMenu("Flow/Play Level Event")]
    public sealed class PlayLevelEventAction : LevelEventAction
    {
        public LevelEvent target;
        public bool waitUntilDone = true;

        public override IEnumerator Run(LevelEvent context)
        {
            if (target == null || target == context) yield break;
            target.Play();
            if (waitUntilDone)
                while (target != null && target.IsRunning) yield return null;
        }

        public override string Summary => $"Play event: {(target != null ? target.name : "(none)")}";
        public override string Validate() => target == null ? "No target event set." : null;
    }

    [Serializable, LevelEventActionMenu("Flow/Invoke UnityEvent")]
    public sealed class InvokeUnityEventAction : LevelEventAction
    {
        public UnityEvent onRun = new();

        public override IEnumerator Run(LevelEvent context)
        {
            onRun?.Invoke();
            yield break;
        }

        public override string Summary => $"UnityEvent ({onRun?.GetPersistentEventCount() ?? 0} call(s))";
    }
}
