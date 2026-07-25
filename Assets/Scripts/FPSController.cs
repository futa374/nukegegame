using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(AudioSource))]
public class FPSController : MonoBehaviour
{
    public float moveSpeed = 3f;
    public float gravity = -9.81f;
    public float mouseSensitivity = 0.2f;

    [Header("Camera")]
    public Transform cameraTransform;

    [Header("Footstep")]
    public AudioClip footstepClip;
    public float footstepInterval = 0.45f;

    private CharacterController controller;
    private AudioSource audioSource;
    private float verticalVelocity = 0f;
    private float cameraPitch = 0f;
    private float footstepTimer = 0f;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        audioSource = GetComponent<AudioSource>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        Look();
        Move();
    }

    void Look()
    {
        Vector2 delta = Mouse.current.delta.ReadValue();
        float yaw   = delta.x * mouseSensitivity;
        float pitch = delta.y * mouseSensitivity;

        transform.Rotate(Vector3.up, yaw, Space.World);

        cameraPitch -= pitch;
        cameraPitch = Mathf.Clamp(cameraPitch, -80f, 80f);
        if (cameraTransform != null)
            cameraTransform.localEulerAngles = new Vector3(cameraPitch, 0f, 0f);
    }

    void Move()
    {
        float h = 0f, v = 0f;
        if (Keyboard.current.aKey.isPressed) h = -1f;
        if (Keyboard.current.dKey.isPressed) h =  1f;
        if (Keyboard.current.wKey.isPressed) v =  1f;
        if (Keyboard.current.sKey.isPressed) v = -1f;

        Vector3 move = transform.right * h + transform.forward * v;
        bool isMoving = move.sqrMagnitude > 0f;
        move = move.normalized * moveSpeed;

        if (controller.isGrounded)
            verticalVelocity = -1f;
        else
            verticalVelocity += gravity * Time.deltaTime;

        move.y = verticalVelocity;
        controller.Move(move * Time.deltaTime);

        HandleFootsteps(isMoving);
    }

    void HandleFootsteps(bool isMoving)
    {
        if (!isMoving || footstepClip == null)
        {
            footstepTimer = 0f;
            return;
        }

        footstepTimer -= Time.deltaTime;
        if (footstepTimer <= 0f)
        {
            audioSource.PlayOneShot(footstepClip);
            footstepTimer = footstepInterval;
        }
    }
}
