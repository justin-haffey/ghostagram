using Diagrams.Core.Abstractions;
using Diagrams.Core.Models;

namespace Editor.Client.Services;

public sealed class DocumentCommandHistory : ICommandHistory
{
    private readonly Stack<DiagramDocument> _undo = new();
    private readonly Stack<DiagramDocument> _redo = new();
    private DiagramDocument? _current;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Reset(DiagramDocument document)
    {
        _undo.Clear();
        _redo.Clear();
        _current = document;
    }

    public void Record(DiagramDocument previous, DiagramDocument current)
    {
        if (previous.DocumentId != current.DocumentId)
        {
            Reset(current);
            return;
        }

        _undo.Push(previous);
        _redo.Clear();
        _current = current;
    }

    public DiagramDocument? Undo()
    {
        if (_undo.Count == 0 || _current is null)
        {
            return null;
        }

        var previous = _undo.Pop();
        _redo.Push(_current);
        _current = previous;
        return previous;
    }

    public DiagramDocument? Redo()
    {
        if (_redo.Count == 0 || _current is null)
        {
            return null;
        }

        var next = _redo.Pop();
        _undo.Push(_current);
        _current = next;
        return next;
    }
}
