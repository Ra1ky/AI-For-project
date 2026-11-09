using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement Parameters")]
    public float walkSpeed = 3f;
    public float runSpeed = 6f;
    public float jumpHeight = 1.2f;
    public float gravity = -35f;

    [Header("Noise Settings (Radii)")]
    public float runNoiseRadius = 4f;
    public float jumpNoiseRadius = 6f;
    public float stepInterval = 0.35f;
    private float stepTimer = 0f;

    [Header("Ground Check")]
    public Transform groundCheck;
    public float groundDistance = 0.2f;
    public LayerMask groundMask;

    private CharacterController controller;
    private Vector3 velocity;
    private bool isGrounded;

    // prevents double jumps during launch phase
    private bool isJumping = false;

    void Start()
    {
        controller = GetComponent<CharacterController>();
    }

    void Update()
    {
        // check ground sphere overlap
        bool touchGround = false;
        if (groundCheck != null)
        {
            touchGround = Physics.CheckSphere(groundCheck.position, groundDistance, groundMask, QueryTriggerInteraction.Ignore);
        }
        else
        {
            touchGround = controller.isGrounded;
        }

        // grounded only when touching ground and not launching up
        isGrounded = touchGround && !isJumping;

        // reset flags upon landing
        if (touchGround && velocity.y <= 0f)
        {
            isJumping = false;
            velocity.y = -2f; // snap to ground force
        }

        // input processing (wasd)
        float x = 0f;
        float z = 0f;

        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed) z += 1f;
            if (Keyboard.current.sKey.isPressed) z -= 1f;
            if (Keyboard.current.dKey.isPressed) x += 1f;
            if (Keyboard.current.aKey.isPressed) x -= 1f;
        }

        bool isMoving = (x != 0f || z != 0f);
        bool isRunning = Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
        float currentSpeed = (isMoving && isRunning) ? runSpeed : walkSpeed;

        // movement vector
        Vector3 move = transform.right * x + transform.forward * z;
        controller.Move(move.normalized * currentSpeed * Time.deltaTime);

        // step noise during sprint
        if (isGrounded && isMoving && isRunning)
        {
            stepTimer += Time.deltaTime;
            if (stepTimer >= stepInterval)
            {
                EmitNoise(transform.position, runNoiseRadius, "Run");
                stepTimer = 0f;
            }
        }
        else
        {
            stepTimer = 0f;
        }

        // jump (strictly while grounded)
        bool spacePressed = Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;

        if (spacePressed && isGrounded)
        {
            isJumping = true;      // block consecutive jumps
            isGrounded = false;     // clear ground state
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            EmitNoise(transform.position, jumpNoiseRadius, "Jump");
        }

        // gravity application
        if (velocity.y < 0)
        {
            velocity.y += gravity * 1.4f * Time.deltaTime; // faster fall rate
        }
        else
        {
            velocity.y += gravity * Time.deltaTime;
        }

        controller.Move(velocity * Time.deltaTime);
    }

    void EmitNoise(Vector3 sourcePosition, float radius, string noiseType)
    {
        Collider[] hitColliders = Physics.OverlapSphere(sourcePosition, radius);
        foreach (var col in hitColliders)
        {
            GuardAI guard = col.GetComponentInParent<GuardAI>();
            if (guard != null)
            {
                guard.OnHearNoise(sourcePosition);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (groundCheck != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(groundCheck.position, groundDistance);
        }
    }
}