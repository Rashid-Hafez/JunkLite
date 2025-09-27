using UnityEngine;

public class PetDrone : MonoBehaviour
{
     // Hovering effect
        float amountToHover = 0.2f; // Height of the hover
        float hoverSpeed = 2f; // Speed of the hover
        Vector2 initpos; // Initial position of the object

        [SerializeField] Vector3 offset = new Vector3(1f, 1.5f, 0f); // side + above the player

    // Reference to the player object
    [Tooltip("Reference to the player object")] [Header("Player Reference")] [SerializeField] private
    GameObject player1;

    void Awake()
    {
        if (player1 == null)
        {
            
        }
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        initpos = transform.position; // Initial position of the object
        player1 = GameObject.FindGameObjectWithTag("Player");
            if (player1 == null)
            {
                Debug.LogError("PetDrone: Player object with tag 'Player' not found in the scene!");
                throw new System.Exception  ("PetDrone requires a player reference!");
            }
        
    }

    // Update is called once per frame
    void Update()
    {
        Hover();

    }

    void Hover()
    {
        // Base position (follow player + offset)
        Vector3 basePos = player1.transform.position + offset;
        basePos.y += Mathf.Sin(Time.time * hoverSpeed) * amountToHover;
        transform.position = basePos;
    }
}
