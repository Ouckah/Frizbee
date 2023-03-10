using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;

public class PlayerMovement : NetworkBehaviour
{
    public float moveSpeed = 5.0f;    // Player movement speed
    public float sprintSpeed = 10.0f; // Player sprint speed
    public float acceleration = 0.1f; // Player acceleration rate
    public float deceleration = 2.0f; // Player deceleration rate
    public float rotationSpeed = 10.0f;    // Speed of the player rotation
    public float jumpForce = 5.0f;    // Player jump force
    public float layoutForce = 10.0f; // Player layout force
    public float layoutDuration = 0.5f;
    public bool isGrounded = false;
    public bool isLayedOut = false;

    public float maxPivotDistance = 1f; // Maximum distance to pivot when with frisbee
    private Vector3 lastPosition; // Store the object's last position without frisbee to calculate pivot distance

    private Rigidbody rb;
    public GameObject cameraPrefab;
    private NetworkObject cameraNetworkObject;
    public Transform cameraTransform;

    public float holdTime = 0.0f;  // time that user held down click (for frisbee power)
    private bool isHolding = false;
    public GameObject frisbeePrefab;    // Prefab for the projectile
    public float launchForce = 10.0f;    // Force applied to the projectile when launched
    public float upwardForce = 0.1f;

    public Transform leftHand;
    public Transform rightHand;
    public GameObject frisbeeVisual;
    private Transform throwingHand;
    public bool hasFrisbee = true;

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        throwingHand = rightHand;

        lastPosition = transform.position;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        Debug.Log("Player has spawned in.");
    }

    void Update()
    {
        if (!IsSpawned || !IsOwner) return;

        // Check if player has frisbee and doesn't have visual yet
        if (hasFrisbee && frisbeeVisual == null)
        {
            // Show frisbee visual
            frisbeeVisual.SetActive(true);
            SpawnFrisbeeVisualServerRpc();
        }

        // Sprint if shift key is down and not layed out
        if (!isLayedOut)
        {
            if (Input.GetKey(KeyCode.LeftShift))
            {
                moveSpeed = Mathf.Lerp(moveSpeed, sprintSpeed, acceleration);
            }
            else
            {
                moveSpeed = Mathf.Lerp(moveSpeed, 5.0f, deceleration);
            }
        }

        // Jump if player is on the ground and space bar is pressed
        if (isGrounded && !isLayedOut && Input.GetKeyDown(KeyCode.Space))
        {
            rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
            isGrounded = false;
        }

        // Layout if layout key pressed (and does'nt have frisbee)
        if (Input.GetKeyDown(KeyCode.R) && !hasFrisbee)
        {
            // Layout
            rb.AddForce(transform.forward * layoutForce, ForceMode.Impulse);

            // // Play animation
            // anim.SetTrigger("Layout");

            // Rotate body
            transform.rotation = Quaternion.Euler(90.0f, transform.rotation.eulerAngles.y, transform.rotation.eulerAngles.z);

            isLayedOut = true;
            moveSpeed = 0.0f;
        }
        if (Input.GetKeyUp(KeyCode.R))
        {
            // Apply force for a limited duration
            StartCoroutine(DissipateForce());
        }

        if (hasFrisbee) // can't move
        {
            if (Input.GetMouseButtonDown(0))
            {
                isHolding = true;
            }

            if (Input.GetMouseButton(0))
            {
                if (isHolding)
                {
                    holdTime += 3.0f * Time.deltaTime;
                }
            }

            if (Input.GetMouseButtonUp(0))
            {
                Throw();
            }
        }

        // hand switching controls
        if (Input.GetKey(KeyCode.E))
        {
            throwingHand = rightHand;
        }

        if (Input.GetKey(KeyCode.Q))
        {
            throwingHand = leftHand;
        }
        if (hasFrisbee && frisbeeVisual != null) frisbeeVisual.transform.position = throwingHand.transform.position;

        if (Input.GetKey(KeyCode.X) && !hasFrisbee)
        {
            Catch();
        }
    }

    void FixedUpdate()
    {
        if (!IsSpawned || !IsOwner) return;

        // Get the movement input axis
        float hInput = Input.GetAxis("Horizontal");
        float vInput = Input.GetAxis("Vertical");

        float distanceFromPivot = Vector3.Distance(transform.position, lastPosition);

        Vector3 cameraForward = Vector3.Scale(cameraTransform.forward, new Vector3(1, 0, 1)).normalized;
        Vector3 playerForward = transform.forward;
        Vector3 movement = (vInput * cameraForward + hInput * cameraTransform.right).normalized * moveSpeed;

        // Only move player if player does not have frisbee or still have pivot movement
        if (!hasFrisbee || distanceFromPivot < maxPivotDistance)
        {
            // Move the player object in the direction of the camera
            rb.MovePosition(rb.position + movement * Time.fixedDeltaTime);
        }

        // Calculate the angle between the camera's forward direction and the player's forward direction.
        float angle = Vector3.SignedAngle(playerForward, cameraForward, Vector3.up);

        if (!isLayedOut)
        {
            // Rotate the player towards the camera's forward direction by the calculated angle.
            transform.Rotate(Vector3.up, angle * rotationSpeed * Time.fixedDeltaTime, Space.World);
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!IsSpawned)
        {
            return;
        }

        if (collision.gameObject.tag == "Ground") 
        {
            isGrounded = true;
        }
    }

    IEnumerator DissipateForce()
    {
        yield return new WaitForSeconds(layoutDuration);
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        isLayedOut = false;

        // Rotate body
        transform.rotation = Quaternion.Euler(0.0f, transform.rotation.eulerAngles.y, transform.rotation.eulerAngles.z);

        moveSpeed = 5.0f;
    }

    public void Throw()
    {
        isHolding = false;
        
        // Hide frisbee visual
        frisbeeVisual.SetActive(false);
        ThrowFrisbeeServerRpc(holdTime);
        DespawnFrisbeeVisualServerRpc();

        // Reset hold time
        holdTime = 0.0f;

        // Remove status
        hasFrisbee = false;
    }

    public void Catch()
    {
        lastPosition = transform.position;
        hasFrisbee = true;
        
        // Show frisbee visual
        frisbeeVisual.SetActive(true);
        SpawnFrisbeeVisualServerRpc();

        Debug.Log(OwnerClientId + " caught the frisbee!");
    }

    [ServerRpc]
    private void ThrowFrisbeeServerRpc(float time)
    {
        Debug.Log(OwnerClientId + " threw a frisbee!");

        // Spawn a new projectile at the position of the script object
        GameObject newProjectile = Instantiate(frisbeePrefab, throwingHand.position, Quaternion.identity);

        // Spawn Network Object
        newProjectile.GetComponent<NetworkObject>().Spawn();

        // Get the forward direction of the camera
        Vector3 throwDirection = cameraTransform.forward + Vector3.up * upwardForce;

        // Set the forward direction of the projectile to the camera forward direction
        newProjectile.transform.forward = cameraTransform.forward;

        // Apply a force to the projectile in the direction of the camera forward direction
        Rigidbody projectileRb = newProjectile.GetComponent<Rigidbody>();
        time = Mathf.Clamp(time, 1.0f, 2.0f);
        projectileRb.AddForce(throwDirection * (time * time) * launchForce, ForceMode.Impulse);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SpawnFrisbeeVisualServerRpc()
    {
        // Make sure server knows frisbee is active
        lastPosition = transform.position;
        hasFrisbee = true;
        frisbeeVisual.SetActive(true);

        // Make sure other clients know frisbee is active
        SpawnFrisbeeVisualClientRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void DespawnFrisbeeVisualServerRpc()
    {
        // Make sure server knows frisbee is hidden
        lastPosition = transform.position;
        hasFrisbee = false;
        frisbeeVisual.SetActive(false);

        // Make sure other clients know frisbee is hidden
        DespawnFrisbeeVisualClientRpc();
    }

    [ClientRpc]
    private void SpawnFrisbeeVisualClientRpc()
    {
        // Make sure clients knows frisbee is active
        lastPosition = transform.position;
        hasFrisbee = true;
        frisbeeVisual.SetActive(true);
    }

    [ClientRpc]
    private void DespawnFrisbeeVisualClientRpc()
    {
        // Make sure clients knows frisbee is hidden
        lastPosition = transform.position;
        hasFrisbee = false;
        frisbeeVisual.SetActive(false);
    }
}