using System;
using System.Collections;
using Unity.VisualScripting;
using UnityEngine;

public class PetDrone : MonoBehaviour
{
    // Hovering effect
    float amountToHover = 0.2f; // Height of the hover
    float hoverSpeed = 2f; // Speed of the hover
    Vector2 initpos; // Initial position of the object

    [SerializeField] Vector3 offset = new Vector3(1f, 1.5f, 0f); // side + above the player

    // Reference to the player object
    [Tooltip("Reference to the player object")]
    [Header("Player Reference")]
    private GameObject player1 = null;
    private Coroutine followCoroutine;
    private Coroutine stateChange;


    private class EnumDroneState
    {
        public static readonly EnumDroneState Idle = new EnumDroneState("Idle");
        public static readonly EnumDroneState Following = new EnumDroneState("Following");
        public static readonly EnumDroneState Attacking = new EnumDroneState("Attacking");
        public static readonly EnumDroneState DoubleJumping = new EnumDroneState("DoubleJumping");
        public static readonly EnumDroneState Dashing = new EnumDroneState("Dashing");
        public static readonly EnumDroneState Stunned = new EnumDroneState("Stunned");

        private string stateName;

        private EnumDroneState(string name)
        {
            stateName = name;
        }

        public override string ToString()
        {
            return stateName;
        }
    }
    private EnumDroneState currentState = EnumDroneState.Idle;

    // --- DRONE SYSTEM DOCUMENTATION ---
    // This script is attached to the drone prefab.
    // It receives a reference to the player GameObject via SetPlayer when spawned.
    // In Update, it uses this reference to follow the player with a hovering effect.

    /// <summary>
    /// Sets the player reference for the drone to follow.
    /// Called by SpawnDrone immediately after instantiation.
    /// </summary>
    public void SetPlayer(GameObject player)
    {
        player1 = player;
    }

    void Start()
    {
        Debug.LogWarning("PetDrone script started.");
        // Ensure player reference is set before starting
        if (player1 != null)
        {
            // Optionally set initial position relative to player
            if (initpos == null)
            {
                initpos = player1.transform.position + offset;
            }
        }
        else
        {
            Debug.LogWarning("Player reference not set for PetDrone.");
            throw new System.Exception("Player reference not set for PetDrone.");
        }

        
    }

    /// <summary>
    /// Change state will decide what the drone should do and how long it will take for the drone to react
    /// to the player's actions.
    /// </summary>
    /// <param name="newState"></param>
    private void ChangeState(EnumDroneState newState)
    {
        if (currentState != newState)
        {
            currentState = newState;
            Debug.Log("Drone state changed to: " + currentState);
        }
    }

}
