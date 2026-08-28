using UnityEngine;
using UnityEngine.Playables;
using junklite;

public class CinematicTrigger : MonoBehaviour
{
    [SerializeField] private PlayableDirector director;
    [SerializeField] private PlayableAsset cinematic;
    [SerializeField] private bool freezePlayerDuringCinematic = true;
    private bool hasPlayed = false;
    private bool directorStopped = false;
    private PlayerCharacter frozenPlayer;
    private Character2D5Controller frozenController;
    private System.IDisposable movementLock;
    private System.IDisposable physicsLock;
    private System.IDisposable kinematicLock;

    private void Awake()
    {
        if (director == null)
            director = GetComponent<PlayableDirector>();

        if (director == null)
            Debug.LogError("[CinematicTrigger] No PlayableDirector assigned. Assign one explicitly on this object.", this);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") && director != null && cinematic != null && !hasPlayed)
        {
            hasPlayed = true;
            StartCoroutine(PlayCinematic(other.GetComponentInParent<PlayerCharacter>()));
        }
    }

    private System.Collections.IEnumerator PlayCinematic(PlayerCharacter player)
    {
        FreezePlayer(player);

        directorStopped = false;
        director.stopped -= HandleDirectorStopped;
        director.stopped += HandleDirectorStopped;
        director.Play(cinematic);

        while (!directorStopped && director.state == PlayState.Playing)
            yield return null;

        director.stopped -= HandleDirectorStopped;
        UnfreezePlayer();
    }

    private void HandleDirectorStopped(PlayableDirector stoppedDirector)
    {
        if (stoppedDirector == director)
            directorStopped = true;
    }

    private void FreezePlayer(PlayerCharacter player)
    {
        if (!freezePlayerDuringCinematic || player == null)
            return;

        frozenPlayer = player;
        frozenController = player.Controller;

        if (frozenController != null)
        {
            movementLock = frozenController.AcquireMovementLock();
            physicsLock = frozenController.AcquirePhysicsOverride();
            kinematicLock = frozenController.AcquireKinematicLock();
        }
    }

    private void UnfreezePlayer()
    {
        if (!freezePlayerDuringCinematic)
            return;

        kinematicLock?.Dispose();
        physicsLock?.Dispose();
        movementLock?.Dispose();

        kinematicLock = null;
        physicsLock = null;
        movementLock = null;
        frozenPlayer = null;
        frozenController = null;
    }

    private void OnDisable()
    {
        if (director != null)
            director.stopped -= HandleDirectorStopped;
        UnfreezePlayer();
    }
}
