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
    private Rigidbody frozenRigidbody;
    private bool frozenWasKinematic;
    private bool frozenControllerCanMove;

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
        frozenRigidbody = player.GetComponent<Rigidbody>();

        if (frozenController != null)
        {
            frozenControllerCanMove = frozenController.CanMove;
            frozenController.CanMove = false;
            frozenController.StopAllVelocity();
        }

        if (frozenRigidbody != null)
        {
            frozenWasKinematic = frozenRigidbody.isKinematic;
            frozenRigidbody.isKinematic = true;
            frozenRigidbody.linearVelocity = Vector3.zero;
            frozenRigidbody.angularVelocity = Vector3.zero;
        }
    }

    private void UnfreezePlayer()
    {
        if (!freezePlayerDuringCinematic)
            return;

        if (frozenRigidbody != null)
            frozenRigidbody.isKinematic = frozenWasKinematic;

        if (frozenController != null)
            frozenController.CanMove = frozenControllerCanMove;

        frozenPlayer = null;
        frozenController = null;
        frozenRigidbody = null;
    }
}
