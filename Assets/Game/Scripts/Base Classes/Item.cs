using System;
using UnityEngine;

public class Item : MonoBehaviour, IInteraction_Interface
{
    public float InteractionDistance { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public WeakReference getInteractableObj()
    {
        throw new NotImplementedException();
    }

    public IInteraction_Interface.InteractionType GetInteractAnimation()
    {
        throw new NotImplementedException();
    }

    public void Interact(Transform interactor)
    {
        throw new NotImplementedException();
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
