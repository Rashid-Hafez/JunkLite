using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Seals a boss room. Put a trigger box inside the room, past the entry door. Once the player is in
    /// and the boss is alive, the entry door stays shut (no way back). The lock is released when the boss
    /// dies, and when the player dies or respawns (she wakes up outside and must be able to come in again;
    /// the door locks again on the next entry).
    /// The boss itself is kept in by an <see cref="EnemyBarrier"/> across the entry door, switched off when
    /// he dies, so he never follows a player out through an open door. When the player dies he walks back to
    /// his post (health kept), so a retry always starts from the doorway with him across the room.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class BossRoomLock : MonoBehaviour
    {
        [SerializeField] private SlidingDoor entryDoor;
        [SerializeField] private EnemyCharacter boss;
        [Tooltip("Optional EnemyBarrier GameObject across the entry door. Removed when the boss dies.")]
        [SerializeField] private GameObject bossBarrier;

        private PlayerLifecycle subscribedLifecycle;
        private BruteBrain bossBrain;
        private Vector3 bossHomePosition;
        private Quaternion bossHomeRotation;
        private EnemyCharacter subscribedBoss;
        private bool sealedRoom;

        public bool IsSealed => sealedRoom;

        private void Awake()
        {
            GetComponent<Collider>().isTrigger = true;
            if (boss != null)
            {
                bossBrain = boss.GetComponent<BruteBrain>();
                bossHomePosition = boss.transform.position;
                bossHomeRotation = boss.transform.rotation;
            }
        }

        private void OnEnable()
        {
            Rebind();
            if (bossBarrier != null) bossBarrier.SetActive(boss != null && boss.IsAlive);
        }

        private void Start() => Rebind();

        private void OnDisable()
        {
            Release();
            Unbind();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (sealedRoom || boss == null || !boss.IsAlive || !other.CompareTag("Player")) return;
            sealedRoom = true;
            if (entryDoor != null) entryDoor.AddLock(this);
        }

        private void Release()
        {
            if (!sealedRoom) return;
            sealedRoom = false;
            if (entryDoor != null) entryDoor.RemoveLock(this);
        }

        private void HandleBossDied(EnemyCharacter enemy)
        {
            Release();
            if (bossBarrier != null) bossBarrier.SetActive(false);
        }

        private void HandlePlayerDied(PlayerCharacter player)
        {
            Release();
            // He would otherwise camp the doorway she respawns outside of, and block her way back in.
            if (bossBrain != null && boss != null && boss.IsAlive)
                bossBrain.ReturnToPost(bossHomePosition, bossHomeRotation);
        }
        private void HandlePlayerSpawned(PlayerCharacter player) => Release();

        private void Rebind()
        {
            if (boss != subscribedBoss)
            {
                if (subscribedBoss != null) subscribedBoss.Died -= HandleBossDied;
                subscribedBoss = boss;
                if (subscribedBoss != null) subscribedBoss.Died += HandleBossDied;
            }

            PlayerLifecycle lifecycle = PlayerLifecycle.Instance;
            if (lifecycle == subscribedLifecycle) return;
            if (subscribedLifecycle != null)
            {
                subscribedLifecycle.PlayerDied -= HandlePlayerDied;
                subscribedLifecycle.PlayerSpawned -= HandlePlayerSpawned;
            }
            subscribedLifecycle = lifecycle;
            if (subscribedLifecycle != null)
            {
                subscribedLifecycle.PlayerDied += HandlePlayerDied;
                subscribedLifecycle.PlayerSpawned += HandlePlayerSpawned;
            }
        }

        private void Unbind()
        {
            if (subscribedBoss != null) subscribedBoss.Died -= HandleBossDied;
            subscribedBoss = null;
            if (subscribedLifecycle != null)
            {
                subscribedLifecycle.PlayerDied -= HandlePlayerDied;
                subscribedLifecycle.PlayerSpawned -= HandlePlayerSpawned;
            }
            subscribedLifecycle = null;
        }

        private void OnDrawGizmos()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) return;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.6f, 0.1f, sealedRoom ? 0.3f : 0.12f);
            Gizmos.DrawCube(box.center, box.size);
        }
    }
}
