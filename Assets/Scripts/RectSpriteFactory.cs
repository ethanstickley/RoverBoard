using UnityEngine;

public static class RectSpriteFactory
{
    static Sprite _white;
    public static Sprite WhiteSprite
    {
        get
        {
            if (_white == null)
            {
                var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                tex.SetPixel(0, 0, Color.white);
                tex.filterMode = FilterMode.Point;
                tex.Apply();
                _white = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f); // PPU=1
                _white.name = "UnitWhite1x1_PPU1";
            }
            return _white;
        }
    }
}
