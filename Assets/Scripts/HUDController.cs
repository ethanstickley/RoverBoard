// HUDController.cs (TMP version)
// Minimal, extensible HUD with two TMP text fields (points & treats)
// and a sprite-based health display (9 frames).
// Drop this on a Canvas GameObject. Assign references in the Inspector.

using UnityEngine;
using UnityEngine.UI;   // for Image
using TMPro;            // for TMP_Text

[DisallowMultipleComponent]
public class HUDController : MonoBehaviour
{
    [Header("Text Elements (TMP)")]
    [Tooltip("TMP text that shows the player's accumulated points.")]
    public TMP_Text pointsText;

    [Tooltip("TMP text that shows how many treats the dog has eaten.")]
    public TMP_Text treatsText;

    [Header("Health Display")]
    [Tooltip("UI Image used to display the player's health sprite.")]
    public Image healthImage;

    [Tooltip("Sprites indexing health from 0..8 (9 images total). Index 0 = lowest/no health, 8 = full health.")]
    public Sprite[] healthSprites = new Sprite[9];

    [Header("Formatting")]
    [Tooltip("Prefix for the points readout.")]
    public string pointsLabel = "Points: ";

    [Tooltip("Prefix for the treats readout.")]
    public string treatsLabel = "Treats: ";

    // Cached values (optional; useful if you later animate changes)
    int _points;
    int _treats;
    int _healthIndex;

    // Optional singleton access
    public static HUDController Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Initialize the UI to whatever defaults are currently cached
        ApplyPointsText(_points);
        ApplyTreatsText(_treats);
        ApplyHealthSprite(_healthIndex);
    }

    // -------------------------
    // Public API (simple & explicit)
    // -------------------------

    /// <summary>Set the player's accumulated points. Updates the points text immediately.</summary>
    public void SetPoints(int points)
    {
        _points = Mathf.Max(0, points);
        ApplyPointsText(_points);
    }

    /// <summary>Add delta to the current points and update the HUD.</summary>
    public void AddPoints(int delta)
    {
        SetPoints(_points + delta);
    }

    /// <summary>Set the number of treats eaten by the dog. Updates the treats text immediately.</summary>
    public void SetTreats(int treats)
    {
        _treats = Mathf.Max(0, treats);
        ApplyTreatsText(_treats);
    }

    /// <summary>Add delta to the current treats count and update the HUD.</summary>
    public void AddTreat(int delta = 1)
    {
        SetTreats(_treats + delta);
    }

    /// <summary>
    /// Set health sprite by explicit index 0..8 (clamped). Index 0 = lowest/no health, 8 = full health.
    /// </summary>
    public void SetHealthIndex(int index0to8)
    {
        _healthIndex = Mathf.Clamp(index0to8, 0, 8);
        ApplyHealthSprite(_healthIndex);
    }

    /// <summary>
    /// Convenience: set health by fraction [0..1] (auto-maps to 0..8).
    /// </summary>
    public void SetHealthFraction(float fraction01)
    {
        int idx = Mathf.RoundToInt(Mathf.Clamp01(fraction01) * 8f);
        SetHealthIndex(idx);
    }

    // -------------------------
    // Internal helpers
    // -------------------------

    void ApplyPointsText(int val)
    {
        if (pointsText)
            pointsText.text = pointsLabel + val.ToString();
    }

    void ApplyTreatsText(int val)
    {
        if (treatsText)
            treatsText.text = treatsLabel + val.ToString();
    }

    void ApplyHealthSprite(int idx)
    {
        if (!healthImage) return;

        if (healthSprites != null && healthSprites.Length == 9)
        {
            var sprite = healthSprites[Mathf.Clamp(idx, 0, 8)];
            if (sprite != null)
            {
                healthImage.enabled = true;
                healthImage.sprite = sprite;
                return;
            }
        }

        // If no sprite available, hide the image to avoid confusion.
        healthImage.enabled = false;
    }
}
