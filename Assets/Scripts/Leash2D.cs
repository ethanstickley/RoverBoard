using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class Leash2D : MonoBehaviour
{
    [Tooltip("Player Rigidbody2D at the leash end.")]
    public Rigidbody2D playerRb;
    [Tooltip("Dog Rigidbody2D at the other leash end.")]
    public Rigidbody2D dogRb;
    [Tooltip("DogAI reference to read mood (good/bad) for force split.")]
    public DogAI2D dogAI;
    [Tooltip("Player controller to check on-board vs on-foot strength.")]
    public PlayerController2D playerCtrl;

    [Tooltip("Player-side local offset (where the leash attaches on the player).")]
    public Vector2 attachOffsetPlayer = new Vector2(0f, 0.20f);
    [Tooltip("Dog-side local offset (where the leash attaches on the dog).")]
    public Vector2 attachOffsetDog = new Vector2(0f, 0.15f);

    [Header("Leash Lengths")]
    [Tooltip("Distance before the leash begins to pull.")]
    public float slackLength = 2.0f;
    [Tooltip("Absolute maximum stretch length of the leash.")]
    public float maxLength = 4.5f;

    [Header("Spring / Damper")]
    [Tooltip("How strongly the leash pulls when stretched beyond slack.")]
    public float springK = 6.0f;
    [Tooltip("Pull smoothing: resists fast changes in leash length (relative velocity along leash).")]
    public float damperC = 1.25f;
    [Tooltip("Upper cap on applied tension (safety clamp).")]
    public float maxTension = 35f;

    [Header("Force Split")]
    [Tooltip("Fraction of tension applied to the dog when mood is GOOD (lower = player gets more pull).")]
    public float dogPullWhenGood = 0.35f;
    [Tooltip("Fraction of tension applied to the dog when mood is BAD (higher = dog resists more).")]
    public float dogPullWhenBad = 0.75f;
    [Tooltip("Player strength multiplier while on-foot (easier to overpower dog).")]
    public float playerOverpowerOnFoot = 2.0f;
    [Tooltip("Player strength multiplier while on-board (harder to overpower dog).")]
    public float playerOverpowerOnBoard = 1.15f;

    [Header("Stability")]
    [Tooltip("Ignore very small tension impulses to reduce jitter.")]
    public float minImpulseToMove = 0.1f;

    [Header("Visual")]
    public LineRenderer line;
    [Tooltip("Leash color while slack (not pulling).")]
    public Color slackColor = new Color(1f, 1f, 1f, 0.65f);
    [Tooltip("Leash color while tight (pulling).")]
    public Color tightColor = new Color(1f, 0.25f, 0.25f, 0.9f);

    void Reset()
    {
        line = GetComponent<LineRenderer>();
        if (line)
        {
            line.positionCount = 2;
            line.widthMultiplier = 0.04f;
        }
    }

    void Awake()
    {
        if (!line) line = GetComponent<LineRenderer>();
        if (line) line.positionCount = 2;
    }

    void LateUpdate()
    {
        if (!playerRb || !dogRb || !line) return;
        Vector2 pa = PlayerAnchor();
        Vector2 da = DogAnchor();
        line.SetPosition(0, pa);
        line.SetPosition(1, da);

        float d = Vector2.Distance(pa, da);
        float t01 = Mathf.InverseLerp(slackLength, maxLength, d);
        line.startColor = Color.Lerp(slackColor, tightColor, t01);
        line.endColor = line.startColor;
    }

    void FixedUpdate()
    {
        if (!playerRb || !dogRb) return;

        Vector2 pa = PlayerAnchor();
        Vector2 da = DogAnchor();

        Vector2 dVec = da - pa;
        float dist = dVec.magnitude;
        if (dist < 1e-4f) return;
        Vector2 dir = dVec / dist;

        if (dist <= slackLength) return;

        Vector2 relVel = dogRb.linearVelocity - playerRb.linearVelocity;
        float relAlong = Vector2.Dot(relVel, dir);

        if (relAlong <= 0f && dist < maxLength) return;

        float extension = Mathf.Min(dist, maxLength) - slackLength;
        float springForce = extension * springK;

        float damperForce = relAlong > 0f ? (relAlong * damperC) : 0f;

        float tension = springForce + damperForce;
        if (tension < 0f) tension = 0f;
        if (tension > maxTension) tension = maxTension;
        if (tension < minImpulseToMove) return;

        Vector2 force = dir * tension;

        float good = dogAI ? Mathf.Clamp01(dogAI.goodBad01) : 1f;
        float dogShare = Mathf.Lerp(dogPullWhenBad, dogPullWhenGood, good);
        dogShare = Mathf.Clamp01(dogShare);
        float playerShare = 1f - dogShare;

        bool onBoard = playerCtrl ? playerCtrl.IsOnBoard : true;
        float playerMult = onBoard ? playerOverpowerOnBoard : playerOverpowerOnFoot;

        dogRb.AddForce(-force * dogShare, ForceMode2D.Force);
        playerRb.AddForce(force * playerShare * playerMult, ForceMode2D.Force);

        if (dist > maxLength)
        {
            float extra = dist - maxLength;
            Vector2 corr = dir * (extra * springK * 0.5f);
            dogRb.AddForce(-corr, ForceMode2D.Force);
            playerRb.AddForce(corr, ForceMode2D.Force);
        }
    }

    Vector2 PlayerAnchor() => (Vector2)playerRb.transform.TransformPoint(attachOffsetPlayer);
    Vector2 DogAnchor() => (Vector2)dogRb.transform.TransformPoint(attachOffsetDog);
}
