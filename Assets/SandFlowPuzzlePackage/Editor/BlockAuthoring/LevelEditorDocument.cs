using System;
using System.Collections.Generic;
using SandFlowPuzzle.BlockAuthoring;

namespace SandFlowPuzzle.BlockAuthoring.EditorTools
{
    /// <summary>
    /// Transactional in-memory level document. Mutations go through
    /// <see cref="TryCommit"/> or <see cref="TryCommitWithoutSave"/>, which clone live data, apply
    /// the mutation inside an exception boundary, validate the candidate, and only then swap it in
    /// and bump the revision. Immediate commits autosave to the current path; a commit whose
    /// autosave fails still returns true: the candidate stays committed in memory and
    /// <see cref="LastError"/> carries the save error.
    /// </summary>
    public sealed class LevelEditorDocument
    {
        private readonly ILevelDataRepository _repository;
        private BlockXLevelFile _data;
        private string _filePath;

        /// <summary>Live in-memory document data; null until a load or create succeeds.</summary>
        public BlockXLevelFile Data => _data;

        /// <summary>Path the document was loaded from, created at, or last saved to.</summary>
        public string FilePath => _filePath;

        /// <summary>True once a load or create has installed live data.</summary>
        public bool HasDocument => _data != null;

        /// <summary>Monotonic commit counter; reset to 0 when a different file is loaded or created.</summary>
        public int Revision { get; private set; }

        /// <summary>True when live data matches the last successful save at <see cref="FilePath"/>.</summary>
        public bool IsSaved { get; private set; }

        /// <summary>Message of the last failed operation, or null when the last operation succeeded.</summary>
        public string LastError { get; private set; }

        public LevelEditorDocument(ILevelDataRepository repository = null)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        /// <summary>
        /// Creates an all-ON rows-by-columns, version-1 document with the supplied ID and an empty
        /// block list, validates and saves it through the repository, and installs it as the live
        /// document only after the initial save succeeds. A failed initial save leaves
        /// <see cref="HasDocument"/> false and the previous document untouched.
        /// </summary>
        public bool CreateNew(string filePath, string levelId, int rows, int columns, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(levelId))
            {
                error = "Level ID cannot be empty.";
                LastError = error;
                return false;
            }

            if (rows < BlockXLevelFormat.MinRows || rows > BlockXLevelFormat.MaxRows)
            {
                error = $"Grid rows must be between {BlockXLevelFormat.MinRows} and {BlockXLevelFormat.MaxRows}.";
                LastError = error;
                return false;
            }

            if (columns < BlockXLevelFormat.MinColumns || columns > BlockXLevelFormat.MaxColumns)
            {
                error = $"Grid columns must be between {BlockXLevelFormat.MinColumns} and {BlockXLevelFormat.MaxColumns}.";
                LastError = error;
                return false;
            }

            var cells = new int[rows * columns];
            for (int cellIndex = 0; cellIndex < cells.Length; cellIndex++)
            {
                cells[cellIndex] = 1;
            }

            var candidate = new BlockXLevelFile
            {
                version = BlockXLevelFormat.Version,
                levelId = levelId,
                grid = new LevelGridFile { rows = rows, columns = columns, cells = cells },
                blocks = new List<LevelBlockFile>()
            };

            if (!_repository.TrySaveAtomic(filePath, candidate, out error))
            {
                LastError = error;
                return false;
            }

            _data = candidate;
            _filePath = filePath;
            Revision = 0;
            IsSaved = true;
            LastError = null;
            return true;
        }

        /// <summary>
        /// Replaces the live document only after the repository load succeeds; the revision resets
        /// to 0, the document is marked saved, and <see cref="LastError"/> clears. On failure the
        /// current document, revision, and path are left untouched.
        /// </summary>
        public bool Load(string filePath, out string error)
        {
            error = null;

            if (!_repository.TryLoad(filePath, out BlockXLevelFile loaded, out error))
            {
                LastError = error;
                return false;
            }

            _data = loaded;
            _filePath = filePath;
            Revision = 0;
            IsSaved = true;
            LastError = null;
            return true;
        }

        /// <summary>
        /// Saves the current document to <see cref="FilePath"/> without incrementing the revision.
        /// Requires a live document.
        /// </summary>
        public bool Save(out string error)
        {
            error = null;

            if (_data == null)
            {
                error = "No level document is loaded.";
                LastError = error;
                return false;
            }

            if (!_repository.TrySaveAtomic(_filePath, _data, out error))
            {
                LastError = error;
                return false;
            }

            IsSaved = true;
            LastError = null;
            return true;
        }

        /// <summary>
        /// Saves the current document to a new path and repoints <see cref="FilePath"/> only after
        /// the save succeeds. Failure leaves the current path and data unchanged. The revision is
        /// not incremented.
        /// </summary>
        public bool SaveAs(string filePath, out string error)
        {
            error = null;

            if (_data == null)
            {
                error = "No level document is loaded.";
                LastError = error;
                return false;
            }

            if (!_repository.TrySaveAtomic(filePath, _data, out error))
            {
                LastError = error;
                return false;
            }

            _filePath = filePath;
            IsSaved = true;
            LastError = null;
            return true;
        }

        /// <summary>
        /// Transactionally applies a mutation: clones live data, runs the mutation inside an
        /// exception boundary, validates the candidate, assigns it, increments the revision exactly
        /// once, marks the document unsaved, and autosaves to the current path exactly once.
        ///
        /// Returns true whenever a valid candidate became the live document, even if the autosave
        /// then fails (committed memory is preserved, the document stays unsaved, and
        /// <see cref="LastError"/> carries the save error). An invalid candidate, an exception from
        /// the mutation callback, or no loaded document returns false with live data, revision, and
        /// save count unchanged.
        /// </summary>
        public bool TryCommit(string actionName, Action<BlockXLevelFile> mutation, out string error)
        {
            return TryCommitInternal(actionName, mutation, saveImmediately: true, out error);
        }

        /// <summary>
        /// Transactionally applies a mutation without saving. This is used for an in-progress editor
        /// stroke: each valid cell is committed and visible in memory, while the caller saves the
        /// completed stroke once at the end.
        /// </summary>
        public bool TryCommitWithoutSave(string actionName, Action<BlockXLevelFile> mutation, out string error)
        {
            return TryCommitInternal(actionName, mutation, saveImmediately: false, out error);
        }

        private bool TryCommitInternal(
            string actionName,
            Action<BlockXLevelFile> mutation,
            bool saveImmediately,
            out string error)
        {
            error = null;

            if (_data == null)
            {
                error = "No level document is loaded.";
                LastError = error;
                return false;
            }

            if (mutation == null)
            {
                error = "Mutation cannot be null.";
                LastError = error;
                return false;
            }

            BlockXLevelFile candidate = LevelDataCloneUtility.DeepClone(_data);
            try
            {
                mutation(candidate);
            }
            catch (Exception ex)
            {
                error = $"{actionName} failed: {ex.Message}";
                LastError = error;
                return false;
            }

            if (!LevelEditorValidator.TryValidate(candidate, out error))
            {
                LastError = error;
                return false;
            }

            _data = candidate;
            Revision++;
            IsSaved = false;
            LastError = null;

            if (!saveImmediately)
            {
                return true;
            }

            if (!_repository.TrySaveAtomic(_filePath, _data, out string saveError))
            {
                LastError = saveError;
                return true;
            }

            IsSaved = true;
            return true;
        }
    }
}
