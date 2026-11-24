using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Lost the player: stand where it was last seen and look both ways before
    /// giving up (ISearcher.OnSearchComplete). ChaseState already walked here
    /// using the last known position; this is only the look-around.
    /// </summary>
    public sealed class SearchState : EnemyStateBase
    {
        private enum Phase
        {
            LookAway,
            LookBack
        }

        private ISearcher searcher;
        private EnemyMovement movement;
        private Phase phase;
        private float timer;
        private bool finished;

        public SearchState(EnemyCharacter enemy) : base(enemy) { }

        public override void Enter()
        {
            searcher = GetCapability<ISearcher>();
            movement = enemy.Movement;
            finished = false;

            movement?.Stop();
            float look = searcher != null ? Mathf.Max(0.05f, searcher.SearchLookDuration) : 0.5f;

            phase = Phase.LookAway;
            timer = look;
            movement?.FlipFacing();
        }

        public override void Update()
        {
            if (finished)
                return;

            timer -= Time.deltaTime;
            if (timer > 0f)
                return;

            if (phase == Phase.LookAway)
            {
                phase = Phase.LookBack;
                timer = searcher != null ? Mathf.Max(0.05f, searcher.SearchLookDuration) : 0.5f;
                movement?.FlipFacing();
                return;
            }

            Finish();
        }

        public override void Exit()
        {
            finished = true;
        }

        private void Finish()
        {
            if (finished)
                return;

            finished = true;
            searcher?.OnSearchComplete();
        }
    }
}
