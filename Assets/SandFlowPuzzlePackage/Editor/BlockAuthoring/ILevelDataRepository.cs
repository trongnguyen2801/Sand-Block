namespace SandFlowPuzzle.BlockAuthoring.EditorTools
{
    // The host stores this document inside LevelData and saves it with the pictures.
    public interface ILevelDataRepository
    {
        bool TryLoad(string path, out BlockXLevelFile data, out string error);
        bool TrySaveAtomic(string path, BlockXLevelFile data, out string error);
    }
}
