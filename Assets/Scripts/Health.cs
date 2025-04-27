using UnityEngine;

public class Health : MonoBehaviour
{
    [SerializeField] private float maxHealth = 100f;
    public float currentHealth;

    [SerializeField] private GameObject damageEffectPrefab; // damage vfx
    [SerializeField] private float damageEffectDuration = 1f;

    [SerializeField] private GameObject deathEffectPrefab; // death vfx
    [SerializeField] private float deathEffectDuration = 2f;

    private void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(float amount)
    {
        currentHealth -= amount;

        if (damageEffectPrefab != null)
        {
            GameObject damageEffect = Instantiate(damageEffectPrefab, transform.position + Vector3.up * 1f, Quaternion.identity);
            Destroy(damageEffect, damageEffectDuration);
        }

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        if (deathEffectPrefab != null)
        {
            GameObject deathEffect = Instantiate(deathEffectPrefab, transform.position, Quaternion.identity);
            Destroy(deathEffect, deathEffectDuration);
        }

        Destroy(gameObject);
    }

}
