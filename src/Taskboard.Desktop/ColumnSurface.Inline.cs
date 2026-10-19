using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed partial class ColumnSurface
{
    internal bool QuickEntryActive => finishInline is not null;
    private void InlineNew()
    {
        _ = controller.CleanupRetainedStages();
        if (finishInline is not null) return;
        var session = controller.Engine.State.Sessions.FirstOrDefault(x => x.Inline) ?? controller.Engine.NewSession(Lane.Inspiration, true);
        var panel = new StackPanel(); var card = new Border { Child = panel, Tag = "QuickTaskEntry" }; TaskCardLayout.Frame(card);
        var title = Input(session.Working.Title, false); title.ToolTip = "任务名称（可空）"; title.FontWeight = FontWeights.SemiBold; title.Foreground = Ui.Accent;
        var detail = Input(session.Working.Stages[0].Text, true); detail.FontSize = 14; detail.MinLines = 2; detail.MinHeight = 46; detail.MaxHeight = 80; detail.Padding = new Thickness(7, 4, 24, 4); detail.ToolTip = "灵感详情";
        detail.BorderBrush = Ui.Line; detail.BorderThickness = new Thickness(1); RoundedInput.SetRadius(detail, 10);
        var timer = inlineTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        Guid? saved = null; var dirty = session.Working.HasContent; var ended = false; var saving = false;
        var titleRow = new Grid(); titleRow.ColumnDefinitions.Add(new ColumnDefinition()); titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleRow.Children.Add(title);
        var expand = Ui.Button("↗", Expand); expand.Tag = "QuickExpand"; expand.Width = expand.Height = 24; expand.Padding = new Thickness(0);
        expand.VerticalAlignment = VerticalAlignment.Top; expand.ToolTip = "在完整任务窗口中继续编辑"; Grid.SetColumn(expand, 1); titleRow.Children.Add(expand);
        panel.Children.Add(TaskCardLayout.Title(titleRow));
        var detailArea = new Grid { Tag = "QuickTaskDetails" }; detailArea.Children.Add(detail); panel.Children.Add(detailArea);
        void SaveInline()
        {
            if (ended || !dirty) return;
            timer.Stop(); session.Working.Title = title.Text; session.Working.Stages[0].Text = detail.Text;
            if (!session.Working.HasContent) { if (saved is null) controller.Engine.StageSession(session); return; }
            if (saved is Guid deleted && !controller.Engine.State.Tasks.Any(x => x.Id == deleted))
            { card.ToolTip = "任务已被删除，输入仍保留。"; return; }
            saving = true;
            try
            {
                inlineTask = session.Working.Id;
                saved = controller.Engine.Submit(session, false).Id;
                session = controller.Engine.Edit(saved.Value);
                controller.Engine.State.Sessions.Remove(session);
                dirty = false;
                // The editing card already occupies this task's list position.
                // Refresh excludes its official copy until the edit finishes.
                controller.Persist();
            }
            finally { saving = false; }
        }
        void Changed()
        {
            dirty = true; session.Working.Title = title.Text; session.Working.Stages[0].Text = detail.Text;
            if (saved is null) controller.Engine.StageSession(session);
            timer.Stop(); timer.Start();
        }
        void OutsideInput(object sender, PreProcessInputEventArgs args)
        {
            if (ended || saving || args.StagingItem.Input is not MouseButtonEventArgs mouse || mouse.ButtonState != MouseButtonState.Pressed || mouse.RoutedEvent != Mouse.PreviewMouseDownEvent) return;
            var shape = new RectangleGeometry(new Rect(card.RenderSize), card.CornerRadius.TopLeft, card.CornerRadius.TopLeft);
            if (!shape.FillContains(mouse.GetPosition(card))) Finish();
        }
        void Release()
        { InputManager.Current.PreProcessInput -= OutsideInput; releaseInlineInput = null; }
        void Finish()
        {
            if (ended) return; SaveInline(); ended = true; timer.Stop(); Release();
            inlineTask = null; finishInline = flushInline = null; inlineTimer = null;
            ResetFooter(); Refresh();
        }
        void Expand()
        {
            if (ended) return;
            timer.Stop();
            session.Working.Title = title.Text; session.Working.Stages[0].Text = detail.Text;
            if (saved is not null) SaveInline();
            ended = true; Release(); inlineTask = null; finishInline = flushInline = null; inlineTimer = null;
            ResetFooter(); Refresh();
            if (saved is Guid id) controller.EditTask(id);
            else controller.OpenDraft(session);
        }
        var finish = Ui.Button("✓", () => { _ = controller.CleanupRetainedStages(); Finish(); }, true); finish.Tag = "QuickFinish"; finish.Width = finish.Height = 20; finish.Padding = new Thickness(0); finish.Margin = new Thickness(0, 0, 2, 3);
        finish.HorizontalAlignment = HorizontalAlignment.Right; finish.VerticalAlignment = VerticalAlignment.Bottom;
        finish.ToolTip = "保存快捷新建任务"; finish.FontSize = 12; detailArea.Children.Add(finish);
        title.TextChanged += (_, _) => Changed(); detail.TextChanged += (_, _) => Changed(); timer.Tick += (_, _) => SaveInline();
        InputManager.Current.PreProcessInput += OutsideInput; releaseInlineInput = Release;
        finishInline = flushInline = Finish;
        footer.Children.Clear(); footer.Children.Add(card); RevealNewEntry();
        panel.LostKeyboardFocus += (_, _) =>
        {
            if (saving) return;
            Dispatcher.BeginInvoke(new Action(() => { if (!panel.IsKeyboardFocusWithin && !ended) Finish(); }), DispatcherPriority.ContextIdle);
        };
        // Wait for the recycled list container to host the new input before
        // giving it focus; a failed synchronous focus would end it immediately.
        Dispatcher.BeginInvoke(new Action(() => { if (!ended) { list.UpdateLayout(); if (!controller.Smoke) title.Focus(); } }), DispatcherPriority.Input);
    }
    private static TextBox Input(string text, bool multiline)
    {
        var input = Ui.Input(text, multiline); input.Background = Brushes.Transparent; input.BorderThickness = new Thickness(0);
        input.Padding = new Thickness(4, 0, 4, 0); input.Margin = new Thickness(0, 2.4, 0, 2.4); input.MinHeight = multiline ? 24 : 18;
        RoundedInput.SetRadius(input, 6); return input;
    }
}
