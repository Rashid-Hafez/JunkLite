using UnityEngine;
using System.Collections;
namespace junklite
{
public abstract class StatusEffect
{
    // Implement effect as a coroutine that operates on the Enemy instance.
    public abstract IEnumerator Apply(EnemyCharacter enemy);
}

public class BurnStatusEffect : StatusEffect
{
    private float damagePerTick;
    private float tickInterval;
    private float duration;

    public BurnStatusEffect(float damagePerTick, float tickInterval, float duration)
    {
        this.damagePerTick = damagePerTick;
        this.tickInterval = tickInterval;
        this.duration = duration;
    }

    public override IEnumerator Apply(EnemyCharacter enemy)
    {
        float elapsed = 0f;
        while (elapsed < duration && enemy != null && enemy.life > 0f)
        {
            enemy.life -= damagePerTick;
            elapsed += tickInterval;
            yield return new WaitForSeconds(tickInterval);
        }
    }
}

public class BleedStatusEffect : StatusEffect
{
    private float damagePerTick;
    private float tickInterval;
    private float duration;

    public BleedStatusEffect(float damagePerTick, float tickInterval, float duration)
    {
        this.damagePerTick = damagePerTick;
        this.tickInterval = tickInterval;
        this.duration = duration;
    }

    public override IEnumerator Apply(EnemyCharacter enemy)
    {
        float elapsed = 0f;
        while (elapsed < duration && enemy != null && enemy.life > 0f)
        {
            // Bleed is typically faster small ticks
            enemy.life -= damagePerTick;
            elapsed += tickInterval;
            yield return new WaitForSeconds(tickInterval);
        }
    }
}

public class PoisonStatusEffect : StatusEffect
{
    private float damagePerTick;
    private float tickInterval;
    private float duration;
    private float slowPercent; // 0..1

    public PoisonStatusEffect(float damagePerTick, float tickInterval, float duration, float slowPercent = 0f)
    {
        this.damagePerTick = damagePerTick;
        this.tickInterval = tickInterval;
        this.duration = duration;
        this.slowPercent = Mathf.Clamp01(slowPercent);
    }

    public override IEnumerator Apply(EnemyCharacter enemy)
    {
        float elapsed = 0f;
        float originalSpeed = enemy != null ? enemy.speed : 0f;
        if (enemy != null)
        {
            enemy.speed = originalSpeed * (1f - slowPercent);
        }

        while (elapsed < duration && enemy != null && enemy.life > 0f)
        {
            enemy.life -= damagePerTick;
            elapsed += tickInterval;
            yield return new WaitForSeconds(tickInterval);
        }

        if (enemy != null)
        {
            enemy.speed = originalSpeed;
        }
    }
}

public class ElectricStatusEffect : StatusEffect
{
    private float damagePerPulse;
    private float pulseInterval;
    private float duration;
    private float stunDurationPerPulse;

    public ElectricStatusEffect(float damagePerPulse, float pulseInterval, float duration, float stunDurationPerPulse = 0.15f)
    {
        this.damagePerPulse = damagePerPulse;
        this.pulseInterval = pulseInterval;
        this.duration = duration;
        this.stunDurationPerPulse = stunDurationPerPulse;
    }

    public override IEnumerator Apply(EnemyCharacter enemy)
    {
        float elapsed = 0f;
        Rigidbody2D rb = enemy != null ? enemy.GetComponent<Rigidbody2D>() : null;
        float originalSpeed = enemy != null ? enemy.speed : 0f;

        while (elapsed < duration && enemy != null && enemy.life > 0f)
        {
            // Apply shock damage
            enemy.life -= damagePerPulse;

            // Briefly "stun" by zeroing velocity and speed
            if (rb != null) rb.linearVelocity = Vector2.zero;
            if (enemy != null) enemy.speed = 0f;

            float stunned = 0f;
            while (stunned < stunDurationPerPulse && enemy != null && enemy.life > 0f)
            {
                stunned += Time.deltaTime;
                yield return null;
            }

            // restore speed after stun (if enemy still exists)
            if (enemy != null) enemy.speed = originalSpeed;

            elapsed += pulseInterval;
            yield return new WaitForSeconds(pulseInterval);
        }

        if (enemy != null) enemy.speed = originalSpeed;
    }
}
}