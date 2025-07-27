using System;
using UnityEngine;

public interface IInteraction_Interface
{
    enum InteractionType
    {
        None = 0,
        Click = 1,
        Hold = 2,
        LookAt = 3,
        Door = 4,
        Item = 5,
        Character = 6
    };
    float InteractionDistance { get; set; } // Distance at which the interaction can occur
    void Interact(Transform interactor);

    // This method should be called when we need a direct ref to the interactable
    WeakReference getInteractableObj();

    //get interaction type, depending on the interaction type, we can use different animations
    InteractionType GetInteractAnimation();
    
    void StopInteract(Transform interactor);
    bool IsInteracting();
    void SetInteractable(bool interactable);
    void SetInteracting(bool interacting);
}