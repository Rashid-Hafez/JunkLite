using System;
using UnityEngine;

public class Collectibles : MonoBehaviour, IInteraction_Interface
{
    public float InteractionDistance { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public WeakReference getInteractableObj()
    {
        if(this == null)
            throw new Exception("OBJ NO EXIST");
        return new WeakReference(this);
    }

    public IInteraction_Interface.InteractionType GetInteractionType()
    {
        return IInteraction_Interface.InteractionType.Item; // Assuming this collectible is of type Item
    }

    public void Interact(Transform interactor)
    {
        // Implement interaction logic here, e.g., collecting the item
        Debug.Log($"Interacted with collectible: {gameObject.name} by {interactor.name}");
        // Optionally, you can destroy the collectible after interaction

        Destroy(gameObject);
    }

    public bool IsInteracting()
    {
        throw new NotImplementedException();
    }

    public void SetInteractable(bool interactable)
    {
        throw new NotImplementedException();
    }

    public void SetInteracting(bool interacting)
    {
        throw new NotImplementedException();
    }

    public void StopInteract(Transform interactor)
    {
        throw new NotImplementedException();
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
