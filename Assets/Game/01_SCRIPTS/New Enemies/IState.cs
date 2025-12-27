namespace junklite
{
    /// <summary>
    /// Interface for all FSM states.
    /// </summary>
    public interface IState
    {
        /// <summary>
        /// Called when entering this state.
        /// </summary>
        void Enter();

        /// <summary>
        /// Called every frame while in this state.
        /// </summary>
        void Update();

        /// <summary>
        /// Called every fixed update while in this state.
        /// </summary>
        void FixedUpdate();

        /// <summary>
        /// Called when exiting this state.
        /// </summary>
        void Exit();
    }
}