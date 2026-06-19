using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Data;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using TextMateSharp.Grammars;

namespace CAP.Avalonia.Behaviors;

/// <summary>
/// Attached behavior that gives AvaloniaEdit's <see cref="TextEditor"/> a two-way
/// bindable text property and installs Python syntax highlighting with a dark theme.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TextEditor.Text"/> is a plain CLR property, not a styled property, so
/// <c>Text="{Binding ...}"</c> does not bind two-way. This behavior bridges the gap by
/// syncing <see cref="TextEditor.Document"/>.Text with the bound <see cref="BoundTextProperty"/>
/// in both directions, guarding against re-entrancy per editor instance.
/// </para>
/// <para>
/// Usage in XAML:
/// <code>
/// &lt;AvaloniaEdit:TextEditor behaviors:TextEditorBindingBehavior.BoundText="{Binding Code, Mode=TwoWay}"/&gt;
/// </code>
/// </para>
/// <para>
/// Grammar/theme installation is wrapped in a try/catch so a failure (missing grammar,
/// runtime issue) degrades gracefully to a plain, still-editable editor.
/// </para>
/// </remarks>
public static class TextEditorBindingBehavior
{
    /// <summary>Per-editor sync state, keyed by the editor instance.</summary>
    private sealed class EditorState
    {
        public bool UpdatingFromProperty;
        public bool UpdatingFromEditor;
        public bool Highlighted;
    }

    private static readonly Dictionary<TextEditor, EditorState> States = new();

    /// <summary>
    /// Two-way bindable text content for a <see cref="TextEditor"/>.
    /// </summary>
    public static readonly AttachedProperty<string?> BoundTextProperty =
        AvaloniaProperty.RegisterAttached<TextEditor, string?>(
            "BoundText",
            typeof(TextEditorBindingBehavior),
            defaultValue: null,
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Gets the bound text of a <see cref="TextEditor"/>.</summary>
    public static string? GetBoundText(AvaloniaObject element) => element.GetValue(BoundTextProperty);

    /// <summary>Sets the bound text of a <see cref="TextEditor"/>.</summary>
    public static void SetBoundText(AvaloniaObject element, string? value) => element.SetValue(BoundTextProperty, value);

    static TextEditorBindingBehavior()
    {
        BoundTextProperty.Changed.AddClassHandler<TextEditor>(OnBoundTextChanged);
    }

    private static void OnBoundTextChanged(TextEditor editor, AvaloniaPropertyChangedEventArgs e)
    {
        if (!States.TryGetValue(editor, out var state))
        {
            state = new EditorState();
            States[editor] = state;
            editor.TextChanged += (_, _) => OnEditorTextChanged(editor, state);
            editor.DetachedFromVisualTree += (_, _) => States.Remove(editor);
            InstallPythonHighlighting(editor, state);
        }

        // Ignore the echo from our own editor→property write.
        if (state.UpdatingFromEditor)
            return;

        var newText = e.NewValue as string ?? string.Empty;
        if (editor.Document?.Text == newText)
            return;

        state.UpdatingFromProperty = true;
        try
        {
            editor.Document!.Text = newText;
        }
        finally
        {
            state.UpdatingFromProperty = false;
        }
    }

    private static void OnEditorTextChanged(TextEditor editor, EditorState state)
    {
        // Ignore the echo from our own property→editor write.
        if (state.UpdatingFromProperty)
            return;

        state.UpdatingFromEditor = true;
        try
        {
            SetBoundText(editor, editor.Document?.Text ?? string.Empty);
        }
        finally
        {
            state.UpdatingFromEditor = false;
        }
    }

    /// <summary>
    /// Installs the TextMate Python grammar with a dark theme. Any failure is swallowed so
    /// the editor remains usable as a plain text editor.
    /// </summary>
    private static void InstallPythonHighlighting(TextEditor editor, EditorState state)
    {
        if (state.Highlighted)
            return;

        try
        {
            var registryOptions = new RegistryOptions(ThemeName.DarkPlus);
            var textMate = editor.InstallTextMate(registryOptions);
            var pythonScope = registryOptions.GetScopeByExtension(".py");
            if (!string.IsNullOrEmpty(pythonScope))
                textMate.SetGrammar(pythonScope);
            state.Highlighted = true;
        }
        catch (Exception)
        {
            // Grammar/theme init failed — fall back to plain editing.
            state.Highlighted = false;
        }
    }
}
