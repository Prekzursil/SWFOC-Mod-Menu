using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Savegame;

namespace SwfocTrainer.App.V2.ViewModels;

/// <summary>
/// 2026-05-22 (iter-289b, spec iter-289 WPF savegame editor tab): operator-facing
/// surface over the <c>SwfocTrainer.Savegame</c> engine — load a
/// <c>.PetroglyphFoC64Save</c> file, walk its chunk tree, edit or delete the
/// micro-chunks of a leaf, validate (and re-anchor) the embedded mod hash, and
/// write the result back so it loads in-game.
///
/// <para>
/// LIVE — pure local-file work. No bridge call; the tab operates on save files
/// at rest and works whether SWFOC is running or not. A successful command
/// here changes bytes on disk only when <c>Save</c> is run; every other command
/// mutates the in-memory document and surfaces that through <c>IsDirty</c>.
/// </para>
///
/// <para>
/// This view-model is the testable core of the tab; the XAML <c>UserControl</c>
/// and the <c>MainViewModelV2</c> tab registration are wired in a follow-up
/// editor-polish iteration. The heavy parse / fix / serialize work runs on a
/// background thread so the WPF dispatcher stays responsive on a 200 MB+ save.
/// </para>
/// </summary>
public sealed class SavegameEditorTabViewModel : ObservableBase
{
    private const string SaveExtension = ".PetroglyphFoC64Save";

    private readonly ModHashValidator _modHashValidator = new();

    private string _savePath = string.Empty;
    private string _outputPath = string.Empty;
    private string _modDataPath = string.Empty;
    private string _editValue = string.Empty;
    private string _status = "(idle — point at a .PetroglyphFoC64Save file and click Load)";
    private string _headerSummary = "(no save loaded)";
    private string _diagnosisSummary = "(Diagnose not run)";
    private string _modHashSummary = "(mod-hash check not run)";
    private bool _isBusy;

    private SavegameDocument? _document;
    private SavegameChunkNode? _selectedChunk;
    private SavegameMicroChunkRow? _selectedMicroChunk;

    public SavegameEditorTabViewModel()
    {
        Chunks = new ObservableCollection<SavegameChunkNode>();
        MicroChunks = new ObservableCollection<SavegameMicroChunkRow>();

        LoadCommand = new AsyncRelayCommand(
            LoadAsync, () => !_isBusy && !string.IsNullOrWhiteSpace(_savePath), OnCommandError);
        DiagnoseCommand = new AsyncRelayCommand(
            DiagnoseAsync, () => !_isBusy && !string.IsNullOrWhiteSpace(_savePath), OnCommandError);
        FixCommand = new AsyncRelayCommand(
            FixAsync, () => !_isBusy && !string.IsNullOrWhiteSpace(_savePath), OnCommandError);
        SaveCommand = new AsyncRelayCommand(
            SaveAsync,
            () => !_isBusy && IsLoaded && !string.IsNullOrWhiteSpace(_outputPath),
            OnCommandError);
        ValidateModHashCommand = new AsyncRelayCommand(
            ValidateModHashAsync,
            () => !_isBusy && IsLoaded && !string.IsNullOrWhiteSpace(_modDataPath),
            OnCommandError);
        ReAnchorModHashCommand = new AsyncRelayCommand(
            ReAnchorModHashAsync,
            () => !_isBusy && IsLoaded && !string.IsNullOrWhiteSpace(_modDataPath),
            OnCommandError);
        ApplyEditCommand = new RelayCommand(ApplyEdit, () => _selectedMicroChunk is not null);
        DeleteMicroChunkCommand = new RelayCommand(
            DeleteSelectedMicroChunk, () => _selectedMicroChunk is not null);
    }

    // ── operator inputs ───────────────────────────────────────────

    /// <summary>Path of the savegame file to load, diagnose or fix.</summary>
    public string SavePath
    {
        get => _savePath;
        set => SetField(ref _savePath, value ?? string.Empty);
    }

    /// <summary>Path the edited document is written to by <see cref="SaveCommand"/>.</summary>
    public string OutputPath
    {
        get => _outputPath;
        set => SetField(ref _outputPath, value ?? string.Empty);
    }

    /// <summary>
    /// A mod ObjectType XML file, or a directory of them, used to compute the
    /// current mod hash for validate / re-anchor.
    /// </summary>
    public string ModDataPath
    {
        get => _modDataPath;
        set => SetField(ref _modDataPath, value ?? string.Empty);
    }

    /// <summary>
    /// The edit field for the selected micro-chunk — a decimal or <c>0x</c>-hex
    /// int32 for the int32 field types, otherwise a hex byte string.
    /// </summary>
    public string EditValue
    {
        get => _editValue;
        set => SetField(ref _editValue, value ?? string.Empty);
    }

    // ── read-only state ───────────────────────────────────────────

    /// <summary>Operator status line — the outcome of the last command.</summary>
    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    /// <summary>One-line description of the loaded save's RGMH header.</summary>
    public string HeaderSummary
    {
        get => _headerSummary;
        private set => SetField(ref _headerSummary, value);
    }

    /// <summary>The result of the last <see cref="DiagnoseCommand"/> pass.</summary>
    public string DiagnosisSummary
    {
        get => _diagnosisSummary;
        private set => SetField(ref _diagnosisSummary, value);
    }

    /// <summary>The result of the last mod-hash validate or re-anchor.</summary>
    public string ModHashSummary
    {
        get => _modHashSummary;
        private set => SetField(ref _modHashSummary, value);
    }

    /// <summary>True while a background load / fix / save is running.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    /// <summary>True when a savegame document is loaded and editable.</summary>
    public bool IsLoaded => _document is not null;

    /// <summary>True when the loaded document has an unsaved edit.</summary>
    public bool IsDirty => _document?.IsDirty ?? false;

    /// <summary>The flattened, depth-tagged chunk tree of the loaded save.</summary>
    public ObservableCollection<SavegameChunkNode> Chunks { get; }

    /// <summary>The micro-chunks of <see cref="SelectedChunk"/>, when it is a leaf.</summary>
    public ObservableCollection<SavegameMicroChunkRow> MicroChunks { get; }

    /// <summary>Whether the last <see cref="FixCommand"/> pass recovered the save.</summary>
    public bool LastFixRecovered { get; private set; }

    /// <summary>
    /// The status name of the last mod-hash check — <c>Match</c>, <c>Mismatch</c>
    /// or <c>NoEmbeddedHash</c> — or empty when no check has run.
    /// </summary>
    public string LastModHashOutcome { get; private set; } = string.Empty;

    /// <summary>The chunk node whose micro-chunks are shown in <see cref="MicroChunks"/>.</summary>
    public SavegameChunkNode? SelectedChunk
    {
        get => _selectedChunk;
        set
        {
            if (SetField(ref _selectedChunk, value))
            {
                SelectedMicroChunk = null;
                if (value is null)
                {
                    MicroChunks.Clear();
                }
                else
                {
                    RebuildMicroChunks(value);
                }
            }
        }
    }

    /// <summary>The micro-chunk targeted by <see cref="ApplyEdit"/> / delete.</summary>
    public SavegameMicroChunkRow? SelectedMicroChunk
    {
        get => _selectedMicroChunk;
        set
        {
            if (SetField(ref _selectedMicroChunk, value))
            {
                EditValue = value is null ? string.Empty : DescribeEditValue(value);
            }
        }
    }

    // ── commands ──────────────────────────────────────────────────

    public ICommand LoadCommand { get; }
    public ICommand DiagnoseCommand { get; }
    public ICommand FixCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ValidateModHashCommand { get; }
    public ICommand ReAnchorModHashCommand { get; }
    public ICommand ApplyEditCommand { get; }
    public ICommand DeleteMicroChunkCommand { get; }

    // ── load ──────────────────────────────────────────────────────

    /// <summary>Reads <see cref="SavePath"/> from disk and loads it as a document.</summary>
    public async Task LoadAsync()
    {
        if (_isBusy)
        {
            return;
        }

        if (!File.Exists(_savePath))
        {
            Status = $"Save file not found: {_savePath}";
            return;
        }

        IsBusy = true;
        try
        {
            var path = _savePath;
            var buffer = await Task.Run(() => File.ReadAllBytes(path));
            OutputPath = SuggestOutputPath(path);
            await LoadBufferCore(buffer);
        }
        catch (IOException ex)
        {
            ClearDocument();
            Status = $"Read failed: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            ClearDocument();
            Status = $"Read failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Loads an in-memory savegame buffer as an editable document.</summary>
    public async Task LoadFromBufferAsync(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (_isBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await LoadBufferCore(buffer);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadBufferCore(byte[] buffer)
    {
        try
        {
            var document = await Task.Run(() => SavegameDocument.Load(buffer));
            SetDocument(document);
            Status =
                $"Loaded {Chunks.Count} chunk node(s) from a {buffer.Length:N0}-byte save.";
        }
        catch (SavegameFormatException ex)
        {
            ClearDocument();
            Status = $"Load failed — malformed save: {ex.Message}. Run Diagnose, then Fix.";
        }
    }

    // ── diagnose ──────────────────────────────────────────────────

    /// <summary>Reads <see cref="SavePath"/> and reports its structural health.</summary>
    public async Task DiagnoseAsync()
    {
        if (_isBusy)
        {
            return;
        }

        if (!File.Exists(_savePath))
        {
            Status = $"Save file not found: {_savePath}";
            return;
        }

        IsBusy = true;
        try
        {
            var path = _savePath;
            var buffer = await Task.Run(() => File.ReadAllBytes(path));
            DiagnoseBuffer(buffer);
        }
        catch (IOException ex)
        {
            Status = $"Read failed: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            Status = $"Read failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Diagnoses a savegame buffer. Safe on a corrupt buffer — a format error is
    /// reported, never thrown.
    /// </summary>
    public void DiagnoseBuffer(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        var report = SavegameParser.Diagnose(buffer);
        if (report.Parsed && !report.HasOverflow)
        {
            DiagnosisSummary =
                $"OK — {report.TopChunkCount} top-level / {report.TotalChunkCount} total " +
                $"chunk(s), {report.UniqueChunkIds.Count} unique id(s)";
            Status = "Diagnose complete — save is structurally sound.";
        }
        else if (report.Parsed)
        {
            // The header parsed and the walk completed, but at least one chunk
            // overflowed its region — the save is structurally damaged.
            DiagnosisSummary =
                $"CORRUPT — {report.TopChunkCount} top-level chunk(s) walked, but at least " +
                "one chunk overflows its region";
            Status = "Diagnose complete — save is CORRUPT (overflowing chunk); run Fix.";
        }
        else
        {
            DiagnosisSummary = $"CORRUPT — {report.Error}";
            Status = "Diagnose complete — save is CORRUPT; run Fix to recover it.";
        }
    }

    // ── fix ───────────────────────────────────────────────────────

    /// <summary>Reads <see cref="SavePath"/>, recovers it, and loads the result.</summary>
    public async Task FixAsync()
    {
        if (_isBusy)
        {
            return;
        }

        if (!File.Exists(_savePath))
        {
            Status = $"Save file not found: {_savePath}";
            return;
        }

        IsBusy = true;
        try
        {
            var path = _savePath;
            var buffer = await Task.Run(() => File.ReadAllBytes(path));
            OutputPath = SuggestOutputPath(path);
            await FixBufferCore(buffer);
        }
        catch (IOException ex)
        {
            Status = $"Read failed: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            Status = $"Read failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Recovers a corrupt savegame buffer and loads the repaired result.</summary>
    public async Task FixFromBufferAsync(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (_isBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await FixBufferCore(buffer);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task FixBufferCore(byte[] buffer)
    {
        var report = await Task.Run(() => SavegameFixer.Fix(buffer));
        LastFixRecovered = report.Recovered;
        if (report.Recovered)
        {
            await LoadBufferCore(report.Output);
            Status =
                $"Fix succeeded ({report.Strategy}) — {report.Summary}. " +
                $"Review the chunks, then Save to {_outputPath}.";
        }
        else
        {
            Status = $"Fix FAILED ({report.Strategy}) — {report.Summary}";
        }
    }

    // ── micro-chunk editing ───────────────────────────────────────

    /// <summary>
    /// Applies <see cref="EditValue"/> to <see cref="SelectedMicroChunk"/> — an
    /// int32 rewrite for the int32 field types, otherwise a hex-byte replacement.
    /// </summary>
    public void ApplyEdit()
    {
        if (_selectedChunk is not { } node || _selectedMicroChunk is not { } row)
        {
            Status = "Select a micro-chunk to edit first.";
            return;
        }

        try
        {
            if (row.IsInt32Field)
            {
                if (!TryParseInt32Field(_editValue, out var value))
                {
                    Status = $"'{_editValue}' is not a valid int32 (use decimal or 0x-hex).";
                    return;
                }

                node.Chunk.SetMicroChunkInt32(row.Index, value);
            }
            else
            {
                if (!TryParseHexBytes(_editValue, out var bytes))
                {
                    Status =
                        $"'{_editValue}' is not a valid hex byte string " +
                        $"(<= {MicroChunk.MaxDataLength} bytes).";
                    return;
                }

                node.Chunk.SetMicroChunk(row.Index, MicroChunk.Create(row.TypeCode, bytes));
            }
        }
        catch (ArgumentException ex)
        {
            Status = $"Edit rejected: {ex.Message}";
            return;
        }
        catch (InvalidOperationException ex)
        {
            Status = $"Edit rejected: {ex.Message}";
            return;
        }

        var index = row.Index;
        RefreshAfterMutation(node);
        Status = $"Edited micro-chunk #{index} of {node.IdHex} — Save to write it back.";
    }

    /// <summary>Removes <see cref="SelectedMicroChunk"/> from its leaf chunk.</summary>
    public void DeleteSelectedMicroChunk()
    {
        if (_selectedChunk is not { } node || _selectedMicroChunk is not { } row)
        {
            Status = "Select a micro-chunk to delete first.";
            return;
        }

        var index = row.Index;
        try
        {
            node.Chunk.DeleteMicroChunk(index);
        }
        catch (ArgumentException ex)
        {
            Status = $"Delete rejected: {ex.Message}";
            return;
        }
        catch (InvalidOperationException ex)
        {
            Status = $"Delete rejected: {ex.Message}";
            return;
        }

        RefreshAfterMutation(node);
        Status = $"Deleted micro-chunk #{index} of {node.IdHex} — Save to write it back.";
    }

    // ── mod-hash validation ───────────────────────────────────────

    /// <summary>Reads <see cref="ModDataPath"/> and validates the embedded mod hash.</summary>
    public async Task ValidateModHashAsync()
    {
        if (_isBusy)
        {
            return;
        }

        if (_document is null)
        {
            Status = "Load a save before validating its mod hash.";
            return;
        }

        IsBusy = true;
        try
        {
            var path = _modDataPath;
            var modData = await Task.Run(() => ReadModObjectTypeData(path));
            if (modData is null)
            {
                Status = $"Mod ObjectType data not found: {path}";
                return;
            }

            ValidateModHash(modData);
        }
        catch (IOException ex)
        {
            Status = $"Mod read failed: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            Status = $"Mod read failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Validates the loaded save's embedded mod hash against mod data.</summary>
    public void ValidateModHash(ReadOnlyMemory<byte> modObjectTypeData)
    {
        if (_document is null)
        {
            Status = "Load a save before validating its mod hash.";
            return;
        }

        var result = _modHashValidator.Validate(_document, modObjectTypeData);
        LastModHashOutcome = result.Status.ToString();
        ModHashSummary = result.Summary;
        Status = result.Status switch
        {
            ModHashStatus.Match => "Mod hash matches — the save is bound to the current mod.",
            ModHashStatus.Mismatch =>
                "Mod hash MISMATCH — Re-Anchor to recover the save under the current mod.",
            _ => "No embedded mod hash — this save cannot be validated or re-anchored.",
        };
    }

    /// <summary>Reads <see cref="ModDataPath"/> and re-anchors the embedded mod hash.</summary>
    public async Task ReAnchorModHashAsync()
    {
        if (_isBusy)
        {
            return;
        }

        if (_document is null)
        {
            Status = "Load a save before re-anchoring its mod hash.";
            return;
        }

        IsBusy = true;
        try
        {
            var path = _modDataPath;
            var modData = await Task.Run(() => ReadModObjectTypeData(path));
            if (modData is null)
            {
                Status = $"Mod ObjectType data not found: {path}";
                return;
            }

            ReAnchorModHash(modData);
        }
        catch (IOException ex)
        {
            Status = $"Mod read failed: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            Status = $"Mod read failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Re-anchors the loaded save to the current mod, overwriting its stale
    /// embedded hash. Returns whether the document was changed.
    /// </summary>
    public bool ReAnchorModHash(ReadOnlyMemory<byte> modObjectTypeData)
    {
        if (_document is null)
        {
            Status = "Load a save before re-anchoring its mod hash.";
            return false;
        }

        var changed = _modHashValidator.ReAnchor(_document, modObjectTypeData);
        if (changed)
        {
            var node = FindNodeById(ModHashValidator.ModContextChunkId);
            if (node is not null)
            {
                RefreshAfterMutation(node);
            }
            else
            {
                OnPropertyChanged(nameof(IsDirty));
            }

            ModHashSummary = "Re-anchored — the embedded mod hash now matches the current mod.";
            Status = "Re-anchored mod hash — Save the document to write the repaired save.";
        }
        else
        {
            Status =
                "Re-anchor not applied — the save already matches its mod, " +
                "or carries no embedded hash.";
        }

        return changed;
    }

    // ── save ──────────────────────────────────────────────────────

    /// <summary>Serialises the loaded document and writes it to <see cref="OutputPath"/>.</summary>
    public async Task SaveAsync()
    {
        if (_isBusy)
        {
            return;
        }

        if (_document is null)
        {
            Status = "Nothing loaded — Load a save first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(_outputPath))
        {
            Status = "Set an output path before saving.";
            return;
        }

        IsBusy = true;
        try
        {
            var document = _document;
            var path = _outputPath;
            var edited = document.IsDirty;
            await Task.Run(() => document.SaveFile(path));
            Status = $"Saved {(edited ? "edited" : "unchanged")} document to {path}.";
        }
        catch (IOException ex)
        {
            Status = $"Save failed: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            Status = $"Save failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Serialises the loaded document — with every edit applied — to a buffer.
    /// Throws when no document is loaded.
    /// </summary>
    public byte[] SerializeCurrentDocument()
    {
        if (_document is null)
        {
            throw new InvalidOperationException(
                "no savegame is loaded; load one before serialising.");
        }

        return _document.Serialize();
    }

    // ── internals ─────────────────────────────────────────────────

    private void SetDocument(SavegameDocument document)
    {
        _document = document;
        HeaderSummary = DescribeHeader(document.Header);
        DiagnosisSummary = "(Diagnose not run)";
        RebuildTree();
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(IsDirty));
    }

    private void ClearDocument()
    {
        _document = null;
        Chunks.Clear();
        MicroChunks.Clear();
        _selectedChunk = null;
        _selectedMicroChunk = null;
        _editValue = string.Empty;
        HeaderSummary = "(no save loaded)";
        OnPropertyChanged(nameof(SelectedChunk));
        OnPropertyChanged(nameof(SelectedMicroChunk));
        OnPropertyChanged(nameof(EditValue));
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(IsDirty));
    }

    private void RebuildTree()
    {
        Chunks.Clear();
        MicroChunks.Clear();
        _selectedChunk = null;
        _selectedMicroChunk = null;
        _editValue = string.Empty;
        OnPropertyChanged(nameof(SelectedChunk));
        OnPropertyChanged(nameof(SelectedMicroChunk));
        OnPropertyChanged(nameof(EditValue));

        if (_document is null)
        {
            return;
        }

        foreach (var chunk in _document.Chunks)
        {
            AppendNode(chunk, depth: 0);
        }
    }

    private void AppendNode(EditableChunk chunk, int depth)
    {
        Chunks.Add(new SavegameChunkNode(chunk, depth));
        foreach (var child in chunk.Children)
        {
            AppendNode(child, depth + 1);
        }
    }

    private void RebuildMicroChunks(SavegameChunkNode node)
    {
        MicroChunks.Clear();
        var micros = node.Chunk.MicroChunks;
        for (var i = 0; i < micros.Count; i++)
        {
            MicroChunks.Add(new SavegameMicroChunkRow(i, micros[i]));
        }
    }

    private void RefreshAfterMutation(SavegameChunkNode node)
    {
        node.Refresh();
        if (ReferenceEquals(node, _selectedChunk))
        {
            RebuildMicroChunks(node);
            SelectedMicroChunk = null;
        }

        OnPropertyChanged(nameof(IsDirty));
    }

    private SavegameChunkNode? FindNodeById(uint id)
    {
        foreach (var node in Chunks)
        {
            if (node.Id == id)
            {
                return node;
            }
        }

        return null;
    }

    private void OnCommandError(Exception ex) => Status = $"[error] {ex.Message}";

    private static string DescribeEditValue(SavegameMicroChunkRow row)
    {
        if (row.IsInt32Field && row.Source.Data.Length >= sizeof(int))
        {
            var value = BinaryPrimitives.ReadUInt32LittleEndian(row.Source.Data);
            return $"0x{value:X8}";
        }

        return Convert.ToHexString(row.Source.Data);
    }

    private static string DescribeHeader(SavegameHeader header)
    {
        var bmp = header.HasBmpThumbnail ? " (after BMP thumbnail)" : string.Empty;
        return
            $"{header.Magic} v{header.Version} — \"{header.Label}\", uuid {header.UuidHex}, " +
            $"chunk stream @ 0x{header.ChunkStreamOffset:X}{bmp}";
    }

    private static string SuggestOutputPath(string savePath)
    {
        var directory = Path.GetDirectoryName(savePath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(savePath);
        var extension = Path.GetExtension(savePath);
        if (string.IsNullOrEmpty(extension))
        {
            extension = SaveExtension;
        }

        return Path.Combine(directory, $"{stem}.edited{extension}");
    }

    private static byte[]? ReadModObjectTypeData(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (File.Exists(path))
        {
            return File.ReadAllBytes(path);
        }

        if (!Directory.Exists(path))
        {
            return null;
        }

        var files = Directory.EnumerateFiles(path, "*.xml", SearchOption.AllDirectories)
            .OrderBy(static p => p, StringComparer.Ordinal)
            .ToArray();
        if (files.Length == 0)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        foreach (var file in files)
        {
            buffer.Write(File.ReadAllBytes(file));
        }

        return buffer.ToArray();
    }

    private static bool TryParseInt32Field(string text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (uint.TryParse(
                    trimmed.AsSpan(2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out var hex))
            {
                value = unchecked((int)hex);
                return true;
            }

            return false;
        }

        if (int.TryParse(
                trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        if (uint.TryParse(
                trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unsigned))
        {
            value = unchecked((int)unsigned);
            return true;
        }

        return false;
    }

    private static bool TryParseHexBytes(string text, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (text is null)
        {
            return false;
        }

        var builder = new StringBuilder();
        foreach (var token in text.Split(
                     new[] { ' ', '\t', ',', '\r', '\n' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var cleaned = token.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? token[2..]
                : token;
            builder.Append(cleaned);
        }

        var joined = builder.ToString();
        if (joined.Length % 2 != 0)
        {
            return false;
        }

        if (joined.Length / 2 > MicroChunk.MaxDataLength)
        {
            return false;
        }

        foreach (var character in joined)
        {
            if (!Uri.IsHexDigit(character))
            {
                return false;
            }
        }

        bytes = Convert.FromHexString(joined);
        return true;
    }
}

/// <summary>
/// A node in the flattened savegame chunk tree shown by
/// <see cref="SavegameEditorTabViewModel"/>. Wraps one
/// <see cref="EditableChunk"/> and carries its tree depth for indentation.
/// </summary>
public sealed class SavegameChunkNode : ObservableBase
{
    internal SavegameChunkNode(EditableChunk chunk, int depth)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        Chunk = chunk;
        Depth = depth;
    }

    internal EditableChunk Chunk { get; }

    /// <summary>Nesting depth of this chunk; 0 for a top-level chunk.</summary>
    public int Depth { get; }

    /// <summary>The 32-bit chunk id, widened to a CLS-compliant <see cref="long"/>.</summary>
    public long Id => Chunk.Id;

    /// <summary>The chunk id rendered as <c>0xXXXXXXXX</c>.</summary>
    public string IdHex => Chunk.IdHex;

    /// <summary>The chunk id decoded as a FourCC, or empty when non-printable.</summary>
    public string IdFourCc => Chunk.IdFourCc;

    /// <summary>Whether the chunk is a container, a micro-chunk leaf or a raw leaf.</summary>
    public string Kind =>
        Chunk.HasSubChunks ? "container"
        : Chunk.IsMicroLeaf ? "micro-leaf"
        : "raw-leaf";

    /// <summary>Count of direct child chunks.</summary>
    public int ChildCount => Chunk.Children.Count;

    /// <summary>Count of editable micro-chunks; non-zero only on a micro-chunk leaf.</summary>
    public int MicroChunkCount => Chunk.MicroChunks.Count;

    /// <summary>True when this chunk — or any descendant — has an unsaved edit.</summary>
    public bool IsDirty => Chunk.IsDirty;

    /// <summary>Leading whitespace that renders the tree depth.</summary>
    public string Indent => new(' ', Depth * 2);

    /// <summary>A one-line, indented description of the chunk for a list row.</summary>
    public string Summary
    {
        get
        {
            var fourCc = string.IsNullOrEmpty(IdFourCc) ? string.Empty : $" '{IdFourCc}'";
            var detail = Kind switch
            {
                "container" => $"{ChildCount} child chunk(s)",
                "micro-leaf" => $"{MicroChunkCount} micro-chunk(s)",
                _ => "raw data",
            };
            var dirty = IsDirty ? " *edited*" : string.Empty;
            return $"{Indent}{IdHex}{fourCc} — {Kind}, {detail}{dirty}";
        }
    }

    /// <summary>Re-raises change notifications for the mutation-sensitive members.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(MicroChunkCount));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(Summary));
    }
}

/// <summary>
/// One editable micro-chunk row shown for the selected leaf chunk in
/// <see cref="SavegameEditorTabViewModel"/>.
/// </summary>
public sealed class SavegameMicroChunkRow
{
    internal SavegameMicroChunkRow(int index, MicroChunk source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Index = index;
        Source = source;
    }

    internal MicroChunk Source { get; }

    /// <summary>Position of this micro-chunk inside the leaf body.</summary>
    public int Index { get; }

    /// <summary>The 1-byte micro-chunk type code.</summary>
    public byte TypeCode => Source.TypeCode;

    /// <summary>The type code rendered as <c>0xXX</c>.</summary>
    public string TypeCodeHex => $"0x{Source.TypeCode:X2}";

    /// <summary>A human-readable name for the type code.</summary>
    public string TypeName => DescribeType(Source.TypeCode);

    /// <summary>The declared data length of this micro-chunk.</summary>
    public int Length => Source.Length;

    /// <summary>True when the type code is one of the int32 field codes 0x01-0x04.</summary>
    public bool IsInt32Field => Source.IsInt32Field;

    /// <summary>A short, display-friendly rendering of the micro-chunk payload.</summary>
    public string ValuePreview
    {
        get
        {
            if (IsInt32Field && Source.Data.Length >= sizeof(int))
            {
                var value = Source.AsInt32();
                return $"0x{(uint)value:X8} ({value})";
            }

            if (Source.Data.Length == 0)
            {
                return "(empty)";
            }

            var hex = Convert.ToHexString(Source.Data);
            return hex.Length <= 32 ? hex : $"{hex[..32]}...";
        }
    }

    private static string DescribeType(byte code) => code switch
    {
        MicroChunk.TypeRaw => "raw",
        >= MicroChunk.TypeInt32First and <= MicroChunk.TypeInt32Last => "int32",
        MicroChunk.TypeStringBlob => "string/blob",
        MicroChunk.TypeIntArray => "int-array",
        _ => "unknown",
    };
}
