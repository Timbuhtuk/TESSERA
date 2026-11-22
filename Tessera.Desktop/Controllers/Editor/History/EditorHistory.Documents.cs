using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Tessera.Core.Imaging;
using Tessera.Core.Alignment;

namespace Tessera.Desktop;

internal sealed partial class EditorHistory
{
    internal void InitializeSourceTrays()
    {
        _editor.sourceStrip.ItemsSource = _traySources;
        RefreshSourceTray();
        _editor.sourceStrip.SelectionChanged += (_, _) =>
        {
            if (_restoringHistory || _editor._processing || _editor.sourceStrip.SelectedItem is not SourceEntry source) return;
            try { _editor.SelectSource(source); }
            catch (Exception ex)
            {
                Host.Notify(Localization.Format("CouldNotOpenSavedSourceFormat", ex.Message), NotificationKind.Error);
                _restoringHistory = true; _editor.sourceStrip.SelectedItem = _editor._activeSource; _restoringHistory = false;
            }
        };
        _editor.generationStrip.SelectionChanged += (_, _) =>
        {
            if (_restoringHistory || _editor._processing) return;
            try
            {
                if (_editor.generationStrip.SelectedItem is HistorySourceRoot root) _editor.SelectSource(root.Source);
                else if (_editor.generationStrip.SelectedItem is GenerationEntry generation) _editor.SelectGeneration(generation);
            }
            catch (Exception ex)
            {
                Host.Notify(Localization.Format("CouldNotOpenResultFormat", ex.Message), NotificationKind.Error);
                _restoringHistory = true; _editor.generationStrip.SelectedItem = _editor.ProcessingBasis == 0 ? _historySourceRoot : _editor._activeSource?.SelectedGeneration; _restoringHistory = false;
            }
        };

    }

    private bool _restoringHistory;

    internal void RemoveLibraryItem(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_editor._processing || sender is not FrameworkElement element) return;
        if (element.DataContext is GenerationEntry generation) { RemoveGenerationByUser(generation); return; }
        _dragCandidate = null;
        try
        {
            if (element.DataContext is SourceEntry source) _editor.RemoveSource(source);
        }
        catch (Exception ex) { Host.Notify(Localization.Format("CouldNotRemoveEntryOrOpenFormat", ex.Message), NotificationKind.Error); }
        finally { _editor.SetProcessingState(_editor._processing); }
    }

    private void RemoveGenerationByUser(GenerationEntry generation)
    {
        if (_editor._processing) return;
        _dragCandidate = null;
        try { RemoveGenerationWithUndo(generation, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)); }
        catch (Exception ex) { Host.Notify(Localization.Format("CouldNotRemoveEntryOrOpenFormat", ex.Message), NotificationKind.Error); }
        finally { _editor.SetProcessingState(_editor._processing); }
    }

    private void RemoveGeneration(GenerationEntry generation)
    {
        var source = Host._sources.FirstOrDefault(s => s.HasLoadedHistory && s.Generations.Contains(generation));
        if (source is null) return;
        int index = source.Generations.IndexOf(generation);
        bool selected = ReferenceEquals(source.SelectedGeneration, generation);
        bool visible = ReferenceEquals(source, _editor._activeSource);
        bool resultWasSelected = visible && _editor.ProcessingBasis == 1;
        var next = source.Generations.Count <= 1 ? null
            : source.Generations[index + 1 < source.Generations.Count ? index + 1 : index - 1];
        Host._library.RemoveGeneration(source, generation);
        _restoringHistory = true;
        try
        {
            source.Generations.Remove(generation);
            if (selected)
            {
                source.SelectedGeneration = visible ? null : next;
                if (visible)
                {
                    _editor.generationStrip.SelectedItem = null;
                    _editor.ClearDisplayedResult();
                }
            }
        }
        finally { _restoringHistory = false; }
        if (selected && visible)
        {
            if (next is not null)
            {
                _editor.SelectGeneration(next);
                if (!resultWasSelected) { _editor.ProcessingBasis = 0; _editor.SyncProcessingSelection(); }
            }
            else
            {
                _editor.ProcessingBasis = 0; _editor.SyncProcessingSelection();
                _editor.Preview.UpdateSourcePreview(); _editor.Tools.UpdateSizeHint();
            }
        }
        Host.Notify(Localization.Text("ResultRemovedFromHistory"), NotificationKind.Success);
    }
}
