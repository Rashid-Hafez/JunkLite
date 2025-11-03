using System;
using System.Collections;
using junklite;
using Unity.VisualScripting;
using UnityEngine;

/// <summary>
/// PetDrone script handles the behavior of the drone that follows the player.
/// It includes hovering effect and smooth following mechanics.
/// 
/// EQUATION FOR DRONE FOLLOW SPRING ARM: F=−k (p−r)
/// p = current position of the drone
/// r = target position (player position + offset)
/// k = spring constant (stiffness)
/// F = force applied to the drone to move it towards the target position
/// 
/// The drone's movement is updated each frame based on the force calculated from the spring equation,
/// resulting in a smooth and natural following behavior.
/// </summary>

public class PetDrone : MonoBehaviour
{
    /// ///////////// FOLLOW EFFECT /////////////
    // Follow equation
    Rigidbody2D rb;
    Rigidbody playerRb;
    Vector2 F; // F
    Vector2 p; // p
    Vector2 r; // r
    public float k = 5f; // spring constant (stiffness)
    public float mass = 1f; // mass of the drone
    public float damping = 0.8f; // damping factor to reduce oscillations
    public float maxSpeed = 10f; // maximum speed of the drone
    public float maxForce = 20f; // maximum force applied to the drone
    /// ///////////// FOLLOW EFFECT /////////////


    /// ///////////// HOVERING EFFECT /////////////

    Vector2 hoverPosition;
    float amountToHover = 0.5f; // Height of the hover
    float hoverSpeed = 2f; // Speed of the hover
    Vector2 initpos; // Initial position of the object
    public float tickRate = 0.02f; // like FixedUpdate (50 Hz)
    [SerializeField] Vector3 offset = new Vector3(1f, 1.5f, 0f); // side + above the player

    // Reference to the player object
    [Tooltip("Reference to the player object")]
    [Header("Player Reference")]
    private PlayerCharacter player1 = null;

    [Header("Momentum Settings")]
    public float smoothTime = 0.3f; // Higher = smoother, floatier follow
    public float overshootFactor = 0.5f; // How much the pet overshoots when player stops
    private Vector3 velocity = Vector3.zero;
    private Vector3 lastPlayerPos;
    private float playerSpeed;
    Coroutine followCoroutine;
    Coroutine flipCoroutine;

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
    private EnumDroneState currentState = EnumDroneState.Following;

    // --- DRONE SYSTEM DOCUMENTATION ---
    // This script is attached to the drone prefab.
    // It receives a reference to the player GameObject via SetPlayer when spawned.
    // In Update, it uses this reference to follow the player with a hovering effect.

    /// <summary>
    /// Sets the player reference for the drone to follow.
    /// Called by SpawnDrone immediately after instantiation.
    /// </summary>
    public void SetPlayer(PlayerCharacter player)
    {
        player1 = player;
    }

    void Start()
    {
        Debug.LogWarning("PetDrone script started.");
        // Ensure player reference is set before starting
        if (player1 != null)
        {
            rb = GetComponent<Rigidbody2D>();
            playerRb = player1.GetComponent<Rigidbody>();

            if (rb == null)
            {
                Debug.LogWarning("Rigidbody2D component missing on PetDrone.");
                throw new Exception("Rigidbody2D component missing on PetDrone.");
            }
            if (initpos == null)
            {
                initpos = player1.transform.position + offset;
            }
            if (currentState == EnumDroneState.Following)
            {
                followCoroutine = StartCoroutine(FollowPlayer());
            }
            flipCoroutine = StartCoroutine(FlipDrone());

            // Subscribe to drone attack event!
            //player1.State.OnDroneAttack += OnDroneAttacked; /// SUBSCRIBE TO EVENT!
        }
        else
        {
            throw new System.Exception("Custom Exception: Player reference not set for PetDrone.");
        }

    }
    
    void OnDroneAttacked()
    {
        ChangeState(EnumDroneState.Attacking);
    }

    private IEnumerator FlipDrone()
    {
        while (true)
        {
            if (player1 == null || rb == null)
            {
                yield break; // Exit coroutine if player or rb is null
            }
            if(currentState == EnumDroneState.Following)
            {
                if(player1.transform.localScale.x > 0)
                {
                    transform.localScale = new Vector3(1, 1, 1);
                }
                else if(player1.transform.localScale.x < 0)
                {
                    transform.localScale = new Vector3(-1, 1, 1);
                }
            }
            // Wait for a short duration before checking again
            yield return new WaitForSeconds(0.4f);
        }
    }

    private IEnumerator FollowPlayer()
    {

        while (player1 != null)
        {
            if (currentState != EnumDroneState.Following)
            {
                yield break; // Exit coroutine if not in Following state
            }

            p   = transform.position; // Current position of the drone
            r   = player1.transform.position + offset; // Target position (player position + offset)
            F = -k * (p - r); // Spring force equation: F = -k(p - r)

            rb.AddForce(F / rb.mass, ForceMode2D.Force); // Apply force to the drone
            
            rb.linearVelocity *= damping; // Apply damping to reduce oscillations
            rb.linearVelocity = Vector2.ClampMagnitude(rb.linearVelocity, maxSpeed); // Limit max speed

            yield return new WaitForFixedUpdate(); // Wait for the next physics update
        }
    }

    void hover()
    {
        hoverPosition = transform.position;
        hoverPosition.y = Mathf.Sin(Time.time * hoverSpeed) * amountToHover + (player1.transform.position.y + offset.y);
        transform.position = hoverPosition;
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
