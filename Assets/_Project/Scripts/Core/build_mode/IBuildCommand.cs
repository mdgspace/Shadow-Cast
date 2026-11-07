namespace ShadowCast.Core.BuildMode
{
    /// <summary>
    /// Interface for any reversible build action in the Build Mode system.
    /// </summary>
    public interface IBuildCommand
    {
        string Description { get; }
        void Execute();
        void Undo();
        void Redo();
    }
}

