using UnityEngine;
using System.Collections.Generic;

public class TrickManager : MonoBehaviour
{
    [Header("Refs")]
    public PlayerController2D player;
    public Transform boardVisual;           // rotate during flips

    [Header("Flip Tricks")]
    public string[] flipNames = { "Kickflip", "Heelflip", "Shuvit", "VarialKickflip" };
    public float[] flipDurations = { 0.35f, 0.35f, 0.30f, 0.45f };
    public int rotationsPerFlip = 1;        // board spins per flip

    [Header("Hold Tricks (hold-to-score)")]
    public string[] holdNames = { "Indy", "Melon", "NoseGrab", "TailGrab" };

    [Header("Scoring")]
    public int flipScore = 150;
    public int holdScorePerSecond = 200;
    public int grindScorePerSecond = 100;

    [Header("Recent Tricks")]
    public int recentBufferSize = 3;

    // runtime
    bool inAir;
    bool doingFlip;
    int currentFlipIndex = -1;
    float flipTimer, flipTotal;

    bool doingHold;
    int currentHoldIndex = -1;
    float holdTimer;

    bool grinding;
    float grindTimer;

    readonly Queue<string> recent = new Queue<string>();
    int points;

    void Awake()
    {
        if (!player) player = FindObjectOfType<PlayerController2D>();
        if (!boardVisual && player) boardVisual = player.boardVisual;
    }

    void Update()
    {
        if (inAir && !doingFlip && !doingHold)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) TriggerFlip(0);
            else if (Input.GetKeyDown(KeyCode.Alpha2)) TriggerFlip(1);
            else if (Input.GetKeyDown(KeyCode.Alpha3)) TriggerFlip(2);
            else if (Input.GetKeyDown(KeyCode.Alpha4)) TriggerFlip(3);

            if (Input.GetKeyDown(KeyCode.Q)) StartHold(0);
            else if (Input.GetKeyDown(KeyCode.W)) StartHold(1);
            else if (Input.GetKeyDown(KeyCode.E)) StartHold(2);
            else if (Input.GetKeyDown(KeyCode.R)) StartHold(3);
        }

        if (doingHold)
        {
            if (Input.GetKeyUp(KeyCode.Q) && currentHoldIndex == 0) FinishHold(true);
            if (Input.GetKeyUp(KeyCode.W) && currentHoldIndex == 1) FinishHold(true);
            if (Input.GetKeyUp(KeyCode.E) && currentHoldIndex == 2) FinishHold(true);
            if (Input.GetKeyUp(KeyCode.R) && currentHoldIndex == 3) FinishHold(true);
        }

        if (doingFlip)
        {
            flipTimer += Time.deltaTime;
            if (boardVisual)
            {
                float t = Mathf.Clamp01(flipTimer / Mathf.Max(0.001f, flipTotal));
                float spins = rotationsPerFlip * 360f * t;
                boardVisual.localRotation = Quaternion.Euler(0, 0, spins);
            }
        }

        if (grinding)
        {
            grindTimer += Time.deltaTime;
            points += Mathf.RoundToInt(grindScorePerSecond * Time.deltaTime);
        }
    }

    void LateUpdate()
    {
        if (!inAir) return;

        // If flip finished mid-air, award now (simpler feedback)
        if (doingFlip && flipTimer >= flipTotal)
        {
            CompleteFlip();
        }

        // If the player touched ground while a trick still active, bail
        if (player && !player.IsAirborne)
        {
            if (doingFlip || doingHold) FailAndBail();
        }
    }

    // Air hooks
    public void OnAirStart()
    {
        inAir = true;
        doingFlip = false; currentFlipIndex = -1; flipTimer = 0f; flipTotal = 0f;
        doingHold = false; currentHoldIndex = -1; holdTimer = 0f;
    }

    public void OnAirEnd(bool landed)
    {
        if (!inAir) return;
        if (landed && (doingFlip || doingHold)) FailAndBail();

        inAir = false;
        doingFlip = false; currentFlipIndex = -1; flipTimer = 0f; flipTotal = 0f;
        FinishHold(false);
        if (boardVisual) boardVisual.localRotation = Quaternion.identity;
    }

    public void OnBail()
    {
        inAir = false;
        doingFlip = false; currentFlipIndex = -1;
        FinishHold(false);
        if (boardVisual) boardVisual.localRotation = Quaternion.identity;
    }

    // Flips
    public void TriggerFlip(int index)
    {
        if (!inAir || doingFlip || doingHold) return;
        if (index < 0 || index >= flipNames.Length) return;

        doingFlip = true;
        currentFlipIndex = index;
        flipTimer = 0f;
        flipTotal = Mathf.Clamp(flipDurations[index], 0.05f, 2f);
        Debug.Log($"Flip start: {flipNames[index]} ({flipTotal:0.00}s)");
    }

    void CompleteFlip()
    {
        if (!doingFlip) return;
        doingFlip = false;
        points += flipScore;
        PushRecent(flipNames[currentFlipIndex]);
        Debug.Log($"Flip landed: {flipNames[currentFlipIndex]} (+{flipScore}) | Score={points}");
        currentFlipIndex = -1;
        if (boardVisual) boardVisual.localRotation = Quaternion.identity;
    }

    // Holds
    public void StartHold(int index)
    {
        if (!inAir || doingFlip || doingHold) return;
        if (index < 0 || index >= holdNames.Length) return;

        doingHold = true;
        currentHoldIndex = index;
        holdTimer = 0f;
        Debug.Log($"Hold start: {holdNames[index]}");
    }

    void FinishHold(bool success)
    {
        if (!doingHold) return;
        if (success)
        {
            int add = Mathf.RoundToInt(holdScorePerSecond * holdTimer);
            points += add;
            PushRecent(holdNames[currentHoldIndex]);
            Debug.Log($"Hold landed: {holdNames[currentHoldIndex]} (+{add}) | Score={points}");
        }
        doingHold = false;
        currentHoldIndex = -1;
        holdTimer = 0f;
    }

    // Grind (for rails)
    public void StartGrind()
    {
        if (grinding) return;
        grinding = true;
        grindTimer = 0f;
        Debug.Log("Grind: start");
    }

    public void StopGrind()
    {
        if (!grinding) return;
        grinding = false;
        PushRecent("Grind");
        Debug.Log("Grind: stop");
    }

    void FailAndBail()
    {
        Debug.Log("TRICK FAIL → BAIL");
        player?.Bail();
        doingFlip = false; currentFlipIndex = -1;
        FinishHold(false);
        if (boardVisual) boardVisual.localRotation = Quaternion.identity;
    }

    void PushRecent(string s)
    {
        recent.Enqueue(s);
        while (recent.Count > recentBufferSize) recent.Dequeue();
        // HUD later can read recent + points
    }
}
