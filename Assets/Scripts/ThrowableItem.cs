using UnityEngine;

public class ThrowableItem : MonoBehaviour
{
    public float noiseRadius = 12f;

    private void OnCollisionEnter(Collision collision)
    {
        // emit noise on floor/wall impact
        Collider[] guards = Physics.OverlapSphere(transform.position, noiseRadius);
        foreach (var hit in guards)
        {
            if (hit.TryGetComponent<GuardAI>(out var guard))
            {
                guard.OnHearNoise(transform.position);
            }
        }
    }
}