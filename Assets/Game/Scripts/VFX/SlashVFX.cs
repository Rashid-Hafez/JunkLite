using UnityEngine;

public class SlashVFX : MonoBehaviour
{
    [SerializeField] private string stateName = "Slash";

    private Animator animator;
    private int stateHash;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        stateHash = Animator.StringToHash(stateName);
    }

    private void OnEnable()
    {
        // Animator is guaranteed active here
        animator.Play(stateHash, 0, 0f);
    }

    private void OnDisable()
    {
        // DO NOT call Update() here
        animator.Rebind();
    }
}
