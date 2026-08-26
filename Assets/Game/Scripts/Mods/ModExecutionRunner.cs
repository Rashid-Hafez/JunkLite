using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Owns active-mod coroutines for one player. Every execution is keyed by its
    /// ModInstance so shared ScriptableObject definitions never retain live state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ModExecutionRunner : MonoBehaviour
    {
        private sealed class RunningExecution
        {
            public ModInstance Instance;
            public ModExecutionContext Context;
            public IEnumerator Routine;
            public Coroutine Coroutine;
        }

        private readonly Dictionary<ModInstance, RunningExecution> runningExecutions = new();

        private PlayerCharacter player;
        private PlayerState playerState;
        private Character2D5Controller controller;
        private Rigidbody playerRigidbody;

        private int movementLockCount;
        private int physicsOverrideCount;
        private int kinematicLockCount;
        private bool movementWasEnabled;
        private bool physicsWasOverridden;
        private bool rigidbodyWasKinematic;

        public PlayerCharacter Player => player;

        private void Awake()
        {
            player = GetComponent<PlayerCharacter>();
            playerState = GetComponent<PlayerState>();
            controller = GetComponent<Character2D5Controller>();
            playerRigidbody = GetComponent<Rigidbody>();
        }

        public bool TryStart(
            ModInstance instance,
            Func<ModExecutionContext, IEnumerator> executionFactory)
        {
            if (instance == null || executionFactory == null || runningExecutions.ContainsKey(instance))
                return false;

            if (!instance.TryBeginExecution())
                return false;

            var context = new ModExecutionContext(this, instance, player);
            IEnumerator routine;

            try
            {
                routine = executionFactory(context);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                instance.EndExecution();
                return false;
            }

            if (routine == null)
            {
                instance.EndExecution();
                return false;
            }

            var execution = new RunningExecution
            {
                Instance = instance,
                Context = context,
                Routine = routine
            };

            runningExecutions.Add(instance, execution);
            execution.Coroutine = StartCoroutine(Run(execution));
            return true;
        }

        public bool Cancel(ModInstance instance)
        {
            if (instance == null || !runningExecutions.TryGetValue(instance, out var execution))
                return false;

            if (execution.Coroutine != null)
                StopCoroutine(execution.Coroutine);

            Finish(execution, wasCancelled: true);
            return true;
        }

        public void CancelAll()
        {
            if (runningExecutions.Count == 0) return;

            var instances = new List<ModInstance>(runningExecutions.Keys);
            for (int i = 0; i < instances.Count; i++)
                Cancel(instances[i]);
        }

        private IEnumerator Run(RunningExecution execution)
        {
            bool moveNext = true;

            while (moveNext && execution.Context.IsRunning)
            {
                object yielded = null;

                try
                {
                    moveNext = execution.Routine.MoveNext();
                    if (moveNext)
                        yielded = execution.Routine.Current;
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                    moveNext = false;
                }

                if (moveNext)
                    yield return yielded;
            }

            Finish(execution, wasCancelled: false);
        }

        private void Finish(RunningExecution execution, bool wasCancelled)
        {
            if (execution == null || !runningExecutions.Remove(execution.Instance))
                return;

            execution.Context.Finish(wasCancelled);
            execution.Instance.EndExecution();
        }

        internal void AcquirePlayerControl(
            ModExecutionContext context,
            bool overridePhysics,
            bool makeKinematic,
            bool grantDamageImmunity)
        {
            IDisposable inputLock = playerState?.AcquireInputLock();
            IDisposable damageImmunity = grantDamageImmunity
                ? playerState?.AcquireDamageImmunity()
                : null;

            bool blockedMovement = controller != null;
            if (blockedMovement)
            {
                if (movementLockCount++ == 0)
                {
                    movementWasEnabled = controller.CanMove;
                    controller.StopAllVelocity();
                    controller.CanMove = false;
                }
            }

            bool appliedPhysicsOverride = overridePhysics && controller != null;
            if (appliedPhysicsOverride && physicsOverrideCount++ == 0)
            {
                physicsWasOverridden = controller.IsPhysicsOverridden;
                controller.SetPhysicsOverride(true);
            }

            bool appliedKinematicLock = makeKinematic && playerRigidbody != null;
            if (appliedKinematicLock && kinematicLockCount++ == 0)
            {
                rigidbodyWasKinematic = playerRigidbody.isKinematic;
                playerRigidbody.isKinematic = true;
                playerRigidbody.linearVelocity = Vector3.zero;
            }

            context.AddCleanup(() =>
            {
                if (appliedKinematicLock && --kinematicLockCount == 0 && playerRigidbody != null)
                    playerRigidbody.isKinematic = rigidbodyWasKinematic;

                if (appliedPhysicsOverride && --physicsOverrideCount == 0 && controller != null)
                    controller.SetPhysicsOverride(physicsWasOverridden);

                if (blockedMovement && --movementLockCount == 0 && controller != null)
                    controller.CanMove = movementWasEnabled;

                damageImmunity?.Dispose();
                inputLock?.Dispose();
            });
        }

        private void OnDisable()
        {
            CancelAll();
        }
    }

    /// <summary>
    /// Per-activation state and cleanup registration. The runner always executes cleanup,
    /// whether an ability completes normally or is cancelled by mode exit/player disable.
    /// </summary>
    public sealed class ModExecutionContext
    {
        private readonly ModExecutionRunner runner;
        private readonly List<Action> cleanupActions = new();

        internal ModExecutionContext(
            ModExecutionRunner runner,
            ModInstance instance,
            PlayerCharacter player)
        {
            this.runner = runner;
            Instance = instance;
            Player = player;
            IsRunning = true;
        }

        public ModInstance Instance { get; }
        public PlayerCharacter Player { get; }
        public bool IsRunning { get; private set; }
        public bool WasCancelled { get; private set; }

        public void AddCleanup(Action cleanup)
        {
            if (cleanup == null) return;

            if (!IsRunning)
            {
                cleanup();
                return;
            }

            cleanupActions.Add(cleanup);
        }

        public void LockPlayerControl(
            bool overridePhysics = false,
            bool makeKinematic = true,
            bool grantDamageImmunity = true)
        {
            if (!IsRunning) return;
            runner.AcquirePlayerControl(this, overridePhysics, makeKinematic, grantDamageImmunity);
        }

        internal void Finish(bool wasCancelled)
        {
            if (!IsRunning) return;

            WasCancelled = wasCancelled;
            IsRunning = false;

            for (int i = cleanupActions.Count - 1; i >= 0; i--)
            {
                try
                {
                    cleanupActions[i]?.Invoke();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }

            cleanupActions.Clear();
        }
    }
}
