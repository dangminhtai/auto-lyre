using System;

namespace AutoLyre.App.ModernWPF.Errors;

public class MissingNotesException : Exception
{
    public MissingNotesException(string message) : base(message) { }
}