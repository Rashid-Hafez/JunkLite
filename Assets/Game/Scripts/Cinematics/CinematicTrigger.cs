using UnityEngine;
using UnityEngine.Playables;

public class CinematicTrigger : MonoBehaviour
{
    [SerializeField] private PlayableAsset cinematic;
    private PlayableDirector director;
    private bool hasPlayed = false;
    private void Awake()
    {
        director = GetComponentInParent<PlayableDirector>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") && director != null && cinematic != null && !hasPlayed)
        {
            director.Play(cinematic);
            hasPlayed = true;
        }
    }
}
