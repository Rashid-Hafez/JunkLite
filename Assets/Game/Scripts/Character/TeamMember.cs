using UnityEngine;

namespace junklite
{
    public enum Team
    {
        Neutral = 0,
        Player = 1,
        Enemy = 2,
    }

    public sealed class TeamMember : MonoBehaviour
    {
        [SerializeField] private Team team = Team.Neutral;
        public Team Team => team;
    }
}
