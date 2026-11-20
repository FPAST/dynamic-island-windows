using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Services;
using DynamicIsland.Views;

namespace DynamicIsland;

/// <summary>
/// The note widget: a note written right on the glass. A click in the text gives the widget the keyboard for a
/// moment (it is normally a window that never takes it); a click elsewhere or Esc gives it back. What is typed
/// is saved half a second after the last key, into the same notes as the island's Notes tab.
/// S: the note in large letters. M: the whole note, scrolling. L: the note and the other notes to switch to.
/// </summary>
public partial class MainWindow
{
    private Action NotesWidget(Grid body)
    {
        var size = _buildSize;
        Action? newNote = null;          // set below, once everything it uses exists
        string shown = "-";
        var accent = Solid(0xFF, 0xD6, 0x0A);
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        if (size == WidgetSize.Large) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.Children.Add(root);

        // header: the yellow mark, "Заметка", a "+" for a new one (not on the small one)
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var title = W(L.T("Заметка"), 12.5, FontWeights.SemiBold, accent); title.VerticalAlignment = VerticalAlignment.Center;
        if (size != WidgetSize.Small)
        {
            var add = WKey(Glyph("", 11, accent), Solid(0xFF, 0xD6, 0x0A, 0x24), null, () => newNote?.Invoke(), 26, 26, 13, name: L.T("Новая заметка"));
            DockPanel.SetDock(add, Dock.Right); head.Children.Add(add);
        }
        head.Children.Add(title);
        root.Children.Add(head);

        var box = new TextBox
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Brushes.White, CaretBrush = Brushes.White,
            SelectionBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xD6, 0x0A)),
            FontSize = size == WidgetSize.Small ? 15 : 14, FontWeight = size == WidgetSize.Small ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, AcceptsTab = false, Padding = new Thickness(0), Margin = new Thickness(-2, 0, 0, 0),
            VerticalScrollBarVisibility = size == WidgetSize.Small ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto,
            Cursor = Cursors.IBeam,
        };
        System.Windows.Automation.AutomationProperties.SetName(box, L.T("Текст заметки"));
        var hint = W(L.T("Нажмите и пишите — сохранится само"), size == WidgetSize.Small ? 13 : 13.5, null, Dim);
        hint.TextWrapping = TextWrapping.Wrap; hint.IsHitTestVisible = false; hint.VerticalAlignment = VerticalAlignment.Top;
        var page = new Grid { Children = { box, hint } };
        Grid.SetRow(page, 1); root.Children.Add(page);

        StackPanel? others = null;
        if (size == WidgetSize.Large)
        {
            others = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            Grid.SetRow(others, 2); root.Children.Add(others);
        }

        Note? current = StickyNote.Current(NotesStore.Notes, _settings.StickyNote);
        bool editing = false, filling = false;
        var save = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        void Commit()
        {
            save.Stop();
            current = StickyNote.Commit(NotesStore.Notes, current, box.Text, DateTime.Now);
            _settings.StickyNote = current?.Created;
            NotesStore.Save();
            if (!Settings.ReadOnly) _settings.Save();
        }
        save.Tick += (_, _) => Commit();
        box.TextChanged += (_, _) =>
        {
            hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (filling) return;
            save.Stop(); save.Start();   // saved half a second after the last key
        };

        // the keyboard: the widget window never takes it, so a click in the text lifts it for a moment
        DesktopWidgetWindow? Host() => Window.GetWindow(body) as DesktopWidgetWindow;
        box.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (_widgetsEditing || editing) return;
            if (Host() is { } h)
            {
                editing = true;
                h.Raised = true;
                Dispatcher.BeginInvoke(() => { box.Focus(); Keyboard.Focus(box); }, DispatcherPriority.Input);
            }
        };
        void StopEditing()
        {
            if (!editing) return;
            editing = false;
            if (save.IsEnabled) Commit();
            if (Host() is { } h && !_openWidgets.ContainsKey(h.Id)) h.Raised = false;
            Keyboard.ClearFocus();
            Refresh();
        }
        body.Loaded += (_, _) =>
        {
            if (Host() is not { } h) return;
            h.Deactivated += (_, _) => StopEditing();
            h.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && editing) { e.Handled = true; StopEditing(); } };
        };

        void NewNote()
        {
            if (save.IsEnabled) Commit();
            current = null; _settings.StickyNote = null;
            filling = true; box.Text = ""; filling = false;
            hint.Visibility = Visibility.Visible;
            if (Host() is { } h && !_widgetsEditing) { editing = true; h.Raised = true; Dispatcher.BeginInvoke(() => { box.Focus(); Keyboard.Focus(box); }, DispatcherPriority.Input); }
        }

        void Pick(Note n)
        {
            if (save.IsEnabled) Commit();
            current = n; _settings.StickyNote = n.Created;
            if (!Settings.ReadOnly) _settings.Save();
            Refresh(force: true);
        }

        newNote = NewNote;
        void Refresh(bool force = false)
        {
            if (editing && !force) return;   // never under the user's fingers
            if (current != null && !NotesStore.Notes.Contains(current)) current = StickyNote.Current(NotesStore.Notes, null);   // deleted in the Notes tab
            current ??= StickyNote.Current(NotesStore.Notes, _settings.StickyNote);
            var text = current?.Text ?? "";
            if (text != box.Text) { filling = true; box.Text = text; filling = false; }
            hint.Visibility = text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (others == null) return;
            var rest = NotesStore.Notes.Where(n => n != current).Take(4).ToList();
            var key = string.Join("\n", rest.Select(n => n.Created.Ticks + n.Text));
            if (key == shown && !force) return;
            shown = key;
            others.Children.Clear();
            if (rest.Count == 0) return;
            others.Children.Add(new Border { Height = 1, Background = Solid(0xFF, 0xFF, 0xFF, 0x22), Margin = new Thickness(0, 0, 0, 6) });
            foreach (var n in rest)
            {
                var note = n;
                var line = W(StickyNote.Title(n.Text, 40), 12.5, FontWeights.SemiBold); line.TextTrimming = TextTrimming.CharacterEllipsis;
                var when = W(n.Created.ToString("d MMM", Loc.Culture), 11, null, Dim); when.Margin = new Thickness(8, 0, 0, 0);
                var row = new DockPanel { Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 4) };
                DockPanel.SetDock(when, Dock.Right); row.Children.Add(when); row.Children.Add(line);
                row.MouseLeftButtonDown += (_, e) => { if (_widgetsEditing) return; e.Handled = true; Pick(note); };
                others.Children.Add(row);
            }
        }

        return () =>
        {
            box.IsHitTestVisible = !_widgetsEditing;   // in edit mode the whole card is for dragging
            Refresh();
        };
    }
}
