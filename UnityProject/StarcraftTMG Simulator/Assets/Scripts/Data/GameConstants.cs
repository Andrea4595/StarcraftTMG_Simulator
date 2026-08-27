using System.Collections.Generic;
using UnityEngine;

namespace TmgBoard
{
    public static class GameConstants
    {
        public const float MmPerInch = 25.4f;
        public const float DefaultMoveInch = 4f;
        public const float DefaultCoherencyInch = 3f;

        public const string DefaultMapSizePreset = "36x36";

        public const float ScoreboardHeight = 84f;

        public static readonly Dictionary<string, Vector2> MapSizePresets = new Dictionary<string, Vector2>
        {
            { "36x36", new Vector2(36f * MmPerInch, 36f * MmPerInch) },
            { "54x36", new Vector2(54f * MmPerInch, 36f * MmPerInch) },
        };

        public static readonly Dictionary<string, Color> TeamColors = new Dictionary<string, Color>
        {
            { "A", new Color(1f, 0.15f, 0.15f, 0.85f) },
            { "B", new Color(0.15f, 0.35f, 1f, 0.85f) },
            { "neutral", new Color(0.6f, 0.6f, 0.6f, 0.85f) },
        };
    }
}
