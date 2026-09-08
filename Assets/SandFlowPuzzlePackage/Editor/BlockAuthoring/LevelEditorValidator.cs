
using SandFlowPuzzle.BlockAuthoring;

namespace SandFlowPuzzle.BlockAuthoring.EditorTools
{
    /// <summary>
    /// Editor-v1 structural validator for <see cref="BlockXLevelFile"/>.
    ///
    /// Runs the editor-specific structural checks in a fixed, deterministic order, then converts
    /// the DTO through <see cref="LevelDataConverter"/> and delegates the final gameplay rules to
    /// the existing <see cref="GameBoardValidator"/>. The gameplay validator alone is not
    /// sufficient because it accepts a null color-block list.
    /// </summary>
    public static class LevelEditorValidator
    {
        public static bool TryValidate(BlockXLevelFile data, out string error)
        {
            return BlockXLevelValidator.TryValidate(data, out error);
        }
    }
}
