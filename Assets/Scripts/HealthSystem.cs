using UnityEngine;
using UnityEngine.Events;

public class HealthSystem : MonoBehaviour
{
    [Header("Health")]
    public int maxHealth = 5;
    public int currentHealth = 5;

    [Header("Damage")]
    public int damagePerBail = 1;

    [Header("Events")]
    public UnityEvent onDeath;

    void Start()
    {
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        if (onDeath == null) onDeath = new UnityEvent();
    }

    public void TakeDamage(int amount)
    {
        currentHealth = Mathf.Max(0, currentHealth - amount);
        if (currentHealth <= 0)
        {
            onDeath?.Invoke();
            Debug.Log("GAME OVER");
        }
    }

    public void Heal(int amount)
    {
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
    }

    public float Health01() => (maxHealth <= 0) ? 0f : (currentHealth / (float)maxHealth);
}
