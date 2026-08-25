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

        public static readonly Dictionary<string, Vector2> MapSizePresets = new Dictionary<string, Vector2>
        {
            { "36x36", new Vector2(36f * MmPerInch, 36f * MmPerInch) },
            { "54x36", new Vector2(54f * MmPerInch, 36f * MmPerInch) },
        };
    }
}
