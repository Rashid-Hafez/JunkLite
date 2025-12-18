using UnityEngine;
using System.Collections;

namespace junklite
{
    /// <summary>
    /// Abstract attack handler for enemies. Manages attack timing and cooldowns.
    /// Accesses all components through EnemyCharacter (the hub).
    /// </summary>
    public abstract class EnemyAttackHandler : MonoBehaviour
    {
        [Header("Attack Timing")]
        [SerializeField] protected float windupTime = 0.15f;
        [SerializeField] protected float activeTime = 0.1f;
        [SerializeField] protected float recoveryTime = 0.25f;
        [SerializeField] protected float cooldownTime = 0.4f;

        [Header("Attack Settings")]
        [SerializeField] protected float attackRange = 1.5f;
        [SerializeField] protected float damage = 10f;
        [SerializeField] protected LayerMask targetLayer;

        // Central hub - all access goes through here
        protected EnemyCharacter enemy;

        // Quick accessors
        protected EnemyController Controller => enemy.Controller;
        protected CharacterState State => enemy.State;

        protected bool isAttacking;
        protected bool onCooldown;
        protected Coroutine attackRoutine;

        // Public accessors
        public bool IsAttacking => isAttacking;
        public bool OnCooldown => onCooldown;
        public float AttackRange => attackRange;
        public float Damage => damage;

        // ================= INIT =================
        public virtual void Initialize(EnemyCharacter owner)
        {
            enemy = owner;
        }

        // ================= PUBLIC API =================
        public bool CanAttack()
        {
            if (enemy == null || !enemy.IsAlive) return false;
            if (isAttacking) return false;
            if (onCooldown) return false;
            if (State != null && !State.CanAttack) return false;
            return true;
        }

        public virtual bool TryAttack()
        {
            if (!CanAttack())
                return false;

            attackRoutine = StartCoroutine(AttackRoutine());
            return true;
        }

        public virtual void CancelAttack()
        {
            if (attackRoutine != null)
            {
                StopCoroutine(attackRoutine);
                attackRoutine = null;
            }

            isAttacking = false;
            onCooldown = false;

            if (State != null)
                State.SetAttacking(false);
        }

        // ================= CORE ROUTINE =================
        protected virtual IEnumerator AttackRoutine()
        {
            isAttacking = true;
            onCooldown = true;

            if (State != null)
                State.SetAttacking(true);

            // ---- WINDUP ----
            OnWindupStart();
            yield return new WaitForSeconds(windupTime);

            // ---- ACTIVE ----
            OnAttackStart();
            DoAttack();
            yield return new WaitForSeconds(activeTime);
            OnAttackEnd();

            // ---- RECOVERY ----
            OnRecoveryStart();
            yield return new WaitForSeconds(recoveryTime);

            isAttacking = false;

            if (State != null)
                State.SetAttacking(false);

            OnRecoveryEnd();

            // ---- COOLDOWN ----
            yield return new WaitForSeconds(cooldownTime);
            onCooldown = false;

            attackRoutine = null;
        }

        // ================= IMPLEMENT PER ENEMY =================
        protected abstract void DoAttack();

        // ================= OPTIONAL HOOKS =================
        protected virtual void OnWindupStart() { }
        protected virtual void OnAttackStart() { }
        protected virtual void OnAttackEnd() { }
        protected virtual void OnRecoveryStart() { }
        protected virtual void OnRecoveryEnd() { }
    }
}