namespace junklite
{
    /// <summary>
    /// Serialized compatibility name for existing prefabs. New code depends on
    /// EnemyPerception, which owns the actual sensing implementation.
    /// </summary>
    public sealed class DetectionZone : EnemyPerception
    {
    }
}
